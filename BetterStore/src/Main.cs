using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BetterStore.API;
using BetterStore.Commands;
using BetterStore.Config;
using BetterStore.Database;
using BetterStore.Items;
using BetterStore.Items.Modules;
using BetterStore.Log;
using BetterStore.Menus;
using BetterStore.Models;
using Economy.Contract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace BetterStore;

[PluginMetadata(Id = "betterstore", Name = "BetterStore", Version = "2.0.0", Author = "BetterStore")]
public class BetterStorePlugin : BasePlugin
{
    public BetterStorePlugin(ISwiftlyCore core) : base(core) { }

    private BetterStoreConfig _config = new();
    private StoreNode _storeRoot = new();
    private BetterStoreDB _db = null!;
    private ItemManager _itemManager = null!;
    private BetterStoreMenus _menus = null!;
    private BetterStoreCommands _commands = null!;
    private ModelApplyService _modelService = null!;
    private BetterStoreAPIv1 _api = null!;

    private CancellationTokenSource? _dbFlushTimer;
    private CancellationTokenSource? _expiryTimer;

    public override void Load(bool hotReload)
    {
        LoadConfig();
        LoadStoreItems();

        _db = new BetterStoreDB(Core, _config);
        _itemManager = new ItemManager(Core, _config, _db);
        _modelService = new ModelApplyService(Core, _config, _itemManager);
        _menus = new BetterStoreMenus(Core, _config, _itemManager, _storeRoot);
        _commands = new BetterStoreCommands(Core, _config, _menus, _modelService, _itemManager, _db);

        _api = new BetterStoreAPIv1(Core, _config, _itemManager);

        var flatItems = StoreItemsConfig.FlattenItems(_storeRoot);
        _itemManager.LoadItemConfigs(flatItems);

        ItemModuleManager.RegisterModules(Core, Assembly.GetExecutingAssembly());
        InitializeModules();
        _commands.RegisterCommands();

        Core.Event.OnClientSteamAuthorize += OnClientAuthorize;
        Core.Event.OnClientDisconnected += OnClientDisconnect;
        Core.Event.OnPrecacheResource += OnPrecacheResource;
        Core.Event.OnMapLoad += OnMapLoad;
        Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        Task.Run(async () =>
        {
            await _db.InitializeSchema();
            var validIds = new HashSet<string>(flatItems.Keys);
            await _db.PurgeOrphanedItems(validIds);
        });

        StartDbFlushTimer();

        _expiryTimer = Core.Scheduler.RepeatBySeconds(60.0f, CheckExpiredItems);
        Core.Scheduler.StopOnMapChange(_expiryTimer);

        Core.Logger.LogInformation("BetterStore v{PluginVersion} loaded with {Count} items.", "2.0.0", flatItems.Count);
    }

    private void LoadConfig()
    {
        Core.Configuration.InitializeJsonWithModel<BetterStoreConfig>("BetterStore.jsonc", "BetterStore")
            .Configure(builder =>
            {
                builder.AddJsonFile(Core.Configuration.GetConfigPath("BetterStore.jsonc"), optional: false, reloadOnChange: true);
            });

        var section = Core.Configuration.Manager.GetSection("BetterStore");
        var loaded = new BetterStoreConfig();
        loaded.Currencies.Clear();
        section.Bind(loaded);
        _config = loaded;
    }

    private void LoadStoreItems()
    {
        // Keep store items next to BetterStore.jsonc under configs/plugins/<plugin-id>.
        string storeItemsPath = Core.Configuration.GetConfigPath("store_items.jsonc");
        string? storeItemsDir = Path.GetDirectoryName(storeItemsPath);
        string legacyStoreItemsPath = Path.Combine(Core.PluginDataDirectory, "configs", "store_items.jsonc");

        if (string.IsNullOrWhiteSpace(storeItemsDir))
        {
            Core.Logger.LogWarning("Unable to resolve directory for store_items.jsonc path: {Path}", storeItemsPath);
            _storeRoot = new StoreNode { DisplayName = "Store" };
            return;
        }

        if (!Directory.Exists(storeItemsDir))
            Directory.CreateDirectory(storeItemsDir);

        // Migrate once from legacy plugin-data path if present.
        if (!File.Exists(storeItemsPath) && File.Exists(legacyStoreItemsPath))
        {
            File.Copy(legacyStoreItemsPath, storeItemsPath);
            Core.Logger.LogInformation(
                "Migrated store_items.jsonc from legacy path {LegacyPath} to {Path}",
                legacyStoreItemsPath,
                storeItemsPath);
        }

        if (!File.Exists(storeItemsPath))
        {
            string templatePath = Path.Combine(Core.PluginPath, "resources", "templates", "store_items.jsonc");
            if (File.Exists(templatePath))
            {
                File.Copy(templatePath, storeItemsPath);
                Core.Logger.LogInformation("Created default store_items.jsonc at {Path}", storeItemsPath);
            }
            else
            {
                Core.Logger.LogWarning("store_items.jsonc template not found at {Path}", templatePath);
                _storeRoot = new StoreNode { DisplayName = "Store" };
                return;
            }
        }

        _storeRoot = StoreItemsConfig.LoadFromFile(storeItemsPath);
        Core.Logger.LogInformation("Loaded store items from {Path}", storeItemsPath);
    }

    private void InitializeModules()
    {
        foreach (var kvp in ItemModuleManager.Modules)
        {
            if (kvp.Value is IModuleInitializable init)
                init.Initialize(Core, _itemManager, _config);
        }
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        try
        {
            if (interfaceManager.HasSharedInterface("Economy.API.v1"))
            {
                var economy = interfaceManager.GetSharedInterface<IEconomyAPIv1>("Economy.API.v1");
                _itemManager.Economy = economy;
                _commands.Economy = economy;
                _menus.Economy = economy;
                _api.Economy = economy;

                foreach (var currencyName in _config.CurrencyNames)
                {
                    if (!economy.WalletKindExists(currencyName))
                        economy.EnsureWalletKind(currencyName);
                }

                Core.Logger.LogInformation("BetterStore connected to Economy API.");
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarning("Economy API not available: {Message}", ex.Message);
        }
    }

    private void OnClientAuthorize(IOnClientSteamAuthorizeEvent e)
    {
        var player = Core.PlayerManager.GetPlayer(e.PlayerId);
        if (player == null || player.IsFakeClient) return;

        Task.Run(async () =>
        {
            try
            {
                await _itemManager.LoadPlayerData(player.SteamID);
                Core.Logger.LogDebug("Loaded data for player {SteamId}", player.SteamID);
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "Failed to load data for player {SteamId}", player.SteamID);
            }
        });
    }

    private void OnClientDisconnect(IOnClientDisconnectedEvent e)
    {
        var player = Core.PlayerManager.GetPlayer(e.PlayerId);
        if (player == null || player.IsFakeClient) return;

        ulong steamId = player.SteamID;

        var equipped = _itemManager.GetPlayerEquipments(player, null).ToList();
        foreach (var equip in equipped)
        {
            var item = _itemManager.GetItem(equip.ItemId);
            if (item != null && ItemModuleManager.Modules.TryGetValue(equip.Type, out var module))
            {
                try { module.OnUnequip(player, item, false); } catch { }
            }
        }

        Task.Run(async () =>
        {
            await _itemManager.FlushPendingDbOps();
            _itemManager.ClearPlayerData(steamId);
        });
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        var controller = @event.UserIdController;
        if (controller == null) return HookResult.Continue;

        var player = controller.ToPlayer();
        if (player == null || player.IsFakeClient) return HookResult.Continue;

        int teamNum = controller.TeamNum;
        if (teamNum < 2) return HookResult.Continue;

        _modelService.OnPlayerSpawn(player);

        Core.Scheduler.NextWorldUpdate(() =>
        {
            ReEquipOnSpawn(player);
        });

        return HookResult.Continue;
    }

    private void ReEquipOnSpawn(IPlayer player)
    {
        var equipped = _itemManager.GetPlayerEquipments(player, null);
        foreach (var equip in equipped)
        {
            if (equip.Type == "playerskin") continue;

            var item = _itemManager.GetItem(equip.ItemId);
            if (item == null) continue;

            if (!ItemModuleManager.Modules.TryGetValue(equip.Type, out var module)) continue;
            if (module.RequiresAlive == false) continue;

            try { module.OnEquip(player, item); } catch { }
        }
    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        foreach (var kvp in ItemModuleManager.Modules)
            kvp.Value.OnMapStart();

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event)
    {
        _modelService.OnRoundEnd();

        if (_config.SaveOnRoundEnd)
            Task.Run(() => _itemManager.FlushPendingDbOps());

        return HookResult.Continue;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent e)
    {
        var seenResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _itemManager.Items.Values)
        {
            foreach (var field in item)
            {
                if (!field.Key.EndsWith("model", StringComparison.OrdinalIgnoreCase))
                    continue;

                AddModelResource(e, seenResources, field.Value);
            }
        }

        foreach (string model in _config.DefaultModels.Terrorist)
            AddModelResource(e, seenResources, model);

        foreach (string model in _config.DefaultModels.CounterTerrorist)
            AddModelResource(e, seenResources, model);
    }

    private static void AddModelResource(IOnPrecacheResourceEvent e, HashSet<string> seenResources, string? resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
            return;

        string normalized = resourcePath.Trim().Replace('\\', '/');
        if (!seenResources.Add(normalized))
            return;

        e.AddItem(normalized);
    }

    private void CheckExpiredItems()
    {
        foreach (ulong steamId in _itemManager.GetLoadedPlayerSteamIds())
            _itemManager.ExpirePlayerItems(steamId);
    }

    private void OnMapLoad(IOnMapLoadEvent e)
    {
        foreach (var kvp in ItemModuleManager.Modules)
            kvp.Value.OnMapStart();

        StartDbFlushTimer();

        _expiryTimer = Core.Scheduler.RepeatBySeconds(60.0f, CheckExpiredItems);
        Core.Scheduler.StopOnMapChange(_expiryTimer);
    }

    private void StartDbFlushTimer()
    {
        if (_config.SaveQueueIntervalSeconds <= 0) return;

        _dbFlushTimer = Core.Scheduler.RepeatBySeconds(_config.SaveQueueIntervalSeconds, () =>
        {
            Task.Run(() => _itemManager.FlushPendingDbOps());
        });
        Core.Scheduler.StopOnMapChange(_dbFlushTimer);
    }

    public override void Unload()
    {
        _dbFlushTimer?.Cancel();
        _expiryTimer?.Cancel();
        Task.Run(() => _itemManager.FlushPendingDbOps()).Wait(TimeSpan.FromSeconds(5));
    }
}

public interface IModuleInitializable
{
    void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config);
}
