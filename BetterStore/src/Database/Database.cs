using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Database;
using BetterStore.Config;
using BetterStore.Items;
using Microsoft.Extensions.Logging;

namespace BetterStore.Database;

public class BetterStoreDB
{
    private readonly ISwiftlyCore _core;
    private readonly BetterStoreConfig _config;

    public BetterStoreDB(ISwiftlyCore core, BetterStoreConfig config)
    {
        _core = core;
        _config = config;
    }

    private IDbConnection GetConnection()
    {
        return _core.Database.GetConnection(_config.DatabaseConnection);
    }

    public Task InitializeSchema()
    {
        return Task.Run(() =>
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();

                ExecuteNonQuery(conn, Queries.CreateItemsTable);
                ExecuteNonQuery(conn, Queries.CreateEquipmentTable);
                MigrateEquipmentConstraint(conn);

                _core.Logger.LogInformation("BetterStore database schema initialized.");
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to initialize BetterStore database schema.");
            }
        });
    }

    private void MigrateEquipmentConstraint(IDbConnection conn)
    {
        try
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = Queries.SelectEquipmentConstraints;
            var constraintNames = new List<string>();
            using (var reader = checkCmd.ExecuteReader())
            {
                while (reader.Read())
                    constraintNames.Add(reader.GetString(0));
            }

            if (constraintNames.Contains("idx_steam_item"))
                return;

            foreach (string name in constraintNames)
            {
                try { ExecuteNonQuery(conn, $"ALTER TABLE betterstore_equipment DROP INDEX `{name}`"); }
                catch { }
            }

            ExecuteNonQuery(conn, "CREATE UNIQUE INDEX idx_steam_item ON betterstore_equipment (steam_id, item_id)");
            _core.Logger.LogInformation("Migrated betterstore_equipment unique constraint to (steam_id, item_id).");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarning(ex, "Equipment constraint migration skipped.");
        }
    }

    public Task<List<PlayerItemRow>> GetPlayerItems(ulong steamId)
    {
        return Task.Run(() =>
        {
            var items = new List<PlayerItemRow>();
            try
            {
                using var conn = GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = Queries.SelectPlayerItems;
                AddParam(cmd, "@SteamId", steamId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new PlayerItemRow
                    {
                        ItemId = reader.GetString(0),
                        Type = reader.GetString(1),
                        DateOfPurchase = reader.IsDBNull(2) ? DateTime.UtcNow : reader.GetDateTime(2),
                        DateOfExpiration = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                        DurationSeconds = reader.IsDBNull(4) ? 0 : reader.GetInt64(4)
                    });
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to fetch items for player {SteamId}", steamId);
            }
            return items;
        });
    }

    public Task<List<PlayerEquipmentRow>> GetPlayerEquipment(ulong steamId)
    {
        return Task.Run(() =>
        {
            var rows = new List<PlayerEquipmentRow>();
            try
            {
                using var conn = GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = Queries.SelectPlayerEquipment;
                AddParam(cmd, "@SteamId", steamId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new PlayerEquipmentRow
                    {
                        ItemId = reader.GetString(0),
                        Type = reader.GetString(1)
                    });
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to fetch equipment for player {SteamId}", steamId);
            }
            return rows;
        });
    }

    public Task PurgeOrphanedItems(HashSet<string> validItemIds)
    {
        return Task.Run(() =>
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();

                var dbItemIds = new List<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = Queries.SelectDistinctItemIds;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        dbItemIds.Add(reader.GetString(0));
                }

                foreach (string dbItemId in dbItemIds)
                {
                    if (validItemIds.Contains(dbItemId))
                        continue;

                    using var delItems = conn.CreateCommand();
                    delItems.CommandText = Queries.DeleteItemsByItemId;
                    AddParam(delItems, "@ItemId", dbItemId);
                    delItems.ExecuteNonQuery();

                    using var delEquip = conn.CreateCommand();
                    delEquip.CommandText = Queries.DeleteEquipmentByItemId;
                    AddParam(delEquip, "@ItemId", dbItemId);
                    delEquip.ExecuteNonQuery();

                    _core.Logger.LogInformation("Purged orphaned item_id '{ItemId}' from database.", dbItemId);
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to purge orphaned items.");
            }
        });
    }

    public Task ExecuteBatch(List<DbOperation> operations)
    {
        return Task.Run(() =>
        {
            if (operations.Count == 0) return;

            try
            {
                using var conn = GetConnection();
                conn.Open();
                using var tx = conn.BeginTransaction();

                foreach (var op in operations)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = op.Sql;
                    foreach (var param in op.Parameters)
                        AddParam(cmd, param.Key, param.Value);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to execute batch of {Count} operations.", operations.Count);
            }
        });
    }

    public Task WipePlayer(ulong steamId)
    {
        return Task.Run(() =>
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();

                using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = Queries.DeletePlayerItems;
                AddParam(cmd1, "@SteamId", steamId);
                cmd1.ExecuteNonQuery();

                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = Queries.DeletePlayerEquipment;
                AddParam(cmd2, "@SteamId", steamId);
                cmd2.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to wipe player {SteamId}", steamId);
            }
        });
    }

    public Task WipeDatabase()
    {
        return Task.Run(() =>
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();
                ExecuteNonQuery(conn, Queries.TruncateItems);
                ExecuteNonQuery(conn, Queries.TruncateEquipment);
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to wipe database.");
            }
        });
    }

    private static void ExecuteNonQuery(IDbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

public class PlayerItemRow
{
    public string ItemId { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime DateOfPurchase { get; set; }
    public DateTime? DateOfExpiration { get; set; }
    public long DurationSeconds { get; set; }
}

public class PlayerEquipmentRow
{
    public string ItemId { get; set; } = "";
    public string Type { get; set; } = "";
}

public class DbOperation
{
    public string Sql { get; set; } = "";
    public Dictionary<string, object> Parameters { get; set; } = new();
}
