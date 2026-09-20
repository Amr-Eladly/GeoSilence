using GeoSilence.Models;
using GeoSilence.PageModels;
using GeoSilence.Controls;

namespace GeoSilence.Pages;

public partial class PlacesPage : ContentPage
{
    private readonly HomeViewModel _vm;
    private readonly HashSet<SwipeView> _activePlaceSwipes = new();

    public PlacesPage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        System.Diagnostics.Debug.WriteLine("PLACES page constructed");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        System.Diagnostics.Debug.WriteLine("PLACES page appeared");
    }

    protected override void OnDisappearing()
    {
        foreach (var swipe in _activePlaceSwipes.ToArray()) ResetPlaceCard(swipe);
        base.OnDisappearing();
    }

    private async void OnPlaceTapped(object sender, TappedEventArgs e)
    {
        if (sender is Border { BindingContext: Place place })
        {
            if (sender is Border { Parent: SwipeView swipe }) ResetPlaceCard(swipe);
            await Shell.Current.GoToAsync($"..?focusPlaceId={place.Id}");
        }
    }

    private async void OnEditSwipe(object sender, EventArgs e)
    {
        if (sender is SwipeItemView { CommandParameter: SwipeView swipe } && swipe.BindingContext is Place place)
        {
            ResetPlaceCard(swipe);
            await Shell.Current.GoToAsync($"..?editPlaceId={place.Id}");
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
