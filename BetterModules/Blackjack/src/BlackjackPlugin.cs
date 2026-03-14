using System;
using System.Collections.Concurrent;
using System.Linq;
using Blackjack.Config;
using Blackjack.Game;
using Economy.Contract;
using Microsoft.Extensions.Configuration;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Commands;

namespace Blackjack;

[PluginMetadata(Id = "blackjack", Name = "Blackjack", Version = "1.0.0", Author = "BetterStore")]
public class BlackjackPlugin : BasePlugin
{
    private readonly ISwiftlyCore _core;
    private IEconomyAPIv1? _economy;
    private BlackjackConfig _config = new();

    private readonly ConcurrentDictionary<ulong, BlackjackGameState> _activeGames = new();

    public BlackjackPlugin(ISwiftlyCore core) : base(core) => _core = core;

    public override void Load(bool hotReload)
    {
        LoadConfig();

        foreach (var cmd in _config.Commands)
            _core.Command.RegisterCommand(cmd, OnBlackjackCommand);

        _core.Command.RegisterCommand(_config.HitCommand, OnHitCommand);
        _core.Command.RegisterCommand(_config.StandCommand, OnStandCommand);

        _core.Event.OnClientDisconnected += (@event) => {
            var p = _core.PlayerManager.GetPlayer(@event.PlayerId);
            if (p == null) return;
            ulong steamId = p.SteamID;
            if (_activeGames.TryRemove(steamId, out var game))
            {
                game.Timer?.Cancel();
                _economy?.AddPlayerBalance(steamId, _config.WalletType, game.BetAmount);
            }
        };
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.HasSharedInterface("Economy.API.v1"))
            _economy = interfaceManager.GetSharedInterface<IEconomyAPIv1>("Economy.API.v1");
    }

    private void LoadConfig()
    {
        _core.Configuration.InitializeJsonWithModel<BlackjackConfig>("Blackjack.jsonc", "blackjack")
            .Configure(builder =>
            {
                builder.AddJsonFile(_core.Configuration.GetConfigPath("Blackjack.jsonc"), optional: false, reloadOnChange: true);
            });

        var section = _core.Configuration.Manager.GetSection("blackjack");
        var loaded = new BlackjackConfig();
        section.Bind(loaded);
        _config = loaded;
    }

    private string Prefix => _core.Localizer["Blackjack.Prefix"];

    private void Reply(IPlayer player, string key, params object[] args)
    {
        player.SendChat($"{Prefix}{_core.Localizer[key, args]}");
    }

    private IPlayer? FindPlayer(ulong steamId)
    {
        return _core.PlayerManager.GetAllPlayers()
            .FirstOrDefault(p => !p.IsFakeClient && p.SteamID == steamId);
    }

    private void OnBlackjackCommand(ICommandContext ctx)
    {
        if (_economy == null) return;
        var player = ctx.Sender;
        if (player == null) return;
        var args = ctx.Args;

        if (args.Length < 1 || !int.TryParse(args[0], out int betAmount) || betAmount <= 0)
        {
            Reply(player, "Blackjack.Usage", _config.MinBet, _config.MaxBet);
            return;
        }

        if (betAmount < _config.MinBet) { Reply(player, "Blackjack.BelowMin", _config.MinBet); return; }
        if (betAmount > _config.MaxBet) { Reply(player, "Blackjack.AboveMax", _config.MaxBet); return; }

        ulong steamId = player.SteamID;

        if (_activeGames.ContainsKey(steamId))
        {
            Reply(player, "Blackjack.AlreadyPlaying");
            return;
        }

        if (!_economy.HasSufficientFunds(player, _config.WalletType, betAmount))
        {
            int balance = (int)(_economy.GetPlayerBalance(player, _config.WalletType));
            Reply(player, "Blackjack.InsufficientFunds", balance);
            return;
        }

        _economy.SubtractPlayerBalance(player, _config.WalletType, betAmount);
        StartGame(player, steamId, betAmount);
    }

    private void OnHitCommand(ICommandContext ctx)
    {
        var player = ctx.Sender;
        if (player == null) return;
        var args = ctx.Args;
        ulong steamId = player.SteamID;
        if (!_activeGames.TryGetValue(steamId, out var game))
        {
            Reply(player, "Blackjack.NoActiveGame");
            return;
        }

        game.PlayerHand.Add(BlackjackDeck.Draw(game));
        int playerTotal = BlackjackDeck.CalculateHand(game.PlayerHand);

        ResetTimer(steamId, game);
        Reply(player, "Blackjack.Hit");

        if (playerTotal > 21)
        {
            Reply(player, "Blackjack.Bust");
            ShowEndGame(player, steamId, game,
                _core.Localizer["Blackjack.Html.LoseTitle"],
                _config.LoseColor,
                _core.Localizer["Blackjack.Html.DealerWins"]);
        }
    }

    private void OnStandCommand(ICommandContext ctx)
    {
        var player = ctx.Sender;
        if (player == null) return;
        var args = ctx.Args;
        ulong steamId = player.SteamID;
        if (!_activeGames.TryGetValue(steamId, out var game))
        {
            Reply(player, "Blackjack.NoActiveGame");
            return;
        }

        while (BlackjackDeck.CalculateHand(game.DealerHand) < 17)
            game.DealerHand.Add(BlackjackDeck.Draw(game));

        int playerTotal = BlackjackDeck.CalculateHand(game.PlayerHand);
        int dealerTotal = BlackjackDeck.CalculateHand(game.DealerHand);

        Reply(player, "Blackjack.Stand");

        if (dealerTotal > 21 || playerTotal > dealerTotal)
        {
            int payout = game.BetAmount * 2;
            _economy?.AddPlayerBalance(player, _config.WalletType, payout);
            Reply(player, "Blackjack.Win", payout);
            ShowEndGame(player, steamId, game,
                _core.Localizer["Blackjack.Html.WinTitle"],
                _config.WinColor,
                $"{_core.Localizer["Blackjack.Html.Prize"]}: {payout}");
        }
        else if (playerTotal == dealerTotal)
        {
            _economy?.AddPlayerBalance(player, _config.WalletType, game.BetAmount);
            Reply(player, "Blackjack.Draw", game.BetAmount);
            ShowEndGame(player, steamId, game,
                _core.Localizer["Blackjack.Html.DrawTitle"],
                _config.DrawColor,
                _core.Localizer["Blackjack.Html.Refunded"]);
        }
        else
        {
            Reply(player, "Blackjack.Lose");
            ShowEndGame(player, steamId, game,
                _core.Localizer["Blackjack.Html.LoseTitle"],
                _config.LoseColor,
                _core.Localizer["Blackjack.Html.DealerWins"]);
        }
    }

    private void StartGame(IPlayer player, ulong steamId, int betAmount)
    {
        var game = new BlackjackGameState
        {
            BetAmount = betAmount,
            SecondsRemaining = _config.InactivityTimeoutSeconds
        };

        game.Deck.AddRange(BlackjackDeck.Create());
        game.PlayerHand.Add(BlackjackDeck.Draw(game));
        game.PlayerHand.Add(BlackjackDeck.Draw(game));
        game.DealerHand.Add(BlackjackDeck.Draw(game));

        _activeGames[steamId] = game;

        var timer = _core.Scheduler.RepeatBySeconds(1f, () => OnPlayerTick(steamId));
        _core.Scheduler.StopOnMapChange(timer);
        game.Timer = timer;

        Reply(player, "Blackjack.GameStarted", betAmount);
        player.SendCenterHTML(BuildGameHtml(game), 1500);
    }

    private void OnPlayerTick(ulong steamId)
    {
        if (!_activeGames.TryGetValue(steamId, out var game)) return;

        var player = FindPlayer(steamId);

        game.SecondsRemaining--;

        if (game.SecondsRemaining <= 0)
        {
            game.Timer?.Cancel();
            _activeGames.TryRemove(steamId, out _);

            if (player != null)
            {
                Reply(player, "Blackjack.Timeout");
                string timeoutHtml = BuildEndGameHtml(game,
                    _core.Localizer["Blackjack.Html.LoseTitle"],
                    _config.LoseColor,
                    _core.Localizer["Blackjack.Html.Timeout"]);
                player.SendCenterHTML(timeoutHtml, 5000);
            }
            return;
        }

        if (player != null)
            player.SendCenterHTML(BuildGameHtml(game), 1000);
    }

    private void ResetTimer(ulong steamId, BlackjackGameState game)
    {
        game.Timer?.Cancel();
        game.SecondsRemaining = _config.InactivityTimeoutSeconds;
        var timer = _core.Scheduler.RepeatBySeconds(1f, () => OnPlayerTick(steamId));
        _core.Scheduler.StopOnMapChange(timer);
        game.Timer = timer;
    }

    private void ShowEndGame(IPlayer player, ulong steamId, BlackjackGameState game, string title, string color, string message)
    {
        game.Timer?.Cancel();
        _activeGames.TryRemove(steamId, out _);

        string html = BuildEndGameHtml(game, title, color, message);
        player.SendCenterHTML(html, 5000);
    }

    private string BuildGameHtml(BlackjackGameState game)
    {
        string playerCards = HandToHtml(game.PlayerHand, game.PlayerHand);
        string dealerCards = $"{HandToHtml([game.DealerHand[0]], game.DealerHand)} <img src='{BlackjackDeck.CardBackUrl}' width='50'>";

        string title = _core.Localizer["Blackjack.Html.Title"];
        string bet = _core.Localizer["Blackjack.Html.Bet"];
        string dealer = _core.Localizer["Blackjack.Html.Dealer"];
        string you = _core.Localizer["Blackjack.Html.You"];
        string hit = _core.Localizer["Blackjack.Html.HitCmd"];
        string stand = _core.Localizer["Blackjack.Html.StandCmd"];
        string timeLabel = _core.Localizer["Blackjack.Html.Time"];

        return $@"<center>
<font color='{_config.AccentColor}'><b>{title}</b></font> <font color='#d255dc'>({bet}: {game.BetAmount})</font><br>
<font color='{_config.DealerColor}'><b>{dealer}:</b><br>{dealerCards}<br></font>
<font color='{_config.PlayerColor}'><b>{you}:</b><br>{playerCards}</font><br>
<font color='#61dc55' class='fontSize-l'>!{_config.HitCommand}: {hit}</font> <font color='{_config.DealerColor}' class='fontSize-l'>!{_config.StandCommand}: {stand}</font><br>
<font color='#aaaaaa'>{timeLabel}: {game.SecondsRemaining}s</font>
</center>";
    }

    private string BuildEndGameHtml(BlackjackGameState game, string resultTitle, string resultColor, string resultMessage)
    {
        string playerCards = HandToHtml(game.PlayerHand, game.PlayerHand);
        string dealerCards = HandToHtml(game.DealerHand, game.DealerHand);

        string title = _core.Localizer["Blackjack.Html.Title"];
        string bet = _core.Localizer["Blackjack.Html.Bet"];
        string dealer = _core.Localizer["Blackjack.Html.Dealer"];
        string you = _core.Localizer["Blackjack.Html.You"];

        return $@"<center>
<font color='{_config.AccentColor}'><b>{title}</b></font> <font color='#d255dc'>({bet}: {game.BetAmount})</font><br>
<font color='{_config.DealerColor}'><b>{dealer}:</b><br>{dealerCards}<br></font>
<font color='{_config.PlayerColor}'><b>{you}:</b><br>{playerCards}</font><br>
<font color='{resultColor}' class='fontSize-l'>{resultTitle} — {resultMessage}</font>
</center>";
    }

    private string HandToHtml(System.Collections.Generic.List<Card> visibleCards, System.Collections.Generic.List<Card> fullHand)
    {
        string cardsHtml = string.Join(" ", visibleCards.Select(c => $"<img src='{BlackjackDeck.GetCardImageUrl(c)}' width='50'>"));

        string valuesHtml;
        if (visibleCards.Count == 1)
        {
            valuesHtml = $"({BlackjackDeck.GetCardValue(visibleCards[0], fullHand)})";
        }
        else
        {
            int total = BlackjackDeck.CalculateHand(fullHand);
            string parts = string.Join(" + ", visibleCards.Select(c => BlackjackDeck.GetCardValue(c, fullHand).ToString()));
            valuesHtml = $"({parts} = {total})";
        }

        return $"{cardsHtml} <font color='{_config.ValueColor}'>{valuesHtml}</font>";
    }

    public override void Unload()
    {
        foreach (var (steamId, game) in _activeGames)
        {
            game.Timer?.Cancel();
            _economy?.AddPlayerBalance(steamId, _config.WalletType, game.BetAmount);
        }
        _activeGames.Clear();
    }
}
