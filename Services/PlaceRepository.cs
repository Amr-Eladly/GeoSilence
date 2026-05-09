using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class PlaceRepository
    {
        private readonly DatabaseService _db;

        public PlaceRepository(DatabaseService db)
        {
            _db = db;
        }

        public async Task InitializeAsync()
        {
            await _db.InitAsync();
        }

        public async Task<List<Place>> GetPlacesAsync(string userId)
        {
            var entities = await _db.GetAllAsync();

            return entities
                .Where(p => p.IsPublic || p.OwnerId == userId)
                .Select(p => new Place
                {
                    Id = p.Id, //REQUIRED
                    Name = p.Name,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    Radius = p.Radius,
                    Mode = (ModeType)p.Mode,
                    IsActive = true
                })
                .ToList();
        }

        public async Task AddPlaceAsync(Place place, string userId)
        {
            var entity = new PlaceEntity
            {
                Name = place.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Radius = place.Radius,
                Mode = (int)place.Mode,
                IsPublic = false,
                OwnerId = userId
            };

            await _db.InsertAsync(entity);

            place.Id = entity.Id; //sync back
        }

        public async Task UpdatePlaceAsync(Place place)
        {
            var entity = new PlaceEntity
            {
                Id = place.Id,
                Name = place.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Radius = place.Radius,
                Mode = (int)place.Mode,
                IsPublic = false,
                OwnerId = "local_user"
            };

            await _db.UpdateAsync(entity);
        }

        public async Task DeletePlaceAsync(int id)
        {
            await _db.DeleteAsync(id); 
        }
    }
}