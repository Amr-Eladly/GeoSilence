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
        private readonly IAuthenticationService _authenticationService;
        private string? _loadedUserId;
        private Task? _backgroundInitialization;
        private int _mutationVersion;

        private CancellationTokenSource? _cts;

        private readonly List<Place> _allPlaces = new();

        private int? _activePlaceId;
        private ModeType? _activeMode;
        private bool _isInZoneSession;
        private DateTime _lastInsideZoneAt = DateTime.MinValue;

        private static readonly TimeSpan ZoneExitGracePeriod = TimeSpan.FromSeconds(12);

        public bool ShowEmptyState => !IsLoading && NearbyPlaces.Count == 0;
        public ObservableCollection<Place> ClosestPlaces { get; } = new();
        public int SavedPlacesCount => NearbyPlaces.Count;
        public string SavedPlacesSummary => $"{SavedPlacesCount} saved location{(SavedPlacesCount == 1 ? "" : "s")}";
        public bool HasMorePlaces => SavedPlacesCount > 3;

        private Place? _selectedPlace;
        public Place? SelectedPlace
        {
            get => _selectedPlace;
            set
            {
                if (_selectedPlace == value) return;
                if (_selectedPlace != null) _selectedPlace.IsSelected = false;
                _selectedPlace = value;
                if (_selectedPlace != null) _selectedPlace.IsSelected = true;
                OnPropertyChanged();
            }
        }

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
            CloudPlaceRepository cloudPlaceRepository,
            IAuthenticationService authenticationService)
        {
            _locationService = locationService;
            _geoService = geoService;
            _modeService = modeService;
            _backgroundGeofenceService = backgroundGeofenceService;
            _repo = repo;
            _syncService = syncService;
            _cloudPlaceRepository = cloudPlaceRepository;
            _authenticationService = authenticationService;
            _authenticationService.AuthStateChanged += OnAuthStateChanged;
        }

        private void OnAuthStateChanged(object? sender, EventArgs e)
        {
            if (_authenticationService.IsSignedIn) return;
            Interlocked.Increment(ref _mutationVersion);
            _loadedUserId = null;
            StopTracking();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SelectedPlace = null;
                _allPlaces.Clear();
                UpdateVisiblePlaces([]);
                PublicPlaces.Clear();
            });
        }

        public async Task InitializeAsync()
        {
            var userId = _authenticationService.CurrentUser?.Uid;
            if (!string.IsNullOrWhiteSpace(userId) && _loadedUserId == userId)
                return;

            await _repo.InitializeAsync();
            var places = await _repo.GetPlacesAsync();

            _allPlaces.Clear();
            _allPlaces.AddRange(places);
            await MainThread.InvokeOnMainThreadAsync(() => UpdateVisiblePlaces(places));

            _loadedUserId = userId;
            IsLoading = false;
            _backgroundInitialization = Task.Run(() => FinishInitializationAsync(userId));
        }

        private async Task FinishInitializationAsync(string? userId)
        {
            var initialMutationVersion = Volatile.Read(ref _mutationVersion);
            try
            {
                await _syncService.SyncAfterLoginAsync();
                if (_authenticationService.CurrentUser?.Uid != userId) return;

                if (Volatile.Read(ref _mutationVersion) == initialMutationVersion)
                {
                    var places = await _repo.GetPlacesAsync();
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (Volatile.Read(ref _mutationVersion) != initialMutationVersion) return;
                        _allPlaces.Clear();
                        _allPlaces.AddRange(places);
                        UpdateVisiblePlaces(places);
                    });
                }
                var privatePlaces = await MainThread.InvokeOnMainThreadAsync(() => GetPrivatePlaces().ToList());
                await _backgroundGeofenceService.RegisterPlacesAsync(privatePlaces);
                await LoadPublicPlacesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BACKGROUND INIT ERROR: {ex}");
            }
        }

        public async Task StartTrackingAsync()
        {
            if (_cts is { IsCancellationRequested: false }) return;
            var cts = new CancellationTokenSource();
            _cts = cts;

            // Background geofencing handles mode changes; the visible list only
            // needs occasional distance updates.
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await LoadAsync();
                    await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"TRACKING ERROR: {ex.Message}");
                    try { await Task.Delay(TimeSpan.FromSeconds(10), cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts.Dispose();
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
            PlaceType placeType,
            PlaceVisibility visibility)
        {
            Interlocked.Increment(ref _mutationVersion);
            var place = new Place
            {
                Name = name,
                Latitude = lat,
                Longitude = lng,
                Radius = radius,
                Mode = mode,
                ActivationType = activationType,
                PlaceType = placeType,
                Visibility = visibility,
                IsActive = true
            };

            // 1. Save to DB and add to local list immediately (fast)
            var entity = await _repo.AddPlaceAsync(place);
            place.Id = entity.Id;
            if (!_allPlaces.Any(existing => existing.Id == place.Id)) _allPlaces.Add(place);

            // 2. Update UI immediately with just distance calculation (fast)
            await LoadAsync();

            // 3. Re-register geofences with the new place (must be done before returning)
            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());

            // 4. Sync now and report explicit failure (local save already succeeded)
            try
            {
                await _syncService.SyncPlaceAsync(entity.Id);
                await LoadPublicPlacesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SYNC PLACE WARNING: {ex.Message}");
            }
        }

        public async Task UpdatePlace(
            Place place,
            string newName,
            ModeType newMode,
            double radius,
            ActivationType activationType,
            PlaceType placeType,
            PlaceVisibility visibility)
        {
            Interlocked.Increment(ref _mutationVersion);
            place.Name = newName;
            place.Mode = newMode;
            place.Radius = radius;
            place.ActivationType = activationType;
            place.PlaceType = placeType;
            place.Visibility = visibility;

            // 1. Save to DB immediately
            await _repo.UpdatePlaceAsync(place);

            // 2. Update UI immediately
            await LoadAsync();

            // 3. Re-register geofences with updated place (must be done before returning)
            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());

            // 4. Sync now and report explicit failure (local update already succeeded)
            try
            {
                await _syncService.SyncPlaceAsync(place.Id);
                await LoadPublicPlacesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SYNC UPDATE WARNING: {ex.Message}");
            }
        }

        public async Task DeletePlace(Place place)
        {
            Interlocked.Increment(ref _mutationVersion);
            // 1. Remove from local list immediately
            _allPlaces.Remove(place);
            if (SelectedPlace?.Id == place.Id) SelectedPlace = null;

            // 2. Mark as deleted in DB
            await _repo.DeletePlaceAsync(place.Id);

            // 3. Update UI immediately
            await LoadAsync();

            // 4. Re-register geofences without the deleted place (must be done before returning)
            await _backgroundGeofenceService.RegisterPlacesAsync(GetPrivatePlaces());

            // 5. Sync now and report explicit failure (local delete already succeeded)
            try
            {
                await _syncService.SyncDeleteAsync(place.Id);
                await LoadPublicPlacesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SYNC DELETE WARNING: {ex.Message}");
                throw new InvalidOperationException(
                    $"Place deleted locally, but cloud sync failed: {ex.Message}",
                    ex);
            }
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
                publicPlace.PlaceType,
                PlaceVisibility.Private);
        }

        public async Task LoadAsync()
        {
            var location = await _locationService.GetCurrentLocationAsync();


            var sorted = _allPlaces
                .Select(p =>
                {
                    if (location != null) p.Distance = Microsoft.Maui.Devices.Sensors.Location.CalculateDistance(
                        location.Latitude,
                        location.Longitude,
                        p.Latitude,
                        p.Longitude,
                        DistanceUnits.Kilometers) * 1000;

                    return p;
                })
                .OrderBy(p => p.Distance)
                .ToList();

            if (location != null) UpdateModeForLocation(location, sorted);

            bool isSame =
                NearbyPlaces.Count == sorted.Count &&
                NearbyPlaces.Select(p => p.Id).SequenceEqual(sorted.Select(p => p.Id));

            if (isSame)
            {
                MainThread.BeginInvokeOnMainThread(() => IsLoading = false);
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateVisiblePlaces(sorted);
                IsLoading = false;
            });
        }

        private void UpdateVisiblePlaces(IEnumerable<Place> places)
        {
            NearbyPlaces.Clear();
            ClosestPlaces.Clear();
            foreach (var place in places)
            {
                NearbyPlaces.Add(place);
                if (ClosestPlaces.Count < 3) ClosestPlaces.Add(place);
            }
            OnPropertyChanged(nameof(SavedPlacesCount));
            OnPropertyChanged(nameof(SavedPlacesSummary));
            OnPropertyChanged(nameof(HasMorePlaces));
            OnPropertyChanged(nameof(ShowEmptyState));
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
                        PlaceType = ParsePlaceType(place.PlaceType),
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

        private static PlaceType ParsePlaceType(string value)
        {
            return Enum.TryParse<PlaceType>(value, true, out var placeType)
                ? placeType
                : PlaceType.Other;
        }
    }
}
