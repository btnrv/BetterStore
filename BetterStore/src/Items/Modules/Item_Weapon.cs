using System;
using System.Collections.Generic;
using BetterStore.Contract;
using SwiftlyS2.Shared.Players;

namespace BetterStore.Items.Modules;

[StoreItemType("weapon")]
public class Item_Weapon : IItemModule
{
    public bool Equipable => false;
    public bool? RequiresAlive => true;

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        string weaponName = item.GetValueOrDefault("weapon", "");
        if (string.IsNullOrEmpty(weaponName)) return false;

        player.ExecuteCommand($"give {weaponName}");
        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update) => true;
}
