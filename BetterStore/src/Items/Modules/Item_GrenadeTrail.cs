using System;
using System.Collections.Generic;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace BetterStore.Items.Modules;

[StoreItemType("grenadetrail")]
public class Item_GrenadeTrail : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;
    private static bool _grenadeTrailExists;

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        _grenadeTrailExists = _itemManager.IsAnyItemOfType("grenadetrail");
        if (_grenadeTrailExists)
        {
            core.Event.OnEntityCreated += OnEntityCreated;
        }
    }

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item) => true;
    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update) => true;

    private void OnEntityCreated(IOnEntityCreatedEvent e)
    {
        if (!_grenadeTrailExists) return;

        var entity = e.Entity;
        if (entity == null || !entity.DesignerName.EndsWith("_projectile")) return;

        _core.Scheduler.NextWorldUpdate(() => ProcessGrenade(entity));
    }

    private void ProcessGrenade(CEntityInstance entity)
    {
        if (!entity.IsValid) return;

        // Try to find the throwing player by checking all players
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player.IsFakeClient) continue;

            var equipped = _itemManager.GetPlayerEquipments(player, "grenadetrail").FirstOrDefault();
            if (equipped == null) continue;

            var itemData = _itemManager.GetItem(equipped.ItemId);
            if (itemData == null) continue;

            string model = itemData.GetValueOrDefault("model", "");
            if (string.IsNullOrEmpty(model)) continue;

            string acceptInput = itemData.GetValueOrDefault("acceptInputValue", "Start");

            var particle = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>("info_particle_system");
            if (particle == null) continue;

            var grenadePos = entity.As<CBaseEntity>()?.AbsOrigin ?? Vector.Zero;
            particle.Teleport(grenadePos, null, null);
            particle.DispatchSpawn(null);
            particle.AcceptInput<string>("FollowEntity", "!activator", entity, particle);
            particle.AcceptInput<string>(acceptInput, "");

            break; // Only apply one trail per grenade
        }
    }
}
