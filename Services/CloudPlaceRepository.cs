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

        public Task<IReadOnlyList<CloudPlaceDto>> DownloadPlacesAsync(string userId)
        {
            _logger.LogInformation("Cloud download requested for user {UserId}", userId);
            return _firestoreService.DownloadPlacesAsync(userId);
        }

        public async Task UploadPlaceAsync(string userId, CloudPlaceDto place)
        {
            _logger.LogInformation("Cloud upload requested for place {CloudId}", place.Id);
            await _firestoreService.UploadPlaceAsync(userId, place);
        }

        public async Task DeletePlaceAsync(string userId, string cloudId)
        {
            _logger.LogInformation("Cloud delete requested for place {CloudId}", cloudId);
            await _firestoreService.DeletePlaceAsync(userId, cloudId);
        }
    }
}
