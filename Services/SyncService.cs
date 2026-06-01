using GeoSilence.Models;
using Microsoft.Extensions.Logging;

namespace GeoSilence.Services
{
    public sealed class SyncService
    {
        private readonly PlaceRepository _placeRepository;
        private readonly CloudPlaceRepository _cloudPlaceRepository;
        private readonly IAuthenticationService _authenticationService;
        private readonly DatabaseService _databaseService;
        private readonly ILogger<SyncService> _logger;
        private readonly SemaphoreSlim _syncLock = new(1, 1);

        public SyncService(
            PlaceRepository placeRepository,
            CloudPlaceRepository cloudPlaceRepository,
            IAuthenticationService authenticationService,
            DatabaseService databaseService,
            ILogger<SyncService> logger)
        {
            _placeRepository = placeRepository;
            _cloudPlaceRepository = cloudPlaceRepository;
            _authenticationService = authenticationService;
            _databaseService = databaseService;
            _logger = logger;
        }

        public Task SyncAfterLoginAsync()
        {
            return RunLockedAsync(async () =>
            {
                var userId = RequireUserId();
                _logger.LogInformation("Sync started for user {UserId}", userId);

                await _placeRepository.InitializeAsync();
                await _placeRepository.EnsureLegacyOwnerMigrationAsync();
                await PushDirtyPlacesAsync(userId);
                await DownloadAndRestorePlacesAsync(userId);

                _logger.LogInformation("Sync completed for user {UserId}", userId);
            });
        }

        public Task SyncPlaceAsync(int localPlaceId)
        {
            return RunLockedAsync(async () =>
            {
                var userId = RequireUserId();
                var entity = await _databaseService.GetByIdAsync(localPlaceId)
                    ?? throw new InvalidOperationException($"Place {localPlaceId} was not found.");

                await SyncEntityAsync(userId, entity);
            });
        }

        public Task SyncDeleteAsync(int localPlaceId)
        {
            return SyncPlaceAsync(localPlaceId);
        }

        private async Task PushDirtyPlacesAsync(string userId)
        {
            var dirtyPlaces = await _databaseService.GetDirtyByOwnerIdAsync(userId);
            foreach (var entity in dirtyPlaces)
            {
                await SyncEntityAsync(userId, entity);
            }
        }

        private async Task SyncEntityAsync(string userId, PlaceEntity entity)
        {
            if (string.IsNullOrWhiteSpace(entity.CloudId))
            {
                entity.CloudId = Guid.NewGuid().ToString("N");
                entity.IsDirty = true;
                await _databaseService.UpdateAsync(entity);
            }

            if (entity.IsDeleted)
            {
                _logger.LogInformation("Cloud delete started for local place {LocalId}", entity.Id);
                await _cloudPlaceRepository.DeletePlaceAsync(userId, entity.CloudId);
                await _databaseService.DeleteAsync(entity.Id);
                _logger.LogInformation("Cloud delete completed for local place {LocalId}", entity.Id);
                return;
            }

            var dto = MapToDto(entity);
            _logger.LogInformation("Cloud upload started for local place {LocalId}", entity.Id);
            await _cloudPlaceRepository.UploadPlaceAsync(userId, dto);

            entity.IsDirty = false;
            entity.LastSyncedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _databaseService.UpdateAsync(entity);
            _logger.LogInformation("Cloud update completed for local place {LocalId}", entity.Id);
        }

        private async Task DownloadAndRestorePlacesAsync(string userId)
        {
            _logger.LogInformation("Cloud download started for user {UserId}", userId);
            var remotePlaces = await _cloudPlaceRepository.DownloadPlacesAsync(userId);
            var remoteIds = new HashSet<string>(remotePlaces.Select(p => p.Id), StringComparer.Ordinal);

            foreach (var remote in remotePlaces)
            {
                await RestorePlaceAsync(userId, remote);
            }

            var localPlaces = await _databaseService.GetByOwnerIdAsync(userId);
            foreach (var local in localPlaces.Where(p => !p.IsDirty && !string.IsNullOrWhiteSpace(p.CloudId) && !remoteIds.Contains(p.CloudId)))
            {
                local.IsDeleted = true;
                local.UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _databaseService.UpdateAsync(local);
                _logger.LogInformation("Cloud restore delete marked locally for place {LocalId}", local.Id);
            }

            _logger.LogInformation("Cloud download completed for user {UserId} with {Count} places", userId, remotePlaces.Count);
        }

        private async Task RestorePlaceAsync(string userId, CloudPlaceDto remote)
        {
            var existing = await _databaseService.GetByCloudIdAsync(remote.Id);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (existing == null)
            {
                var entity = new PlaceEntity
                {
                    CloudId = remote.Id,
                    Name = remote.Name,
                    Latitude = remote.Latitude,
                    Longitude = remote.Longitude,
                    Radius = remote.Radius,
                    Mode = (int)ParseMode(remote.Mode),
                    IsPublic = remote.IsPublic,
                    IsDeleted = remote.Deleted,
                    IsDirty = false,
                    OwnerId = userId,
                    CreatedAtUtcMs = remote.CreatedAtUtcMs <= 0 ? now : remote.CreatedAtUtcMs,
                    UpdatedAtUtcMs = remote.UpdatedAtUtcMs <= 0 ? now : remote.UpdatedAtUtcMs,
                    LastSyncedAtUtcMs = now,
                    Version = remote.Version <= 0 ? 1 : remote.Version
                };

                await _databaseService.InsertAsync(entity);
                _logger.LogInformation("Cloud restore inserted place {CloudId}", remote.Id);
                return;
            }

            if (existing.IsDirty)
            {
                _logger.LogInformation("Cloud restore skipped dirty place {CloudId}", existing.CloudId);
                return;
            }

            existing.Name = remote.Name;
            existing.Latitude = remote.Latitude;
            existing.Longitude = remote.Longitude;
            existing.Radius = remote.Radius;
            existing.Mode = (int)ParseMode(remote.Mode);
            existing.IsPublic = remote.IsPublic;
            existing.IsDeleted = remote.Deleted;
            existing.OwnerId = userId;
            existing.IsDirty = false;
            existing.UpdatedAtUtcMs = remote.UpdatedAtUtcMs <= 0 ? now : remote.UpdatedAtUtcMs;
            existing.LastSyncedAtUtcMs = now;
            existing.Version = remote.Version <= 0 ? existing.Version : remote.Version;

            await _databaseService.UpdateAsync(existing);
            _logger.LogInformation("Cloud restore updated place {CloudId}", remote.Id);
        }

        private static CloudPlaceDto MapToDto(PlaceEntity entity)
        {
            return new CloudPlaceDto
            {
                Id = entity.CloudId,
                OwnerId = entity.OwnerId,
                Name = entity.Name,
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                Radius = entity.Radius,
                Mode = ((ModeType)entity.Mode).ToString(),
                IsActive = true,
                IsPublic = entity.IsPublic,
                Deleted = entity.IsDeleted,
                CreatedAtUtcMs = entity.CreatedAtUtcMs,
                UpdatedAtUtcMs = entity.UpdatedAtUtcMs,
                Version = entity.Version
            };
        }

        private static ModeType ParseMode(string value)
        {
            return Enum.TryParse<ModeType>(value, true, out var mode) ? mode : ModeType.Silent;
        }

        private string RequireUserId()
        {
            return _authenticationService.CurrentUser?.Uid
                ?? throw new InvalidOperationException("A signed-in user is required.");
        }

        private async Task RunLockedAsync(Func<Task> action)
        {
            await _syncLock.WaitAsync();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloud sync failed");
                throw;
            }
            finally
            {
                _syncLock.Release();
            }
        }
    }
}
