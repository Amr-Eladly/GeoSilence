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
        private readonly AccountProfileService _accountProfileService;
        private readonly SearchService _searchService = new();
        private NotifyCollectionChangedEventHandler? _collectionHandler;
        private NotifyCollectionChangedEventHandler? _publicCollectionHandler;
        private Pin? _searchResultPin;
        private Pin? _focusedPlacePin;
        private CancellationTokenSource? _searchCts;
        private Microsoft.Maui.Devices.Sensors.Location? _pendingPlaceLocation;
        private string? _pendingPlaceName;
        private Place? _editingPlace;
        private readonly VisualElement? _placeFormCard;
        private ModeType _selectedMode = ModeType.Silent;
        private ActivationType _selectedActivationType = ActivationType.Automatic;
        private PlaceVisibility _selectedVisibility = PlaceVisibility.Private;
        private double _bottomSheetPanStartHeight;
        private bool _isSearching;
        private bool _isSavingPlace;

        private const double BottomSheetCollapsedHeight = 160;
        private const double BottomSheetExpandedHeight = 360;
        private const double KeyboardFormOffset = -150;

        public MainPage(HomeViewModel vm, AccountProfileService accountProfileService)
        {
            InitializeComponent();
            BindingContext = vm;
            _vm = vm;
            _accountProfileService = accountProfileService;
            _placeFormCard = this.FindByName<VisualElement>("PlaceFormCard");
        }

        private void OnMapClicked(object sender, MapClickedEventArgs e)
        {
            // Allow adding places anywhere on the map by tapping
            var location = new Microsoft.Maui.Devices.Sensors.Location(
                e.Location.Latitude,
                e.Location.Longitude);

            ShowPlaceForm(location);
        }

        private async void OnSearchSubmitted(object sender, EventArgs e)
        {
            var query = PlaceSearchBar.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                ClearSearchState();
                return;
            }

            if (_isSearching)
                return;

            var trimmedQuery = query.Trim();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            SetSearchInProgress(true);

            try
            {
                var result = await _searchService.SearchPlaceAsync(trimmedQuery, token);

                if (token.IsCancellationRequested)
                    return;

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
                UpdateSearchUiState();
                PlaceSearchBar.Unfocus();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    return;

                System.Diagnostics.Debug.WriteLine($"SEARCH ERROR: {ex}");
                await DisplayAlert("Search Error", "Could not search for that place right now.", "OK");
            }
            finally
            {
                if (_searchCts != null && token == _searchCts.Token)
                {
                    _searchCts.Dispose();
                    _searchCts = null;
                    SetSearchInProgress(false);
                }
            }
        }


        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
                ClearSearchState();
        }

        private void RefreshPins()
        {
            MainMap.Pins.Clear();
            _focusedPlacePin = null;

            foreach (var place in _vm.NearbyPlaces.Where(place => place.Visibility == PlaceVisibility.Private).ToList())
            {
                var pin = new Pin
                {
                    Label = place.Name,
                    Address = $"{place.Mode} · {place.Radius:F0} m",
                    Type = PinType.Place,
                    Location = new Microsoft.Maui.Devices.Sensors.Location(
                        place.Latitude,
                        place.Longitude)
                };
                pin.MarkerClicked += OnMapPinClicked;

                MainMap.Pins.Add(pin);

                if (_focusedPlacePin == null &&
                    AreSamePlace(pin.Location, _pendingPlaceLocation) &&
                    string.Equals(pin.Label, _pendingPlaceName, StringComparison.Ordinal))
                {
                    _focusedPlacePin = pin;
                }
            }

            foreach (var place in _vm.PublicPlaces.ToList())
            {
                var pin = new Pin
                {
                    Label = place.Name,
                    Address = $"{place.Mode} · {place.Radius:F0} m",
                    Type = PinType.SearchResult,
                    Location = new Microsoft.Maui.Devices.Sensors.Location(
                        place.Latitude,
                        place.Longitude),
                    BindingContext = place
                };
                pin.MarkerClicked += OnMapPinClicked;
                MainMap.Pins.Add(pin);
            }

            if (_searchResultPin != null)
                MainMap.Pins.Add(_searchResultPin);
        }

        private void OnPlacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RefreshPins();
            });
        }

        private void UpdateSearchUiState()
        {
            // Search result pin is shown on map; user can click it to add via the form
        }

        private void ClearSearchState()
        {
            CancelSearch();
            _pendingPlaceLocation = null;
            _pendingPlaceName = null;

            if (_searchResultPin != null)
            {
                MainMap.Pins.Remove(_searchResultPin);
                _searchResultPin = null;
            }

            UpdateSearchUiState();
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

            var radius = place?.Radius ?? 50;
            PlaceRadiusSlider.Value = radius;
            RadiusValueLabel.Text = $"{(int)radius} m";

            SetSelectedMode(place?.Mode ?? ModeType.Silent);
            SetSelectedActivationType(place?.ActivationType ?? ActivationType.Automatic);
            SetSelectedVisibility(place?.Visibility ?? PlaceVisibility.Private);
            PlacesBottomSheet.HeightRequest = BottomSheetCollapsedHeight;
            PlacesBottomSheet.InputTransparent = true;
            PlaceFormOverlay.IsVisible = true;
        }


        private void OnModeButtonTapped(object sender, TappedEventArgs e)
        {
            if (sender is not Border border ||
                e.Parameter is not string modeStr ||
                !Enum.TryParse<ModeType>(modeStr, out var mode))
                return;

            SetSelectedMode(mode);
        }

        private void SetSelectedMode(ModeType mode)
        {
            _selectedMode = mode;

            // Update border highlighting
            var silentSelected = mode == ModeType.Silent;
            var vibrateSelected = mode == ModeType.Vibrate;
            var normalSelected = mode == ModeType.Normal;

            ModeSilentBorder.StrokeThickness = silentSelected ? 2 : 1;
            ModeVibrateBorder.StrokeThickness = vibrateSelected ? 2 : 1;
            ModeNormalBorder.StrokeThickness = normalSelected ? 2 : 1;

            ModeSilentBorder.BackgroundColor = silentSelected ? Color.FromArgb("#FEE2E2") : Color.FromArgb("#FFFFFF");
            ModeVibrateBorder.BackgroundColor = vibrateSelected ? Color.FromArgb("#FFFBEB") : Color.FromArgb("#FFFFFF");
            ModeNormalBorder.BackgroundColor = normalSelected ? Color.FromArgb("#F0FDF4") : Color.FromArgb("#FFFFFF");
        }

        private void OnRadiusSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            var radiusValue = (int)e.NewValue;
            RadiusValueLabel.Text = $"{radiusValue} m";
        }

        private void SetSelectedActivationType(ActivationType activationType)
        {
            _selectedActivationType = activationType;
            ActivationAutomaticRadio.IsChecked = activationType == ActivationType.Automatic;
            ActivationConfirmRadio.IsChecked = activationType == ActivationType.ConfirmFirst;
        }

        private void SetSelectedVisibility(PlaceVisibility visibility)
        {
            _selectedVisibility = visibility;
            VisibilityPrivateRadio.IsChecked = visibility == PlaceVisibility.Private;
            VisibilityPublicRadio.IsChecked = visibility == PlaceVisibility.Public;
        }

        private void OnActivationTypeCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            if (!e.Value || sender is not RadioButton radioButton)
                return;

            _selectedActivationType = radioButton == ActivationConfirmRadio
                ? ActivationType.ConfirmFirst
                : ActivationType.Automatic;
        }

        private void OnVisibilityCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            if (!e.Value || sender is not RadioButton radioButton)
                return;

            _selectedVisibility = radioButton == VisibilityPublicRadio
                ? PlaceVisibility.Public
                : PlaceVisibility.Private;
        }

        private void OnCancelPlaceFormClicked(object sender, EventArgs e)
        {
            HidePlaceForm();
        }

        private async void OnSavePlaceFormClicked(object sender, EventArgs e)
        {
            if (_isSavingPlace)
                return;

            _isSavingPlace = true;
            SetPlaceSaveState(isBusy: true);

            try
            {
                var saved = await SavePlaceAsync();
                if (!saved)
                    return;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    HidePlaceForm();
                    RefreshPins();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SAVE PLACE ERROR: {ex}");
                await DisplayAlert("Save Error", ex.Message, "OK");
            }
            finally
            {
                _isSavingPlace = false;
                SetPlaceSaveState(isBusy: false);
            }
        }

        private void HidePlaceForm()
        {
            PlaceFormOverlay.IsVisible = false;
            _editingPlace = null;
            PlaceNameEntry.Unfocus();
            if (_placeFormCard != null)
                _placeFormCard.TranslationY = 0;
            PlacesBottomSheet.InputTransparent = false;
        }

        private async Task<bool> SavePlaceAsync()
        {
            if (_pendingPlaceLocation == null)
                return false;

            var name = PlaceNameEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlert("Place", "Enter a place name.", "OK");
                return false;
            }

            var radius = PlaceRadiusSlider.Value;

            if (_editingPlace == null)
            {
                await _vm.AddPlace(
                    _pendingPlaceLocation.Latitude,
                    _pendingPlaceLocation.Longitude,
                    name,
                    _selectedMode,
                    radius,
                    _selectedActivationType,
                    _selectedVisibility);

                ClearSearchState();
            }
            else
            {
                await _vm.UpdatePlace(
                    _editingPlace,
                    name,
                    _selectedMode,
                    radius,
                    _selectedActivationType,
                    _selectedVisibility);
            }

            return true;
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

        private void OnLocateMeClicked(object sender, EventArgs e)
        {
            _ = RecenterOnCurrentLocationAsync();
        }

        private async Task RecenterOnCurrentLocationAsync()
        {
            var loc = await _vm.GetCurrentLocationForMap();
            if (loc == null)
                return;

            MainMap.MoveToRegion(
                MapSpan.FromCenterAndRadius(
                    new Microsoft.Maui.Devices.Sensors.Location(loc.Latitude, loc.Longitude),
                    Distance.FromMeters(500)));
        }

        private void OnPlaceTapped(object sender, TappedEventArgs e)
        {
            if (sender is not Grid grid ||
                grid.BindingContext is not Place place)
                return;

            FocusPlaceOnMap(place);
        }

        private void FocusPlaceOnMap(Place place)
        {
            var location = new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude);

            MainMap.MoveToRegion(
                MapSpan.FromCenterAndRadius(
                    location,
                    Distance.FromMeters(Math.Max(place.Radius * 3, 220))));

            _focusedPlacePin = MainMap.Pins.FirstOrDefault(pin =>
                string.Equals(pin.Label, place.Name, StringComparison.Ordinal) &&
                AreSamePlace(pin.Location, location));
        }

        private async void OnAddPlaceFabClicked(object sender, EventArgs e)
        {
            // If there's a search result pending, add at that location
            if (_pendingPlaceLocation != null)
            {
                ShowPlaceForm(_pendingPlaceLocation, _pendingPlaceName);
                return;
            }

            // Otherwise add at current location
            var loc = await _vm.GetCurrentLocationForMap();
            if (loc == null)
                return;

            var location = new Microsoft.Maui.Devices.Sensors.Location(loc.Latitude, loc.Longitude);
            ShowPlaceForm(location);
        }

        private async void OnAccountAvatarTapped(object sender, TappedEventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(AccountPage));
        }

        private void SetSearchInProgress(bool isSearching)
        {
            _isSearching = isSearching;
            SearchLoadingIndicator.IsVisible = isSearching;
            SearchLoadingIndicator.IsRunning = isSearching;
            PlaceSearchBar.IsEnabled = !isSearching;
            SearchBarBorder.BackgroundColor = Colors.White;
        }

        private void CancelSearch()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            SetSearchInProgress(false);
        }

        private void SetPlaceSaveState(bool isBusy)
        {
            PlaceFormSaveButton.IsEnabled = !isBusy;
            PlaceFormCancelButton.IsEnabled = !isBusy;
            PlaceFormSaveButton.Text = isBusy ? "Saving..." : "Save";
        }

        private static bool AreSamePlace(
            Microsoft.Maui.Devices.Sensors.Location? first,
            Microsoft.Maui.Devices.Sensors.Location? second)
        {
            if (first == null || second == null)
                return false;

            const double tolerance = 0.000001;

            return Math.Abs(first.Latitude - second.Latitude) < tolerance &&
                   Math.Abs(first.Longitude - second.Longitude) < tolerance;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                await _vm.InitializeAsync();

                _collectionHandler = OnPlacesChanged;
                _vm.NearbyPlaces.CollectionChanged += _collectionHandler;
                _publicCollectionHandler = OnPlacesChanged;
                _vm.PublicPlaces.CollectionChanged += _publicCollectionHandler;

                await SetupMapAsync();
                UpdateSearchUiState();
                _accountProfileService.ProfileChanged += OnProfileChanged;
                await LoadAvatarAsync();

                // Load distances immediately before starting polling loop
                await _vm.LoadAsync();

                _ = Task.Run(async () =>
                {
                    await _vm.StartTrackingAsync();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            CancelSearch();
            _accountProfileService.ProfileChanged -= OnProfileChanged;
            _vm.StopTracking();

            if (_collectionHandler != null)
                _vm.NearbyPlaces.CollectionChanged -= _collectionHandler;

            if (_publicCollectionHandler != null)
                _vm.PublicPlaces.CollectionChanged -= _publicCollectionHandler;
        }

        private async void OnMapPinClicked(object? sender, PinClickedEventArgs e)
        {
            if (sender is not Pin pin)
                return;

            if (pin.BindingContext is Place publicPlace && publicPlace.Visibility == PlaceVisibility.Public)
            {
                e.HideInfoWindow = true;
                await ShowPublicPlaceDetailsAsync(publicPlace);
            }
        }

        private async Task ShowPublicPlaceDetailsAsync(Place publicPlace)
        {
            var addToMyPlaces = await DisplayAlert(
                publicPlace.Name,
                $"Mode: {publicPlace.Mode}\nRadius: {publicPlace.Radius:F0} m",
                "Add To My Places",
                "Close");

            if (addToMyPlaces)
                await _vm.AddPublicPlaceToMyPlacesAsync(publicPlace);
        }

        private async Task LoadAvatarAsync()
        {
            var profile = await _accountProfileService.EnsureLoadedAsync();
            var hasPhoto = !string.IsNullOrWhiteSpace(profile.LocalPhotoPath) && File.Exists(profile.LocalPhotoPath);

            AccountAvatarBorder.Background = Color.FromArgb(_accountProfileService.GetAvatarColor(profile));
            AccountAvatarImage.IsVisible = hasPhoto;
            AccountAvatarInitialsLabel.IsVisible = !hasPhoto;

            if (hasPhoto)
            {
                AccountAvatarImage.Source = ImageSource.FromFile(profile.LocalPhotoPath);
                AccountAvatarInitialsLabel.Text = string.Empty;
            }
            else
            {
                AccountAvatarImage.Source = null;
                AccountAvatarInitialsLabel.Text = _accountProfileService.GetInitials(profile);
            }
        }

        private async void OnProfileChanged(object? sender, EventArgs e)
        {
            await MainThread.InvokeOnMainThreadAsync(LoadAvatarAsync);
        }
    }
}
