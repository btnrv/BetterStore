using System;
using System.Collections.Generic;
using System.Linq;
using BetterStore.Database;
using BetterStore.Items;
using BetterStore.Log;
using BetterStore.Menus;
using BetterStore.Models;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Economy.Contract;

namespace BetterStore.Commands;

public class BetterStoreCommands
{
    private readonly ISwiftlyCore _core;
    public IEconomyAPIv1? Economy { get; set; }
    private readonly Config.BetterStoreConfig _config;
    private readonly BetterStoreMenus _menus;
    private readonly ModelApplyService _modelService;
    private readonly ItemManager _itemManager;
    private readonly BetterStoreDB _db;

    public BetterStoreCommands(ISwiftlyCore core, Config.BetterStoreConfig config,
        BetterStoreMenus menus, ModelApplyService modelService, ItemManager itemManager, BetterStoreDB db)
    {
        _core = core;
        _config = config;
        _menus = menus;
        _modelService = modelService;
        _itemManager = itemManager;
        _db = db;
    }

    public void RegisterCommands()
    {
        RegisterAliases(_config.AdminCommands.GiveCurrency, OnGiveCurrency);
        RegisterAliases(_config.AdminCommands.RemoveCurrency, OnRemoveCurrency);
        RegisterAliases(_config.AdminCommands.ResetPlayer, OnResetPlayer);
        RegisterAliases(_config.AdminCommands.ResetDatabase, OnResetDatabase);
        RegisterAliases(_config.AdminCommands.Model0, OnModel0);
        RegisterAliases(_config.AdminCommands.Model1, OnModel1);

        RegisterAliases(_config.MenuCommands, OnStoreMenu);
        RegisterAliases(_config.InventoryCommands, OnInventoryMenu);
        RegisterAliases(_config.GiftCommands, OnGift);
        RegisterAliases(_config.RichCommands, OnRich);

        foreach (var (currencyName, currencyConfig) in _config.Currencies)
        {
            string captured = currencyName;
            RegisterAliases(currencyConfig.BalanceCommands, (ctx) => OnCheckBalance(ctx, captured));
        }
    }

    private void RegisterAliases(List<string> aliases, ICommandService.CommandListener handler)
    {
        if (aliases == null || aliases.Count == 0) return;

        string baseCommand = CleanPrefix(aliases[0]);
        _core.Command.RegisterCommand(baseCommand, handler);

        for (int i = 1; i < aliases.Count; i++)
            _core.Command.RegisterCommandAlias(baseCommand, CleanPrefix(aliases[i]));
    }

    private static string CleanPrefix(string cmd) => cmd.StartsWith("sw_") ? cmd[3..] : cmd;

    private void Reply(ICommandContext context, string key, params object[] args)
    {
        context.Reply($"{_config.PluginPrefix}{_core.Localizer[key, args]}");
    }

    private void Broadcast(string key, params object[] args)
    {
        string msg = $"{_config.PluginPrefix}{_core.Localizer[key, args]}";
        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(p => !p.IsFakeClient))
        {
            p.SendChat(msg);
        }
    }

    private void Reply(IPlayer player, string key, params object[] args)
    {
        player.SendChat($"{_config.PluginPrefix}{_core.Localizer[key, args]}");
    }

    private bool CheckPermission(ICommandContext context, string permission)
    {
        if (context.Sender == null) return true;
        if (string.IsNullOrWhiteSpace(permission)) return true;

        if (!_core.Permission.PlayerHasPermission(context.Sender.SteamID, permission))
        {
            Reply(context, "BetterStore.Admin.NoPermission");
            return false;
        }
        return true;
    }

    private List<IPlayer> ResolveTargets(ICommandContext context, string targetArg)
    {
        IEnumerable<IPlayer> targets;
        if (context.Sender != null)
        {
            targets = _core.PlayerManager.FindTargettedPlayers(
                context.Sender, targetArg,
                TargetSearchMode.IncludeSelf | TargetSearchMode.NoBots);
        }
        else
        {
            var all = _core.PlayerManager.GetAllPlayers().Where(p => !p.IsFakeClient);
            targets = targetArg is "@all" or "@human" or "@!bots"
                ? all
                : all.Where(p => p.Controller.PlayerName.Contains(targetArg, StringComparison.OrdinalIgnoreCase));
        }
        return targets.ToList();
    }

    private void OnGiveCurrency(ICommandContext context)
    {
        if (!CheckPermission(context, _config.Permissions.AdminCommands.GiveCurrency)) return;

        if (context.Args.Length < 2) { Reply(context, "BetterStore.Admin.GiveCurrency.Usage"); return; }
        if (!int.TryParse(context.Args[1], out var amount)) { Reply(context, "BetterStore.InvalidAmount"); return; }

        string currency = context.Args.Length > 2 ? context.Args[2] : _config.DefaultCurrency;
        if (!_config.Currencies.ContainsKey(currency))
        {
            Reply(context, "BetterStore.InvalidCurrency", currency);
            return;
        }

        var matched = ResolveTargets(context, context.Args[0]);
        if (matched.Count == 0) { Reply(context, "BetterStore.PlayerNotFound", context.Args[0]); return; }

        foreach (var target in matched)
        {
            Economy?.AddPlayerBalance(target, currency, amount);
            Reply(target, "BetterStore.GiveCurrencyReceived", amount, currency);
        }

        string label = matched.Count == 1 ? matched[0].Controller.PlayerName : context.Args[0];
        string adminName = context.Sender?.Controller.PlayerName ?? "Console";
        Broadcast("BetterStore.Admin.GiveCurrency.Success", adminName, amount, currency, label);
    }

    private void OnRemoveCurrency(ICommandContext context)
    {
        if (!CheckPermission(context, _config.Permissions.AdminCommands.RemoveCurrency)) return;

        if (context.Args.Length < 2) { Reply(context, "BetterStore.Admin.RemoveCurrency.Usage"); return; }
        if (!int.TryParse(context.Args[1], out var amount)) { Reply(context, "BetterStore.InvalidAmount"); return; }

        string currency = context.Args.Length > 2 ? context.Args[2] : _config.DefaultCurrency;

        var matched = ResolveTargets(context, context.Args[0]);
        if (matched.Count == 0) { Reply(context, "BetterStore.PlayerNotFound", context.Args[0]); return; }

        foreach (var target in matched)
            Economy?.SubtractPlayerBalance(target, currency, amount);

        string label = matched.Count == 1 ? matched[0].Controller.PlayerName : context.Args[0];
        string adminName = context.Sender?.Controller.PlayerName ?? "Console";
        Broadcast("BetterStore.Admin.RemoveCurrency.Success", adminName, amount, currency, label);
    }

    private void OnResetPlayer(ICommandContext context)
    {
        if (!CheckPermission(context, _config.Permissions.AdminCommands.ResetPlayer)) return;

        if (context.Args.Length < 1) { Reply(context, "BetterStore.Admin.ResetPlayer.Usage"); return; }

        var matched = ResolveTargets(context, context.Args[0]);
        if (matched.Count == 0) { Reply(context, "BetterStore.PlayerNotFound", context.Args[0]); return; }

        foreach (var target in matched)
        {
            _itemManager.ClearPlayerData(target.SteamID);
            _db.WipePlayer(target.SteamID);
            foreach (var kind in _config.CurrencyNames)
                Economy?.SetPlayerBalance(target, kind, 0);
        }

        string label = matched.Count == 1 ? matched[0].Controller.PlayerName : $"{matched.Count} players";
        Reply(context, "BetterStore.Admin.ResetPlayer.Success", label);
    }

    private void OnResetDatabase(ICommandContext context)
    {
        if (!CheckPermission(context, _config.Permissions.AdminCommands.ResetDatabase)) return;
        _itemManager.ClearAllPlayerData();
        _db.WipeDatabase();
        Reply(context, "BetterStore.Admin.ResetDatabase.Success");
    }

    private void OnStoreMenu(ICommandContext context)
    {
        if (context.Sender == null) { Reply(context, "BetterStore.InGameOnly"); return; }
        _menus.OpenMainMenu(context.Sender);
    }

    private void OnInventoryMenu(ICommandContext context)
    {
        if (context.Sender == null) { Reply(context, "BetterStore.InGameOnly"); return; }
        _menus.OpenInventoryMenu(context.Sender);
    }

    private void OnCheckBalance(ICommandContext context, string currencyName)
    {
        if (context.Sender == null) { Reply(context, "BetterStore.InGameOnly"); return; }

        int balance = (int)(Economy?.GetPlayerBalance(context.Sender, currencyName) ?? 0m);
        string icon = _config.GetCurrencyChatIcon(currencyName);
        Reply(context, "BetterStore.Balance", balance, icon, currencyName);
    }

    private void OnModel0(ICommandContext context)
    {
        if (!CheckPermission(context, _config.Permissions.AdminCommands.Model0)) return;
        _modelService.ForceDefaultAll();
        string adminName = context.Sender?.Controller.PlayerName ?? "Console";
        Broadcast("BetterStore.Model0", adminName);
    }

    private void OnModel1(ICommandContext context)
    {
        if (!CheckPermission(context, _config.Permissions.AdminCommands.Model1)) return;
        _modelService.ForceReapplyAll();
        string adminName = context.Sender?.Controller.PlayerName ?? "Console";
        Broadcast("BetterStore.Model1", adminName);
    }

    private void OnGift(ICommandContext context)
    {
        if (context.Sender == null) { Reply(context, "BetterStore.InGameOnly"); return; }

        if (context.Args.Length < 2) { Reply(context, "BetterStore.Gift.Usage"); return; }
        if (!int.TryParse(context.Args[1], out int amount) || amount <= 0) { Reply(context, "BetterStore.InvalidAmount"); return; }

        string currency = context.Args.Length > 2 ? context.Args[2] : _config.DefaultCurrency;

        if (!_config.Currencies.TryGetValue(currency, out var currencyConfig))
        {
            Reply(context, "BetterStore.InvalidCurrency", currency);
            return;
        }

        if (!currencyConfig.Giftable)
        {
            Reply(context, "BetterStore.Gift.NotGiftable", currency);
            return;
        }

        var matched = _core.PlayerManager.FindTargettedPlayers(
            context.Sender, context.Args[0],
            TargetSearchMode.NoBots | TargetSearchMode.NoMultipleTargets).ToList();

        if (matched.Count == 0) { Reply(context, "BetterStore.PlayerNotFound", context.Args[0]); return; }

        var target = matched[0];
        if (target.SteamID == context.Sender.SteamID)
        {
            Reply(context, "BetterStore.Gift.CannotGiftSelf");
            return;
        }

        if (Economy == null || !Economy.HasSufficientFunds(context.Sender, currency, amount))
        {
            Reply(context, "BetterStore.Gift.InsufficientFunds");
            return;
        }

        Economy.TransferFunds(context.Sender, target, currency, amount);

        string icon = _config.GetCurrencyChatIcon(currency);
        Reply(context, "BetterStore.Gift.Success.Sender", amount, icon, target.Controller.PlayerName);
        Reply(target, "BetterStore.Gift.Success.Receiver", amount, icon, context.Sender.Controller.PlayerName);

        if (_config.EnableLogging)
            StoreLog.SaveLog(
                context.Sender.Controller.PlayerName, context.Sender.SteamID.ToString(),
                target.Controller.PlayerName, target.SteamID.ToString(),
                amount, StoreLog.LogType.GiftCurrency, _core);
    }

    private void OnRich(ICommandContext context)
    {
        if (context.Sender == null) { Reply(context, "BetterStore.InGameOnly"); return; }
        _menus.OpenRichMenu(context.Sender);
    }
}
