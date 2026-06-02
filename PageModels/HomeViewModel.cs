using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoSilence.Models;
using GeoSilence.Services;

namespace GeoSilence.PageModels
{
    public partial class HomeViewModel : ObservableObject
    {
        private readonly LocationService _locationService;
        private readonly GeofencingService _geoService;
        private readonly ModeService _modeService;
        private readonly BackgroundGeofenceService _backgroundGeofenceService;
        private readonly PlaceRepository _repo;
        private readonly SyncService _syncService;
        private readonly CloudPlaceRepository _cloudPlaceRepository;

        private CancellationTokenSource? _cts;

        private readonly List<Place> _allPlaces = new();

        private int? _activePlaceId;
        private ModeType? _activeMode;
        private bool _isInZoneSession;
        private DateTime _lastInsideZoneAt = DateTime.MinValue;

        private static readonly TimeSpan ZoneExitGracePeriod = TimeSpan.FromSeconds(12);

        public bool ShowEmptyState => !IsLoading && NearbyPlaces.Count == 0;

        [ObservableProperty]
        private ObservableCollection<Place> nearbyPlaces = new();

        [ObservableProperty]
        private ObservableCollection<Place> publicPlaces = new();

        [ObservableProperty]
        private bool isLoading = true;

        partial void OnNearbyPlacesChanged(ObservableCollection<Place> value)
        {
            OnPropertyChanged(nameof(ShowEmptyState));
        }

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowEmptyState));
        }

        public HomeViewModel(
            LocationService locationService,
            GeofencingService geoService,
            ModeService modeService,
            BackgroundGeofenceService backgroundGeofenceService,
            PlaceRepository repo,
            SyncService syncService,
            CloudPlaceRepository cloudPlaceRepository)
        {
            _locationService = locationService;
            _geoService = geoService;
            _modeService = modeService;
            _backgroundGeofenceService = backgroundGeofenceService;
            _repo = repo;
            _syncService = syncService;
            _cloudPlaceRepository = cloudPlaceRepository;
        }

        public async Task InitializeAsync()
        {
            await _repo.InitializeAsync();
            await _syncService.SyncAfterLoginAsync();
            var places = await _repo.GetPlacesAsync();

            _allPlaces.Clear();
            _allPlaces.AddRange(places);

            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());
            await LoadPublicPlacesAsync();

            await LoadAsync();
        }

        public async Task StartTrackingAsync()
        {
            _cts = new CancellationTokenSource();

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"TRACKING ERROR: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
            }
        }

        public void StopTracking()
        {
            _cts?.Cancel();
        }

        public async Task AddPlace(
            double lat,
            double lng,
            string name,
            ModeType mode,
            double radius,
            ActivationType activationType,
            PlaceVisibility visibility)
        {
            var place = new Place
            {
                Name = name,
                Latitude = lat,
                Longitude = lng,
                Radius = radius,
                Mode = mode,
                ActivationType = activationType,
                Visibility = visibility,
                IsActive = true
            };

            var entity = await _repo.AddPlaceAsync(place);
            _allPlaces.Add(place);

            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());

            try
            {
                await _syncService.SyncPlaceAsync(entity.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SYNC PLACE WARNING: {ex.Message}");
            }
            await LoadPublicPlacesAsync();

            await LoadAsync();
        }

        public async Task UpdatePlace(
            Place place,
            string newName,
            ModeType newMode,
            double radius,
            ActivationType activationType,
            PlaceVisibility visibility)
        {
            place.Name = newName;
            place.Mode = newMode;
            place.Radius = radius;
            place.ActivationType = activationType;
            place.Visibility = visibility;

            await _repo.UpdatePlaceAsync(place);

            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());

            try
            {
                await _syncService.SyncPlaceAsync(place.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SYNC UPDATE WARNING: {ex.Message}");
            }
            await LoadPublicPlacesAsync();

            await LoadAsync();
        }

        public async Task DeletePlace(Place place)
        {
            _allPlaces.Remove(place);

            await _repo.DeletePlaceAsync(place.Id);

            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());

            try
            {
                await _syncService.SyncDeleteAsync(place.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SYNC DELETE WARNING: {ex.Message}");
            }
            await LoadPublicPlacesAsync();

            await LoadAsync();
        }

        public async Task AddPublicPlaceToMyPlacesAsync(Place publicPlace)
        {
            await AddPlace(
                publicPlace.Latitude,
                publicPlace.Longitude,
                publicPlace.Name,
                publicPlace.Mode,
                publicPlace.Radius,
                ActivationType.Automatic,
                PlaceVisibility.Private);
        }

        public async Task LoadAsync()
        {
            var location = await _locationService.GetCurrentLocationAsync();

            if (location == null)
                return;

            var sorted = _allPlaces
                .Select(p =>
                {
                    p.Distance = Microsoft.Maui.Devices.Sensors.Location.CalculateDistance(
                        location.Latitude,
                        location.Longitude,
                        p.Latitude,
                        p.Longitude,
                        DistanceUnits.Kilometers) * 1000;

                    return p;
                })
                .OrderBy(p => p.Distance)
                .ToList();

            UpdateModeForLocation(location, sorted);

            bool isSame =
                NearbyPlaces.Count == sorted.Count &&
                NearbyPlaces.Select(p => p.Id).SequenceEqual(sorted.Select(p => p.Id));

            if (isSame)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                NearbyPlaces.Clear();

                foreach (var place in sorted)
                    NearbyPlaces.Add(place);

                IsLoading = false;
            });
        }

        private void UpdateModeForLocation(
            GeoSilence.Models.Location location,
            List<Place> sorted)
        {
            var activePlace = sorted
                .Where(place => place.IsActive &&
                                place.Visibility == PlaceVisibility.Private &&
                                place.ActivationType == ActivationType.Automatic)
                .FirstOrDefault(place => _geoService.IsInside(location, place));

            if (activePlace == null)
            {
                if (_isInZoneSession &&
                    DateTime.UtcNow - _lastInsideZoneAt >= ZoneExitGracePeriod)
                {
                    _modeService.RestoreOriginalMode();

                    _isInZoneSession = false;
                    _activePlaceId = null;
                    _activeMode = null;
                }

                return;
            }

            _isInZoneSession = true;
            _lastInsideZoneAt = DateTime.UtcNow;

            if (_activePlaceId == activePlace.Id &&
                _activeMode == activePlace.Mode)
                return;

            _modeService.ApplyZoneMode(activePlace.Mode);

            _activePlaceId = activePlace.Id;
            _activeMode = activePlace.Mode;

            Console.WriteLine($"ENTERED: {activePlace.Name}");
            Console.WriteLine($"SETTING MODE: {activePlace.Mode}");
        }

        public async Task<GeoSilence.Models.Location?> GetCurrentLocationForMap()
        {
            return await _locationService.GetCurrentLocationAsync();
        }

        private IEnumerable<Place> GetPrivatePlaces()
        {
            return _allPlaces.Where(place => place.Visibility == PlaceVisibility.Private);
        }

        private async Task LoadPublicPlacesAsync()
        {
            List<Place> mapped;

            try
            {
                var remotePlaces = await _cloudPlaceRepository.DownloadPublicPlacesAsync();
                mapped = remotePlaces
                    .Where(place => !place.Deleted)
                    .Select(place => new Place
                    {
                        CloudId = place.Id,
                        OwnerId = place.OwnerId,
                        Name = place.Name,
                        Latitude = place.Latitude,
                        Longitude = place.Longitude,
                        Radius = place.Radius,
                        Mode = ParseMode(place.Mode),
                        ActivationType = ParseActivationType(place.ActivationType),
                        Visibility = PlaceVisibility.Public,
                        IsActive = false
                    })
                    .OrderBy(place => place.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PUBLIC PLACES LOAD WARNING: {ex.Message}");
                mapped = new List<Place>();
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                PublicPlaces.Clear();
                foreach (var place in mapped)
                    PublicPlaces.Add(place);
            });
        }

        private static ModeType ParseMode(string value)
        {
            return Enum.TryParse<ModeType>(value, true, out var mode) ? mode : ModeType.Silent;
        }

        private static ActivationType ParseActivationType(string value)
        {
            return Enum.TryParse<ActivationType>(value, true, out var activationType)
                ? activationType
                : ActivationType.Automatic;
        }
    }
}
