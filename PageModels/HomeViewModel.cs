using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoSilence.Models;
using GeoSilence.Services;
using Microsoft.Maui.Networking;

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
        private bool _placeListUpdatesDeferred;
        private List<Place>? _pendingVisiblePlaces;
        private GeoSilence.Models.Location? _lastKnownLocation;

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
            SessionPlaceSchedules.UseModeService(modeService);
            _backgroundGeofenceService = backgroundGeofenceService;
            _repo = repo;
            _syncService = syncService;
            _cloudPlaceRepository = cloudPlaceRepository;
            _authenticationService = authenticationService;
            _authenticationService.AuthStateChanged += OnAuthStateChanged;
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess != NetworkAccess.Internet || !_authenticationService.IsSignedIn)
                return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncService.RetryDirtyPlacesAsync();
                    var userId = _authenticationService.CurrentUser?.Uid;
                    if (userId == null) return;
                    var statuses = await _repo.GetPlacesAsync();
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (_authenticationService.CurrentUser?.Uid != userId) return;
                        var pendingById = statuses.ToDictionary(p => p.Id,
                            p => p.IsCloudSyncPending);
                        foreach (var place in _allPlaces)
                            if (pendingById.TryGetValue(place.Id, out var pending))
                            {
                                place.IsCloudSyncPending = pending;
                                if (!pending) place.HasCloudSyncError = false;
                            }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CLOUD RETRY WARNING: {ex}");
                }
            });
        }

        private void OnAuthStateChanged(object? sender, EventArgs e)
        {
            if (_authenticationService.IsSignedIn) return;
            SessionPlaceSchedules.Current.Clear();
            Interlocked.Increment(ref _mutationVersion);
            _loadedUserId = null;
            StopTracking();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SelectedPlace = null;
                _placeListUpdatesDeferred = false;
                _pendingVisiblePlaces = null;
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
                        MergeReloadedPlaces(places);
                        UpdateVisiblePlaces(_allPlaces);
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
            PlaceVisibility visibility,
            Place? publicSource = null,
            WeeklySchedule? schedule = null)
        {
            if (publicSource != null && FindSavedPublicPlace(publicSource) != null)
                throw new InvalidOperationException("This place is already in your places.");
            var saveTimer = System.Diagnostics.Stopwatch.StartNew();
            Interlocked.Increment(ref _mutationVersion);
            var place = new Place
            {
                CloudId = publicSource == null ? string.Empty : SavedPublicCloudId(publicSource.CloudId) + "_" + Guid.NewGuid().ToString("N"),
                Name = name,
                Latitude = lat,
                Longitude = lng,
                Radius = radius,
                Mode = mode,
                ActivationType = activationType,
                PlaceType = placeType,
                Visibility = publicSource == null ? visibility : PlaceVisibility.Private,
                IsActive = true
            };

            // 1. Save to DB and add to local list immediately (fast)
            var entity = await _repo.AddPlaceAsync(place);
            place.Id = entity.Id;
            if (!_allPlaces.Any(existing => existing.Id == place.Id)) _allPlaces.Add(place);
            SaveSessionSchedule(place, schedule);

            place.IsCloudSyncPending = true;
            place.HasCloudSyncError = false;
            await RefreshVisiblePlacesAsync();
            System.Diagnostics.Debug.WriteLine($"SAVE local ready: {saveTimer.ElapsedMilliseconds} ms");
            QueueSavedPlaceWork(place, geofenceWasPrivate: false);
        }

        public async Task UpdatePlace(
            Place place,
            string newName,
            ModeType newMode,
            double radius,
            ActivationType activationType,
            PlaceType placeType,
            PlaceVisibility visibility,
            WeeklySchedule? schedule = null)
        {
            Interlocked.Increment(ref _mutationVersion);
            var geofenceWasPrivate = place.Visibility == PlaceVisibility.Private;
            place.Name = newName;
            place.Mode = newMode;
            place.Radius = radius;
            place.ActivationType = activationType;
            place.PlaceType = placeType;
            place.Visibility = visibility;
            SaveSessionSchedule(place, schedule);

            // 1. Save to DB immediately
            await _repo.UpdatePlaceAsync(place);

            place.IsCloudSyncPending = true;
            place.HasCloudSyncError = false;
            await RefreshVisiblePlacesAsync();
            QueueSavedPlaceWork(place, geofenceWasPrivate);
        }

        private void SaveSessionSchedule(Place place, WeeklySchedule? schedule)
        {
            if (schedule == null && !SessionPlaceSchedules.Current.HasSchedules) return;
            foreach (var item in _allPlaces)
                SessionPlaceSchedules.Current.Observe(SessionPlaceSchedules.Zone(item),
                    _lastKnownLocation == null ? null : _geoService.IsInside(_lastKnownLocation, item));
            SessionPlaceSchedules.Current.Set(SessionPlaceSchedules.Zone(place), schedule);
            _isInZoneSession = false;
            _activePlaceId = null;
            _activeMode = null;
        }

        private async Task RefreshVisiblePlacesAsync()
        {
            var location = _lastKnownLocation;
            var sorted = _allPlaces.OrderBy(place =>
            {
                if (location != null)
                    place.Distance = Microsoft.Maui.Devices.Sensors.Location.CalculateDistance(
                        location.Latitude, location.Longitude, place.Latitude, place.Longitude,
                        DistanceUnits.Kilometers) * 1000;
                return place.Distance;
            }).ToList();
            await MainThread.InvokeOnMainThreadAsync(() => UpdateVisiblePlaces(sorted));
        }

        private void QueueSavedPlaceWork(Place place, bool geofenceWasPrivate)
        {
            var userId = place.OwnerId;
            var placeId = place.Id;
            var isPrivate = place.Visibility == PlaceVisibility.Private;
            var remainingPrivateIds = GetPrivatePlaces().Select(p => p.Id).ToArray();
            var visibilityChanged = geofenceWasPrivate != isPrivate;
            var geofenceSnapshot = new Place
            {
                Id = place.Id,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Radius = place.Radius,
                Name = place.Name,
                Mode = place.Mode,
                Visibility = place.Visibility,
                IsActive = place.IsActive
            };
            // Start geofence operations in save order. The service runs its
            // platform calls on a worker and serializes them internally.
            _ = UpdateSavedPlaceGeofenceAsync(geofenceSnapshot, geofenceWasPrivate,
                remainingPrivateIds);
            _ = Task.Run(() => SyncSavedPlaceAsync(place, placeId, userId, visibilityChanged));
        }

        private async Task UpdateSavedPlaceGeofenceAsync(Place place, bool wasPrivate,
            IReadOnlyCollection<int> remainingPrivateIds)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (place.Visibility == PlaceVisibility.Private)
                    await _backgroundGeofenceService.UpsertPlaceAsync(place);
                else if (wasPrivate)
                    await _backgroundGeofenceService.RemovePlaceAsync(place.Id, remainingPrivateIds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GEOFENCE UPDATE WARNING: {ex}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine($"SAVE geofence work: {timer.ElapsedMilliseconds} ms");
            }
        }

        private async Task SyncSavedPlaceAsync(Place place, int placeId, string userId,
            bool refreshPublicPlaces)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (_authenticationService.CurrentUser?.Uid != userId) return;
                try
                {
                    await _syncService.SyncPlaceAsync(placeId);
                    var saved = await _repo.GetEntityByIdAsync(placeId);
                    if (saved?.IsDirty == false)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            place.IsCloudSyncPending = false;
                            place.HasCloudSyncError = false;
                        });
                        if (refreshPublicPlaces || place.Visibility == PlaceVisibility.Public)
                            await LoadPublicPlacesAsync();
                    }
                    System.Diagnostics.Debug.WriteLine($"SAVE cloud work: {timer.ElapsedMilliseconds} ms");
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CLOUD SAVE ATTEMPT {attempt + 1}: {ex}");
                    if (attempt < 2)
                        await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));
                }
            }
            // The SQLite row stays dirty; login/startup sync retries it later.
            await MainThread.InvokeOnMainThreadAsync(() => place.HasCloudSyncError = true);
        }

        public async Task DeletePlace(Place place)
        {
            Interlocked.Increment(ref _mutationVersion);
            var wasPrivate = place.Visibility == PlaceVisibility.Private;
            // 1. Remove from local list immediately
            _allPlaces.Remove(place);
            if (SessionPlaceSchedules.Current.HasSchedules) SessionPlaceSchedules.Current.Remove(place.Id);
            if (SelectedPlace?.Id == place.Id) SelectedPlace = null;

            // 2. Mark as deleted in DB
            await _repo.DeletePlaceAsync(place.Id);

            // 3. Update UI immediately
            await RefreshVisiblePlacesAsync();

            // 4. Only the removed private place needs a geofence change.
            if (wasPrivate)
                await _backgroundGeofenceService.RemovePlaceAsync(place.Id,
                    GetPrivatePlaces().Select(p => p.Id).ToArray());

            // 5. Sync now and report explicit failure (local delete already succeeded)
            try
            {
                await _syncService.SyncDeleteAsync(place.Id);
                if (!wasPrivate)
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
                PlaceVisibility.Private, publicPlace);
        }

        // A personal document has its own identity, stable across cloud restores,
        // without modifying the shared document or requiring a new cloud schema.
        private static string SavedPublicCloudId(string sourceId) => "saved_" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(sourceId))).ToLowerInvariant();

        public Place? FindSavedPublicPlace(Place source)
        {
            var prefix = SavedPublicCloudId(source.CloudId) + "_";
            return _allPlaces.FirstOrDefault(place =>
                place.CloudId == source.CloudId || place.CloudId.StartsWith(prefix, StringComparison.Ordinal));
        }

        public async Task LoadAsync()
        {
            var location = await _locationService.GetCurrentLocationAsync();
            if (location != null)
                _lastKnownLocation = location;


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

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateVisiblePlaces(sorted);
                IsLoading = false;
            });
        }

        public event EventHandler? VisiblePlacesUpdated;

        public void SetPlaceListUpdatesDeferred(bool deferred)
        {
            _placeListUpdatesDeferred = deferred;
            if (!deferred && _pendingVisiblePlaces != null)
            {
                var latest = _pendingVisiblePlaces;
                _pendingVisiblePlaces = null;
                UpdateVisiblePlaces(latest);
            }
        }

        private void UpdateVisiblePlaces(IEnumerable<Place> places)
        {
            var desired = places.ToList();
            if (_placeListUpdatesDeferred)
            {
                // Coalesce updates during a gesture; only the latest order matters.
                _pendingVisiblePlaces = desired;
                return;
            }

            var previousCount = NearbyPlaces.Count;
            GeoSilence.Controls.CollectionReconciler.Apply(NearbyPlaces, desired);
            GeoSilence.Controls.CollectionReconciler.Apply(ClosestPlaces, desired.Take(3).ToList());
            VisiblePlacesUpdated?.Invoke(this, EventArgs.Empty);
            if (previousCount != NearbyPlaces.Count)
            {
                OnPropertyChanged(nameof(SavedPlacesCount));
                OnPropertyChanged(nameof(SavedPlacesSummary));
                OnPropertyChanged(nameof(HasMorePlaces));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }

        private void MergeReloadedPlaces(IEnumerable<Place> reloaded)
        {
            // Repository reloads create new objects. Keep the instances bound to cards
            // and update their observable properties instead of rebuilding those cards.
            var existing = _allPlaces.ToDictionary(p => (p.OwnerId, p.Id));
            var merged = new List<Place>();
            foreach (var incoming in reloaded)
            {
                if (!existing.TryGetValue((incoming.OwnerId, incoming.Id), out var place))
                {
                    merged.Add(incoming);
                    continue;
                }
                place.CloudId = incoming.CloudId;
                place.Name = incoming.Name;
                place.Latitude = incoming.Latitude;
                place.Longitude = incoming.Longitude;
                place.Radius = incoming.Radius;
                place.Mode = incoming.Mode;
                place.ActivationType = incoming.ActivationType;
                place.PlaceType = incoming.PlaceType;
                place.Visibility = incoming.Visibility;
                place.IsActive = incoming.IsActive;
                place.IsCloudSyncPending = incoming.IsCloudSyncPending;
                if (!incoming.IsCloudSyncPending) place.HasCloudSyncError = false;
                merged.Add(place);
            }
            _allPlaces.Clear();
            _allPlaces.AddRange(merged);
            if (SelectedPlace != null && !merged.Contains(SelectedPlace)) SelectedPlace = null;
        }

        private void UpdateModeForLocation(
            GeoSilence.Models.Location location,
            List<Place> sorted)
        {
            if (SessionPlaceSchedules.Current.HasSchedules)
            {
                foreach (var place in sorted)
                    SessionPlaceSchedules.Current.Observe(SessionPlaceSchedules.Zone(place), _geoService.IsInside(location, place));
                SessionPlaceSchedules.Current.Evaluate();
                _isInZoneSession = false;
                _activePlaceId = null;
                _activeMode = null;
                return;
            }
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

        public IEnumerable<Place> GetExplorePlaces() => PublicPlaces.Where(place =>
            !string.Equals(place.OwnerId, _authenticationService.CurrentUser?.Uid, StringComparison.Ordinal) &&
            FindSavedPublicPlace(place) == null);

        public async Task<string?> LoadPublicPlacesAsync()
        {
            var userId = _authenticationService.CurrentUser?.Uid;
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
                return ex.Message;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_authenticationService.CurrentUser?.Uid != userId) return;
                PublicPlaces.Clear();
                foreach (var place in mapped)
                    PublicPlaces.Add(place);
            });
            return null;
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
