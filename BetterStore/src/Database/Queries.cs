namespace BetterStore.Database;

public static class Queries
{
    public const string CreateItemsTable = @"
        CREATE TABLE IF NOT EXISTS betterstore_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            steam_id BIGINT NOT NULL,
            item_id VARCHAR(255) NOT NULL,
            type VARCHAR(128) NOT NULL,
            date_of_purchase DATETIME DEFAULT CURRENT_TIMESTAMP,
            date_of_expiration DATETIME NULL,
            duration_seconds BIGINT DEFAULT 0,
            UNIQUE(steam_id, item_id)
        )";

    public const string CreateEquipmentTable = @"
        CREATE TABLE IF NOT EXISTS betterstore_equipment (
            id INT AUTO_INCREMENT PRIMARY KEY,
            steam_id BIGINT NOT NULL,
            item_id VARCHAR(255) NOT NULL,
            type VARCHAR(128) NOT NULL,
            UNIQUE(steam_id, item_id)
        )";

    public const string SelectPlayerItems = @"
        SELECT item_id, type, date_of_purchase, date_of_expiration, duration_seconds
        FROM betterstore_items
        WHERE steam_id = @SteamId";

    public const string SelectPlayerEquipment = @"
        SELECT item_id, type
        FROM betterstore_equipment
        WHERE steam_id = @SteamId";

    public const string InsertItem = @"
        INSERT IGNORE INTO betterstore_items
            (steam_id, item_id, type, date_of_expiration, duration_seconds)
        VALUES
            (@SteamId, @ItemId, @Type, @Expiration, @Duration)";

    public const string UpsertEquipment = @"
        INSERT INTO betterstore_equipment (steam_id, item_id, type)
        VALUES (@SteamId, @ItemId, @Type)
        ON DUPLICATE KEY UPDATE type = @Type";

    public const string DeleteItem = @"
        DELETE FROM betterstore_items
        WHERE steam_id = @SteamId AND item_id = @ItemId";

    public const string DeleteEquipment = @"
        DELETE FROM betterstore_equipment
        WHERE steam_id = @SteamId AND item_id = @ItemId";

    public const string DeletePlayerItems = @"
        DELETE FROM betterstore_items WHERE steam_id = @SteamId";

    public const string DeletePlayerEquipment = @"
        DELETE FROM betterstore_equipment WHERE steam_id = @SteamId";

    public const string TruncateItems = @"TRUNCATE TABLE betterstore_items";

    public const string TruncateEquipment = @"TRUNCATE TABLE betterstore_equipment";

    public const string SelectDistinctItemIds = @"
        SELECT DISTINCT item_id FROM betterstore_items";

    public const string DeleteItemsByItemId = @"
        DELETE FROM betterstore_items WHERE item_id = @ItemId";

    public const string DeleteEquipmentByItemId = @"
        DELETE FROM betterstore_equipment WHERE item_id = @ItemId";

    public const string SelectEquipmentConstraints = @"
        SELECT CONSTRAINT_NAME
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'betterstore_equipment'
          AND CONSTRAINT_TYPE = 'UNIQUE'
          AND CONSTRAINT_NAME != 'PRIMARY'";
}
