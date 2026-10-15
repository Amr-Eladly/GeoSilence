# Add search to the saved Places page

This guide adds a search field below **All places**. Typing filters saved places by name, ignoring case and surrounding spaces. Clearing the field restores the list in its existing distance order.

Only edit `Pages/PlacesPage.xaml` and `Pages/PlacesPage.xaml.cs`. Keep filtering local to this page: `HomeViewModel.NearbyPlaces` also feeds the map, so do not remove items from that shared collection to implement search. No database, cloud request, or new package is needed.

## 1. Add the search field

In `Pages/PlacesPage.xaml`, add this namespace to the opening `ContentPage`:

```xml
xmlns:controls="clr-namespace:GeoSilence.Controls"
```

Change the outer Grid's rows from `Auto,*` to `Auto,Auto,*`.

Give the existing subtitle Label a name and remove its `Text="{Binding SavedPlacesSummary}"` binding:

```xml
<Label x:Name="ResultsSummaryLabel" FontSize="14"
       TextColor="{AppThemeBinding Light={StaticResource MapSecondaryText}, Dark={StaticResource MapSecondaryTextDark}}" />
```

After the heading's closing `VerticalStackLayout`, add:

```xml
<Border Grid.Row="1" Margin="0,0,0,14"
        StrokeShape="RoundRectangle 16" StrokeThickness="1"
        Stroke="{AppThemeBinding Light={StaticResource MapOutline}, Dark={StaticResource MapOutlineDark}}"
        BackgroundColor="{AppThemeBinding Light={StaticResource MapSurface}, Dark={StaticResource MapSurfaceDark}}">
    <controls:SurfaceSearchBar
        x:Name="SavedPlacesSearchBar"
        Placeholder="Search saved places"
        TextChanged="OnSavedPlacesSearchChanged"
        BackgroundColor="Transparent"
        FontFamily="OpenSansRegular" FontSize="15" HeightRequest="52"
        TextColor="{AppThemeBinding Light={StaticResource MapText}, Dark={StaticResource MapTextDark}}"
        PlaceholderColor="{AppThemeBinding Light={StaticResource MapSecondaryText}, Dark={StaticResource MapSecondaryTextDark}}"
        CancelButtonColor="{AppThemeBinding Light={StaticResource MapSecondaryText}, Dark={StaticResource MapSecondaryTextDark}}"
        SemanticProperties.Description="Search saved places by name" />
</Border>
```

Move the existing list-containing Grid from `Grid.Row="1"` to `Grid.Row="2"`.

## 2. Give the list and empty state names

Replace only the opening CollectionView tag with:

```xml
<CollectionView x:Name="SavedPlacesList" SelectionMode="None"
                Scrolled="OnPlacesCollectionScrolled">
```

This removes `ItemsSource="{Binding NearbyPlaces}"`. Keep the complete item template and its tap/edit/delete handlers unchanged.

In the empty-state VerticalStackLayout below the CollectionView:

- Replace `IsVisible="{Binding ShowEmptyState}"` with `x:Name="SearchEmptyState" IsVisible="False"`.
- Name its first Label `x:Name="EmptyTitleLabel"`.
- Name its second Label `x:Name="EmptyMessageLabel"`.
- Keep their existing styling. The code below supplies their text.

## 3. Add page-local filtering

In `Pages/PlacesPage.xaml.cs`, add:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
```

Add these fields inside `PlacesPage`:

```csharp
private ObservableCollection<Place>? _searchSource;
private bool _searchVisible;
private bool _filterQueued;
```

In the constructor, assign `_vm` before `InitializeComponent`, since XAML can raise events during initialization:

```csharp
public PlacesPage(HomeViewModel vm)
{
    _vm = vm;
    InitializeComponent();
    BindingContext = _vm;
}
```

Add these methods inside the same class:

```csharp
private void OnSavedPlacesSearchChanged(object sender, TextChangedEventArgs e)
    => QueueFilter();

private void AttachSearchSource()
{
    if (ReferenceEquals(_searchSource, _vm.NearbyPlaces)) return;
    if (_searchSource != null)
        _searchSource.CollectionChanged -= OnSearchSourceChanged;

    _searchSource = _vm.NearbyPlaces;
    _searchSource.CollectionChanged += OnSearchSourceChanged;
}

private void OnSearchSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    => QueueFilter();

private void OnSearchViewModelChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(HomeViewModel.NearbyPlaces) ||
        e.PropertyName == nameof(HomeViewModel.IsLoading))
        QueueFilter();
}

private void QueueFilter()
{
    // Always dispatch: a refresh may originate from asynchronous data loading.
    Dispatcher.Dispatch(() =>
    {
        if (!_searchVisible || _filterQueued) return;
        _filterQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _filterQueued = false;
            if (!_searchVisible) return;
            AttachSearchSource();
            ApplySavedPlacesFilter();
        });
    });
}

private void ApplySavedPlacesFilter()
{
    var query = SavedPlacesSearchBar.Text?.Trim() ?? string.Empty;
    var results = _vm.NearbyPlaces
        .Where(place => query.Length == 0 ||
            place.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        .ToList();

    // Close an open swipe before its card disappears from the filtered list.
    foreach (var swipe in _activePlaceSwipes.ToArray())
        ResetPlaceCard(swipe);

    // These are the original Place objects, so existing edit/delete/tap actions work.
    SavedPlacesList.ItemsSource = results;
    ResultsSummaryLabel.Text = query.Length == 0
        ? _vm.SavedPlacesSummary
        : $"{results.Count} of {_vm.NearbyPlaces.Count} saved places";

    SearchEmptyState.IsVisible = !_vm.IsLoading && results.Count == 0;
    bool hasSavedPlaces = _vm.NearbyPlaces.Count > 0;
    EmptyTitleLabel.Text = hasSavedPlaces ? "No matching places" : "No saved places yet";
    EmptyMessageLabel.Text = hasSavedPlaces
        ? "Try another name or clear your search."
        : "Go back to the map to add one.";
}
```

The extra dispatcher pass combines collection-change bursts (such as Clear followed by several Add operations) into one refresh. For the current small saved list, filtering in memory is sufficient; there is no need to start a network request or background task on each keystroke.

## 4. Connect the page lifecycle

Replace the existing `OnAppearing` with:

```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    _searchVisible = true;
    _vm.PropertyChanged -= OnSearchViewModelChanged;
    _vm.PropertyChanged += OnSearchViewModelChanged;
    AttachSearchSource();
    ApplySavedPlacesFilter();
}
```

Add the following at the **start** of the existing `OnDisappearing`. Preserve its existing swipe-reset loop and `base.OnDisappearing()` call:

```csharp
_searchVisible = false;
_vm.PropertyChanged -= OnSearchViewModelChanged;
if (_searchSource != null)
    _searchSource.CollectionChanged -= OnSearchSourceChanged;
_searchSource = null;
```

The query stays when switching tabs, because Shell caches the page. Returning from editing a place on Map reapplies the filter, so a renamed place is matched using its new name. Collection changes while Places is visible, including deletions and sync updates, also refresh the results.

This implementation matches names only. It does not search Google Maps or public places that have not been added to your saved list. If you later add inline name editing directly on Places, subscribe to each item's Name property changes as well; the current editing flow returns through Map.

## 5. Check the behavior

1. Build the Android target and open Places. All seven current saved places should appear.
2. Search for a partial name, then repeat with different capitalization and spaces around it.
3. Enter a nonexistent name: see **No matching places**, not **No saved places yet**.
4. Clear the search: the full list and original distance order return.
5. Tap a filtered result: Map should focus the correct place.
6. Edit a filtered place and return: the results should reflect its new name.
7. Switch between all three tabs: the query should persist and Map pins should remain unchanged by filtering.
8. Verify deletion with a disposable test place: the filtered result and count should update. Do not delete a real saved place just to test this.
9. Check both light and dark themes, plus the genuinely empty-list state using a test account if available.

Only this guide was added; the feature has not been applied or compiled as part of writing it.
