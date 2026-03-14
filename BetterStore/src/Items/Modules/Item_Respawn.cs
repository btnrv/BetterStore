using System;
using System.Collections.Generic;
using BetterStore.Contract;
using SwiftlyS2.Shared.Players;

namespace BetterStore.Items.Modules;

[StoreItemType("respawn")]
public class Item_Respawn : IItemModule
{
    public bool Equipable => false;
    public bool? RequiresAlive => false;

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item)
    {
        int teamNum = player.Controller.TeamNum;
        if (teamNum < 2) return false; // Must be on T or CT

        player.Respawn();
        return true;
    }

    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update) => true;
}
