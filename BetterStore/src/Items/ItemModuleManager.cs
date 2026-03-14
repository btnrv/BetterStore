using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterStore.Contract;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace BetterStore.Items;

public static class ItemModuleManager
{
    public static readonly Dictionary<string, IItemModule> Modules = new();

    public static void RegisterModules(ISwiftlyCore core, Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IItemModule).IsAssignableFrom(t)))
        {
            if (Activator.CreateInstance(type) is not IItemModule module)
                continue;

            if (type.GetCustomAttribute<StoreItemTypeAttribute>() is { } attr)
            {
                LoadModule(core, attr.Name, module);
            }
            else if (type.GetCustomAttribute<StoreItemTypesAttribute>()?.Names is { } attrs)
            {
                foreach (string attrName in attrs)
                {
                    LoadModule(core, attrName, module);
                }
            }
        }
    }

    private static void LoadModule(ISwiftlyCore core, string name, IItemModule module)
    {
        Modules[name] = module;
        core.Logger.LogInformation($"BetterStore module '{name}' registered.");
        module.OnPluginStart();
    }
}
