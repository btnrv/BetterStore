using System;
using System.Collections.Generic;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace BetterStore.Items.Modules;

[StoreItemType("playerskin")]
public class Item_PlayerSkin : IItemModule, IModuleInitializable
{
    private const float PreviewDurationSeconds = 8f;
    private const float PreviewDistance = 75f;
    private const float PreviewRotateIntervalSeconds = 0.05f;

    private static readonly object PreviewSync = new();
    private static readonly Dictionary<int, PlayerModelPreviewState> PreviewStateByPlayerId = new();
    private static long _previewSessionCounter;

    private readonly record struct PlayerModelPreviewState(long SessionId, uint EntityIndex);

    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;
    private BetterStoreConfig _config = null!;

    public static bool ForceModelDefault { get; set; }

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;
        _config = config;
    }

    public void OnPluginStart() { }

    public void OnMapStart()
    {
        ForceModelDefault = false;
        ClearAllPreviewEntities(_core);
    }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        if (ForceModelDefault) return true;

        if (!item.TryGetValue("model", out string? model) || string.IsNullOrEmpty(model))
            return false;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid || !player.IsAlive) return true;

        int teamNum = player.Controller.TeamNum;
        if (teamNum < 2 || !ItemTeamHelper.AppliesToTeam(item, teamNum))
            return true;

        _core.Scheduler.NextWorldUpdate(() => ApplyModelToPawn(player, model, item));
        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        if (!update || ForceModelDefault) return true;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid || !player.IsAlive) return true;

        int teamNum = player.Controller.TeamNum;
        if (teamNum < 2) return true;

        var fallback = GetModelForPlayer(player, teamNum, _itemManager);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            if (!player.IsValid || !player.IsAlive || player.PlayerPawn is not { IsValid: true } p)
                return;

            if (fallback.HasValue)
            {
                p.SetModel(fallback.Value.model);
                if (!string.IsNullOrEmpty(fallback.Value.skin))
                    p.AcceptInput<string>("Skin", fallback.Value.skin);
            }
            else
            {
                List<string> defaults = teamNum == 2
                    ? _config.DefaultModels.Terrorist
                    : _config.DefaultModels.CounterTerrorist;

                if (defaults.Count > 0)
                    p.SetModel(defaults[Random.Shared.Next(defaults.Count)]);
            }
        });

        return true;
    }

    private void ApplyModelToPawn(IPlayer player, string model, Dictionary<string, string> item)
    {
        if (!player.IsValid || !player.IsAlive) return;
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return;

        pawn.SetModel(model);

        if (item.TryGetValue("skin", out string? skin) && !string.IsNullOrEmpty(skin))
            pawn.AcceptInput<string>("Skin", skin);
    }

    public static (string model, string? skin)? GetModelForPlayer(IPlayer player, int teamNum, ItemManager itemManager)
    {
        if (ForceModelDefault) return null;

        var equipped = itemManager.GetPlayerEquipments(player, "playerskin");
        if (equipped.Count == 0) return null;

        (string model, string? skin)? teamSpecific = null;
        (string model, string? skin)? anyTeam = null;

        foreach (var equip in equipped)
        {
            var item = itemManager.GetItem(equip.ItemId);
            if (item == null) continue;

            string model = item.GetValueOrDefault("model", "");
            if (string.IsNullOrEmpty(model)) continue;

            string? skin = item.GetValueOrDefault("skin");
            int itemTeam = ItemTeamHelper.ResolvePlayerskinTeamSlot(item);

            if (itemTeam == teamNum && teamSpecific == null)
                teamSpecific = (model, skin);
            else if (itemTeam == ItemTeamHelper.AnyTeam && anyTeam == null)
                anyTeam = (model, skin);
        }

        return teamSpecific ?? anyTeam;
    }

    public static void Inspect(ISwiftlyCore core, IPlayer player, string model, string? skin,
        Dictionary<ulong, DateTime>? cooldowns, float cooldownSeconds)
    {
        if (string.IsNullOrWhiteSpace(model)) return;

        if (cooldowns != null)
        {
            ulong steamId = player.SteamID;
            if (cooldowns.TryGetValue(steamId, out var lastInspect) &&
                (DateTime.UtcNow - lastInspect).TotalSeconds < cooldownSeconds)
                return;
            cooldowns[steamId] = DateTime.UtcNow;
        }

        core.Scheduler.NextWorldUpdate(() =>
        {
            if (!TryGetAlivePawn(player, out var pawn))
                return;

            int playerId = player.PlayerID;
            long previewSession = ReservePreviewSession(playerId, out uint previousPreviewEntityIndex);
            if (previousPreviewEntityIndex != 0)
                DespawnPreviewEntityByIndex(core, previousPreviewEntityIndex);

            CDynamicProp? preview = null;
            try
            {
                var origin = pawn.AbsOrigin ?? Vector.Zero;
                var rotation = pawn.AbsRotation ?? QAngle.Zero;
                float yawRadians = rotation.Yaw * (MathF.PI / 180f);
                var previewPosition = new Vector(
                    origin.X + (MathF.Cos(yawRadians) * PreviewDistance),
                    origin.Y + (MathF.Sin(yawRadians) * PreviewDistance),
                    origin.Z
                );

                float initialYaw = NormalizeYaw(rotation.Yaw + 180f);
                var previewRotation = new QAngle(rotation.Pitch, initialYaw, rotation.Roll);

                preview = core.EntitySystem.CreateEntityByDesignerName<CDynamicProp>("prop_dynamic_override");
                if (preview is null || !preview.IsValid)
                {
                    ClearPreviewSessionIfCurrent(playerId, previewSession);
                    return;
                }

                preview.Teleport(previewPosition, previewRotation, Vector.Zero);
                preview.DispatchSpawn();
                preview.SetModel(model);
                if (!string.IsNullOrWhiteSpace(skin))
                    preview.AcceptInput<string>("Skin", skin);

                ConfigurePreviewVisibility(preview, player);
                ConfigurePreviewGlow(preview);
                SetPreviewEntityIndex(playerId, previewSession, preview.Index);
                RotatePreviewModel(core, playerId, previewSession, preview, previewPosition, previewRotation);
            }
            catch
            {
                ClearPreviewSessionIfCurrent(playerId, previewSession);
                return;
            }

            core.Scheduler.DelayBySeconds(PreviewDurationSeconds, () =>
            {
                core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!IsCurrentPreviewSession(playerId, previewSession))
                        return;
                    try
                    {
                        if (preview is not null && preview.IsValid)
                            preview.Despawn();
                    }
                    catch { }
                    finally
                    {
                        ClearPreviewSessionIfCurrent(playerId, previewSession);
                    }
                });
            });
        });
    }

    private static long ReservePreviewSession(int playerId, out uint previousEntityIndex)
    {
        lock (PreviewSync)
        {
            previousEntityIndex = 0;
            if (PreviewStateByPlayerId.TryGetValue(playerId, out var previousState))
                previousEntityIndex = previousState.EntityIndex;

            long session = ++_previewSessionCounter;
            PreviewStateByPlayerId[playerId] = new PlayerModelPreviewState(session, 0);
            return session;
        }
    }

    private static void SetPreviewEntityIndex(int playerId, long sessionId, uint entityIndex)
    {
        lock (PreviewSync)
        {
            if (PreviewStateByPlayerId.TryGetValue(playerId, out var state) && state.SessionId == sessionId)
                PreviewStateByPlayerId[playerId] = state with { EntityIndex = entityIndex };
        }
    }

    private static bool IsCurrentPreviewSession(int playerId, long sessionId)
    {
        lock (PreviewSync)
            return PreviewStateByPlayerId.TryGetValue(playerId, out var state) && state.SessionId == sessionId;
    }

    private static void ClearPreviewSessionIfCurrent(int playerId, long sessionId)
    {
        lock (PreviewSync)
        {
            if (PreviewStateByPlayerId.TryGetValue(playerId, out var state) && state.SessionId == sessionId)
                PreviewStateByPlayerId.Remove(playerId);
        }
    }

    private static void DespawnPreviewEntityByIndex(ISwiftlyCore core, uint entityIndex)
    {
        if (entityIndex == 0) return;
        try
        {
            var existing = core.EntitySystem.GetEntityByIndex<CDynamicProp>(entityIndex);
            if (existing is not null && existing.IsValid)
                existing.Despawn();
        }
        catch { }
    }

    private static void ClearAllPreviewEntities(ISwiftlyCore core)
    {
        uint[] entityIndexes;
        lock (PreviewSync)
        {
            entityIndexes = PreviewStateByPlayerId.Values
                .Select(static s => s.EntityIndex)
                .Where(static i => i != 0)
                .ToArray();
            PreviewStateByPlayerId.Clear();
        }
        foreach (uint idx in entityIndexes)
            DespawnPreviewEntityByIndex(core, idx);
    }

    private static void ConfigurePreviewVisibility(CDynamicProp preview, IPlayer previewOwner)
    {
        if (preview is null || !preview.IsValid || previewOwner is null || !previewOwner.IsValid) return;
        if (previewOwner.PlayerID < 0) return;
        try
        {
            preview.SetTransmitState(false);
            preview.SetTransmitState(true, previewOwner.PlayerID);
        }
        catch { }
    }

    private static void ConfigurePreviewGlow(CDynamicProp preview)
    {
        if (preview is null || !preview.IsValid) return;
        try
        {
            preview.InitialGlowState = 1;
            preview.GlowRangeMin = 0;
            preview.GlowRange = 2048;
            preview.GlowTeam = -1;
            preview.GlowColor = new Color(255, 180, 40, 255);

            var glow = preview.Glow;
            glow.GlowType = 3;
            glow.GlowTeam = -1;
            glow.GlowRangeMin = 0;
            glow.GlowRange = 2048;
            glow.GlowColorOverride = new Color(255, 180, 40, 255);
            glow.GlowColorOverrideUpdated();
            glow.GlowTypeUpdated();
            glow.GlowTeamUpdated();
            glow.GlowRangeMinUpdated();
            glow.GlowRangeUpdated();
            glow.EligibleForScreenHighlight = true;
            glow.EligibleForScreenHighlightUpdated();
            glow.Glowing = true;
            preview.GlowUpdated();
        }
        catch { }
    }

    private static void RotatePreviewModel(ISwiftlyCore core, int playerId, long previewSession,
        CDynamicProp preview, Vector origin, QAngle initialRotation)
    {
        int steps = (int)MathF.Ceiling(PreviewDurationSeconds / PreviewRotateIntervalSeconds);
        if (steps <= 0) return;

        for (int step = 1; step <= steps; step++)
        {
            int currentStep = step;
            float targetYaw = NormalizeYaw(initialRotation.Yaw + (360f * currentStep / steps));

            core.Scheduler.DelayBySeconds(currentStep * PreviewRotateIntervalSeconds, () =>
            {
                core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!IsCurrentPreviewSession(playerId, previewSession)) return;
                    if (preview is null || !preview.IsValid) return;
                    try { preview.Teleport(origin, new QAngle(initialRotation.Pitch, targetYaw, initialRotation.Roll), Vector.Zero); }
                    catch { }
                });
            });
        }
    }

    private static float NormalizeYaw(float yaw)
    {
        float normalized = yaw % 360f;
        return normalized < 0f ? normalized + 360f : normalized;
    }

    private static bool TryGetAlivePawn(IPlayer player, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (player is null || !player.IsValid || player.IsFakeClient) return false;
        var playerPawn = player.PlayerPawn;
        if (playerPawn is null || !playerPawn.IsValid || playerPawn.LifeState != (int)LifeState_t.LIFE_ALIVE) return false;
        pawn = playerPawn;
        return true;
    }
}
