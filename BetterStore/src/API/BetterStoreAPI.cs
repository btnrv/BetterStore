using System;
using System.Collections.Generic;
using BetterStore.Contract;
using BetterStore.Config;
using Economy.Contract;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared;
using BetterStore.Items;
using Microsoft.Extensions.Logging;

namespace BetterStore.API;

public class BetterStoreAPIv1 : IBetterStoreAPIv1
{
    private readonly ISwiftlyCore _core;
    public IEconomyAPIv1? Economy { get; set; }
    private readonly BetterStoreConfig _config;
    private readonly ItemManager _itemManager;

    public event Action<IPlayer, string, int, string>? OnPlayerPurchaseItem;
    public event Action<IPlayer, string>? OnPlayerEquipItem;
    public event Action<IPlayer, string>? OnPlayerUnequipItem;
    public event Action<IPlayer, string, int>? OnPlayerSellItem;

    public BetterStoreAPIv1(ISwiftlyCore core, BetterStoreConfig config, ItemManager itemManager)
    {
        _core = core;
        _config = config;
        _itemManager = itemManager;

        _itemManager.OnPlayerPurchaseItem += (player, item) =>
        {
            int price = int.TryParse(item.GetValueOrDefault("price", "0"), out int p) ? p : 0;
            string currency = item.GetValueOrDefault("currency", _config.DefaultCurrency);
            OnPlayerPurchaseItem?.Invoke(player, currency, price, item["item_id"]);
        };

        _itemManager.OnPlayerEquipItem += (player, item) =>
            OnPlayerEquipItem?.Invoke(player, item["item_id"]);

        _itemManager.OnPlayerUnequipItem += (player, item) =>
            OnPlayerUnequipItem?.Invoke(player, item["item_id"]);

        _itemManager.OnPlayerSellItem += (player, item) =>
        {
            int price = (int)(int.TryParse(item.GetValueOrDefault("price", "0"), out int p) ? p * _config.SellRatio : 0);
            OnPlayerSellItem?.Invoke(player, item["item_id"], price);
        };
    }

    public void RegisterModule(IItemModule module)
    {
        _core.Logger.LogInformation("BetterStore module registered via API: {ModuleName}", module.GetType().Name);
    }

    public Dictionary<string, string>? GetItem(string itemId) => _itemManager.GetItem(itemId);

    public List<KeyValuePair<string, Dictionary<string, string>>> GetItemsByType(string type) =>
        _itemManager.GetItemsByType(type);

    public bool PlayerHasItem(IPlayer player, string type, string itemId) =>
        _itemManager.PlayerHas(player, type, itemId);

    public bool PlayerUsingItem(IPlayer player, string type, string itemId) =>
        _itemManager.PlayerUsing(player, type, itemId);

    public bool GiveItem(IPlayer player, string itemId)
    {
        var item = _itemManager.GetItem(itemId);
        if (item == null) return false;

        string type = item.GetValueOrDefault("type", "unknown");
        int price = int.TryParse(item.GetValueOrDefault("price", "0"), out int p) ? p : 0;
        string durationStr = item.GetValueOrDefault("duration", "0");
        long durationSeconds = StoreItemsConfig.ParseDurationToSeconds(durationStr);
        DateTime? expiration = durationSeconds > 0 ? DateTime.UtcNow.AddSeconds(durationSeconds) : null;

        _itemManager.AddPlayerItem(player.SteamID, new Store_Item
        {
            SteamID = player.SteamID,
            Type = type,
            ItemId = itemId,
            Price = price,
            DateOfPurchase = DateTime.UtcNow,
            DateOfExpiration = expiration,
            DurationSeconds = durationSeconds
        });

        return true;
    }

    public bool PurchaseItem(IPlayer player, string itemId)
    {
        var item = _itemManager.GetItem(itemId);
        if (item == null) return false;
        var result = _itemManager.Purchase(player, item);
        return result == PurchaseResult.Success || result == PurchaseResult.FreeUseEquipped;
    }

    public bool EquipItem(IPlayer player, string itemId)
    {
        var item = _itemManager.GetItem(itemId);
        return item != null && _itemManager.Equip(player, item);
    }

    public bool UnequipItem(IPlayer player, string itemId, bool update = true)
    {
        var item = _itemManager.GetItem(itemId);
        return item != null && _itemManager.Unequip(player, item, update);
    }

    public bool SellItem(IPlayer player, string itemId)
    {
        var item = _itemManager.GetItem(itemId);
        return item != null && _itemManager.Sell(player, item);
    }

    private string ResolveCurrency(string? currency) => currency ?? _config.DefaultCurrency;

    public int GetPlayerBalance(IPlayer player, string? currency = null) =>
        (int)(Economy?.GetPlayerBalance(player, ResolveCurrency(currency)) ?? 0m);

    public void AddPlayerBalance(IPlayer player, int amount, string? currency = null) =>
        Economy?.AddPlayerBalance(player, ResolveCurrency(currency), amount);

    public void SubtractPlayerBalance(IPlayer player, int amount, string? currency = null) =>
        Economy?.SubtractPlayerBalance(player, ResolveCurrency(currency), amount);

    public void SetPlayerBalance(IPlayer player, int amount, string? currency = null) =>
        Economy?.SetPlayerBalance(player, ResolveCurrency(currency), amount);

    public bool HasSufficientFunds(IPlayer player, int amount, string? currency = null) =>
        Economy != null && (Economy.GetPlayerBalance(player, ResolveCurrency(currency)) >= amount);
}
