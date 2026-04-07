using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoSilence.Models;
using GeoSilence.Services;

namespace GeoSilence.PageModels
{
    public partial class HomeViewModel : ObservableObject
    {
        private readonly LocationService _locationService;
        private readonly PlaceManagerService _placeManager;
        private readonly GeofencingService _geoService;
        private readonly ModeService _modeService;

        [ObservableProperty]
        private ObservableCollection<Place> nearbyPlaces = new();

        public HomeViewModel(
            LocationService locationService,
            PlaceManagerService placeManager,
            GeofencingService geoService,
            ModeService modeService)
        {
            _locationService = locationService;
            _placeManager = placeManager;
            _geoService = geoService;
            _modeService = modeService;
        }

        private List<Place> GetMockPlaces()
        {
            return new List<Place>
            {
                new Place
                {
                    Name = "University",
                    Latitude = 47.4979,
                    Longitude = 19.0402,
                    Radius = 100,
                    Mode = ModeType.Silent
                }
            };
        }

        public async Task LoadAsync()
        {
            var location = await _locationService.GetCurrentLocationAsync();

            if (location == null)
            {
                Console.WriteLine("Location is NULL");
                return;
            }

            Console.WriteLine($"Location: {location.Latitude}, {location.Longitude}");

            var places = GetMockPlaces();

            Console.WriteLine($"Places BEFORE filter: {places.Count}");

            // TEMP: bypass filtering
            NearbyPlaces = new ObservableCollection<Place>(places);

            Console.WriteLine($"Places AFTER filter: {NearbyPlaces.Count}");
        }
    }
}