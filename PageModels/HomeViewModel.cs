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
        private readonly PlaceRepository _repo;

        private CancellationTokenSource? _cts;

        private const string USER_ID = "local_user";

        private readonly List<Place> _allPlaces = new();

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
            PlaceRepository repo)
        {
            _locationService = locationService;
            _geoService = geoService;
            _modeService = modeService;
            _repo = repo;
        }

        public async Task InitializeAsync()
        {
            await _repo.InitializeAsync();

            var places = await _repo.GetPlacesAsync(USER_ID);

            _allPlaces.Clear();
            _allPlaces.AddRange(places);

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

                await Task.Delay(5000, _cts.Token);
            }
        }

        public void StopTracking()
        {
            _cts?.Cancel();
        }

        public async Task AddPlace(double lat, double lng, string name, ModeType mode)
        {
            var place = new Place
            {
                Name = name,
                Latitude = lat,
                Longitude = lng,
                Radius = 100,
                Mode = mode,
                IsActive = true
            };

            _allPlaces.Add(place);

            await _repo.AddPlaceAsync(place, USER_ID);

            await LoadAsync();
        }

        public async Task UpdatePlace(Place place, string newName, ModeType newMode)
        {
            place.Name = newName;
            place.Mode = newMode;

            await _repo.UpdatePlaceAsync(place);

            await LoadAsync();
        }

        public async Task DeletePlace(Place place)
        {
            _allPlaces.Remove(place);

            await _repo.DeletePlaceAsync(place.Id);

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

            foreach (var place in sorted)
            {
                if (_geoService.IsInside(location, place))
                {
                    _modeService.SetMode(place.Mode);
                    Console.WriteLine($"ENTERED: {place.Name}");
                    Console.WriteLine($"SETTING MODE: {place.Mode}");
                }
            }
        }

        public async Task<GeoSilence.Models.Location?> GetCurrentLocationForMap()
        {
            return await _locationService.GetCurrentLocationAsync();
        }
    }
}