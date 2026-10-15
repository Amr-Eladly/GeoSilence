# Places and tab-switching follow-up — 15 October 2026

Analysis only. No application code or saved data changed.

## Runtime observations

Both phone and emulator were connected. The phone was locked (black screenshot, NotificationShade focus), so it was not unlocked or used for gestures. Tests used the already-running Android 13 Debug emulator process 11478, with previously visited/cached tabs.

Six switches were sampled individually with gfxinfo reset before each tap, followed by a two-second observation window:

| Destination | Frames | Janky frames | 95th percentile | 99th percentile |
| --- | ---: | ---: | ---: | ---: |
| Places | 70 | 2 (2.86%) | 24 ms | 46 ms |
| Map | 69 | 2 (2.90%) | 18 ms | 20 ms |
| Account | 69 | 2 (2.90%) | 18 ms | 19 ms |
| Map | 70 | 2 (2.86%) | 19 ms | 34 ms |
| Places | 69 | 2 (2.90%) | 19 ms | 53 ms |
| Map | 70 | 2 (2.86%) | 18 ms | 26 ms |

The diagnostic log marker preceding each input command to Places was followed by OnAppearing 55 ms and 58 ms later. This interval includes shell/input overhead and measures the lifecycle event, not visual completion. Map's analogous cached-local-data log appeared 57–79 ms after the marker and reported InitializeAsync taking 0 ms. Places was not reconstructed in these switches. The existing process log contains its initial construction at 00:42:19.576 and a 71-skipped-frame report at 00:42:20.794; this establishes an earlier first-use stall, not its exact method or duration.

These brief warmed-tab samples did not reproduce a multi-second delay. They do not invalidate the user's phone experience or measure first-use latency. App-window gfxinfo is not a complete Maps GL frame trace. No reliable percentage improvement follows from these samples.

A subsequent Places scrolling sample returned zero rendered frames/attached views. Focus was then NotificationShade, so that sample is invalid and was discarded. The app process remained alive, and the retained crash buffer returned no entries. Do not interpret the zero-frame sample as perfect scrolling or an app crash.

## Source findings, ordered by practical relevance

### 1. First Places visit still pays native card construction/layout

PlacesPage is created lazily after the startup change. Its constructor initializes XAML and binds the existing shared HomeViewModel. Its OnAppearing only logs; it does not fetch Firestore or reread the place database. Shell reuses the page on subsequent visits, as the live log confirms.

Each card includes a SwipeView, two custom swipe-action views, rounded borders, nested layouts, labels, bindings, and gesture/reset handlers. The CollectionView is properly placed in a bounded Grid, so there is no evidence here of the classic unbounded ScrollView/StackLayout virtualization problem. Root-level view counts cannot prove all rows are realized.

No ItemSizingStrategy is specified, so CollectionView uses individual item measurement. For these visually uniform cards, MeasureFirstItem is a promising low-impact experiment, with font scaling/accessibility verified before adoption. Do not hard-code a row height that clips larger fonts.

Reference: [Microsoft CollectionView layout and item sizing](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/layout?view=net-maui-9.0). Microsoft documents MeasureFirstItem as a performance improvement when item sizes are uniform.

### 2. Map re-entry restarts fresh location and list processing

MainPage.OnAppearing runs cached initialization, reattaches events, reapplies mode-button state, checks pins/layout, and starts tracking again. StartTrackingAsync immediately calls LoadAsync, which requests location, recalculates all distances, sorts places, and publishes collection updates. Therefore, every return to Map can create location/list work shortly after the tab transition, even when local initialization reports 0 ms. That 0 ms log does not measure the whole appearance path.

StopTracking cancels the loop token, but LocationService's Geolocation.GetLocationAsync request does not receive that token. LoadAsync does not recheck it before publishing. An in-flight request can complete after leaving Map and update NearbyPlaces while Places is appearing or scrolling. This is a confirmed cancellation gap; its contribution to the user's pauses is not yet timed. Reusing recent fixes plus preventing obsolete requests from publishing would address it without disabling background geofencing.

### 3. Distance order can still move list rows

The map now ignores list reorder for marker rendering, but NearbyPlaces remains distance-sorted. CollectionReconciler preserves objects and avoids Reset, yet issues individual Move notifications when order changes. Native list updates can overlap tab appearance or gestures. VisiblePlacesUpdated is also raised even when membership/order is unchanged. These mechanisms are visible in source, but no collection-mutation trace was captured in this test.

### 4. Custom horizontal swipe changes layout during the gesture

PlaceSwipeLayout.SetInset changes the inner Grid.Margin for each SwipeChanging event. Unlike native translation, a margin change participates in measurement/layout and can remeasure card text. Binding-context, unloading, scrolling, and disappearance handlers also reset swipe state. This is a concrete performance candidate for horizontal card swipes, particularly during recycling. It does not explain every ordinary tab switch, and should not be conflated with the already-translated map bottom sheet.

### 5. Account is cached, but still performs repeat work

AccountPage's first appearance loads remote profile/account/avatar information. Later EnsureLoadedAsync calls use the cached profile, but LoadCoreAsync still assigns labels/avatar state and calls GetLocationsCountAsync. That method reads and materializes all saved places just to count them on every completed visit. This is avoidable repeated work; no evidence establishes it as the primary pause with this small list.

Places item-template bindings already specify x:DataType. Adding compiled bindings at the page root could reduce a few header bindings, but is unlikely by itself to explain seconds of delay.

## Recommended next implementation order

1. Trial MeasureFirstItem with the existing card design and verify large fonts, swipe actions, scrolling, and first/repeated visits.
2. Reuse a recent location fix on Map re-entry and reject stale tracking results after disappearance; keep background geofencing independent.
3. Replace swipe-time margin resizing with a layout-independent visual approach that preserves the current action reveal behavior.
4. Avoid unchanged appearance assignments/list notifications and use cached/observable saved-place count on Account.
5. Measure first and repeated switches in a Release build on the unlocked physical phone. If stalls remain, capture managed/native layout spans before proposing a navigation-framework rewrite.

Artifacts: `obj/tab-investigation/` contains per-switch gfxinfo output, switch summary JSON, process logs, and the discarded scrolling sample. No performance fix is claimed in this investigation.

## Requested follow-up changes — 15 October 2026

- Removed Account's saved-place count label and its per-appearance database count call.
- Added a direct Take photo button beside the photo options, a photo-operation busy indicator, and duplicate-operation protection. The existing media capture path now opens before remote profile loading, checks camera availability, and handles account changes. Android declares camera permission, optional camera hardware, and image-capture discovery; iOS has camera/photo-library usage descriptions.
- Places initially creates a lightweight header/loading view. A deferred DataTemplate builds the list after the page can present its spinner. The spinner is dismissed after initial list layout, with error/retry handling and protection against finishing an obsolete tab appearance.
- The created list view stays attached to the cached Shell page and remains bound to the shared HomeViewModel's observable collection. Repeated appearances do not reconstruct the template, replace ItemsSource, or wait for another Firestore download. Changes continue through the shared collection. CollectionView uses MeasureFirstItem for the uniform cards.
- Retaining views preserves the managed layout tree and allows native row reuse; Android may still measure/redraw on attachment, rotation, font changes, or data changes. This is an in-session view cache, not a persistent screenshot cache or a guarantee of zero rendering. First native row construction still requires UI-thread work, so a spinner cannot eliminate all frame stalls.

Initial emulator validation captured the in-page spinner, all ten saved places after loading, and a repeat Places appearance with no second template-preparation log. Screenshots are in `obj/account-places-check/`. The freshly booted emulator was heavily stalled during startup; timings from this session should not be used as a performance benchmark. Final Android build output is `obj/account-places-loading-build.log`.

Final full Android build passed with 0 errors and 83 warnings. The Account screenshot confirms the count is gone and Take photo is directly available. No crash entries were returned for the tested process. The merged build manifest includes CAMERA permission, optional camera hardware, and IMAGE_CAPTURE discovery. The emulator resolves an image-capture activity, but full APK update failed with INSTALL_FAILED_INSUFFICIENT_STORAGE, so application camera permission/launch and completed photo capture/upload were not verified. No app data was cleared to make space. Native spinner animation can still pause during synchronous UI construction; the retained list avoids repeating that construction on ordinary later visits.
