using System;

namespace BetterStore.Items;

public enum PurchaseResult
{
    Success,
    FreeUseEquipped,
    AlreadyOwned,
    InsufficientFunds,
    InvalidModule,
    Failed
}

public class Store_Item
{
    public required ulong SteamID { get; set; }
    public int Price { get; set; }
    public required string Type { get; set; }
    public required string ItemId { get; set; }
    public DateTime DateOfPurchase { get; set; }
    public DateTime? DateOfExpiration { get; set; }
    public long DurationSeconds { get; set; }

    public bool IsExpired => DateOfExpiration.HasValue && DateTime.UtcNow >= DateOfExpiration.Value;
    public bool IsPermanent => !DateOfExpiration.HasValue || DurationSeconds <= 0;
}

public class Store_Equipment
{
    public required ulong SteamID { get; set; }
    public required string Type { get; set; }
    public required string ItemId { get; set; }
    public int Slot { get; set; }
}
