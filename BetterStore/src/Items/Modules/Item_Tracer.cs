using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterStore.Config;
using BetterStore.Contract;
using BetterStore.Items;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

namespace BetterStore.Items.Modules;

[StoreItemType("tracer")]
public class Item_Tracer : IItemModule, IModuleInitializable
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private ISwiftlyCore _core = null!;
    private ItemManager _itemManager = null!;

    public void Initialize(ISwiftlyCore core, ItemManager itemManager, BetterStoreConfig config)
    {
        _core = core;
        _itemManager = itemManager;

        if (_itemManager.IsAnyItemOfType("tracer"))
        {
            core.GameEvent.HookPost<EventBulletImpact>(OnBulletImpact);
        }
    }

    public void OnPluginStart() { }
    public void OnMapStart() { }

    public bool OnEquip(IPlayer player, Dictionary<string, string> item) => true;
    public bool OnUnequip(IPlayer player, Dictionary<string, string> item, bool update) => true;

    private HookResult OnBulletImpact(EventBulletImpact @event)
    {
        var controller = @event.UserIdController;
        if (controller == null) return HookResult.Continue;

        var player = controller.ToPlayer();
        if (player == null || !player.IsValid) return HookResult.Continue;

        var equipped = _itemManager.GetPlayerEquipments(player, "tracer").FirstOrDefault();
        if (equipped == null) return HookResult.Continue;

        var itemData = _itemManager.GetItem(equipped.ItemId);
        if (itemData == null) return HookResult.Continue;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return HookResult.Continue;

        // Get eye position (origin + eye offset)
        var origin = pawn.AbsOrigin ?? Vector.Zero;
        var eyePos = new Vector(origin.X, origin.Y, origin.Z + 64.0f); // approximate eye height

        float impactX = @event.X;
        float impactY = @event.Y;
        float impactZ = @event.Z;

        string model = itemData.GetValueOrDefault("model", "");
        float lifetime = itemData.TryGetValue("lifetime", out string? lt) && float.TryParse(lt, CultureInfo.InvariantCulture, out float l) ? l : 0.3f;

        // Create beam from eye to impact point
        var beam = _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>("beam");
        if (beam == null) return HookResult.Continue;

        if (!string.IsNullOrEmpty(model))
            beam.SetModel(model);

        beam.Teleport(eyePos, null, null);
        beam.DispatchSpawn(null);

        string acceptInput = itemData.GetValueOrDefault("acceptInputValue", "Start");
        beam.AcceptInput<string>(acceptInput, "");

        // Remove after lifetime
        _core.Scheduler.DelayBySeconds(lifetime, () =>
        {
            if (beam.IsValid)
                beam.AcceptInput<string>("Kill", "");
        });

        return HookResult.Continue;
    }
}
