using System.Collections.Specialized;
using System.Collections.ObjectModel;
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

        private readonly IAuthenticationService _authenticationService;
        private readonly SearchService _searchService = new();
        private NotifyCollectionChangedEventHandler? _collectionHandler;
        private NotifyCollectionChangedEventHandler? _publicCollectionHandler;
        private Pin? _searchResultPin;
        private Pin? _focusedPlacePin;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _suggestionCts;
        private readonly ObservableCollection<PlaceSuggestion> _suggestions = new();
        private string? _suggestionSessionToken;
        private bool _settingSuggestionText;
        private Microsoft.Maui.Devices.Sensors.Location? _placeFormLocation;
        private string? _searchResultQuery;
        private Place? _editingPlace;
        private Place? _publicPlaceToSave;
        private bool _exploreMode;
        private bool _loadingExplore;
        private int _clusterLevel = -1;
        private bool _publicPreviewOpen;
        private readonly VisualElement? _placeFormCard;
        private ModeType _selectedMode = ModeType.Silent;
        private ActivationType _selectedActivationType = ActivationType.Automatic;
        private PlaceVisibility _selectedVisibility = PlaceVisibility.Private;
        private PlaceType _selectedPlaceType = PlaceType.Other;
        private readonly BottomSheetGesture _sheetGesture = new();
        private static double SheetGestureTime => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        private bool _sheetExpanded;
        private bool _isSearching;
        private bool _isSavingPlace;
        private bool _isChoosingPlaceLocation;
        private int _appearanceVersion;
        private bool _pinRefreshScheduled;
        private bool _placesRefreshPending;
        private readonly HashSet<SwipeView> _activePlaceSwipes = new();
        private bool _isDraggingSheet;
        private bool _isAnimatingSheet;
        private bool _sheetLayoutPending;
        private int _sheetAnimationVersion;
        private string? _mapInitializedForUserId;
        private double _sheetDragOffset;
        private double _sheetDensity = 1;
        private double _bottomSheetExpandedHeight = 220;

        private const double BottomSheetCollapsedHeight = 104;
        private const string BottomSheetAnimationName = "PlacesSheetSnap";
        private double BottomSheetCollapsedTranslation => _bottomSheetExpandedHeight - BottomSheetCollapsedHeight;
        private bool IsSheetMoving => _isDraggingSheet || _isAnimatingSheet;
        private int? _routeFocusId;
        private int? _routeEditId;
        private const double KeyboardFormOffset = -150;

        public MainPage(HomeViewModel vm, IAuthenticationService authenticationService)
        {
            InitializeComponent();
            BindingContext = vm;
            _vm = vm;

            _authenticationService = authenticationService;
            SearchSuggestionsPanel.BindingContext = _suggestions;
            _placeFormCard = this.FindByName<VisualElement>("PlaceFormCard");
            SizeChanged += (_, _) => UpdateBottomSheetLayout();
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("focusPlaceId", out var focus) && int.TryParse(focus?.ToString(), out var focusId)) _routeFocusId = focusId;
            if (query.TryGetValue("editPlaceId", out var edit) && int.TryParse(edit?.ToString(), out var editId)) _routeEditId = editId;
        }

        private void OnMapLongPressed(object sender, MapClickedEventArgs e)
        {
            if (_isChoosingPlaceLocation || PlaceFormOverlay.IsVisible || Shell.Current.CurrentPage != this)
                return;

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
            HideSuggestions();
            _suggestionSessionToken = null;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            SetSearchInProgress(true);

            try
            {
                var results = await _searchService.SearchPlacesAsync(trimmedQuery, GetSearchArea(), token);

                if (token.IsCancellationRequested)
                    return;

                if (results.Count == 0)
                {
                    await DisplayAlert("Search", "No place found.", "OK");
                    return;
                }
                if (results.Count == 1)
                {
                    SetSearchText(results[0].Name);
                    ShowSearchResult(results[0], results[0].Name);
                    return;
                }

                _suggestions.Clear();
                foreach (var result in results)
                    _suggestions.Add(new PlaceSuggestion(string.Empty, result.Name,
                        result.Address, result.Latitude, result.Longitude));
                SearchSuggestionsPanel.IsVisible = true;
                PlaceSearchBar.Unfocus();
            }
            catch (OperationCanceledException)
            {
            }
            catch (PlaceSearchException ex)
            {
                if (!token.IsCancellationRequested)
                    await DisplayAlert("Search unavailable", ex.Message, "OK");
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
            if (_settingSuggestionText) return;
            if (!string.Equals(e.NewTextValue?.Trim(), _searchResultQuery, StringComparison.Ordinal))
                ClearSearchState();
            _ = LoadSuggestionsAsync(e.NewTextValue);
        }

        private async Task LoadSuggestionsAsync(string? query)
        {
            _suggestionCts?.Cancel();
            _suggestionCts?.Dispose();
            _suggestionCts = null;
            _suggestions.Clear();
            SearchSuggestionsPanel.IsVisible = false;

            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3 || _isSearching)
            {
                if (string.IsNullOrWhiteSpace(query)) _suggestionSessionToken = null;
                return;
            }

            _suggestionSessionToken ??= Guid.NewGuid().ToString("D");
            _suggestions.Add(new PlaceSuggestion(string.Empty,
                $"Search nearby for \"{query.Trim()}\"", "Show matching places near this map", IsSearchAction: true));
            SearchSuggestionsPanel.IsVisible = true;
            var cts = new CancellationTokenSource();
            _suggestionCts = cts;
            try
            {
                await Task.Delay(300, cts.Token);
                var results = await _searchService.GetSuggestionsAsync(query, _suggestionSessionToken,
                    GetSearchArea(), cts.Token);
                if (cts.IsCancellationRequested || !ReferenceEquals(_suggestionCts, cts) ||
                    Shell.Current.CurrentPage != this) return;
                foreach (var result in results) _suggestions.Add(result);
                SearchSuggestionsPanel.IsVisible = _suggestions.Count > 0;
            }
            catch (OperationCanceledException) { }
            catch (PlaceSearchException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SUGGESTION WARNING: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SUGGESTION ERROR: {ex}");
            }
            finally
            {
                if (ReferenceEquals(_suggestionCts, cts)) _suggestionCts = null;
                cts.Dispose();
            }
        }

        private async void OnSuggestionTapped(object sender, TappedEventArgs e)
        {
            if (sender is not Grid grid || grid.BindingContext is not PlaceSuggestion suggestion || _isSearching)
                return;

            if (suggestion.IsSearchAction)
            {
                OnSearchSubmitted(sender, EventArgs.Empty);
                return;
            }
            if (suggestion.Latitude is double latitude && suggestion.Longitude is double longitude)
            {
                HideSuggestions();
                SetSearchText(suggestion.Name);
                ShowSearchResult(new PlaceSearchResult(suggestion.Name, latitude, longitude,
                    suggestion.Description), suggestion.Name);
                return;
            }

            var token = _suggestionSessionToken;
            if (token == null) return;
            HideSuggestions();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var cancellationToken = _searchCts.Token;
            SetSearchInProgress(true);
            try
            {
                var result = await _searchService.GetSuggestionPlaceAsync(suggestion, token, cancellationToken);
                if (cancellationToken.IsCancellationRequested || Shell.Current.CurrentPage != this) return;
                _suggestionSessionToken = null;
                SetSearchText(suggestion.Name);
                ShowSearchResult(result, suggestion.Name);
            }
            catch (OperationCanceledException) { }
            catch (PlaceSearchException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await DisplayAlert("Search unavailable", ex.Message, "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PLACE DETAILS ERROR: {ex}");
                if (!cancellationToken.IsCancellationRequested)
                    await DisplayAlert("Search Error", "Could not open that place right now.", "OK");
            }
            finally
            {
                if (_searchCts != null && cancellationToken == _searchCts.Token)
                {
                    _searchCts.Dispose();
                    _searchCts = null;
                    SetSearchInProgress(false);
                }
            }
        }

        private SearchArea? GetSearchArea()
        {
            var region = MainMap.VisibleRegion;
            if (region == null || region.LatitudeDegrees <= 0) return null;
            var radiusMeters = Math.Clamp(region.LatitudeDegrees * 111_000 / 2, 1_500, 50_000);
            return new SearchArea(region.Center.Latitude, region.Center.Longitude, radiusMeters);
        }

        private void SetSearchText(string text)
        {
            _settingSuggestionText = true;
            try { PlaceSearchBar.Text = text; }
            finally { _settingSuggestionText = false; }
        }

        private void ShowSearchResult(PlaceSearchResult result, string query)
        {
            _suggestionSessionToken = null;
            var location = new Microsoft.Maui.Devices.Sensors.Location(result.Latitude, result.Longitude);
            if (_searchResultPin != null) MainMap.Pins.Remove(_searchResultPin);
            _searchResultPin = new Pin { Label = result.Name, Location = location };
            MainMap.Pins.Add(_searchResultPin);
            MainMap.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromMeters(700)));
            _searchResultQuery = query;
            UpdateSearchUiState();
            PlaceSearchBar.Unfocus();
        }

        private void HideSuggestions()
        {
            _suggestionCts?.Cancel();
            _suggestionCts?.Dispose();
            _suggestionCts = null;
            _suggestions.Clear();
            SearchSuggestionsPanel.IsVisible = false;
        }

        private sealed record PinState(Place Place, string Name, ModeType Mode,
            double Radius, double Latitude, double Longitude, PlaceType Type, string? CloudId, PlaceVisibility Visibility);
        private PinState[]? _renderedPinState;

        private readonly Dictionary<PinState, PlaceMapPin> _placePins = new();
        private readonly Dictionary<(int Level, int X, int Y, int Count, double Lat, double Lon, bool Public), PlaceMapPin> _clusterPins = new();

        private void RefreshPins()
        {
            var clusterLevel = GetClusterLevel();
            if (_clusterLevel != clusterLevel) _renderedPinState = null;
            _clusterLevel = clusterLevel;
            // Distance sorting belongs to the list, not the map's render identity.
            var places = (_exploreMode ? _vm.GetExplorePlaces() : _vm.NearbyPlaces)
                .OrderBy(p => p.OwnerId, StringComparer.Ordinal)
                .ThenBy(p => p.CloudId, StringComparer.Ordinal).ThenBy(p => p.Id).ToList();
            var state = places.Select(p => new PinState(p, p.Name, p.Mode, p.Radius,
                p.Latitude, p.Longitude, p.PlaceType, p.CloudId, p.Visibility)).ToArray();
            if (_renderedPinState != null && _renderedPinState.SequenceEqual(state)) return;
            _renderedPinState = state;

            var desired = new List<Pin>();
            var retainedPlaces = new HashSet<PinState>();
            var retainedClusters = new HashSet<(int, int, int, int, double, double, bool)>();
            var scale = 256 * Math.Pow(2, _clusterLevel) / 64;
            // Group plain data before allocating any UI pin or native handler.
            var groups = state.GroupBy(p =>
            {
                if (_clusterLevel >= 18) return (X: 0, Y: 0);
                var lat = Math.Clamp(p.Latitude, -85.05112878, 85.05112878) * Math.PI / 180;
                var x = (p.Longitude + 180) / 360;
                var y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2;
                return (X: (int)Math.Floor(x * scale), Y: (int)Math.Floor(y * scale));
            });
            foreach (var group in groups)
            {
                var members = group.ToList();
                if (_clusterLevel < 18 && members.Count > 1)
                {
                    var key = (_clusterLevel, group.Key.X, group.Key.Y, members.Count,
                        members.Average(p => p.Latitude), members.Average(p => p.Longitude), _exploreMode);
                    retainedClusters.Add(key);
                    if (!_clusterPins.TryGetValue(key, out var cluster))
                    {
                        cluster = new PlaceMapPin
                        {
                            Label = $"{members.Count} places", Address = "Tap to zoom in",
                            ClusterCount = members.Count, IsPublicCluster = _exploreMode,
                            Location = new Microsoft.Maui.Devices.Sensors.Location(key.Item5, key.Item6)
                        };
                        cluster.MarkerClicked += (_, args) =>
                        {
                            args.HideInfoWindow = true;
                            var span = MainMap.VisibleRegion;
                            if (span != null) MainMap.MoveToRegion(new MapSpan(cluster.Location,
                                Math.Max(span.LatitudeDegrees / 4, 0.0001),
                                Math.Max(span.LongitudeDegrees / 4, 0.0001)));
                        };
                        _clusterPins.Add(key, cluster);
                    }
                    desired.Add(cluster);
                    continue;
                }
                foreach (var item in members)
                {
                    retainedPlaces.Add(item);
                    if (!_placePins.TryGetValue(item, out var pin))
                    {
                        pin = new PlaceMapPin
                        {
                            Label = item.Name,
                            Address = _exploreMode ? "Public place \u00B7 Add to my places"
                                : $"Saved \u00B7 {item.Visibility} \u00B7 {item.Mode} \u00B7 {item.Radius:F0} m",
                            Type = _exploreMode ? PinType.SearchResult : PinType.Place,
                            ImageSource = ImageSource.FromFile((_exploreMode ? "explore_" : "") + GetPlaceTypeMarkerImage(item.Type)),
                            Location = new Microsoft.Maui.Devices.Sensors.Location(item.Latitude, item.Longitude),
                            BindingContext = item.Place
                        };
                        pin.MarkerClicked += OnMapPinClicked;
                        _placePins.Add(item, pin);
                    }
                    desired.Add(pin);
                }
            }
            foreach (var key in _placePins.Keys.Where(k => !retainedPlaces.Contains(k)).ToArray()) _placePins.Remove(key);
            foreach (var key in _clusterPins.Keys.Where(k => !retainedClusters.Contains(k)).ToArray()) _clusterPins.Remove(key);
            if (_searchResultPin != null) desired.Add(_searchResultPin);
            var retained = desired.ToHashSet();
            for (var i = MainMap.Pins.Count - 1; i >= 0; i--)
                if (!retained.Contains(MainMap.Pins[i])) MainMap.Pins.RemoveAt(i);
            foreach (var pin in desired)
                if (!MainMap.Pins.Contains(pin)) MainMap.Pins.Add(pin);
            _focusedPlacePin = desired.FirstOrDefault(p => p.BindingContext is Place place &&
                ReferenceEquals(place, _vm.SelectedPlace));
        }

        private int GetClusterLevel()
        {
            var span = MainMap.VisibleRegion;
            if (span == null || MainMap.Width <= 0) return 20;
            return Math.Clamp((int)Math.Floor(Math.Log2(360 * MainMap.Width /
                (256 * Math.Max(span.LongitudeDegrees, 0.000001)))), 0, 20);
        }

        private void OnMapViewportChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_vm == null || MainMap == null) return;
            if (e.PropertyName != nameof(MainMap.VisibleRegion) && e.PropertyName != nameof(MainMap.Width)) return;
            if (_clusterLevel != GetClusterLevel()) RefreshPins();
        }

        private static string GetPlaceTypeMarkerImage(PlaceType placeType) => placeType switch
        {
            PlaceType.Education => "marker_education.svg",
            PlaceType.Religious => "marker_religious.svg",
            PlaceType.Work => "marker_work.svg",
            PlaceType.Hospital => "marker_hospital.svg",
            _ => "marker_other.svg"
        };

        private void OnMyPlacesModeClicked(object sender, EventArgs e) => SetExploreMode(false);
        private async void OnExploreModeClicked(object sender, EventArgs e)
        {
            SetExploreMode(true);
            ExploreModeButton.IsEnabled = false;
            _loadingExplore = true;
            ExploreLoading.IsVisible = ExploreLoading.IsRunning = true;
            var appearanceVersion = _appearanceVersion;
            try
            {
                var error = await _vm.LoadPublicPlacesAsync();
                _loadingExplore = false;
                ExploreLoading.IsVisible = ExploreLoading.IsRunning = false;
                RefreshPins();
                if (_exploreMode && appearanceVersion == _appearanceVersion)
                {
                    if (error != null)
                        await DisplayAlert("Explore unavailable", "Public places could not load. Tap Explore to retry.", "OK");
                    else if (!_vm.GetExplorePlaces().Any())
                        await DisplayAlert("Explore", "No unsaved public places from other users are available yet.", "OK");
                }
            }
            finally
            {
                _loadingExplore = false;
                ExploreLoading.IsVisible = ExploreLoading.IsRunning = false;
                ExploreModeButton.IsEnabled = true;
            }
        }

        private void SetExploreMode(bool explore)
        {
            var changed = _exploreMode != explore;
            _exploreMode = explore;
            ExploreLoading.IsVisible = ExploreLoading.IsRunning = explore && _loadingExplore;
            UpdateSelectedGeofence();
            var text = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#F4F4F7") : Color.FromArgb("#17171A");
            MyPlacesModeButton.BackgroundColor = explore ? Colors.Transparent : Color.FromArgb("#6D28D9");
            MyPlacesModeButton.TextColor = explore ? text : Colors.White;
            ExploreModeButton.BackgroundColor = explore ? Color.FromArgb("#0F766E") : Colors.Transparent;
            ExploreModeButton.TextColor = explore ? Colors.White : text;
            if (changed)
            {
                _renderedPinState = null;
                _placePins.Clear();
                _clusterPins.Clear();
            }
            RefreshPins();
        }

        private void OnPlacesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            OnVisiblePlacesUpdated(sender, EventArgs.Empty);

        private void OnVisiblePlacesUpdated(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _placesRefreshPending = true;
                SchedulePlacesRefresh();
            });
        }

        private void SchedulePlacesRefresh()
        {
            if (!_placesRefreshPending || _pinRefreshScheduled || IsSheetMoving || Shell.Current.CurrentPage != this)
                return;

            _pinRefreshScheduled = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
            {
                _pinRefreshScheduled = false;
                if (!_placesRefreshPending || IsSheetMoving || Shell.Current.CurrentPage != this)
                    return;

                _placesRefreshPending = false;
                UpdateBottomSheetLayout();
                RefreshPins();
                UpdateSelectedGeofence();
            });
        }

        private void UpdateSearchUiState()
        {
            // The active search result is offered by the Add Place location chooser.
        }

        private void ClearSearchState()
        {
            CancelSearch();
            _searchResultQuery = null;

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
            _placeFormLocation = location;
            _editingPlace = place;
            _publicPlaceToSave = null;
            VisibilityPublicRadio.IsEnabled = true;

            PlaceFormTitle.Text = place == null ? "Add Place" : "Edit Place";
            PlaceNameEntry.Text = suggestedName ?? string.Empty;

            var radius = place?.Radius ?? 50;
            PlaceRadiusSlider.Value = radius;
            RadiusValueLabel.Text = $"{(int)radius} m";

            SetSelectedMode(place?.Mode ?? ModeType.Silent);
            SetSelectedActivationType(place?.ActivationType ?? ActivationType.Automatic);
            SetSelectedPlaceType(place?.PlaceType ?? PlaceType.Other);
            SetSelectedVisibility(place?.Visibility ?? PlaceVisibility.Private);
            PlaceScheduleEditor.Load(place == null ? null : SessionPlaceSchedules.Current.Get(place.Id));
            _isDraggingSheet = false;
            StopSheetAnimation();
            _vm.SetPlaceListUpdatesDeferred(false);
            _sheetExpanded = false;
            UpdateBottomSheetLayout();
            SchedulePlacesRefresh();
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
            _placeFormLocation = null;
            PlaceNameEntry.Unfocus();
            if (_placeFormCard != null)
                _placeFormCard.TranslationY = 0;
            PlacesBottomSheet.InputTransparent = false;
        }

        private async Task<bool> SavePlaceAsync()
        {
            if (_placeFormLocation == null)
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

            if (!PlaceScheduleEditor.TryGetSchedule(out var schedule, out var scheduleError))
            {
                await DisplayAlert("Active times", scheduleError, "OK");
                return false;
            }

            if (radius < 25 || radius > 5000)
            {
                await DisplayAlert("Place", "Radius must be between 25 and 5000 meters.", "OK");
                return false;
            }

            if (_editingPlace == null)
            {
                await _vm.AddPlace(
                    _placeFormLocation.Latitude,
                    _placeFormLocation.Longitude,
                    name,
                    _selectedMode,
                    radius,
                    _selectedActivationType,
                    _selectedPlaceType,
                    _selectedVisibility, _publicPlaceToSave, schedule);

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
                    _selectedVisibility, schedule);
            }

            return true;
        }

        private void OnBottomSheetHandleTapped(object sender, TappedEventArgs e)
        {
            if (PlaceFormOverlay.IsVisible || _isDraggingSheet)
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
                    _vm.SetPlaceListUpdatesDeferred(true);
                    StopSheetAnimation();
                    _isDraggingSheet = true;
                    CacheSheetDensity();
                    SetSheetOffset(_sheetDragOffset, useNative: false);
                    _sheetGesture.Begin(_sheetDragOffset, SheetGestureTime);
                    break;

                case GestureStatus.Running:
                    if (!_isDraggingSheet) return;
#if ANDROID
                    const bool relativeToMovingView = true;
#else
                    const bool relativeToMovingView = false;
#endif
                    SetSheetOffset(Math.Clamp(
                        _sheetGesture.Move(e.TotalY, _sheetDragOffset, relativeToMovingView, SheetGestureTime),
                        0,
                        BottomSheetCollapsedTranslation), useNative: true);
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (!_isDraggingSheet) return;
                    _isDraggingSheet = false;
                    _sheetExpanded = _sheetGesture.ShouldExpand(_sheetDragOffset,
                        BottomSheetCollapsedTranslation, SheetGestureTime, e.StatusType == GestureStatus.Canceled);
                    SnapBottomSheet();
                    break;
            }
        }

        private void ToggleBottomSheet()
        {
            _sheetExpanded = !_sheetExpanded;
            SnapBottomSheet();
        }

        private void SnapBottomSheet()
        {
            _vm.SetPlaceListUpdatesDeferred(true);
            StopSheetAnimation();
            CacheSheetDensity();
            var target = _sheetExpanded ? 0 : BottomSheetCollapsedTranslation;

            if (Math.Abs(_sheetDragOffset - target) < 0.1)
            {
                SetSheetOffset(target, useNative: false);
                FinishSheetMotion();
                return;
            }

            _isAnimatingSheet = true;
            var animationVersion = _sheetAnimationVersion;
            // Keep the logical position current on every frame so an interrupted
            // snap starts the next drag at the position actually on screen.
            var animation = new Animation(
                offset => SetSheetOffset(offset, useNative: true), _sheetDragOffset, target);
            animation.Commit(this, BottomSheetAnimationName, rate: 16, length: 180, easing: Easing.CubicOut,
                finished: (_, canceled) =>
                {
                    if (animationVersion != _sheetAnimationVersion) return;
                    _isAnimatingSheet = false;
                    if (!canceled) SetSheetOffset(target, useNative: true);
                    FinishSheetMotion();
                });
        }

        private void StopSheetAnimation()
        {
            // Abort invokes the old completion callback; invalidate it first.
            _sheetAnimationVersion++;
            this.AbortAnimation(BottomSheetAnimationName);
            _isAnimatingSheet = false;
        }

        private void FinishSheetMotion()
        {
            _vm.SetPlaceListUpdatesDeferred(false);
            SetSheetOffset(_sheetDragOffset, useNative: false);
            if (_sheetLayoutPending) UpdateBottomSheetLayout();
            SchedulePlacesRefresh();
        }

        private void UpdateBottomSheetLayout()
        {
            if (Height <= 0) return;
            if (IsSheetMoving)
            {
                _sheetLayoutPending = true;
                return;
            }

            _sheetLayoutPending = false;
            _bottomSheetExpandedHeight = Math.Max(220, Math.Min(Height * 0.52, 130 + _vm.ClosestPlaces.Count * 60));
            if (PlacesBottomSheet.HeightRequest != _bottomSheetExpandedHeight)
                PlacesBottomSheet.HeightRequest = _bottomSheetExpandedHeight;
            CacheSheetDensity();
            SetSheetOffset(_sheetExpanded ? 0 : BottomSheetCollapsedTranslation, useNative: false);
        }

        private void SetSheetOffset(double offset, bool useNative)
        {
            _sheetDragOffset = offset;
            var controlsOffset = -Math.Max(0, BottomSheetCollapsedTranslation - offset);
            if (useNative)
            {
                SetDragTranslation(PlacesBottomSheet, offset);
                SetDragTranslation(MapControls, controlsOffset);
            }
            else
            {
                PlacesBottomSheet.TranslationY = offset;
                MapControls.TranslationY = controlsOffset;
            }
        }

        private void CacheSheetDensity()
        {
#if ANDROID
            _sheetDensity = DeviceDisplay.MainDisplayInfo.Density;
#endif
        }

        private void SetDragTranslation(VisualElement element, double offset)
        {
#if ANDROID
            if (element.Handler?.PlatformView is Android.Views.View view)
            {
                view.TranslationY = (float)(offset * _sheetDensity);
                return;
            }
#endif
            element.TranslationY = offset;
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
            SetExploreMode(false);
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

        private Circle? _selectedGeofenceCircle;
        private (double Lat, double Lon, double Radius)? _selectedGeofenceGeometry;

        private void UpdateSelectedGeofence()
        {
            var place = _vm.SelectedPlace;
            if (place == null || _exploreMode)
            {
                if (_selectedGeofenceCircle != null) MainMap.MapElements.Remove(_selectedGeofenceCircle);
                _selectedGeofenceCircle = null;
                _selectedGeofenceGeometry = null;
                return;
            }
            var geometry = (place.Latitude, place.Longitude, place.Radius);
            if (_selectedGeofenceGeometry == geometry) return;
            _selectedGeofenceGeometry = geometry;
            if (_selectedGeofenceCircle == null)
            {
                _selectedGeofenceCircle = new Circle
                {
                    Center = new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude),
                    Radius = Distance.FromMeters(place.Radius),
                    FillColor = Color.FromArgb("268B5CF6"),
                    StrokeColor = Color.FromArgb("4D7C3AED"), StrokeWidth = 2
                };
                MainMap.MapElements.Add(_selectedGeofenceCircle);
            }
            else
            {
                if (_selectedGeofenceCircle.Center.Latitude != place.Latitude ||
                    _selectedGeofenceCircle.Center.Longitude != place.Longitude)
                    _selectedGeofenceCircle.Center = new Microsoft.Maui.Devices.Sensors.Location(place.Latitude, place.Longitude);
                if (_selectedGeofenceCircle.Radius.Meters != place.Radius)
                    _selectedGeofenceCircle.Radius = Distance.FromMeters(place.Radius);
            }
        }

        private async void OnAddPlaceFabClicked(object sender, EventArgs e)
        {
            if (_isChoosingPlaceLocation || PlaceFormOverlay.IsVisible)
                return;

            _isChoosingPlaceLocation = true;
            AddPlaceButton.IsEnabled = false;
            var appearanceVersion = _appearanceVersion;

            try
            {
                const string currentLocationOption = "My current location";
                var searchResult = _searchResultPin;
                var searchOption = searchResult == null
                    ? "Search for a place"
                    : $"Search result: {searchResult.Label}";
                var choice = await DisplayActionSheet(
                    "Choose a location", "Cancel", null, currentLocationOption, searchOption);

                if (_appearanceVersion != appearanceVersion || Shell.Current.CurrentPage != this)
                    return;

                if (choice == searchOption)
                {
                    if (searchResult == null || !ReferenceEquals(searchResult, _searchResultPin))
                    {
                        PlaceSearchBar.Focus();
                        return;
                    }

                    ShowPlaceForm(new Microsoft.Maui.Devices.Sensors.Location(
                        searchResult.Location.Latitude, searchResult.Location.Longitude), searchResult.Label);
                    return;
                }

                if (choice != currentLocationOption)
                    return;

                AddPlaceButton.Text = "Locating...";
                var loc = await _vm.GetCurrentLocationForMap();
                if (_appearanceVersion != appearanceVersion || Shell.Current.CurrentPage != this)
                    return;

                if (loc == null)
                {
                    await DisplayAlert("Location unavailable",
                        "Could not get your current location. Check location permissions and try again, or search for a place.", "OK");
                    return;
                }

                ShowPlaceForm(new Microsoft.Maui.Devices.Sensors.Location(loc.Latitude, loc.Longitude));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CHOOSE PLACE LOCATION ERROR: {ex}");
                if (_appearanceVersion == appearanceVersion && Shell.Current.CurrentPage == this)
                    await DisplayAlert("Add Place", "Could not select a location. Please try again.", "OK");
            }
            finally
            {
                _isChoosingPlaceLocation = false;
                AddPlaceButton.IsEnabled = true;
                AddPlaceButton.Text = "+ Add Place";
            }
        }

        private void SetSearchInProgress(bool isSearching)
        {
            _isSearching = isSearching;
            SearchLoadingIndicator.IsVisible = isSearching;
            SearchLoadingIndicator.IsRunning = isSearching;
            PlaceSearchBar.IsEnabled = !isSearching;
        }

        private void CancelSearch()
        {
            HideSuggestions();
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
            var appearanceVersion = ++_appearanceVersion;

            try
            {
                var appearanceTimer = System.Diagnostics.Stopwatch.StartNew();
                var userId = _authenticationService.CurrentUser?.Uid;
                await _vm.InitializeAsync();
                // Tab switches may finish before asynchronous initialization.
                // Only the current appearance may attach handlers or start tracking.
                if (appearanceVersion != _appearanceVersion || Shell.Current.CurrentPage != this)
                    return;
                System.Diagnostics.Debug.WriteLine($"MAIN local places ready: {appearanceTimer.ElapsedMilliseconds} ms");

                _vm.VisiblePlacesUpdated += OnVisiblePlacesUpdated;
                _collectionHandler = OnPlacesChanged;
                _vm.NearbyPlaces.CollectionChanged += _collectionHandler;
                _publicCollectionHandler = OnPlacesChanged;
                _vm.PublicPlaces.CollectionChanged += _publicCollectionHandler;

                _placesRefreshPending = false;
                SetExploreMode(_mapInitializedForUserId == userId && _exploreMode);
                UpdateBottomSheetLayout();
                ProcessRouteSelection();
                UpdateSearchUiState();

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
            _appearanceVersion++;
            _vm.VisiblePlacesUpdated -= OnVisiblePlacesUpdated;
            _isDraggingSheet = false;
            StopSheetAnimation();
            _vm.SetPlaceListUpdatesDeferred(false);
            SetSheetOffset(_sheetDragOffset, useNative: false);
            foreach (var swipe in _activePlaceSwipes.ToArray()) ResetPlaceCard(swipe);
            base.OnDisappearing();

            CancelSearch();
            _suggestionSessionToken = null;
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
            if (_publicPreviewOpen) return;
            var saved = _vm.FindSavedPublicPlace(publicPlace);
            if (saved != null) { FocusPlaceOnMap(saved); return; }
            _publicPreviewOpen = true;
            var appearance = _appearanceVersion;
            try
            {
                var add = await DisplayAlert(publicPlace.Name,
                    $"Public place · {publicPlace.PlaceType}\nAdd a personal copy and choose your sound mode and radius.",
                    "Add to my places", "Close");
                if (!add || appearance != _appearanceVersion || Shell.Current.CurrentPage != this) return;
                ShowPlaceForm(new Microsoft.Maui.Devices.Sensors.Location(
                    publicPlace.Latitude, publicPlace.Longitude), publicPlace.Name);
                _publicPlaceToSave = publicPlace;
                PlaceFormTitle.Text = "Add to my places";
                SetSelectedPlaceType(publicPlace.PlaceType);
                SetSelectedVisibility(PlaceVisibility.Private);
                VisibilityPublicRadio.IsEnabled = false;
            }
            finally { _publicPreviewOpen = false; }
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
