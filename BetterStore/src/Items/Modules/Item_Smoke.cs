using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace BetterStore.Items.Modules;

[StoreItemType("smoke")]
public class Item_Smoke : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;
    private static bool _smokeExists;
    private static readonly Random _random = new();

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        _smokeExists = _itemManager.IsAnyItemOfType("smoke");
        if (_smokeExists)
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
        if (!_smokeExists) return;

        var entity = e.Entity;
        if (entity == null || entity.DesignerName != "smokegrenade_projectile") return;

        _core.Scheduler.NextWorldUpdate(() => ProcessSmoke(entity));
    }

    private void ProcessSmoke(CEntityInstance entity)
    {
        if (!entity.IsValid) return;

        // Look for the throwing player's equipped smoke item
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player.IsFakeClient) continue;

            var equipped = _itemManager.GetPlayerEquipments(player, "smoke").FirstOrDefault();
            if (equipped == null) continue;

            var itemData = _itemManager.GetItem(equipped.ItemId);
            if (itemData == null) continue;

            // Try to set the smoke color via AcceptInput or schema property
            if (equipped.ItemId == "colorsmoke")
            {
                // Random color
                float r = _random.NextSingle() * 255.0f;
                float g = _random.NextSingle() * 255.0f;
                float b = _random.NextSingle() * 255.0f;

                // Set smoke color using schema if available
                var smokeProjectile = entity.As<CSmokeGrenadeProjectile>();
                if (smokeProjectile != null)
                {
                    smokeProjectile.SmokeColor = new SwiftlyS2.Shared.Natives.Vector(r, g, b);
                }
            }
            else if (itemData.TryGetValue("color", out string? colorStr) && !string.IsNullOrEmpty(colorStr))
            {
                string[] parts = colorStr.Split(' ');
                if (parts.Length >= 3 &&
                    float.TryParse(parts[0], CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(parts[1], CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(parts[2], CultureInfo.InvariantCulture, out float b))
                {
                    var smokeProjectile = entity.As<CSmokeGrenadeProjectile>();
                    if (smokeProjectile != null)
                    {
                        smokeProjectile.SmokeColor = new SwiftlyS2.Shared.Natives.Vector(r, g, b);
                    }
                }
            }

            break;
        }
    }
}
