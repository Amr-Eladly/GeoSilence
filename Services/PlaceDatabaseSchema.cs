using GeoSilence.Models;
using SQLite;

namespace GeoSilence.Services
{
    internal static class PlaceDatabaseSchema
    {
        private const string TableName = nameof(PlaceEntity);

        internal static async Task EnsureMigratedAsync(SQLiteAsyncConnection db)
        {
            await db.CreateTableAsync<PlaceEntity>();
            await EnsureColumnAsync(db, nameof(PlaceEntity.CloudId), "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, nameof(PlaceEntity.ActivationType), $"INTEGER NOT NULL DEFAULT {(int)ActivationType.Automatic}");
            await EnsureColumnAsync(db, nameof(PlaceEntity.Visibility), $"INTEGER NOT NULL DEFAULT {(int)PlaceVisibility.Private}");
            await EnsureColumnAsync(db, nameof(PlaceEntity.IsDeleted), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, nameof(PlaceEntity.IsDirty), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, nameof(PlaceEntity.OwnerId), "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, nameof(PlaceEntity.CreatedAtUtcMs), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, nameof(PlaceEntity.UpdatedAtUtcMs), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, nameof(PlaceEntity.LastSyncedAtUtcMs), "INTEGER NULL");
            await EnsureColumnAsync(db, nameof(PlaceEntity.Version), "INTEGER NOT NULL DEFAULT 1");

            await BackfillLegacyRowsAsync(db);
        }

        private static async Task EnsureColumnAsync(SQLiteAsyncConnection db, string columnName, string columnSql)
        {
            var columns = await db.QueryAsync<ColumnInfo>($"PRAGMA table_info({TableName})");

            if (columns.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)))
                return;

            await db.ExecuteAsync($"ALTER TABLE {TableName} ADD COLUMN {columnName} {columnSql}");
        }

        private static async Task BackfillLegacyRowsAsync(SQLiteAsyncConnection db)
        {
            var rows = await db.Table<PlaceEntity>().ToListAsync();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var changed = false;

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.CloudId))
                {
                    row.CloudId = Guid.NewGuid().ToString("N");
                    row.IsDirty = true;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(row.OwnerId))
                {
                    row.OwnerId = "local_user";
                    row.IsDirty = true;
                    changed = true;
                }

                if (row.CreatedAtUtcMs <= 0)
                {
                    row.CreatedAtUtcMs = now;
                    changed = true;
                }

                if (row.UpdatedAtUtcMs <= 0)
                {
                    row.UpdatedAtUtcMs = now;
                    changed = true;
                }

                if (row.Version <= 0)
                {
                    row.Version = 1;
                    changed = true;
                }

                if (!Enum.IsDefined(typeof(ActivationType), row.ActivationType))
                {
                    row.ActivationType = (int)ActivationType.Automatic;
                    changed = true;
                }

                if (!Enum.IsDefined(typeof(PlaceVisibility), row.Visibility))
                {
                    row.Visibility = (int)PlaceVisibility.Private;
                    changed = true;
                }
            }

            if (changed)
            {
                foreach (var row in rows)
                    await db.UpdateAsync(row);
            }
        }

        private sealed class ColumnInfo
        {
            public string Name { get; set; } = string.Empty;
        }
    }
}
