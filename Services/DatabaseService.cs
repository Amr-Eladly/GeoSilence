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

        public async Task InitAsync()
        {
            if (_db != null)
                return;

            var path = Path.Combine(FileSystem.AppDataDirectory, "geosilence.db");

            _db = new SQLiteAsyncConnection(path);

            await _db.CreateTableAsync<PlaceEntity>();
        }

        public Task<List<PlaceEntity>> GetAllAsync()
        {
            return _db!.Table<PlaceEntity>().ToListAsync();
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
    }
}