using System;
using System.Linq;
using System.Threading.Tasks;
using Economy.Contract;
using Microsoft.Extensions.Configuration;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Commands;
using Microsoft.Extensions.Logging;
using Wage.Config;

namespace Wage;

[PluginMetadata(Id = "wage", Name = "Wage", Version = "1.0.0", Author = "BetterStore")]
public class WagePlugin : BasePlugin
{
    private readonly ISwiftlyCore _core;
    private IEconomyAPIv1? _economy;
    private WageConfig _config = new();

    public WagePlugin(ISwiftlyCore core) : base(core) => _core = core;

    public override void Load(bool hotReload)
    {
        LoadConfig();
        EnsureTable();

        foreach (var cmd in _config.Commands)
            _core.Command.RegisterCommand(cmd, OnWageCommand);
    }
    public override void Unload()
    {
        foreach (var cmd in _config.Commands)
            _core.Command.UnregisterCommand(cmd);
    }
    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.HasSharedInterface("Economy.API.v1"))
            _economy = interfaceManager.GetSharedInterface<IEconomyAPIv1>("Economy.API.v1");
    }

    private void LoadConfig()
    {
        _core.Configuration.InitializeJsonWithModel<WageConfig>("Wage.jsonc", "wage")
            .Configure(builder =>
            {
                builder.AddJsonFile(_core.Configuration.GetConfigPath("Wage.jsonc"), optional: false, reloadOnChange: true);
            });

        var section = _core.Configuration.Manager.GetSection("wage");
        var loaded = new WageConfig();
        section.Bind(loaded);
        _config = loaded;
    }

    private void EnsureTable()
    {
        try
        {
            using var conn = _core.Database.GetConnection(_config.DatabaseConnection);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS wage_claims (
                    steam_id BIGINT UNSIGNED PRIMARY KEY,
                    last_claim DATETIME NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Wage] Failed to create table: {ex.Message}");
        }
    }

    private string Prefix => _core.Localizer["Wage.Prefix"];

    private void Reply(IPlayer player, string key, params object[] args)
    {
        player.SendChat($"{Prefix}{_core.Localizer[key, args]}");
    }

    private IPlayer? FindPlayer(ulong steamId)
    {
        return _core.PlayerManager.GetAllPlayers()
            .FirstOrDefault(p => !p.IsFakeClient && p.SteamID == steamId);
    }

    private WageTierConfig? ResolveTier(IPlayer player)
    {
        foreach (var tier in _config.Tiers)
        {
            if (string.IsNullOrEmpty(tier.Permission))
                return tier;
            if (_core.Permission.PlayerHasPermission(player.SteamID, tier.Permission))
                return tier;
        }
        return null;
    }

    private void OnWageCommand(ICommandContext ctx)
    {
        if (_economy == null) return;
        var player = ctx.Sender;
        if (player == null) return;

        var tier = ResolveTier(player);
        if (tier == null)
        {
            Reply(player, "Wage.NoTier");
            return;
        }

        ulong steamId = player.SteamID;
        int amount = tier.Amount;

        Task.Run(() =>
        {
            try
            {
                DateTime? lastClaim = GetLastClaimFromDb(steamId);

                _core.Scheduler.NextTick(() =>
                {
                    var p = FindPlayer(steamId);
                    if (p == null) return;

                    if (lastClaim.HasValue)
                    {
                        TimeSpan elapsed = DateTime.UtcNow - lastClaim.Value;
                        TimeSpan cooldown = TimeSpan.FromHours(_config.CooldownHours);
                        if (elapsed < cooldown)
                        {
                            TimeSpan remaining = cooldown - elapsed;
                            Reply(p, "Wage.Cooldown", (int)remaining.TotalHours, remaining.Minutes);
                            return;
                        }
                    }

                    _economy?.AddPlayerBalance(p, _config.WalletType, amount);
                    Reply(p, "Wage.Claimed", amount);

                    Task.Run(() =>
                    {
                        try
                        {
                            SetLastClaimInDb(steamId, DateTime.UtcNow);
                        }
                        catch (Exception ex)
                        {
                            _core.Logger.LogError($"[Wage] Failed to save claim for {steamId}: {ex.Message}");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                _core.Logger.LogError($"[Wage] Error reading claim for {steamId}: {ex.Message}");
                _core.Scheduler.NextTick(() =>
                {
                    var p = FindPlayer(steamId);
                    if (p != null) Reply(p, "Wage.Error");
                });
            }
        });
    }

    private DateTime? GetLastClaimFromDb(ulong steamId)
    {
        using var conn = _core.Database.GetConnection(_config.DatabaseConnection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_claim FROM wage_claims WHERE steam_id = @sid";
        var param = cmd.CreateParameter();
        param.ParameterName = "@sid";
        param.Value = steamId;
        cmd.Parameters.Add(param);

        var result = cmd.ExecuteScalar();
        return result is DateTime dt ? dt : null;
    }

    private void SetLastClaimInDb(ulong steamId, DateTime time)
    {
        using var conn = _core.Database.GetConnection(_config.DatabaseConnection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO wage_claims (steam_id, last_claim) VALUES (@sid, @time)
            ON DUPLICATE KEY UPDATE last_claim = @time";

        var pSid = cmd.CreateParameter();
        pSid.ParameterName = "@sid";
        pSid.Value = steamId;
        cmd.Parameters.Add(pSid);

        var pTime = cmd.CreateParameter();
        pTime.ParameterName = "@time";
        pTime.Value = time;
        cmd.Parameters.Add(pTime);

        cmd.ExecuteNonQuery();
    }
}
