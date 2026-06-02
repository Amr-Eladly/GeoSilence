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
            await PlaceDatabaseSchema.EnsureMigratedAsync(_db);
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
    }
}
