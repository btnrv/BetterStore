using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Database;
using BetterStore.Log;
using Economy.Contract;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace BetterStore.Items;

public class ItemManager
{
    private readonly ISwiftlyCore _core;
    public IEconomyAPIv1? Economy { get; set; }
    private readonly BetterStoreConfig _config;
    private readonly BetterStoreDB _db;

    public Dictionary<string, Dictionary<string, string>> Items { get; } = new();

    private readonly Dictionary<ulong, List<Store_Item>> _playerItems = new();
    private readonly Dictionary<ulong, List<Store_Equipment>> _playerEquipments = new();
    private readonly object _dataLock = new();

    private readonly ConcurrentQueue<DbOperation> _pendingDbOps = new();

    public event Action<IPlayer, Dictionary<string, string>>? OnPlayerPurchaseItem;
    public event Action<IPlayer, Dictionary<string, string>>? OnPlayerEquipItem;
    public event Action<IPlayer, Dictionary<string, string>>? OnPlayerUnequipItem;
    public event Action<IPlayer, Dictionary<string, string>>? OnPlayerSellItem;

    public ItemManager(ISwiftlyCore core, BetterStoreConfig config, BetterStoreDB db)
    {
        _core = core;
        _config = config;
        _db = db;
    }

    public void LoadItemConfigs(Dictionary<string, Dictionary<string, string>> items)
    {
        Items.Clear();
        foreach (var kvp in items)
            Items[kvp.Key] = kvp.Value;
    }

    public Dictionary<string, string>? GetItem(string itemId)
    {
        return Items.TryGetValue(itemId, out var item) ? item : null;
    }

    public List<KeyValuePair<string, Dictionary<string, string>>> GetItemsByType(string type)
    {
        return Items.Where(kvp => kvp.Value.TryGetValue("type", out var t) && t == type).ToList();
    }

    public bool IsAnyItemOfType(string type)
    {
        return Items.Values.Any(item => item.TryGetValue("type", out var t) && t == type);
    }

    public bool PlayerHas(IPlayer player, string type, string itemId)
    {
        if (!ItemModuleManager.Modules.TryGetValue(type, out var module) || !module.Equipable)
            return false;

        lock (_dataLock)
        {
            var items = GetItemsUnsafe(player.SteamID);
            var storeItem = items.FirstOrDefault(i => i.Type == type && i.ItemId == itemId);
            if (storeItem == null) return false;

            if (storeItem.IsExpired)
            {
                ExpireItemUnsafe(player.SteamID, storeItem);
                return false;
            }
            return true;
        }
    }

    public bool PlayerUsing(IPlayer player, string type, string itemId)
    {
        lock (_dataLock)
        {
            var equips = GetEquipmentsUnsafe(player.SteamID);
            if (!equips.Any(e => e.Type == type && e.ItemId == itemId))
                return false;
        }

        if (PlayerHas(player, type, itemId))
            return true;

        var item = GetItem(itemId);
        return item != null && CanUseWithoutPurchase(player, item);
    }

    public PurchaseResult Purchase(IPlayer player, Dictionary<string, string> item)
    {
        string itemId = item["item_id"];
        string itemType = item["type"];
        int price = int.TryParse(item.GetValueOrDefault("price", "0"), out int p) ? p : 0;
        string currency = item.GetValueOrDefault("currency", _config.DefaultCurrency);

        if (!ItemModuleManager.Modules.TryGetValue(itemType, out var module))
            return PurchaseResult.InvalidModule;

        bool freeUse = CanUseWithoutPurchase(player, item);

        if (!module.Equipable)
        {
            if (!freeUse && price > 0)
            {
                if (Economy == null || !Economy.HasSufficientFunds(player, currency, price))
                    return PurchaseResult.InsufficientFunds;
                Economy.SubtractPlayerBalance(player, currency, price);
            }

            if (!module.OnEquip(player, item))
                return PurchaseResult.Failed;

            LogPurchase(player, price);
            OnPlayerPurchaseItem?.Invoke(player, item);
            return PurchaseResult.Success;
        }

        lock (_dataLock)
        {
            if (PlayerOwnsItemUnsafe(player.SteamID, itemId))
                return PurchaseResult.AlreadyOwned;
        }

        if (freeUse)
        {
            Equip(player, item);
            return PurchaseResult.FreeUseEquipped;
        }

        if (price > 0)
        {
            if (Economy == null || !Economy.HasSufficientFunds(player, currency, price))
                return PurchaseResult.InsufficientFunds;
            Economy.SubtractPlayerBalance(player, currency, price);
        }

        string durationStr = item.GetValueOrDefault("duration", "0");
        long durationSeconds = StoreItemsConfig.ParseDurationToSeconds(durationStr);
        DateTime? expiration = durationSeconds > 0 ? DateTime.UtcNow.AddSeconds(durationSeconds) : null;

        var playerItem = new Store_Item
        {
            SteamID = player.SteamID,
            Price = price,
            Type = itemType,
            ItemId = itemId,
            DateOfPurchase = DateTime.UtcNow,
            DateOfExpiration = expiration,
            DurationSeconds = durationSeconds
        };

        lock (_dataLock)
        {
            GetItemsUnsafe(player.SteamID).Add(playerItem);
        }

        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.InsertItem,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = player.SteamID,
                ["@ItemId"] = itemId,
                ["@Type"] = itemType,
                ["@Expiration"] = expiration.HasValue ? (object)expiration.Value : DBNull.Value,
                ["@Duration"] = durationSeconds
            }
        });

        LogPurchase(player, price);
        OnPlayerPurchaseItem?.Invoke(player, item);

        Equip(player, item);
        return PurchaseResult.Success;
    }

    public bool Equip(IPlayer player, Dictionary<string, string> item)
    {
        string itemType = item["type"];
        string itemId = item["item_id"];

        bool owns = false;
        lock (_dataLock)
        {
            owns = PlayerOwnsItemUnsafe(player.SteamID, itemId);
            if (owns)
            {
                var storeItem = GetItemsUnsafe(player.SteamID).FirstOrDefault(i => i.ItemId == itemId);
                if (storeItem?.IsExpired == true)
                {
                    ExpireItemUnsafe(player.SteamID, storeItem);
                    owns = false;
                }
            }
        }

        if (!owns && !CanUseWithoutPurchase(player, item))
            return false;

        if (item.TryGetValue("disabled", out string? disabled)
            && disabled.Equals("true", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ItemModuleManager.Modules.TryGetValue(itemType, out var module))
            return false;

        var conflicting = GetConflictingEquipments(player, item, itemType);
        foreach (var conflict in conflicting)
        {
            var conflictItem = GetItem(conflict.ItemId);
            if (conflictItem != null)
                Unequip(player, conflictItem, false);
        }

        if (!module.OnEquip(player, item))
            return false;

        int slot = ResolveSlot(item, itemType);

        lock (_dataLock)
        {
            var equips = GetEquipmentsUnsafe(player.SteamID);
            if (!equips.Any(e => e.ItemId == itemId))
            {
                equips.Add(new Store_Equipment
                {
                    SteamID = player.SteamID,
                    Type = itemType,
                    ItemId = itemId,
                    Slot = slot
                });
            }
        }

        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.UpsertEquipment,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = player.SteamID,
                ["@ItemId"] = itemId,
                ["@Type"] = itemType
            }
        });

        OnPlayerEquipItem?.Invoke(player, item);
        return true;
    }

    public bool Unequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        string itemId = item["item_id"];

        if (!ItemModuleManager.Modules.TryGetValue(item["type"], out var module))
            return false;

        lock (_dataLock)
        {
            var equips = GetEquipmentsUnsafe(player.SteamID);
            var equipped = equips.FirstOrDefault(e => e.ItemId == itemId);
            if (equipped == null)
                return false;
            equips.Remove(equipped);
        }

        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.DeleteEquipment,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = player.SteamID,
                ["@ItemId"] = itemId
            }
        });

        OnPlayerUnequipItem?.Invoke(player, item);
        return module.OnUnequip(player, item, update);
    }

    public bool Sell(IPlayer player, Dictionary<string, string> item)
    {
        string itemId = item["item_id"];
        Store_Item? playerItem;

        lock (_dataLock)
        {
            var items = GetItemsUnsafe(player.SteamID);
            playerItem = items.FirstOrDefault(i => i.ItemId == itemId);
            if (playerItem == null) return false;

            if (playerItem.IsExpired)
            {
                ExpireItemUnsafe(player.SteamID, playerItem);
                return false;
            }
        }

        int sellPrice = (int)(playerItem.Price * _config.SellRatio);
        string currency = item.GetValueOrDefault("currency", _config.DefaultCurrency);

        if (Economy != null && sellPrice > 0)
            Economy.AddPlayerBalance(player, currency, sellPrice);

        Unequip(player, item, true);

        lock (_dataLock)
        {
            GetItemsUnsafe(player.SteamID).Remove(playerItem);
        }

        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.DeleteItem,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = player.SteamID,
                ["@ItemId"] = itemId
            }
        });

        if (_config.EnableLogging)
            StoreLog.SaveLog(player.Controller.PlayerName, player.SteamID.ToString(),
                "", "", sellPrice, StoreLog.LogType.Sell, _core);

        OnPlayerSellItem?.Invoke(player, item);
        return true;
    }

    public bool CanUseWithoutPurchase(IPlayer player, Dictionary<string, string> item)
    {
        string permission = _config.Permissions.FreeUse;
        if (string.IsNullOrWhiteSpace(permission))
            return false;

        if (!_core.Permission.PlayerHasPermission(player.SteamID, permission))
            return false;

        if (!item.TryGetValue("type", out string? itemType) || string.IsNullOrWhiteSpace(itemType))
            return false;

        if (!ItemModuleManager.Modules.TryGetValue(itemType, out var module) || !module.Equipable)
            return false;

        return true;
    }

    public void ExpirePlayerItems(ulong steamId)
    {
        lock (_dataLock)
        {
            var items = GetItemsUnsafe(steamId);
            var expired = items.Where(i => i.IsExpired).ToList();
            foreach (var item in expired)
                ExpireItemUnsafe(steamId, item);
        }
    }

    public async Task LoadPlayerData(ulong steamId)
    {
        var dbItems = await _db.GetPlayerItems(steamId);
        var dbEquipment = await _db.GetPlayerEquipment(steamId);

        lock (_dataLock)
        {
            var items = GetItemsUnsafe(steamId);
            foreach (var row in dbItems)
            {
                if (items.Any(i => i.ItemId == row.ItemId))
                    continue;

                var itemData = GetItem(row.ItemId);
                int price = 0;
                if (itemData != null && itemData.TryGetValue("price", out var pStr))
                    int.TryParse(pStr, out price);

                var storeItem = new Store_Item
                {
                    SteamID = steamId,
                    Type = row.Type,
                    ItemId = row.ItemId,
                    Price = price,
                    DateOfPurchase = row.DateOfPurchase,
                    DateOfExpiration = row.DateOfExpiration,
                    DurationSeconds = row.DurationSeconds
                };

                if (storeItem.IsExpired)
                {
                    ExpireItemUnsafe(steamId, storeItem);
                    continue;
                }

                items.Add(storeItem);
            }

            var equips = GetEquipmentsUnsafe(steamId);
            foreach (var row in dbEquipment)
            {
                if (equips.Any(e => e.ItemId == row.ItemId))
                    continue;

                var itemData = GetItem(row.ItemId);
                if (itemData == null)
                    continue;

                string type = row.Type;
                int slot = ResolveSlot(itemData, type);

                equips.Add(new Store_Equipment
                {
                    SteamID = steamId,
                    Type = type,
                    ItemId = row.ItemId,
                    Slot = slot
                });
            }
        }
    }

    public void ClearPlayerData(ulong steamId)
    {
        lock (_dataLock)
        {
            _playerItems.Remove(steamId);
            _playerEquipments.Remove(steamId);
        }
    }

    public void ClearAllPlayerData()
    {
        lock (_dataLock)
        {
            _playerItems.Clear();
            _playerEquipments.Clear();
        }
    }

    public void AddPlayerItem(ulong steamId, Store_Item storeItem)
    {
        lock (_dataLock)
        {
            GetItemsUnsafe(steamId).Add(storeItem);
        }

        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.InsertItem,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = steamId,
                ["@ItemId"] = storeItem.ItemId,
                ["@Type"] = storeItem.Type,
                ["@Expiration"] = storeItem.DateOfExpiration.HasValue ? (object)storeItem.DateOfExpiration.Value : DBNull.Value,
                ["@Duration"] = storeItem.DurationSeconds
            }
        });
    }

    public IEnumerable<ulong> GetLoadedPlayerSteamIds()
    {
        lock (_dataLock)
        {
            return _playerItems.Keys.ToList();
        }
    }

    public async Task FlushPendingDbOps()
    {
        var operations = new List<DbOperation>();
        while (_pendingDbOps.TryDequeue(out var op))
            operations.Add(op);

        if (operations.Count > 0)
            await _db.ExecuteBatch(operations);
    }

    public List<Store_Equipment> GetPlayerEquipments(IPlayer player, string? type)
    {
        lock (_dataLock)
        {
            return GetEquipmentsUnsafe(player.SteamID)
                .Where(e => type == null || e.Type == type)
                .ToList();
        }
    }

    public List<Store_Item> GetPlayerItems(IPlayer player, string? type)
    {
        lock (_dataLock)
        {
            return GetItemsUnsafe(player.SteamID)
                .Where(i => type == null || i.Type == type)
                .ToList();
        }
    }

    public List<Store_Equipment> GetAllEquipments()
    {
        lock (_dataLock)
        {
            return _playerEquipments.Values.SelectMany(e => e).ToList();
        }
    }

    private List<Store_Item> GetItemsUnsafe(ulong steamId)
    {
        if (!_playerItems.TryGetValue(steamId, out var list))
        {
            list = new List<Store_Item>();
            _playerItems[steamId] = list;
        }
        return list;
    }

    private List<Store_Equipment> GetEquipmentsUnsafe(ulong steamId)
    {
        if (!_playerEquipments.TryGetValue(steamId, out var list))
        {
            list = new List<Store_Equipment>();
            _playerEquipments[steamId] = list;
        }
        return list;
    }

    private bool PlayerOwnsItemUnsafe(ulong steamId, string itemId)
    {
        return GetItemsUnsafe(steamId).Any(i => i.ItemId == itemId && !i.IsExpired);
    }

    private void ExpireItemUnsafe(ulong steamId, Store_Item storeItem)
    {
        GetItemsUnsafe(steamId).Remove(storeItem);
        GetEquipmentsUnsafe(steamId).RemoveAll(e => e.ItemId == storeItem.ItemId);

        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.DeleteItem,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = steamId,
                ["@ItemId"] = storeItem.ItemId
            }
        });
        _pendingDbOps.Enqueue(new DbOperation
        {
            Sql = Queries.DeleteEquipment,
            Parameters = new Dictionary<string, object>
            {
                ["@SteamId"] = steamId,
                ["@ItemId"] = storeItem.ItemId
            }
        });
    }

    private List<Store_Equipment> GetConflictingEquipments(IPlayer player, Dictionary<string, string> item, string itemType)
    {
        lock (_dataLock)
        {
            var equips = GetEquipmentsUnsafe(player.SteamID);

            return itemType switch
            {
                "playerskin" => equips.FindAll(e =>
                {
                    if (e.Type != itemType) return false;
                    var equippedItem = GetItem(e.ItemId);
                    return equippedItem != null && ItemTeamHelper.IsPlayerskinTeamConflict(equippedItem, item);
                }),

                "customweapon" => equips.FindAll(e =>
                {
                    if (e.Type != itemType) return false;
                    var equippedItem = GetItem(e.ItemId);
                    if (equippedItem == null) return false;
                    return equippedItem.TryGetValue("weapon", out string? ew)
                        && item.TryGetValue("weapon", out string? nw)
                        && SameWeaponBase(ew, nw);
                }),

                _ => equips.FindAll(e =>
                    e.Type == itemType
                    && item.TryGetValue("slot", out string? s)
                    && int.TryParse(s, out int sl)
                    && e.Slot == sl)
            };
        }
    }

    private static bool SameWeaponBase(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return string.Equals(GetWeaponBase(a), GetWeaponBase(b), StringComparison.Ordinal);
    }

    private static string GetWeaponBase(string weaponSpec)
    {
        int idx = weaponSpec.IndexOf(':');
        return idx > 0 ? weaponSpec[..idx] : weaponSpec;
    }

    private static int ResolveSlot(Dictionary<string, string> item, string itemType)
    {
        return itemType == "playerskin"
            ? ItemTeamHelper.ResolvePlayerskinTeamSlot(item)
            : item.TryGetValue("slot", out string? s) && int.TryParse(s, out int sl) ? sl : 0;
    }

    private void LogPurchase(IPlayer player, int price)
    {
        if (_config.EnableLogging)
            StoreLog.SaveLog(player.Controller.PlayerName, player.SteamID.ToString(),
                "", "", price, StoreLog.LogType.Purchase, _core);
    }
}
