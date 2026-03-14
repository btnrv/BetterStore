using System;
using System.Collections.Concurrent;
using System.Linq;
using Economy.Contract;
using Microsoft.Extensions.Configuration;
using Roulette.Config;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using Microsoft.Extensions.Logging;

namespace Roulette;

public enum RouletteColor { Red, Green, Black }

public record RouletteBet(ulong SteamId, string PlayerName, RouletteColor Color, int Amount);

[PluginMetadata(Id = "roulette", Name = "Roulette", Version = "1.0.0", Author = "BetterStore")]
public class RoulettePlugin : BasePlugin
{
    private readonly ISwiftlyCore _core;
    private IEconomyAPIv1? _economy;
    private RouletteConfig _config = new();

    private readonly ConcurrentDictionary<ulong, RouletteBet> _bets = new();
    private RouletteColor? _predeterminedWinner;
    private bool _betsOpen;

    public RoulettePlugin(ISwiftlyCore core) : base(core) => _core = core;

    public override void Load(bool hotReload)
    {
        LoadConfig();

        _core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        _core.GameEvent.HookPost<EventRoundMvp>(OnRoundMvp);

        foreach (var cmd in _config.RedCommands)
            _core.Command.RegisterCommand(cmd, (ctx) => PlaceBet(ctx, RouletteColor.Red));

        foreach (var cmd in _config.GreenCommands)
            _core.Command.RegisterCommand(cmd, (ctx) => PlaceBet(ctx, RouletteColor.Green));

        foreach (var cmd in _config.BlackCommands)
            _core.Command.RegisterCommand(cmd, (ctx) => PlaceBet(ctx, RouletteColor.Black));
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.HasSharedInterface("Economy.API.v1"))
            _economy = interfaceManager.GetSharedInterface<IEconomyAPIv1>("Economy.API.v1");
    }

    private void LoadConfig()
    {
        _core.Configuration.InitializeJsonWithModel<RouletteConfig>("Roulette.jsonc", "roulette")
            .Configure(builder =>
            {
                builder.AddJsonFile(_core.Configuration.GetConfigPath("Roulette.jsonc"), optional: false, reloadOnChange: true);
            });

        var section = _core.Configuration.Manager.GetSection("roulette");
        var loaded = new RouletteConfig();
        section.Bind(loaded);
        _config = loaded;
    }

    private string Prefix => _core.Localizer["Roulette.Prefix"];

    private void Broadcast(string key, params object[] args)
    {
        string msg = $"{Prefix}{_core.Localizer[key, args]}";
        foreach (var p in _core.PlayerManager.GetAllPlayers())
        {
            if (!p.IsFakeClient)
                p.SendChat(msg);
        }
    }

    private void Reply(IPlayer player, string key, params object[] args)
    {
        player.SendChat($"{Prefix}{_core.Localizer[key, args]}");
    }

    private string ColorName(RouletteColor color) => color switch
    {
        RouletteColor.Red => _core.Localizer["Roulette.Color.Red"],
        RouletteColor.Green => _core.Localizer["Roulette.Color.Green"],
        RouletteColor.Black => _core.Localizer["Roulette.Color.Black"],
        _ => color.ToString()
    };

    private double GetMultiplier(RouletteColor color) => color switch
    {
        RouletteColor.Red => _config.RedMultiplier,
        RouletteColor.Black => _config.BlackMultiplier,
        RouletteColor.Green => _config.GreenMultiplier,
        _ => 1.0
    };

    private static RouletteColor RollWinner()
    {
        int slot = Random.Shared.Next(15);
        if (slot == 0) return RouletteColor.Green;
        return slot <= 7 ? RouletteColor.Red : RouletteColor.Black;
    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        _bets.Clear();
        _predeterminedWinner = RollWinner();
        _betsOpen = true;

        _core.Logger.LogInformation($"[Roulette] Pre-determined winner: {_predeterminedWinner}");
        Broadcast("Roulette.BetsOpen");

        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp @event)
    {
        _betsOpen = false;
        if (_predeterminedWinner == null) return HookResult.Continue;

        var winner = _predeterminedWinner.Value;
        Broadcast("Roulette.Result", ColorName(winner));

        var winners = _bets.Values.Where(b => b.Color == winner).ToList();
        if (winners.Count == 0)
        {
            Broadcast("Roulette.NoWinners");
        }
        else
        {
            foreach (var bet in winners)
            {
                int payout = (int)(bet.Amount * GetMultiplier(winner));
                _economy?.AddPlayerBalance(bet.SteamId, _config.WalletType, payout);
                Broadcast("Roulette.Winner", bet.PlayerName, payout, ColorName(winner));
            }
        }

        _bets.Clear();
        _predeterminedWinner = null;
        return HookResult.Continue;
    }

    private void PlaceBet(ICommandContext ctx, RouletteColor color)
    {
        if (_economy == null) return;
        var player = ctx.Sender;
        if (player == null) return;
        var args = ctx.Args;

        if (!_betsOpen)
        {
            Reply(player, "Roulette.NoBetsOpen");
            return;
        }

        string colorCmd = color.ToString().ToLowerInvariant();

        if (args.Length < 1 || !int.TryParse(args[0], out int amount) || amount <= 0)
        {
            Reply(player, "Roulette.InvalidAmount", colorCmd, _config.MinBet, _config.MaxBet);
            return;
        }

        if (amount < _config.MinBet)
        {
            Reply(player, "Roulette.BelowMin", _config.MinBet);
            return;
        }

        if (amount > _config.MaxBet)
        {
            Reply(player, "Roulette.AboveMax", _config.MaxBet);
            return;
        }

        ulong steamId = player.SteamID;

        if (_bets.TryGetValue(steamId, out var existingBet) && existingBet.Color != color)
        {
            Reply(player, "Roulette.AlreadyBetOther", ColorName(existingBet.Color));
            return;
        }

        int additionalNeeded = amount;
        if (existingBet != null)
        {
            if (amount > existingBet.Amount)
            {
                additionalNeeded = amount - existingBet.Amount;
            }
            else
            {
                int refund = existingBet.Amount - amount;
                if (refund > 0)
                    _economy.AddPlayerBalance(player, _config.WalletType, refund);
                _bets[steamId] = new RouletteBet(steamId, player.Controller.PlayerName, color, amount);
                Reply(player, "Roulette.BetUpdated", amount, ColorName(color));
                return;
            }
        }

        if (!_economy.HasSufficientFunds(player, _config.WalletType, additionalNeeded))
        {
            Reply(player, "Roulette.InsufficientFunds");
            return;
        }

        _economy.SubtractPlayerBalance(player, _config.WalletType, additionalNeeded);
        _bets[steamId] = new RouletteBet(steamId, player.Controller.PlayerName, color, amount);

        Reply(player, existingBet != null ? "Roulette.BetUpdated" : "Roulette.BetPlaced", amount, ColorName(color));
    }

    public override void Unload()
    {
        if (_economy != null)
        {
            foreach (var bet in _bets.Values)
                _economy.AddPlayerBalance(bet.SteamId, _config.WalletType, bet.Amount);
        }
        _bets.Clear();
    }
}
