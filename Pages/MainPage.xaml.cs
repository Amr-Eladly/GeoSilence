using System.Collections.Specialized;
using System.Globalization;
using GeoSilence.Models;
using GeoSilence.PageModels;
using GeoSilence.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;

namespace GeoSilence.Pages
{
    public partial class MainPage : ContentPage
    {
        private readonly HomeViewModel _vm;
        private readonly SearchService _searchService = new();
        private NotifyCollectionChangedEventHandler? _collectionHandler;
        private Pin? _searchResultPin;
        private Microsoft.Maui.Devices.Sensors.Location? _pendingPlaceLocation;
        private string? _pendingPlaceName;
        private Place? _editingPlace;
        private readonly VisualElement? _placeFormCard;
        private ModeType _selectedMode = ModeType.Silent;
        private double _bottomSheetPanStartHeight;

        private const double BottomSheetCollapsedHeight = 160;
        private const double BottomSheetExpandedHeight = 360;
        private const double KeyboardFormOffset = -230;

        public MainPage(HomeViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            _vm = vm;
            _placeFormCard = this.FindByName<VisualElement>("PlaceFormCard");
        }

        private void OnMapClicked(object sender, MapClickedEventArgs e)
        {
            ShowPlaceForm(e.Location);
        }

        private async void OnSearchSubmitted(object sender, EventArgs e)
        {
            var query = PlaceSearchBar.Text;

            if (string.IsNullOrWhiteSpace(query))
                return;

            try
            {
                var result =
                    await _searchService.SearchPlaceAsync(query);

                if (result == null)
                {
                    await DisplayAlert("Search", "No place found.", "OK");
                    return;
                }

                var location = new Microsoft.Maui.Devices.Sensors.Location(
                    result.Latitude,
                    result.Longitude);

                if (_searchResultPin != null)
                    MainMap.Pins.Remove(_searchResultPin);

                _searchResultPin = new Pin
                {
                    Label = result.Name,
                    Location = location
                };

                MainMap.Pins.Add(_searchResultPin);
                MainMap.MoveToRegion(
                    MapSpan.FromCenterAndRadius(
                        location,
                        Distance.FromMeters(700)));

                _pendingPlaceLocation = location;
                _pendingPlaceName = result.Name;
                AddSearchResultButton.IsVisible = true;
                PlaceSearchBar.Unfocus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SEARCH ERROR: {ex}");
                await DisplayAlert("Search Error", "Could not search for that place right now.", "OK");
            }
        }

        private void OnAddSearchResultClicked(object sender, EventArgs e)
        {
            if (_pendingPlaceLocation == null)
                return;

            ShowPlaceForm(_pendingPlaceLocation, _pendingPlaceName);
        }

        private void RefreshPins()
        {
            MainMap.Pins.Clear();

            foreach (var place in _vm.NearbyPlaces.ToList())
            {
                MainMap.Pins.Add(new Pin
                {
                    Label = place.Name,
                    Location = new Microsoft.Maui.Devices.Sensors.Location(
                        place.Latitude,
                        place.Longitude)
                });
            }

            if (_searchResultPin != null)
                MainMap.Pins.Add(_searchResultPin);
        }

        private void OnPlacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(RefreshPins);
        }

        private void OnEditSwipe(object sender, EventArgs e)
        {
            if (GetSwipePlace(sender) is Place place)
            {
                ShowPlaceForm(
                    new Microsoft.Maui.Devices.Sensors.Location(
                        place.Latitude,
                        place.Longitude),
                    place.Name,
                    place);
            }
        }

        private async void OnDeleteSwipe(object sender, EventArgs e)
        {
            if (GetSwipePlace(sender) is Place place)
            {
                bool confirm = await DisplayAlert(
                    "Delete",
                    $"Delete {place.Name}?",
                    "Yes",
                    "No");

                if (confirm)
                    await _vm.DeletePlace(place);
            }
        }

        private static Place? GetSwipePlace(object sender)
        {
            return sender switch
            {
                SwipeItem swipeItem => swipeItem.CommandParameter as Place,
                SwipeItemView swipeItemView => swipeItemView.CommandParameter as Place,
                _ => null
            };
        }

        private void ShowPlaceForm(
            Microsoft.Maui.Devices.Sensors.Location location,
            string? suggestedName = null,
            Place? place = null)
        {
            _pendingPlaceLocation = location;
            _editingPlace = place;

            PlaceFormTitle.Text = place == null ? "Add Place" : "Edit Place";
            PlaceNameEntry.Text = suggestedName ?? string.Empty;
            PlaceRadiusEntry.Text = place?.Radius.ToString("F0", CultureInfo.InvariantCulture) ?? "50";
            SetSelectedMode(place?.Mode ?? ModeType.Silent);
            PlaceModeDropdown.IsVisible = false;
            PlacesBottomSheet.HeightRequest = BottomSheetCollapsedHeight;
            PlacesBottomSheet.InputTransparent = true;
            PlaceFormOverlay.IsVisible = true;
        }

        private async void OnPlaceFormInputFocused(object sender, FocusEventArgs e)
        {
            if (_placeFormCard != null)
                await _placeFormCard.TranslateTo(0, KeyboardFormOffset, 120, Easing.CubicOut);
        }

        private async void OnPlaceFormInputUnfocused(object sender, FocusEventArgs e)
        {
            if (_placeFormCard != null)
                await _placeFormCard.TranslateTo(0, 0, 120, Easing.CubicOut);
        }

        private void OnModeDropdownClicked(object sender, EventArgs e)
        {
            PlaceModeDropdown.IsVisible = !PlaceModeDropdown.IsVisible;
        }

        private void OnModeOptionClicked(object sender, EventArgs e)
        {
            if (sender is not Button button ||
                !Enum.TryParse<ModeType>(button.Text, out var mode))
                return;

            SetSelectedMode(mode);
            PlaceModeDropdown.IsVisible = false;
        }

        private void SetSelectedMode(ModeType mode)
        {
            _selectedMode = mode;
            PlaceModeButton.Text = mode.ToString();
        }

        private void OnCancelPlaceFormClicked(object sender, EventArgs e)
        {
            HidePlaceForm();
        }

        private async void OnSavePlaceFormClicked(object sender, EventArgs e)
        {
            if (_pendingPlaceLocation == null)
                return;

            var name = PlaceNameEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlert("Place", "Enter a place name.", "OK");
                return;
            }

            if (!double.TryParse(
                    PlaceRadiusEntry.Text,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var radius) ||
                radius <= 0)
            {
                await DisplayAlert("Place", "Enter a valid radius in meters.", "OK");
                return;
            }

            if (_editingPlace == null)
            {
                await _vm.AddPlace(
                    _pendingPlaceLocation.Latitude,
                    _pendingPlaceLocation.Longitude,
                    name,
                    _selectedMode,
                    radius);

                AddSearchResultButton.IsVisible = false;
            }
            else
            {
                await _vm.UpdatePlace(
                    _editingPlace,
                    name,
                    _selectedMode,
                    radius);
            }

            HidePlaceForm();
        }

        private void HidePlaceForm()
        {
            PlaceFormOverlay.IsVisible = false;
            _editingPlace = null;
            PlaceModeDropdown.IsVisible = false;
            if (_placeFormCard != null)
                _placeFormCard.TranslationY = 0;
            PlacesBottomSheet.InputTransparent = false;
        }

        private void OnBottomSheetHandleTapped(object sender, TappedEventArgs e)
        {
            if (PlaceFormOverlay.IsVisible)
                return;

            ToggleBottomSheet();
        }

        private void OnBottomSheetPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            if (PlaceFormOverlay.IsVisible)
                return;

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _bottomSheetPanStartHeight = PlacesBottomSheet.HeightRequest;
                    break;

                case GestureStatus.Running:
                    PlacesBottomSheet.HeightRequest = Math.Clamp(
                        _bottomSheetPanStartHeight - e.TotalY,
                        BottomSheetCollapsedHeight,
                        BottomSheetExpandedHeight);
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    PlacesBottomSheet.HeightRequest =
                        PlacesBottomSheet.HeightRequest >
                        (BottomSheetCollapsedHeight + BottomSheetExpandedHeight) / 2
                            ? BottomSheetExpandedHeight
                            : BottomSheetCollapsedHeight;
                    break;
            }
        }

        private void ToggleBottomSheet()
        {
            PlacesBottomSheet.HeightRequest =
                PlacesBottomSheet.HeightRequest <= BottomSheetCollapsedHeight
                    ? BottomSheetExpandedHeight
                    : BottomSheetCollapsedHeight;
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

                _collectionHandler = OnPlacesChanged;
                _vm.NearbyPlaces.CollectionChanged += _collectionHandler;

                await SetupMapAsync();

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
