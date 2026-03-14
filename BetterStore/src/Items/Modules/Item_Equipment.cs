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

[StoreItemType("equipment")]
public class Item_Equipment : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;

    private static readonly Dictionary<ulong, Dictionary<int, CEntityInstance>> PlayerEquipmentEntities = new();

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        if (_itemManager.IsAnyItemOfType("equipment"))
        {
            core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
            core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeam);
            core.Event.OnClientDisconnected += OnClientDisconnect;
        }
    }

    public void OnPluginStart() { }

    public void OnMapStart()
    {
        PlayerEquipmentEntities.Clear();
    }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        if (!item.TryGetValue("slot", out string? slotStr) || !int.TryParse(slotStr, out int slot) || slot < 0)
            return false;

        string model = item.GetValueOrDefault("model", "");
        if (string.IsNullOrEmpty(model)) return false;

        EquipModel(player, model, slot);
        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        if (!item.TryGetValue("slot", out string? slotStr) || !int.TryParse(slotStr, out int slot))
            return false;

        UnEquipModel(player.SteamID, slot);
        return true;
    }

    private void EquipModel(IPlayer player, string model, int slot)
    {
        ulong steamId = player.SteamID;
        UnEquipModel(steamId, slot);

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var entity = CreatePropEntity(player, model);
            if (entity != null)
            {
                if (!PlayerEquipmentEntities.ContainsKey(steamId))
                    PlayerEquipmentEntities[steamId] = new();
                PlayerEquipmentEntities[steamId][slot] = entity;
            }
        });
    }

    private void UnEquipModel(ulong steamId, int slot)
    {
        if (!PlayerEquipmentEntities.TryGetValue(steamId, out var slots) || !slots.ContainsKey(slot))
            return;

        var entity = slots[slot];
        if (entity != null && entity.IsValid)
            entity.AcceptInput<string>("Kill", "");

        slots.Remove(slot);
        if (slots.Count == 0)
            PlayerEquipmentEntities.Remove(steamId);
    }

    private CEntityInstance? CreatePropEntity(IPlayer player, string model)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return null;

        var entity = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>("prop_dynamic_override");
        if (entity == null) return null;

        entity.SetModel(model);
        entity.DispatchSpawn(null);
        entity.AcceptInput<string>("FollowEntity", "!activator", pawn, pawn);

        return entity;
    }

    private void CleanUpAll(ulong steamId)
    {
        if (!PlayerEquipmentEntities.TryGetValue(steamId, out var slots)) return;

        foreach (var entity in slots.Values)
        {
            if (entity != null && entity.IsValid)
                entity.AcceptInput<string>("Kill", "");
        }

        PlayerEquipmentEntities.Remove(steamId);
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        var controller = @event.UserIdController;
        if (controller == null) return HookResult.Continue;

        var player = controller.ToPlayer();
        if (player == null || player.IsFakeClient) return HookResult.Continue;

        _core.Scheduler.NextWorldUpdate(() =>
        {
            var equipped = _itemManager.GetPlayerEquipments(player, "equipment");

            // Clean up stale entities
            if (PlayerEquipmentEntities.TryGetValue(player.SteamID, out var existingSlots))
            {
                var staleSlotsToRemove = existingSlots.Keys
                    .Where(s => !equipped.Any(e => e.Slot == s))
                    .ToList();
                foreach (int s in staleSlotsToRemove)
                    UnEquipModel(player.SteamID, s);
            }

            foreach (var equip in equipped)
            {
                var item = _itemManager.GetItem(equip.ItemId);
                if (item == null) continue;

                string model = item.GetValueOrDefault("model", "");
                int slot = item.TryGetValue("slot", out string? s) && int.TryParse(s, out int sl) ? sl : 0;

                // Skip if already spawned for this slot
                if (PlayerEquipmentEntities.TryGetValue(player.SteamID, out var existing)
                    && existing.TryGetValue(slot, out var ent) && ent != null && ent.IsValid)
                    continue;

                if (!string.IsNullOrEmpty(model))
                    EquipModel(player, model, slot);
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
            CleanUpAll(player.SteamID);

        return HookResult.Continue;
    }

    private void OnClientDisconnect(IOnClientDisconnectedEvent e)
    {
        var player = _core.PlayerManager.GetPlayer(e.PlayerId);
        if (player != null)
            CleanUpAll(player.SteamID);
    }
}
