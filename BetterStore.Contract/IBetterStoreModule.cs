using SwiftlyS2.Shared.Players;
using System.Collections.Generic;

namespace BetterStore.Contract;

public interface IItemModule
{
    bool Equipable { get; }
    bool? RequiresAlive { get; }

    void OnPluginStart();
    void OnMapStart();

    bool OnEquip(IPlayer player, Dictionary<string, string> item);
    bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update);
}

[AttributeUsage(AttributeTargets.Class)]
public class StoreItemTypeAttribute : Attribute
{
    public string Name { get; }
    public StoreItemTypeAttribute(string name) { Name = name; }
}

[AttributeUsage(AttributeTargets.Class)]
public class StoreItemTypesAttribute : Attribute
{
    public string[] Names { get; }
    public StoreItemTypesAttribute(string[] names) { Names = names; }
}
