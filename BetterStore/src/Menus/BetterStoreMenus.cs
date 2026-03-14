using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using BetterStore.Items.Modules;
using Economy.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;

namespace BetterStore.Menus;

public class BetterStoreMenus
{
    private readonly ISwiftlyCore _core;
    private readonly BetterStoreConfig _config;
    private readonly ItemManager _itemManager;
    private readonly StoreNode _storeRoot;
    private readonly Dictionary<ulong, DateTime> _inspectCooldowns = new();

    public IEconomyAPIv1? Economy { get; set; }

    public BetterStoreMenus(ISwiftlyCore core, BetterStoreConfig config, ItemManager itemManager, StoreNode storeRoot)
    {
        _core = core;
        _config = config;
        _itemManager = itemManager;
        _storeRoot = storeRoot;
    }

    private void Reply(IPlayer player, string key, params object[] args)
    {
        player.SendChat($"{_config.PluginPrefix}{_core.Localizer[key, args]}");
    }

    private string GetGradientTitle(string text)
    {
        var colors = _config.MenuTitleGradient;
        if (colors == null || colors.Count == 0) return text;
        if (colors.Count == 1) return $"<font color='{colors[0]}'>{text}</font>";
        return HtmlGradient.GenerateGradientText(text, colors.ToArray());
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length == 1) return s.ToUpperInvariant();
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
    private void ApplyDefaultMenuSignature(IMenuBuilderAPI builder)
    {
        builder.Design.SetDefaultComment(_core.Localizer["BetterStore.Menu.SelectOne"]);
        builder.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll);

        var gradient = _config.MenuTitleGradient;
        if (gradient != null && gradient.Count >= 2)
        {
            builder.Design.SetMenuFooterColor(gradient[0]);
            builder.Design.SetVisualGuideLineColor(gradient[0]);
            builder.Design.SetNavigationMarkerColor(gradient[1]);
        }
    }

    public void OpenMainMenu(IPlayer player)
    {
        var menu = BuildNodeMenu(player, _config.MenuTitle, _storeRoot.Children);
        _core.MenusAPI.OpenMenuForPlayer(player, menu);
    }

    public void OpenInventoryMenu(IPlayer player)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        ApplyDefaultMenuSignature(builder);
        builder.Design.SetMenuTitle(GetGradientTitle(_core.Localizer["BetterStore.Menu.InventoryTitle"]));

        var ownedItems = _itemManager.GetPlayerItems(player, null)
            .Where(i => !i.IsExpired)
            .OrderByDescending(i => i.DateOfPurchase)
            .ToList();

        if (ownedItems.Count == 0)
        {
            builder.AddOption(new TextMenuOption(_core.Localizer["BetterStore.Menu.InventoryEmpty"]) { Enabled = false });
            _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
            return;
        }

        foreach (var owned in ownedItems)
        {
            var item = _itemManager.GetItem(owned.ItemId);
            if (item == null) continue;

            string itemType = item.GetValueOrDefault("type", owned.Type);
            string name = item.GetValueOrDefault("name", owned.ItemId);
            bool isUsing = _itemManager.PlayerUsing(player, itemType, owned.ItemId);

            string indicator = isUsing
                ? _core.Localizer["BetterStore.Menu.Indicator.Equipped"]
                : _core.Localizer["BetterStore.Menu.Indicator.Owned"];

            string optionText = $"{indicator} {name}";
            if (!owned.IsPermanent)
                optionText += $" ({FormatRemaining(owned)})";

            string capturedItemId = owned.ItemId;
            var btn = new ButtonMenuOption(optionText);
            btn.Click += async (sender, args) =>
            {
                _core.Scheduler.NextTick(() => OpenOwnedItemMenu(player, capturedItemId));
            };
            builder.AddOption(btn);
        }

        _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }

    public void OpenRichMenu(IPlayer player)
    {
        if (_config.Currencies.Count > 1)
        {
            var builder = _core.MenusAPI.CreateBuilder();
            ApplyDefaultMenuSignature(builder);
            builder.Design.SetMenuTitle(GetGradientTitle(_core.Localizer["BetterStore.Rich.SelectCurrency"]));

            foreach (var (name, cfg) in _config.Currencies)
            {
                string captured = name;
                string displayName = Capitalize(name);
                string icon = cfg.HTMLIcon;
                var btn = new ButtonMenuOption($"{displayName} {icon}");
                btn.Click += async (sender, args) =>
                {
                    _core.Scheduler.NextTick(() => ShowLeaderboard(player, captured));
                };
                builder.AddOption(btn);
            }

            _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
        }
        else
        {
            ShowLeaderboard(player, _config.DefaultCurrency);
        }
    }

    private void ShowLeaderboard(IPlayer player, string currency)
    {
        if (Economy == null) return;

        string icon = _config.GetCurrencyIcon(currency);
        string menuTitle = _core.Localizer["BetterStore.Rich.Title", Capitalize(currency)];
        string emptyText = _core.Localizer["BetterStore.Rich.Empty"];

        // Capture player name+steamid on game thread; balance queries go off-thread.
        var snapshot = _core.PlayerManager.GetAllPlayers()
            .Where(p => !p.IsFakeClient && p.IsAuthorized)
            .Select(p => (SteamID: p.SteamID, Name: p.Controller.PlayerName))
            .ToList();

        var economy = Economy;
        Task.Run(() =>
        {
            var ranked = snapshot
                .Select(s => (s.Name, Balance: economy.GetPlayerBalance(s.SteamID, currency)))
                .OrderByDescending(x => x.Balance)
                .Take(10)
                .ToList();

            _core.Scheduler.NextTick(() =>
            {
                var builder = _core.MenusAPI.CreateBuilder();
                ApplyDefaultMenuSignature(builder);
                builder.Design.SetMenuTitle(GetGradientTitle(menuTitle));

                if (ranked.Count == 0)
                {
                    builder.AddOption(new TextMenuOption(emptyText) { Enabled = false });
                }
                else
                {
                    for (int i = 0; i < ranked.Count; i++)
                    {
                        string text = _core.Localizer["BetterStore.Rich.Entry", i + 1, ranked[i].Name, ranked[i].Balance, icon];
                        builder.AddOption(new TextMenuOption(text) { Enabled = false });
                    }
                }

                _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
            });
        });
    }

    private IMenuAPI BuildNodeMenu(IPlayer player, string title, List<StoreNode> children, IMenuAPI? parentMenu = null)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        ApplyDefaultMenuSignature(builder);
        builder.Design.SetMenuTitle(GetGradientTitle(title));

        if (parentMenu != null)
            builder.BindToParent(parentMenu);

        IMenuAPI? selfRef = null;

        foreach (StoreNode node in children)
        {
            if (node.IsItem)
                AddItemOption(builder, player, node);
            else
            {
                var capturedNode = node;
                builder.AddOption(new SubmenuMenuOption(capturedNode.DisplayName,
                    () => BuildNodeMenu(player, capturedNode.DisplayName, capturedNode.Children, selfRef)));
            }
        }

        var builtMenu = builder.Build();
        selfRef = builtMenu;
        return builtMenu;
    }

    private void AddItemOption(IMenuBuilderAPI builder, IPlayer player, StoreNode node)
    {
        var item = node.ItemData;

        if (item.TryGetValue("enable", out string? enable) && enable == "false")
            return;
        if (item.TryGetValue("hide", out string? hide) && hide == "true")
            return;

        bool isDisabled = item.TryGetValue("disabled", out string? disabledVal)
            && disabledVal.Equals("true", StringComparison.OrdinalIgnoreCase);

        string name = node.DisplayName;
        string priceStr = item.GetValueOrDefault("price", "0");
        string currency = item.GetValueOrDefault("currency", _config.DefaultCurrency);
        string currencyIcon = _config.GetCurrencyIcon(currency);
        string itemType = item.GetValueOrDefault("type", "");
        string itemId = node.ItemId;

        bool isUsing = _itemManager.PlayerUsing(player, itemType, itemId);
        bool playerOwns = !isUsing && _itemManager.PlayerHas(player, itemType, itemId);

        string icon = isUsing
            ? _core.Localizer["BetterStore.Menu.Indicator.Equipped"] + " "
            : playerOwns
                ? _core.Localizer["BetterStore.Menu.Indicator.Owned"] + " "
                : "";

        string durationSuffix = FormatDurationLabel(item.GetValueOrDefault("duration", ""));

        string optionText = $"{icon}{name} - {priceStr}{currencyIcon}{durationSuffix}";

        var btn = new ButtonMenuOption(optionText) { Enabled = !isDisabled };
        if (!isDisabled)
        {
            btn.Click += async (sender, args) =>
            {
                _core.Scheduler.NextTick(() => HandleItemInteraction(player, itemId, itemType));
            };
        }
        builder.AddOption(btn);
    }

    private void HandleItemInteraction(IPlayer player, string itemId, string itemType)
    {
        var item = _itemManager.GetItem(itemId);
        if (item == null) return;

        string name = item.GetValueOrDefault("name", itemId);
        string priceStr = item.GetValueOrDefault("price", "0");
        string currency = item.GetValueOrDefault("currency", _config.DefaultCurrency);
        string currencyIcon = _config.GetCurrencyIcon(currency);

        if (_itemManager.PlayerUsing(player, itemType, itemId))
        {
            if (_itemManager.Unequip(player, item, true))
                Reply(player, "BetterStore.Unequip.Success", name);
        }
        else if (_itemManager.PlayerHas(player, itemType, itemId))
        {
            ShowOwnedItemMenu(player, item, name, priceStr, currency, currencyIcon, itemType, itemId);
        }
        else
        {
            HandlePurchase(player, item, name, priceStr, currency, currencyIcon, itemType);
        }
    }

    private void ShowOwnedItemMenu(IPlayer player, Dictionary<string, string> item,
        string name, string priceStr, string currency, string currencyIcon, string itemType, string itemId)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        ApplyDefaultMenuSignature(builder);
        builder.AddOption(new TextMenuOption(name) { Enabled = false });

        var equipBtn = new ButtonMenuOption(_core.Localizer["BetterStore.Menu.Equip"]);
        equipBtn.Click += async (sender, args) =>
        {
            _core.Scheduler.NextTick(() =>
            {
                if (_itemManager.Equip(player, item))
                    Reply(player, "BetterStore.Equip.Success", name);
            });
        };
        builder.AddOption(equipBtn);

        if (itemType is "playerskin" or "customweapon")
        {
            var inspectBtn = new ButtonMenuOption(_core.Localizer["BetterStore.Menu.Inspect"]);
            inspectBtn.Click += async (sender, args) =>
            {
                _core.Scheduler.NextTick(() => HandleInspect(player, item, itemType));
            };
            builder.AddOption(inspectBtn);
        }

        if (_config.EnableSelling)
        {
            int price = int.TryParse(priceStr, out int p) ? p : 0;
            int sellPrice = (int)(price * _config.SellRatio);
            if (sellPrice > 0)
            {
                var sellBtn = new ButtonMenuOption($"{_core.Localizer["BetterStore.Menu.Sell"]} ({sellPrice}{currencyIcon})");
                sellBtn.Click += async (sender, args) =>
                {
                    _core.Scheduler.NextTick(() =>
                    {
                        if (_itemManager.Sell(player, item))
                            Reply(player, "BetterStore.Sell.Success", name, sellPrice.ToString(), _config.GetCurrencyChatIcon(currency));
                    });
                };
                builder.AddOption(sellBtn);
            }
        }

        string remainingTime = GetRemainingTimeLabel(player, itemType, itemId);
        builder.AddOption(new TextMenuOption(_core.Localizer["BetterStore.Menu.RemainingTime", remainingTime]) { Enabled = false });

        _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }

    private void OpenOwnedItemMenu(IPlayer player, string itemId)
    {
        var item = _itemManager.GetItem(itemId);
        if (item == null) return;

        string itemType = item.GetValueOrDefault("type", "");
        if (!_itemManager.PlayerHas(player, itemType, itemId))
        {
            Reply(player, "BetterStore.NotOwned");
            return;
        }

        string name = item.GetValueOrDefault("name", itemId);
        string priceStr = item.GetValueOrDefault("price", "0");
        string currency = item.GetValueOrDefault("currency", _config.DefaultCurrency);
        string currencyIcon = _config.GetCurrencyIcon(currency);

        ShowOwnedItemMenu(player, item, name, priceStr, currency, currencyIcon, itemType, itemId);
    }

    private void HandlePurchase(IPlayer player, Dictionary<string, string> item,
        string name, string priceStr, string currency, string currencyIcon, string itemType)
    {
        if (_config.EnableConfirmMenu)
            ShowConfirmPurchaseMenu(player, item, name, priceStr, currency, currencyIcon, itemType);
        else
            ExecutePurchase(player, item, name, priceStr, currencyIcon, currency);
    }

    private void ShowConfirmPurchaseMenu(IPlayer player, Dictionary<string, string> item,
        string name, string priceStr, string currency, string currencyIcon, string itemType)
    {
        var builder = _core.MenusAPI.CreateBuilder();
        ApplyDefaultMenuSignature(builder);
        builder.AddOption(new TextMenuOption(_core.Localizer["BetterStore.Menu.ConfirmPurchase"]) { Enabled = false });
        builder.AddOption(new TextMenuOption($"{name} - {priceStr}{currencyIcon}") { Enabled = false });

        if (itemType is "playerskin" or "customweapon")
        {
            var inspectBtn = new ButtonMenuOption(_core.Localizer["BetterStore.Menu.Inspect"]);
            inspectBtn.Click += async (sender, args) =>
            {
                _core.Scheduler.NextTick(() => HandleInspect(player, item, itemType));
            };
            builder.AddOption(inspectBtn);
        }

        var yesBtn = new ButtonMenuOption(_core.Localizer["BetterStore.Menu.Yes"]) { CloseAfterClick = true };
        yesBtn.Click += async (sender, args) =>
        {
            _core.Scheduler.NextTick(() => ExecutePurchase(player, item, name, priceStr, currencyIcon, currency));
        };
        builder.AddOption(yesBtn);

        var noBtn = new ButtonMenuOption(_core.Localizer["BetterStore.Menu.No"]) { CloseAfterClick = true };
        noBtn.Click += async (sender, args) =>
        {
            _core.Scheduler.NextTick(() => OpenMainMenu(player));
        };
        builder.AddOption(noBtn);

        _core.MenusAPI.OpenMenuForPlayer(player, builder.Build());
    }

    private void ExecutePurchase(IPlayer player, Dictionary<string, string> item,
        string name, string priceStr, string currencyIcon, string currency)
    {
        string chatIcon = _config.GetCurrencyChatIcon(currency);
        var result = _itemManager.Purchase(player, item);

        switch (result)
        {
            case PurchaseResult.Success:
                Reply(player, "BetterStore.Purchase.Success", name, priceStr, chatIcon);
                break;

            case PurchaseResult.FreeUseEquipped:
                Reply(player, "BetterStore.FreeUse.Equip.Success", name);
                break;

            case PurchaseResult.AlreadyOwned:
                Reply(player, "BetterStore.Purchase.AlreadyOwned", name);
                break;

            case PurchaseResult.InsufficientFunds:
                Reply(player, "BetterStore.Purchase.NoFunds", currency);
                break;

            case PurchaseResult.InvalidModule:
            case PurchaseResult.Failed:
                Reply(player, "BetterStore.Purchase.Failed");
                break;
        }
    }

    private void HandleInspect(IPlayer player, Dictionary<string, string> item, string itemType)
    {
        DateTime now = DateTime.UtcNow;
        if (_inspectCooldowns.TryGetValue(player.SteamID, out DateTime lastInspect)
            && (now - lastInspect).TotalSeconds < _config.InspectCooldownSeconds)
        {
            Reply(player, "BetterStore.Inspect.Cooldown");
            return;
        }

        _inspectCooldowns[player.SteamID] = now;

        if (itemType == "playerskin")
        {
            Item_PlayerSkin.Inspect(_core, player, item.GetValueOrDefault("model", ""),
                item.GetValueOrDefault("skin"), null, 0f);
        }
        else if (itemType == "customweapon")
        {
            Item_CustomWeapon.Inspect(_core, player, item.GetValueOrDefault("weapon", ""),
                null, 0f);
        }
    }

    private string GetRemainingTimeLabel(IPlayer player, string itemType, string itemId)
    {
        var ownedItem = _itemManager.GetPlayerItems(player, itemType)
            .FirstOrDefault(i => i.ItemId == itemId);

        if (ownedItem == null)
            return _core.Localizer["BetterStore.Menu.RemainingTime.Expired"];

        if (ownedItem.IsPermanent)
            return _core.Localizer["BetterStore.Menu.RemainingTime.Permanent"];

        return FormatRemaining(ownedItem);
    }

    private string FormatRemaining(Store_Item item)
    {
        if (item.IsPermanent || !item.DateOfExpiration.HasValue)
            return _core.Localizer["BetterStore.Menu.RemainingTime.Permanent"];

        TimeSpan remaining = item.DateOfExpiration.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return _core.Localizer["BetterStore.Menu.RemainingTime.Expired"];

        int days = (int)remaining.TotalDays;
        string time = $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        return days > 0
            ? $"{_core.Localizer["BetterStore.Duration.Days", days]} {time}"
            : time;
    }

    private string FormatDurationLabel(string durationStr)
    {
        if (string.IsNullOrEmpty(durationStr) || durationStr == "0"
            || durationStr.Equals("permanent", StringComparison.OrdinalIgnoreCase))
            return "";

        var matches = Regex.Matches(durationStr, @"(\d+)\s*(y|d|h|m|s)", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return "";

        var parts = new List<string>();
        foreach (Match match in matches)
        {
            int value = int.Parse(match.Groups[1].Value);
            string key = match.Groups[2].Value.ToLowerInvariant() switch
            {
                "y" => "BetterStore.Duration.Years",
                "d" => "BetterStore.Duration.Days",
                "h" => "BetterStore.Duration.Hours",
                "m" => "BetterStore.Duration.Minutes",
                "s" => "BetterStore.Duration.Seconds",
                _ => ""
            };
            if (!string.IsNullOrEmpty(key))
                parts.Add(_core.Localizer[key, value]);
        }

        return parts.Count > 0 ? $" ({string.Join(" ", parts)})" : "";
    }
}
