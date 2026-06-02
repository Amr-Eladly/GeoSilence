using GeoSilence.Models;
using Microsoft.Extensions.Logging;

namespace GeoSilence.Services
{
    public sealed class CloudPlaceRepository
    {
        private readonly FirestoreService _firestoreService;
        private readonly ILogger<CloudPlaceRepository> _logger;

        public CloudPlaceRepository(FirestoreService firestoreService, ILogger<CloudPlaceRepository> logger)
        {
            _firestoreService = firestoreService;
            _logger = logger;
        }

        public Task<IReadOnlyList<CloudPlaceDto>> DownloadPrivatePlacesAsync(string userId)
        {
            _logger.LogInformation("Private cloud download requested for user {UserId}", userId);
            return _firestoreService.DownloadPrivatePlacesAsync(userId);
        }

        public Task<IReadOnlyList<CloudPlaceDto>> DownloadPublicPlacesAsync()
        {
            _logger.LogInformation("Public cloud download requested");
            return _firestoreService.DownloadPublicPlacesAsync();
        }

        public async Task UploadPrivatePlaceAsync(string userId, CloudPlaceDto place)
        {
            _logger.LogInformation("Private cloud upload requested for place {CloudId}", place.Id);
            await _firestoreService.UploadPrivatePlaceAsync(userId, place);
        }

        public async Task UploadPublicPlaceAsync(CloudPlaceDto place)
        {
            _logger.LogInformation("Public cloud upload requested for place {CloudId}", place.Id);
            await _firestoreService.UploadPublicPlaceAsync(place);
        }

        public async Task DeletePrivatePlaceAsync(string userId, string cloudId, bool ignoreNotFound = false)
        {
            _logger.LogInformation("Private cloud delete requested for place {CloudId}", cloudId);
            await _firestoreService.DeletePrivatePlaceAsync(userId, cloudId, ignoreNotFound);
        }

        public async Task DeletePublicPlaceAsync(string cloudId, bool ignoreNotFound = false)
        {
            _logger.LogInformation("Public cloud delete requested for place {CloudId}", cloudId);
            await _firestoreService.DeletePublicPlaceAsync(cloudId, ignoreNotFound);
        }
    }
}
