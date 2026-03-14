using System;
using System.Collections.Generic;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace BetterStore.Items.Modules;

[StoreItemType("coloredskin")]
public class Item_ColoredSkin : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;
    private static bool _coloredSkinExists;
    private static readonly Random _random = new();

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        _coloredSkinExists = _itemManager.IsAnyItemOfType("coloredskin");
        if (_coloredSkinExists)
        {
            core.Event.OnTick += OnTick;
        }
    }

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item) => true;

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update)
    {
        if (!update) return true;

        // Reset color to white (default)
        var pawn = player.PlayerPawn;
        if (pawn != null && pawn.IsValid)
        {
            pawn.Render = new Color(255, 255, 255);
            pawn.RenderUpdated();
        }

        return true;
    }

    private void OnTick()
    {
        if (!_coloredSkinExists) return;

        foreach (var player in _core.PlayerManager.GetAlive())
        {
            if (player.IsFakeClient) continue;

            var equipped = _itemManager.GetPlayerEquipments(player, "coloredskin").FirstOrDefault();
            if (equipped == null) continue;

            var itemData = _itemManager.GetItem(equipped.ItemId);
            if (itemData == null) continue;

            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid) continue;

            if (itemData.TryGetValue("color", out string? colorStr) && !string.IsNullOrEmpty(colorStr))
            {
                string[] parts = colorStr.Split(' ');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out int r) &&
                    int.TryParse(parts[1], out int g) &&
                    int.TryParse(parts[2], out int b))
                {
                    pawn.Render = new Color(r, g, b);
                    pawn.RenderUpdated();
                }
            }
            else
            {
                // Random color
                pawn.Render = new Color(
                    _random.Next(256), _random.Next(256), _random.Next(256));
                pawn.RenderUpdated();
            }
        }
    }
}
