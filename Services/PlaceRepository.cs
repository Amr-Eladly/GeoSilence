using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class PlaceRepository
    {
        private readonly DatabaseService _db;
        private readonly IAuthenticationService _authenticationService;

        public PlaceRepository(DatabaseService db, IAuthenticationService authenticationService)
        {
            _db = db;
            _authenticationService = authenticationService;
        }

        public async Task InitializeAsync()
        {
            await _db.InitAsync();
        }

        public async Task<List<Place>> GetPlacesAsync()
        {
            var userId = RequireUserId();
            var entities = await _db.GetByOwnerIdAsync(userId);

            return entities.Select(MapToPlace).ToList();
        }

        public async Task<PlaceEntity> AddPlaceAsync(Place place)
        {
            var userId = RequireUserId();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var entity = new PlaceEntity
            {
                CloudId = Guid.NewGuid().ToString("N"),
                Name = place.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Radius = place.Radius,
                Mode = (int)place.Mode,
                IsPublic = false,
                IsDeleted = false,
                IsDirty = true,
                OwnerId = userId,
                CreatedAtUtcMs = now,
                UpdatedAtUtcMs = now,
                Version = 1
            };

            await _db.InsertAsync(entity);
            place.Id = entity.Id;

            return entity;
        }

        public async Task<PlaceEntity> UpdatePlaceAsync(Place place)
        {
            var entity = await GetOwnedEntityAsync(place.Id);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            entity.Name = place.Name;
            entity.Latitude = place.Latitude;
            entity.Longitude = place.Longitude;
            entity.Radius = place.Radius;
            entity.Mode = (int)place.Mode;
            entity.IsPublic = false;
            entity.IsDeleted = false;
            entity.IsDirty = true;
            entity.UpdatedAtUtcMs = now;
            entity.Version = Math.Max(entity.Version, 1) + 1;

            await _db.UpdateAsync(entity);
            return entity;
        }

        public async Task<PlaceEntity> DeletePlaceAsync(int id)
        {
            var entity = await GetOwnedEntityAsync(id);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            entity.IsDeleted = true;
            entity.IsDirty = true;
            entity.UpdatedAtUtcMs = now;
            entity.Version = Math.Max(entity.Version, 1) + 1;

            await _db.UpdateAsync(entity);
            return entity;
        }

        public Task<PlaceEntity?> GetEntityByIdAsync(int id)
        {
            return _db.GetByIdAsync(id);
        }

        public Task<List<PlaceEntity>> GetDirtyEntitiesAsync()
        {
            return _db.GetDirtyByOwnerIdAsync(RequireUserId());
        }

        public async Task EnsureLegacyOwnerMigrationAsync()
        {
            var userId = RequireUserId();
            var entities = await _db.GetAllAsync();
            var legacyRows = entities
                .Where(p => string.Equals(p.OwnerId, "local_user", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(p.OwnerId))
                .ToList();

            if (legacyRows.Count == 0)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var entity in legacyRows)
            {
                entity.OwnerId = userId;

                if (string.IsNullOrWhiteSpace(entity.CloudId))
                    entity.CloudId = Guid.NewGuid().ToString("N");

                entity.IsDirty = true;
                entity.UpdatedAtUtcMs = now;
                if (entity.CreatedAtUtcMs <= 0)
                    entity.CreatedAtUtcMs = now;
                if (entity.Version <= 0)
                    entity.Version = 1;
            }

            await _db.UpdateAllAsync(legacyRows);
        }

        public async Task UpsertLocalFromCloudAsync(PlaceEntity entity)
        {
            var existing = await _db.GetByCloudIdAsync(entity.CloudId);

            if (existing == null)
            {
                await _db.InsertAsync(entity);
                return;
            }

            entity.Id = existing.Id;
            await _db.UpdateAsync(entity);
        }

        public Task DeleteLocalAsync(int id)
        {
            return _db.DeleteAsync(id);
        }

        public async Task<List<PlaceEntity>> GetAllOwnedEntitiesAsync()
        {
            return await _db.GetByOwnerIdAsync(RequireUserId());
        }

        private static Place MapToPlace(PlaceEntity entity)
        {
            return new Place
            {
                Id = entity.Id,
                Name = entity.Name,
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                Radius = entity.Radius,
                Mode = (ModeType)entity.Mode,
                IsActive = true
            };
        }

        private async Task<PlaceEntity> GetOwnedEntityAsync(int id)
        {
            var entity = await _db.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"Place {id} was not found.");

            var userId = RequireUserId();
            if (!string.Equals(entity.OwnerId, userId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("You can only modify your own places.");

            return entity;
        }

        private string RequireUserId()
        {
            return _authenticationService.CurrentUser?.Uid
                ?? throw new InvalidOperationException("A signed-in user is required.");
        }
    }
}
