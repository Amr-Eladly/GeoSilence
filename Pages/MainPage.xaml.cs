using System.Collections.Specialized;
using GeoSilence.Models;
using GeoSilence.PageModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace GeoSilence.Pages
{
    public partial class MainPage : ContentPage
    {
        private readonly HomeViewModel _vm;
        private NotifyCollectionChangedEventHandler? _collectionHandler;

        private DateTime _lastRefresh = DateTime.MinValue;

        public MainPage(HomeViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            _vm = vm;
        }

        private async void OnMapClicked(object sender, MapClickedEventArgs e)
        {
            var location = e.Location;

            var name = await DisplayPromptAsync("New Place", "Enter place name:");

            if (string.IsNullOrWhiteSpace(name))
                return;

            string action = await DisplayActionSheet(
                "Select Mode",
                "Cancel",
                null,
                "Silent",
                "Vibrate",
                "Normal");

            ModeType mode = action switch
            {
                "Silent" => ModeType.Silent,
                "Vibrate" => ModeType.Vibrate,
                _ => ModeType.Normal
            };

            await _vm.AddPlace(location.Latitude, location.Longitude, name, mode);
        }

        private void RefreshPins()
        {
            MainMap.Pins.Clear();

            foreach (var place in _vm.NearbyPlaces.ToList())
            {
                MainMap.Pins.Add(new Pin
                {
                    Label = place.Name,
                    Location = new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude)
                });
            }
        }

        private void OnPlacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RefreshPins();
            });
        }

        private async Task EditPlace(Place place)
        {
            var newName = await DisplayPromptAsync("Edit Name", "Enter new name:", initialValue: place.Name);

            if (string.IsNullOrWhiteSpace(newName))
                return;

            string action = await DisplayActionSheet(
                "Select Mode",
                "Cancel",
                null,
                "Silent",
                "Vibrate",
                "Normal");

            ModeType newMode = action switch
            {
                "Silent" => ModeType.Silent,
                "Vibrate" => ModeType.Vibrate,
                _ => ModeType.Normal
            };

            await _vm.UpdatePlace(place, newName, newMode);
        }

        private async void OnEditSwipe(object sender, EventArgs e)
        {
            if (sender is SwipeItem item && item.CommandParameter is Place place)
            {
                await EditPlace(place);
            }
        }

        private async void OnDeleteSwipe(object sender, EventArgs e)
        {
            if (sender is SwipeItem item && item.CommandParameter is Place place)
            {
                bool confirm = await DisplayAlert(
                    "Delete",
                    $"Delete {place.Name}?",
                    "Yes",
                    "No");

                if (confirm)
                {
                    await _vm.DeletePlace(place);
                }
            }
        }

        private async Task SetupMapAsync()
        {
            var loc = await _vm.GetCurrentLocationForMap();

            if (loc != null)
            {
                MainMap.MoveToRegion(
                    MapSpan.FromCenterAndRadius(
                        new Microsoft.Maui.Devices.Sensors.Location(loc.Latitude, loc.Longitude),
                        Distance.FromMeters(500)));
            }

            RefreshPins();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                await _vm.InitializeAsync();

                // 🔥 attach listener BEFORE UI update
                _collectionHandler = OnPlacesChanged;
                _vm.NearbyPlaces.CollectionChanged += _collectionHandler;

                await SetupMapAsync();

                // 🔥 start tracking AFTER UI is ready
                _ = Task.Run(async () =>
                {
                    await _vm.StartTrackingAsync();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.ToString(), "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            _vm.StopTracking();

            if (_collectionHandler != null)
                _vm.NearbyPlaces.CollectionChanged -= _collectionHandler;
        }
    }
}