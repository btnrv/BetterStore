using System;
using System.Collections.Generic;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace BetterStore.Items.Modules;

[StoreItemType("customweapon")]
public class Item_CustomWeapon : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;
    private static bool _customWeaponExists;

    private static readonly Dictionary<IntPtr, string> OldSubclassByHandle = new();

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        _customWeaponExists = _itemManager.IsAnyItemOfType("customweapon");
        if (_customWeaponExists)
        {
            core.Event.OnEntityCreated += OnEntityCreated;
            core.GameEvent.HookPost<EventItemEquip>(OnItemEquip);
        }
    }

    public void OnPluginStart() { }
    public void OnMapStart()
    {
        OldSubclassByHandle.Clear();
    }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        if (!TryParseWeaponSpec(item.GetValueOrDefault("weapon", ""), out string weaponBase, out string weaponSubclass))
            return true;

        if (!player.IsAlive) return true;

        // Try to apply to currently held weapon
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return true;

        var weapon = FindPlayerWeapon(pawn, weaponBase);
        if (weapon != null)
            SetSubclass(weapon, weaponBase, weaponSubclass);

        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        if (!update) return true;

        if (!TryParseWeaponSpec(item.GetValueOrDefault("weapon", ""), out string weaponBase, out _))
            return true;

        if (!player.IsAlive) return true;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return true;

        var weapon = FindPlayerWeapon(pawn, weaponBase);
        if (weapon != null)
            ResetSubclass(weapon);

        return true;
    }

    private void OnEntityCreated(IOnEntityCreatedEvent e)
    {
        if (!_customWeaponExists) return;

        var entity = e.Entity;
        if (entity == null) return;

        string designerName = entity.DesignerName;
        bool isWeapon = designerName.StartsWith("weapon_");
        bool isProjectile = designerName.EndsWith("_projectile");

        if (!isWeapon && !isProjectile) return;

        _core.Scheduler.NextWorldUpdate(() => ProcessEntity(entity, isWeapon));
    }

    private void ProcessEntity(CEntityInstance entityInstance, bool isWeapon)
    {
        if (!entityInstance.IsValid || !isWeapon) return;

        var weapon = entityInstance.As<CBasePlayerWeapon>();
        if (weapon == null || !weapon.IsValid) return;

        uint ownerXuidLow = weapon.OriginalOwnerXuidLow;
        if (ownerXuidLow <= 0) return;

        IPlayer? owner = FindPlayerByXuidLow(ownerXuidLow);
        if (owner == null) return;

        var equipments = _itemManager.GetPlayerEquipments(owner, "customweapon");
        if (equipments.Count == 0) return;

        string weaponDesignerName = GetDesignerName(weapon);

        foreach (var equipment in equipments)
        {
            var itemData = _itemManager.GetItem(equipment.ItemId);
            if (itemData == null) continue;

            if (!TryParseWeaponSpec(itemData.GetValueOrDefault("weapon", ""), out string weaponBase, out string weaponSubclass))
                continue;

            if (weaponDesignerName.Equals(weaponBase, StringComparison.Ordinal))
            {
                SetSubclass(weapon, weaponBase, weaponSubclass);
                break;
            }
        }
    }

    private HookResult OnItemEquip(EventItemEquip @event)
    {
        var player = @event.UserIdPlayer;
        if (player == null || player.IsFakeClient) return HookResult.Continue;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return HookResult.Continue;

        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null) return HookResult.Continue;

        var activeWeaponHandle = weaponServices.ActiveWeapon;
        if (!activeWeaponHandle.IsValid) return HookResult.Continue;

        var activeWeapon = activeWeaponHandle.Value;
        if (activeWeapon == null || !activeWeapon.IsValid) return HookResult.Continue;

        string weaponDesignerName = GetDesignerName(activeWeapon);

        var equipments = _itemManager.GetPlayerEquipments(player, "customweapon");
        foreach (var equipment in equipments)
        {
            var itemData = _itemManager.GetItem(equipment.ItemId);
            if (itemData == null) continue;

            if (!TryParseWeaponSpec(itemData.GetValueOrDefault("weapon", ""), out string weaponBase, out string weaponSubclass))
                continue;

            if (weaponDesignerName.Equals(weaponBase, StringComparison.Ordinal))
            {
                SetSubclass(activeWeapon, weaponBase, weaponSubclass);
                break;
            }
        }

        return HookResult.Continue;
    }

    public static string GetDesignerName(CBasePlayerWeapon weapon)
    {
        string designerName = weapon.DesignerName;
        ushort itemIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

        return (designerName, itemIndex) switch
        {
            var (name, _) when name.Contains("bayonet") => "weapon_knife",
            ("weapon_deagle", 64) => "weapon_revolver",
            ("weapon_m4a1", 60) => "weapon_m4a1_silencer",
            ("weapon_hkp2000", 61) => "weapon_usp_silencer",
            ("weapon_mp7", 23) => "weapon_mp5sd",
            _ => designerName
        };
    }

    public static bool TryParseWeaponSpec(string weaponSpec, out string weaponName, out string weaponSubclass)
    {
        weaponName = string.Empty;
        weaponSubclass = string.Empty;

        if (string.IsNullOrEmpty(weaponSpec)) return false;

        string[] parts = weaponSpec.Split(':', 2);
        weaponName = parts[0].Trim();
        weaponSubclass = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return !string.IsNullOrEmpty(weaponName) && !string.IsNullOrEmpty(weaponSubclass);
    }

    public static void SetSubclass(CBasePlayerWeapon weapon, string oldSubclass, string newSubclass)
    {
        if (string.IsNullOrEmpty(newSubclass)) return;

        OldSubclassByHandle[weapon.Address] = oldSubclass;
        weapon.AcceptInput<string>("ChangeSubclass", newSubclass, weapon, weapon);
    }

    public static void ResetSubclass(CBasePlayerWeapon weapon)
    {
        if (!OldSubclassByHandle.TryGetValue(weapon.Address, out string? oldSubclass) || string.IsNullOrEmpty(oldSubclass))
            return;

        weapon.AcceptInput<string>("ChangeSubclass", oldSubclass, weapon, weapon);
        OldSubclassByHandle.Remove(weapon.Address);
    }

    private static CBasePlayerWeapon? FindPlayerWeapon(CCSPlayerPawn pawn, string weaponName)
    {
        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null) return null;

        var activeHandle = weaponServices.ActiveWeapon;
        if (activeHandle.IsValid)
        {
            var active = activeHandle.Value;
            if (active != null && active.IsValid && GetDesignerName(active) == weaponName)
                return active;
        }

        var myWeapons = weaponServices.MyWeapons;
        for (int i = 0; i < myWeapons.Count; i++)
        {
            var handle = myWeapons[i];
            if (!handle.IsValid) continue;

            var w = handle.Value;
            if (w != null && w.IsValid && GetDesignerName(w) == weaponName)
                return w;
        }

        return null;
    }

    private IPlayer? FindPlayerByXuidLow(uint xuidLow)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            // SteamID64 low 32 bits == OriginalOwnerXuidLow
            if ((uint)(player.SteamID & 0xFFFFFFFF) == xuidLow)
                return player;
        }
        return null;
    }

    public static void Inspect(ISwiftlyCore core, IPlayer player, string weaponSpec,
        Dictionary<ulong, DateTime>? cooldowns, float cooldownSeconds)
    {
        if (!TryParseWeaponSpec(weaponSpec, out string weaponBase, out string weaponSubclass))
            return;

        if (!player.IsAlive) return;
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return;

        // Cooldown check
        if (cooldowns != null)
        {
            ulong steamId = player.SteamID;
            if (cooldowns.TryGetValue(steamId, out var lastInspect) &&
                (DateTime.UtcNow - lastInspect).TotalSeconds < cooldownSeconds)
                return;

            cooldowns[steamId] = DateTime.UtcNow;
        }

        var weapon = FindPlayerWeapon(pawn, weaponBase);
        if (weapon == null)
        {
            player.SendChat(core.Localizer["BetterStore.CustomWeapon.WrongWeapon", weaponBase]);
            return;
        }

        SetSubclass(weapon, weaponBase, weaponSubclass);

        core.Scheduler.DelayBySeconds(5.0f, () =>
        {
            if (!weapon.IsValid) return;
            ResetSubclass(weapon);
        });
    }
}
