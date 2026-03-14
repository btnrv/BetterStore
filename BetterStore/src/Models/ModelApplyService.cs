using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using BetterStore.Config;
using BetterStore.Items;
using BetterStore.Items.Modules;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace BetterStore.Models;

public class ModelApplyService
{
    private readonly ISwiftlyCore _core;
    private readonly BetterStoreConfig _config;
    private readonly ItemManager _itemManager;

    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _pendingApply = new();
    private volatile int _roundGeneration;

    public bool SkinsEnabled { get; set; } = true;

    public ModelApplyService(ISwiftlyCore core, BetterStoreConfig config, ItemManager itemManager)
    {
        _core = core;
        _config = config;
        _itemManager = itemManager;
    }

    public void OnRoundEnd()
    {
        Interlocked.Increment(ref _roundGeneration);

        foreach (var kvp in _pendingApply)
            kvp.Value.Cancel();

        _pendingApply.Clear();
    }

    public void OnPlayerSpawn(IPlayer player)
    {
        if (player.PlayerPawn == null) return;

        int teamNum = player.Controller.TeamNum;
        if (teamNum < 2) return;

        ulong steamId = player.SteamID;
        int currentGen = _roundGeneration;

        if (_pendingApply.TryRemove(steamId, out var oldCts))
            oldCts.Cancel();

        var cts = _core.Scheduler.DelayBySeconds(_config.ModelApplyCooldownSeconds, () =>
        {
            if (_roundGeneration != currentGen) return;
            _pendingApply.TryRemove(steamId, out _);

            if (SkinsEnabled)
                ApplyModel(player, teamNum);
            else
                ApplyDefaultModel(player, teamNum);
        });

        _pendingApply[steamId] = cts;
    }

    public void ForceDefaultAll()
    {
        SkinsEnabled = false;

        foreach (var kvp in _pendingApply)
            kvp.Value.Cancel();
        _pendingApply.Clear();

        foreach (var player in _core.PlayerManager.GetAlive())
        {
            int teamNum = player.Controller.TeamNum;
            if (teamNum < 2) continue;
            ApplyDefaultModel(player, teamNum);
        }
    }

    public void ForceReapplyAll()
    {
        SkinsEnabled = true;

        foreach (var kvp in _pendingApply)
            kvp.Value.Cancel();
        _pendingApply.Clear();

        foreach (var player in _core.PlayerManager.GetAlive())
        {
            if (!player.IsAlive) continue;
            int teamNum = player.Controller.TeamNum;
            if (teamNum < 2) continue;
            ApplyModel(player, teamNum);
        }
    }

    private void ApplyModel(IPlayer player, int teamNum)
    {
        var skinResult = Item_PlayerSkin.GetModelForPlayer(player, teamNum, _itemManager);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (player is null || !player.IsValid || player.IsFakeClient) return;
            var pawn = player.PlayerPawn;
            if (pawn is null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            if (skinResult.HasValue)
                pawn.SetModel(skinResult.Value.model);
            else
                ApplyDefaultModelInner(pawn, teamNum);
        });
    }

    private void ApplyDefaultModel(IPlayer player, int teamNum)
    {
        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (player is null || !player.IsValid || player.IsFakeClient) return;
            var pawn = player.PlayerPawn;
            if (pawn is null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            ApplyDefaultModelInner(pawn, teamNum);
        });
    }

    private void ApplyDefaultModelInner(CCSPlayerPawn pawn, int teamNum)
    {
        List<string> defaults = teamNum == 2
            ? _config.DefaultModels.Terrorist
            : _config.DefaultModels.CounterTerrorist;

        if (defaults.Count == 0) return;
        pawn.SetModel(defaults[Random.Shared.Next(defaults.Count)]);
    }
}
