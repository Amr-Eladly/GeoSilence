using GeoSilence.Models;
using GeoSilence.PageModels;
using GeoSilence.Controls;

namespace GeoSilence.Pages;

public partial class PlacesPage : ContentPage
{
    private readonly HomeViewModel _vm;
    private readonly HashSet<SwipeView> _activePlaceSwipes = new();
    private View? _cachedList;
    private bool _listReady;
    private int _appearanceVersion;

    public PlacesPage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        System.Diagnostics.Debug.WriteLine("PLACES page constructed");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        System.Diagnostics.Debug.WriteLine("PLACES page appeared");
        await PrepareListAsync(++_appearanceVersion);
    }

    private async Task PrepareListAsync(int version)
    {
        if (_listReady) return;
        PlacesLoadingPanel.IsVisible = true;
        PlacesLoadingIndicator.IsRunning = true;
        PlacesLoadingLabel.Text = "Loading your places…";
        PlacesRetryButton.IsVisible = false;
        try
        {
            // Yield a frame so the lightweight page/loading indicator can appear.
            await Task.Delay(50);
            if (version != _appearanceVersion) return;
            await _vm.InitializeAsync();
            if (version != _appearanceVersion) return;
            _cachedList ??= (View)((DataTemplate)Resources["PlacesListTemplate"]).CreateContent();
            if (PlacesListHost.Content != _cachedList) PlacesListHost.Content = _cachedList;

            // Keep the cover until the retained list has its initial native layout.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!_cachedList.IsLoaded || _cachedList.Width <= 0 || _cachedList.Height <= 0)
            {
                if (version != _appearanceVersion) return;
                if (timer.Elapsed > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("The places list did not finish loading.");
                await Task.Delay(25);
            }
            await Task.Delay(50);
            if (version != _appearanceVersion) return;
            _listReady = true;
            PlacesLoadingPanel.IsVisible = PlacesLoadingIndicator.IsRunning = false;
            System.Diagnostics.Debug.WriteLine("PLACES list prepared and retained");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PLACES LOAD: {ex}");
            if (version != _appearanceVersion) return;
            PlacesLoadingIndicator.IsRunning = false;
            PlacesLoadingLabel.Text = "Couldn't load your places. Please try again.";
            PlacesRetryButton.IsVisible = true;
        }
    }

    private async void OnRetryPlacesClicked(object sender, EventArgs e) =>
        await PrepareListAsync(++_appearanceVersion);

    protected override void OnDisappearing()
    {
        ++_appearanceVersion;
        PlacesLoadingIndicator.IsRunning = false;
        foreach (var swipe in _activePlaceSwipes.ToArray()) ResetPlaceCard(swipe);
        base.OnDisappearing();
    }

    private async void OnPlaceTapped(object sender, TappedEventArgs e)
    {
        if (sender is Border { BindingContext: Place place })
        {
            if (sender is Border { Parent: SwipeView swipe }) ResetPlaceCard(swipe);
            await Shell.Current.GoToAsync($"//home/map/main?focusPlaceId={place.Id}");
        }
    }

    private async void OnEditSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItemView { CommandParameter: SwipeView swipe } && swipe.BindingContext is Place place)
        {
            ResetPlaceCard(swipe);
            await Shell.Current.GoToAsync($"//home/map/main?editPlaceId={place.Id}");
        }
    }

    private async void OnDeleteSwipe(object sender, EventArgs e)
    {
        if (sender is not SwipeItemView { CommandParameter: SwipeView swipe } || swipe.BindingContext is not Place place) return;
        ResetPlaceCard(swipe);
        if (!await DisplayAlert("Delete", $"Delete {place.Name}?", "Yes", "No")) return;
        try { await _vm.DeletePlace(place); }
        catch (Exception ex) { await DisplayAlert("Sync Error", ex.Message, "OK"); }
    }

    private void OnPlaceCardSwipeChanging(object sender, SwipeChangingEventArgs e)
    {
        if (sender is SwipeView swipe) PlaceSwipeLayout.Change(swipe, e.Offset, 128);
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
        PlaceSwipeLayout.End(swipe, e.IsOpen, 128);
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

}
