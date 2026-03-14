using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace BetterStore.Items.Modules;

[StoreItemType("trail")]
public class Item_Trail : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;

    private static readonly Vector[] LastOrigin = new Vector[64];
    private static readonly Vector[] EndOrigin = new Vector[64];
    private static bool _trailExists;
    private static readonly Random _random = new();

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        _trailExists = _itemManager.IsAnyItemOfType("trail");
        if (_trailExists)
        {
            for (int i = 0; i < 64; i++)
            {
                LastOrigin[i] = Vector.Zero;
                EndOrigin[i] = Vector.Zero;
            }

            core.Event.OnTick += OnTick;
        }
    }

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item) => true;
    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update) => true;

    private void OnTick()
    {
        if (!_trailExists) return;

        foreach (var player in _core.PlayerManager.GetAlive())
        {
            if (player.IsFakeClient) continue;
            ProcessPlayerTrail(player);
        }
    }

    private void ProcessPlayerTrail(IPlayer player)
    {
        var equipped = _itemManager.GetPlayerEquipments(player, "trail").FirstOrDefault();
        if (equipped == null) return;

        var itemData = _itemManager.GetItem(equipped.ItemId);
        if (itemData == null) return;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return;

        var absOrigin = pawn.AbsOrigin ?? Vector.Zero;
        int slot = player.Slot;
        if (slot < 0 || slot >= 64) return;

        // Distance check - only create trail when player has moved enough
        float dx = LastOrigin[slot].X - absOrigin.X;
        float dy = LastOrigin[slot].Y - absOrigin.Y;
        float dz = LastOrigin[slot].Z - absOrigin.Z;
        if (dx * dx + dy * dy + dz * dz <= 25.0f) return; // 5 units squared

        LastOrigin[slot] = new Vector(absOrigin.X, absOrigin.Y, absOrigin.Z);

        float lifetime = itemData.TryGetValue("lifetime", out string? ltv) && float.TryParse(ltv, CultureInfo.InvariantCulture, out float lt)
            ? lt : 1.3f;

        if (itemData.TryGetValue("color", out string? colorStr) && !string.IsNullOrEmpty(colorStr))
        {
            // Beam trail with color
            CreateBeam(player, absOrigin, lifetime, colorStr, itemData, slot);
        }
        else if (itemData.TryGetValue("model", out string? model) && !string.IsNullOrEmpty(model))
        {
            // Particle trail
            CreateParticle(player, absOrigin, lifetime, model, itemData);
        }
    }

    private void CreateBeam(IPlayer player, Vector absOrigin, float lifetime, string colorStr, Dictionary<string, string> itemData, int slot)
    {
        var beam = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>("env_beam");
        if (beam == null) return;

        // If endpoint hasn't been set yet, set to current position
        if (EndOrigin[slot].X == 0 && EndOrigin[slot].Y == 0 && EndOrigin[slot].Z == 0)
            EndOrigin[slot] = new Vector(absOrigin.X, absOrigin.Y, absOrigin.Z);

        beam.Teleport(absOrigin, null, null);
        beam.DispatchSpawn(null);

        EndOrigin[slot] = new Vector(absOrigin.X, absOrigin.Y, absOrigin.Z);

        _core.Scheduler.DelayBySeconds(lifetime, () =>
        {
            if (beam.IsValid)
                beam.AcceptInput<string>("Kill", "");
        });
    }

    private void CreateParticle(IPlayer player, Vector absOrigin, float lifetime, string effectName, Dictionary<string, string> itemData)
    {
        var particle = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>("info_particle_system");
        if (particle == null) return;

        string acceptInput = itemData.GetValueOrDefault("acceptInputValue", "Start");
        string angleStr = itemData.GetValueOrDefault("angleValue", "90 90 90");
        var angle = ParseAngle(angleStr);

        particle.Teleport(absOrigin, angle, null);
        particle.DispatchSpawn(null);
        particle.AcceptInput<string>(acceptInput, "");

        // Follow the player
        if (player.PlayerPawn != null)
            particle.AcceptInput<string>("FollowEntity", "!activator", player.PlayerPawn, player.PlayerPawn);

        _core.Scheduler.DelayBySeconds(lifetime, () =>
        {
            if (particle.IsValid)
                particle.AcceptInput<string>("Kill", "");
        });
    }

    private static QAngle ParseAngle(string angleValue)
    {
        string[] parts = angleValue.Split(' ');
        if (parts.Length >= 3 &&
            float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], CultureInfo.InvariantCulture, out float z))
        {
            return new QAngle(x, y, z);
        }
        return new QAngle(90, 90, 90);
    }
}
