using System.Collections.Specialized;
using System.Globalization;
using GeoSilence.Models;
using GeoSilence.Controls;
using GeoSilence.PageModels;
using GeoSilence.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;

namespace GeoSilence.Pages
{
    public partial class MainPage : ContentPage, IQueryAttributable
    {
        private readonly HomeViewModel _vm;
        private readonly AccountProfileService _accountProfileService;
        private readonly IAuthenticationService _authenticationService;
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
        private PlaceType _selectedPlaceType = PlaceType.Other;
        private double _bottomSheetPanStartTranslation;
        private bool _sheetExpanded;
        private bool _isSearching;
        private bool _isSavingPlace;
        private bool _pinRefreshScheduled;
        private readonly HashSet<SwipeView> _activePlaceSwipes = new();
        private bool _isDraggingSheet;
        private string? _mapInitializedForUserId;
        private double _sheetDragOffset;

        private const double BottomSheetCollapsedHeight = 104;
        private double BottomSheetExpandedHeight => Math.Max(220, Math.Min(Height * 0.52, 130 + _vm.ClosestPlaces.Count * 60));
        private double BottomSheetCollapsedTranslation => BottomSheetExpandedHeight - BottomSheetCollapsedHeight;
        private int? _routeFocusId;
        private int? _routeEditId;
        private const double KeyboardFormOffset = -150;

        public MainPage(HomeViewModel vm, AccountProfileService accountProfileService, IAuthenticationService authenticationService)
        {
            InitializeComponent();
            BindingContext = vm;
            _vm = vm;
            _accountProfileService = accountProfileService;
            _authenticationService = authenticationService;
            _placeFormCard = this.FindByName<VisualElement>("PlaceFormCard");
            SizeChanged += (_, _) => UpdateBottomSheetLayout();
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("focusPlaceId", out var focus) && int.TryParse(focus?.ToString(), out var focusId)) _routeFocusId = focusId;
            if (query.TryGetValue("editPlaceId", out var edit) && int.TryParse(edit?.ToString(), out var editId)) _routeEditId = editId;
        }

        private void OnMapClicked(object sender, MapClickedEventArgs e)
        {
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

            foreach (var place in _vm.NearbyPlaces.ToList())
            {
                var pin = new PlaceMapPin
                {
                    Label = place.Name,
                    Address = $"{place.Mode} · {place.Radius:F0} m",
                    Type = PinType.Place,
                    ImageSource = ImageSource.FromFile(GetPlaceTypeMarkerImage(place.PlaceType)),
                    Location = new Microsoft.Maui.Devices.Sensors.Location(
                        place.Latitude,
                        place.Longitude),
                    BindingContext = place
                };
                pin.MarkerClicked += OnMapPinClicked;

                MainMap.Pins.Add(pin);

                if (_vm.SelectedPlace?.Id == place.Id) _focusedPlacePin = pin;
            }

            var ownedCloudIds = _vm.NearbyPlaces
                .Where(place => !string.IsNullOrWhiteSpace(place.CloudId))
                .Select(place => place.CloudId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var place in _vm.PublicPlaces.Where(place => !ownedCloudIds.Contains(place.CloudId)).ToList())
            {
                var pin = new PlaceMapPin
                {
                    Label = place.Name,
                    Address = $"{place.Mode} · {place.Radius:F0} m",
                    Type = PinType.SearchResult,
                    ImageSource = ImageSource.FromFile(GetPlaceTypeMarkerImage(place.PlaceType)),
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

        private static string GetPlaceTypeMarkerImage(PlaceType placeType) => placeType switch
        {
            PlaceType.Education => "marker_education.svg",
            PlaceType.Religious => "marker_religious.svg",
            PlaceType.Work => "marker_work.svg",
            PlaceType.Hospital => "marker_hospital.svg",
            _ => "marker_other.svg"
        };

        private void OnPlacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_pinRefreshScheduled) return;
                _pinRefreshScheduled = true;
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
                {
                    _pinRefreshScheduled = false;
                    if (Shell.Current.CurrentPage != this) return;
                    if (_isDraggingSheet)
                    {
                        OnPlacesChanged(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                        return;
                    }
                    UpdateBottomSheetLayout();
                    RefreshPins();
                    UpdateSelectedGeofence();
                });
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
                ResetSwipeForItem(sender);
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
                ResetSwipeForItem(sender);
                bool confirm = await DisplayAlert(
                    "Delete",
                    $"Delete {place.Name}?",
                    "Yes",
                    "No");

                if (confirm)
                {
                    try
                    {
                        await _vm.DeletePlace(place);
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Sync Error", ex.Message, "OK");
                    }
                }
            }
        }

        private static Place? GetSwipePlace(object sender)
        {
            return sender switch
            {
                SwipeItem swipeItem => swipeItem.CommandParameter as Place,
                SwipeItemView { CommandParameter: SwipeView swipeView } => swipeView.BindingContext as Place,
                _ => null
            };
        }

        private void ResetSwipeForItem(object sender)
        {
            if (sender is SwipeItemView { CommandParameter: SwipeView swipeView })
            {
                PlaceSwipeLayout.Reset(swipeView, close: true);
                _activePlaceSwipes.Remove(swipeView);
            }
        }

        private void OnPlaceCardSwipeChanging(object sender, SwipeChangingEventArgs e)
        {
            if (sender is SwipeView swipe) PlaceSwipeLayout.Change(swipe, e.Offset, 116);
        }

        private void OnPlaceCardSwipeStarted(object sender, SwipeStartedEventArgs e)
        {
            if (sender is not SwipeView swipe) return;
            _activePlaceSwipes.Add(swipe);
            PlaceSwipeLayout.Start(swipe);
        }

        private void OnPlaceCardSwipeEnded(object sender, SwipeEndedEventArgs e)
        {
            if (sender is not SwipeView swipe) return;
            PlaceSwipeLayout.End(swipe, e.IsOpen, 116);
            if (!e.IsOpen) _activePlaceSwipes.Remove(swipe);
        }

        private void OnPlaceCardBindingContextChanged(object sender, EventArgs e)
        {
            ResetPlaceCard(sender);
        }

        private void OnPlaceCardUnloaded(object sender, EventArgs e) => ResetPlaceCard(sender, close: false);

        private void OnPlaceCardContentBindingContextChanged(object sender, EventArgs e)
        {
            if (sender is not Grid content) return;
            content.Margin = Thickness.Zero;
            if (content.Parent is Border { Parent: SwipeView swipe }) ResetPlaceCard(swipe);
        }

        private void OnPlacesCollectionScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            foreach (var swipe in _activePlaceSwipes.ToArray()) ResetPlaceCard(swipe);
        }

        private void ResetPlaceCard(object sender, bool close = true)
        {
            if (sender is not SwipeView swipe) return;
            PlaceSwipeLayout.Reset(swipe, close);
            _activePlaceSwipes.Remove(swipe);
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
            SetSelectedPlaceType(place?.PlaceType ?? PlaceType.Other);
            SetSelectedVisibility(place?.Visibility ?? PlaceVisibility.Private);
            _sheetExpanded = false;
            PlacesBottomSheet.CancelAnimations();
            _sheetDragOffset = BottomSheetCollapsedTranslation;
            PlacesBottomSheet.TranslationY = _sheetDragOffset;
            UpdateMapControlsPosition();
            PlacesBottomSheet.InputTransparent = true;
            PlaceFormOverlay.IsVisible = true;
        }


        private void OnModeButtonTapped(object sender, EventArgs e)
        {
            var mode = sender switch
            {
                Button { Text: "Silent" } => ModeType.Silent,
                Button { Text: "Vibrate" } => ModeType.Vibrate,
                Button { Text: "Ring" } => ModeType.Normal,
                _ => _selectedMode
            };

            SetSelectedMode(mode);
        }

        private void SetSelectedMode(ModeType mode)
        {
            _selectedMode = mode;

            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var idle = Color.FromArgb(dark ? "#202028" : "#F5F3FF");
            ModeSilentButton.BackgroundColor = idle;
            ModeSilentButton.TextColor = Color.FromArgb("#20A447");
            ModeVibrateButton.BackgroundColor = idle;
            ModeVibrateButton.TextColor = Color.FromArgb("#E69208");
            ModeNormalButton.BackgroundColor = idle;
            ModeNormalButton.TextColor = Color.FromArgb(dark ? "#B0AFBA" : "#737381");

            // Highlight selected mode with its color background and white text
            switch (mode)
            {
                case ModeType.Silent:
                    ModeSilentButton.BackgroundColor = Color.FromArgb("#20A447");
                    ModeSilentButton.TextColor = Colors.White;
                    break;
                case ModeType.Vibrate:
                    ModeVibrateButton.BackgroundColor = Color.FromArgb("#E69208");
                    ModeVibrateButton.TextColor = Colors.White;
                    break;
                case ModeType.Normal:
                    ModeNormalButton.BackgroundColor = Color.FromArgb("#737381");
                    ModeNormalButton.TextColor = Colors.White;
                    break;
            }
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

        private void SetSelectedPlaceType(PlaceType placeType)
        {
            _selectedPlaceType = placeType;
            var options = Enum.GetValues<PlaceType>()
                .Select(type => new PlaceTypeOption
                {
                    Value = type,
                    Icon = type.ToIcon(),
                    Label = type.ToDisplayName()
                })
                .ToList();

            PlaceTypeOptionsList.ItemsSource = options;
            SelectedPlaceTypeLabel.Text = $"{placeType.ToIcon()} {placeType.ToDisplayName()}";
            PlaceTypeMenu.IsVisible = false;
        }

        private void OnPlaceTypeDropdownTapped(object sender, TappedEventArgs e)
        {
            PlaceTypeMenu.IsVisible = !PlaceTypeMenu.IsVisible;
        }

        private void OnPlaceTypeSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not PlaceTypeOption option)
                return;

            _selectedPlaceType = option.Value;
            SelectedPlaceTypeLabel.Text = $"{option.Value.ToIcon()} {option.Value.ToDisplayName()}";
            PlaceTypeMenu.IsVisible = false;
            PlaceTypeOptionsList.SelectedItem = null;
        }

        private sealed class PlaceTypeOption
        {
            public PlaceType Value { get; set; }
            public string Icon { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
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

            if (name.Length > 120)
            {
                await DisplayAlert("Place", "Place name must be 120 characters or less.", "OK");
                return false;
            }

            var radius = PlaceRadiusSlider.Value;

            if (radius < 25 || radius > 5000)
            {
                await DisplayAlert("Place", "Radius must be between 25 and 5000 meters.", "OK");
                return false;
            }

            if (_editingPlace == null)
            {
                await _vm.AddPlace(
                    _pendingPlaceLocation.Latitude,
                    _pendingPlaceLocation.Longitude,
                    name,
                    _selectedMode,
                    radius,
                    _selectedActivationType,
                    _selectedPlaceType,
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
                    _selectedPlaceType,
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
                    _isDraggingSheet = true;
                    PlacesBottomSheet.CancelAnimations();
                    MapControls.CancelAnimations();
                    _bottomSheetPanStartTranslation = _sheetDragOffset;
                    break;

                case GestureStatus.Running:
                    _sheetDragOffset = Math.Clamp(
                        _bottomSheetPanStartTranslation + e.TotalY,
                        0,
                        BottomSheetCollapsedTranslation);
                    SetDragTranslation(PlacesBottomSheet, _sheetDragOffset);
                    UpdateMapControlsPosition(useNative: true);
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    _isDraggingSheet = false;
                    _sheetExpanded = _sheetDragOffset < BottomSheetCollapsedTranslation / 2;
                    _ = SnapBottomSheetAsync();
                    break;
            }
        }

        private void ToggleBottomSheet()
        {
            _sheetExpanded = !_sheetExpanded;
            _ = SnapBottomSheetAsync();
        }

        private async Task SnapBottomSheetAsync()
        {
            PlacesBottomSheet.TranslationY = _sheetDragOffset;
            UpdateMapControlsPosition(useNative: false);
            var target = _sheetExpanded ? 0 : BottomSheetCollapsedTranslation;
            var controlsTarget = -Math.Max(0, BottomSheetExpandedHeight - target - BottomSheetCollapsedHeight);
            await Task.WhenAll(
                PlacesBottomSheet.TranslateTo(0, target, 180, Easing.CubicOut),
                MapControls.TranslateTo(0, controlsTarget, 180, Easing.CubicOut));
            _sheetDragOffset = target;
        }

        private void UpdateBottomSheetLayout()
        {
            if (Height <= 0) return;
            PlacesBottomSheet.HeightRequest = BottomSheetExpandedHeight;
            _sheetDragOffset = _sheetExpanded ? 0 : BottomSheetCollapsedTranslation;
            PlacesBottomSheet.TranslationY = _sheetDragOffset;
            UpdateMapControlsPosition();
        }

        private void UpdateMapControlsPosition(bool useNative = false)
        {
            var offset = -Math.Max(0, BottomSheetExpandedHeight - _sheetDragOffset - BottomSheetCollapsedHeight);
            if (useNative) SetDragTranslation(MapControls, offset);
            else MapControls.TranslationY = offset;
        }

        private static void SetDragTranslation(VisualElement element, double offset)
        {
#if ANDROID
            if (element.Handler?.PlatformView is Android.Views.View view)
            {
                view.TranslationY = (float)(offset * DeviceDisplay.MainDisplayInfo.Density);
                return;
            }
#endif
            element.TranslationY = offset;
        }

        private async void OnViewAllClicked(object sender, EventArgs e)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine("VIEW ALL tapped");
            await Shell.Current.GoToAsync(nameof(PlacesPage), false);
            System.Diagnostics.Debug.WriteLine($"VIEW ALL route completed: {timer.ElapsedMilliseconds} ms");
        }

        private async Task SetupMapAsync()
        {
            var loc = await _vm.GetCurrentLocationForMap();
            if (Shell.Current.CurrentPage != this) return;

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
            if (sender is not Border card || card.BindingContext is not Place place)
                return;

            if (card.Parent is SwipeView swipe) ResetPlaceCard(swipe);
            FocusPlaceOnMap(place);
        }

        private void FocusPlaceOnMap(Place place)
        {
            _vm.SelectedPlace = place;
            UpdateSelectedGeofence();
            var location = new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude);

            MainMap.MoveToRegion(
                MapSpan.FromCenterAndRadius(
                    location,
                    Distance.FromMeters(Math.Max(place.Radius * 3, 220))));

            _focusedPlacePin = MainMap.Pins.FirstOrDefault(pin =>
                string.Equals(pin.Label, place.Name, StringComparison.Ordinal) &&
                AreSamePlace(pin.Location, location));
        }

        private void UpdateSelectedGeofence()
        {
            MainMap.MapElements.Clear();
            var place = _vm.SelectedPlace;
            if (place == null) return;
            MainMap.MapElements.Add(new Circle
            {
                Center = new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude),
                Radius = Distance.FromMeters(place.Radius),
                FillColor = Color.FromArgb("268B5CF6"),
                StrokeColor = Color.FromArgb("4D7C3AED"),
                StrokeWidth = 2
            });
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
            SearchBarBorder.BackgroundColor = Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark ? "#18181E" : "#FFFFFF");
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
                var appearanceTimer = System.Diagnostics.Stopwatch.StartNew();
                var userId = _authenticationService.CurrentUser?.Uid;
                await _vm.InitializeAsync();
                System.Diagnostics.Debug.WriteLine($"MAIN local places ready: {appearanceTimer.ElapsedMilliseconds} ms");

                _collectionHandler = OnPlacesChanged;
                _vm.NearbyPlaces.CollectionChanged += _collectionHandler;
                _publicCollectionHandler = OnPlacesChanged;
                _vm.PublicPlaces.CollectionChanged += _publicCollectionHandler;

                RefreshPins();
                UpdateBottomSheetLayout();
                ProcessRouteSelection();
                UpdateSearchUiState();
                _accountProfileService.ProfileChanged += OnProfileChanged;
                _ = LoadAvatarSafelyAsync();

                if (_mapInitializedForUserId != userId)
                {
                    _mapInitializedForUserId = userId;
                    _ = InitializeMapAndTrackingAsync();
                }
                else
                {
                    _ = _vm.StartTrackingAsync();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        protected override void OnDisappearing()
        {
            foreach (var swipe in _activePlaceSwipes.ToArray()) ResetPlaceCard(swipe);
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

            if (pin.BindingContext is Place ownedPlace && _vm.NearbyPlaces.Contains(ownedPlace))
            {
                FocusPlaceOnMap(ownedPlace);
            }
            else if (pin.BindingContext is Place publicPlace && publicPlace.Visibility == PlaceVisibility.Public)
            {
                e.HideInfoWindow = true;
                await ShowPublicPlaceDetailsAsync(publicPlace);
            }
        }

        private void ProcessRouteSelection()
        {
            var editId = _routeEditId;
            var id = editId ?? _routeFocusId;
            if (id == null) return;
            _routeEditId = null;
            _routeFocusId = null;
            var place = _vm.NearbyPlaces.FirstOrDefault(p => p.Id == id.Value);
            if (place == null) return;
            FocusPlaceOnMap(place);
            if (editId != null)
                ShowPlaceForm(new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude), place.Name, place);
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

        private async Task LoadAvatarSafelyAsync()
        {
            try { await LoadAvatarAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AVATAR LOAD ERROR: {ex}"); }
        }

        private async Task SetupMapSafelyAsync()
        {
            try { await SetupMapAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MAP SETUP ERROR: {ex}"); }
        }

        private async Task InitializeMapAndTrackingAsync()
        {
            await SetupMapSafelyAsync();
            if (Shell.Current.CurrentPage == this)
                await _vm.StartTrackingAsync();
        }
    }
}
