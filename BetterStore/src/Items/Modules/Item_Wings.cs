using System;
using System.Collections.Generic;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace BetterStore.Items.Modules;

[StoreItemType("wings")]
public class Item_Wings : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;

    private static readonly Dictionary<ulong, Dictionary<int, CEntityInstance>> PlayerWingsEntities = new();

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        if (_itemManager.IsAnyItemOfType("wings"))
        {
            core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
            core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeam);
            core.Event.OnClientDisconnected += OnClientDisconnect;
        }
    }

    public void OnPluginStart() { }

    public void OnMapStart()
    {
        PlayerWingsEntities.Clear();
    }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        if (!item.TryGetValue("slot", out string? slotStr) || !int.TryParse(slotStr, out int slot) || slot < 0)
            return false;

        string model = item.GetValueOrDefault("model", "");
        if (string.IsNullOrEmpty(model)) return false;

        string animation = item.GetValueOrDefault("animation", "");
        EquipWings(player, model, slot, animation);
        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        if (!item.TryGetValue("slot", out string? slotStr) || !int.TryParse(slotStr, out int slot))
            return false;

        UnEquipWings(player.SteamID, slot);
        return true;
    }

    private void EquipWings(IPlayer player, string model, int slot, string animation)
    {
        ulong steamId = player.SteamID;
        UnEquipWings(steamId, slot);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var entity = CreateWingsEntity(player, model, animation);
            if (entity != null)
            {
                if (!PlayerWingsEntities.ContainsKey(steamId))
                    PlayerWingsEntities[steamId] = new();
                PlayerWingsEntities[steamId][slot] = entity;
            }
        });
    }

    private void UnEquipWings(ulong steamId, int slot)
    {
        if (!PlayerWingsEntities.TryGetValue(steamId, out var slots) || !slots.ContainsKey(slot))
            return;

        var entity = slots[slot];
        if (entity != null && entity.IsValid)
            entity.AcceptInput<string>("Kill", "");

        slots.Remove(slot);
        if (slots.Count == 0)
            PlayerWingsEntities.Remove(steamId);
    }

    private CEntityInstance? CreateWingsEntity(IPlayer player, string model, string animation)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return null;

        var entity = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>("prop_dynamic_override");
        if (entity == null) return null;

        entity.SetModel(model);
        entity.DispatchSpawn(null);

        // Follow the player pawn
        entity.AcceptInput<string>("FollowEntity", "!activator", pawn, pawn);

        if (!string.IsNullOrEmpty(animation))
            entity.AcceptInput<string>("SetAnimation", animation);

        return entity;
    }

    private void CleanUpAllWings(ulong steamId)
    {
        if (!PlayerWingsEntities.TryGetValue(steamId, out var slots)) return;

        foreach (var entity in slots.Values)
        {
            if (entity != null && entity.IsValid)
                entity.AcceptInput<string>("Kill", "");
        }

        PlayerWingsEntities.Remove(steamId);
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        var controller = @event.UserIdController;
        if (controller == null) return HookResult.Continue;

        var player = controller.ToPlayer();
        if (player == null || player.IsFakeClient) return HookResult.Continue;

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var equipped = _itemManager.GetPlayerEquipments(player, "wings");
            foreach (var equip in equipped)
            {
                var item = _itemManager.GetItem(equip.ItemId);
                if (item == null) continue;

                string model = item.GetValueOrDefault("model", "");
                if (string.IsNullOrEmpty(model)) continue;

                int slot = item.TryGetValue("slot", out string? s) && int.TryParse(s, out int sl) ? sl : 0;
                string animation = item.GetValueOrDefault("animation", "");
                EquipWings(player, model, slot, animation);
            }
        });

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event)
    {
        var controller = @event.UserIdController;
        if (controller == null) return HookResult.Continue;

        var player = controller.ToPlayer();
        if (player != null)
            CleanUpAllWings(player.SteamID);

        return HookResult.Continue;
    }

    private void OnClientDisconnect(IOnClientDisconnectedEvent e)
    {
        var player = _core.PlayerManager.GetPlayer(e.PlayerId);
        if (player != null)
            CleanUpAllWings(player.SteamID);
    }
}
