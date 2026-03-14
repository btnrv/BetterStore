using SwiftlyS2.Shared.Players;
using System.Collections.Generic;

namespace BetterStore.Contract;

public interface IBetterStoreAPIv1
{
    event Action<IPlayer, string, int, string>? OnPlayerPurchaseItem;
    event Action<IPlayer, string>? OnPlayerEquipItem;
    event Action<IPlayer, string>? OnPlayerUnequipItem;
    event Action<IPlayer, string, int>? OnPlayerSellItem;

    bool GiveItem(IPlayer player, string uniqueId);
    bool PurchaseItem(IPlayer player, string uniqueId);
    bool EquipItem(IPlayer player, string uniqueId);
    bool UnequipItem(IPlayer player, string uniqueId, bool update = true);
    bool SellItem(IPlayer player, string uniqueId);

    bool PlayerHasItem(IPlayer player, string type, string uniqueId);
    bool PlayerUsingItem(IPlayer player, string type, string uniqueId);

    Dictionary<string, string>? GetItem(string uniqueId);
    List<KeyValuePair<string, Dictionary<string, string>>> GetItemsByType(string type);

    void RegisterModule(IItemModule module);

    int GetPlayerBalance(IPlayer player, string? currency = null);
    void AddPlayerBalance(IPlayer player, int amount, string? currency = null);
    void SubtractPlayerBalance(IPlayer player, int amount, string? currency = null);
    void SetPlayerBalance(IPlayer player, int amount, string? currency = null);
    bool HasSufficientFunds(IPlayer player, int amount, string? currency = null);
}
