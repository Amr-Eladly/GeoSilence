using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class DatabaseService
    {
        private SQLiteAsyncConnection? _db;
        private const string TableName = nameof(PlaceEntity);

        public async Task InitAsync()
        {
            if (_db != null)
                return;

            var path = Path.Combine(FileSystem.AppDataDirectory, "geosilence.db");

            _db = new SQLiteAsyncConnection(path);

            await _db.CreateTableAsync<PlaceEntity>();
            await EnsureColumnAsync(nameof(PlaceEntity.CloudId), "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(nameof(PlaceEntity.IsDeleted), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(nameof(PlaceEntity.IsDirty), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(nameof(PlaceEntity.CreatedAtUtcMs), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(nameof(PlaceEntity.UpdatedAtUtcMs), "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(nameof(PlaceEntity.LastSyncedAtUtcMs), "INTEGER NULL");
            await EnsureColumnAsync(nameof(PlaceEntity.Version), "INTEGER NOT NULL DEFAULT 1");

            await BackfillLegacyRowsAsync();
        }

        public Task<List<PlaceEntity>> GetAllAsync()
        {
            return _db!.Table<PlaceEntity>().ToListAsync();
        }

        public Task<List<PlaceEntity>> GetByOwnerIdAsync(string ownerId)
        {
            return _db!.Table<PlaceEntity>()
                .Where(p => p.OwnerId == ownerId && !p.IsDeleted)
                .ToListAsync();
        }

        public Task<List<PlaceEntity>> GetDirtyByOwnerIdAsync(string ownerId)
        {
            return _db!.Table<PlaceEntity>()
                .Where(p => p.OwnerId == ownerId && p.IsDirty)
                .ToListAsync();
        }

        public Task<PlaceEntity?> GetByIdAsync(int id)
        {
            return _db!.FindAsync<PlaceEntity>(id);
        }

        public Task<PlaceEntity?> GetByCloudIdAsync(string cloudId)
        {
            return _db!.Table<PlaceEntity>()
                .Where(p => p.CloudId == cloudId)
                .FirstOrDefaultAsync();
        }

        public Task<int> InsertAsync(PlaceEntity place)
        {
            return _db!.InsertAsync(place);
        }

        public Task<int> UpdateAsync(PlaceEntity place)
        {
            return _db!.UpdateAsync(place);
        }

        public Task<int> DeleteAsync(int id)
        {
            return _db!.DeleteAsync<PlaceEntity>(id);
        }

        public async Task<int> UpdateAllAsync(IEnumerable<PlaceEntity> places)
        {
            var count = 0;

            foreach (var place in places)
                count += await _db!.UpdateAsync(place);

            return count;
        }

        public Task<int> ExecuteAsync(string sql, params object[] args)
        {
            return _db!.ExecuteAsync(sql, args);
        }

        private async Task EnsureColumnAsync(string columnName, string columnSql)
        {
            var columns = await _db!.QueryAsync<ColumnInfo>($"PRAGMA table_info({TableName})");

            if (columns.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)))
                return;

            await _db.ExecuteAsync($"ALTER TABLE {TableName} ADD COLUMN {columnName} {columnSql}");
        }

        private async Task BackfillLegacyRowsAsync()
        {
            var rows = await _db!.Table<PlaceEntity>().ToListAsync();
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
            }

            if (changed)
                await _db.UpdateAllAsync(rows);
        }

        private sealed class ColumnInfo
        {
            public string Name { get; set; } = "";
        }
    }
}
