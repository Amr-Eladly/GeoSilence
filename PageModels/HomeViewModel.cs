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
            SyncService syncService)
        {
            _locationService = locationService;
            _geoService = geoService;
            _modeService = modeService;
            _backgroundGeofenceService = backgroundGeofenceService;
            _repo = repo;
            _syncService = syncService;
        }

        public async Task InitializeAsync()
        {
            await _repo.InitializeAsync();
            await _syncService.SyncAfterLoginAsync();
            var places = await _repo.GetPlacesAsync();

            _allPlaces.Clear();
            _allPlaces.AddRange(places);

            await _backgroundGeofenceService.RegisterPlacesAsync(_allPlaces);

            await LoadAsync(); // ✅ correct initial load
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

        public async Task AddPlace(double lat, double lng, string name, ModeType mode, double radius)
        {
            var place = new Place
            {
                Name = name,
                Latitude = lat,
                Longitude = lng,
                Radius = radius,
                Mode = mode,
                IsActive = true
            };

            var entity = await _repo.AddPlaceAsync(place);
            _allPlaces.Add(place);

            await _backgroundGeofenceService.RegisterPlacesAsync(_allPlaces);

            await _syncService.SyncPlaceAsync(entity.Id);

            await LoadAsync();
        }

        public async Task UpdatePlace(Place place, string newName, ModeType newMode, double radius)
        {
            place.Name = newName;
            place.Mode = newMode;
            place.Radius = radius;

            await _repo.UpdatePlaceAsync(place);

            await _backgroundGeofenceService.RegisterPlacesAsync(_allPlaces);

            await _syncService.SyncPlaceAsync(place.Id);

            await LoadAsync();
        }

        public async Task DeletePlace(Place place)
        {
            _allPlaces.Remove(place);

            await _repo.DeletePlaceAsync(place.Id);

            await _backgroundGeofenceService.RegisterPlacesAsync(_allPlaces);

            await _syncService.SyncDeleteAsync(place.Id);

            await LoadAsync();
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
                .Where(place => place.IsActive)
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
    }
}
