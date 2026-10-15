# Map rendering investigation — 14 October 2026

## Confirmed from source

- `Pages/MainPage.xaml.cs:327`: a changed pin snapshot clears the entire MAUI pin collection and creates every pin again. The snapshot is order-sensitive, so distance-driven reordering can invalidate it even if marker positions and appearances are unchanged.
- `Platforms/Android/PlaceMapHandler.cs:39`: each native refresh removes all existing Google Maps markers and rebuilds them. Dispatch coalescing already prevents a rebuild for every individual collection change, but the resulting rebuild still runs on the UI dispatcher.
- `Platforms/Android/PlaceMapHandler.cs:60`: every cluster rebuild allocates a bitmap, canvas, and paint, draws the numbered icon, and creates a bitmap descriptor. There is no application-level cluster-icon cache. Non-cluster icons also go through LoadImage and descriptor creation on each rebuild; underlying image caching may reduce decoding, so repeated file decoding is not established.
- `Pages/MainPage.xaml.cs:401`: clustering first creates all individual MAUI pins, adds them to the collection, then removes grouped pins and replaces them with clusters. That creates avoidable objects, handlers/events, and collection notifications. Cluster plain place data first, then create only the final displayed pins.
- Zoom-level changes trigger complete regrouping/rebuilding. Ordinary panning at the same computed zoom level is already guarded and does not itself force regrouping.

These paths explain avoidable UI work and allocations. They do not quantify battery drain or prove which method caused the multi-second launch stalls.

## Existing runtime evidence

The preceding physical-phone launch recorded approximately 4.97-second and 3.06-second frame stalls during page preparation/Map startup. These are whole UI frame stalls, not timings of RefreshPins or Google Maps alone. Mandatory Places/Account preloading also overlaps startup. The installed build is Debug.

## Initial priorities

Additional recurring work found:

- `HomeViewModel.StartTrackingAsync` requests a fresh medium-accuracy location through `LocationService`, recalculates distances, sorts places, then waits ten seconds before repeating. Each location request has a five-second timeout. `OnDisappearing` stops this loop, so it is not continuously polling on every tab. Awaiting location is not itself a five-second main-thread block.
- Initial Map setup requests location, then starts tracking, whose first iteration immediately requests location again. Reusing the initial fix would avoid this back-to-back request.
- `UpdateVisiblePlaces` raises its event even when marker data is unchanged. The scheduled page refresh also runs `UpdateSelectedGeofence`, which clears and recreates the selected radius circle even when its center/radius are unchanged. Marker snapshot equality prevents some pin rebuilds, but does not prevent this circle churn.
- Map has `IsShowingUser=True`, while the foreground polling and a balanced-power background heartbeat also use location. These are multiple location consumers, not proof of independent GPS sessions: Android may share fixes. Profile their combined power cost before changing accuracy or geofence behavior. The heartbeat requests a three-minute interval with a one-minute fastest interval.
- `RefreshPins` enumerates Explore candidates even in My Places mode, before checking snapshot equality. Avoid this filtering when public pins will not be displayed.

## Recommended changes

1. Keep native markers by stable place/cluster identity; add, update, or remove only changed markers. Compare pin state independently of nearby-list ordering.
2. Cluster data before creating UI pins. Cache cluster icons by count/style/density and reuse ordinary marker descriptors with explicit lifetime management.
3. Measure first native Map creation separately from pin generation and marker application. Compare complete Release builds on the same phone before attributing initialization cost to Google Maps.
4. Reuse recent location fixes for initial centering/tracking; skip unchanged circle updates and unnecessary Explore filtering. Preserve background geofencing. Measure CPU/frame times and power separately: frame stalls alone do not establish battery consumption.

No rendering behavior has been changed. Battery/power impact has not been measured.

## Live emulator follow-up — 14 October 2026

Tested the existing Debug build on Android 13 x86_64, with 11 saved places visible in the UI. Fresh launch PID 5347. No application-code changes or installation. Google Maps uses its LATEST renderer and the emulator's OpenGL ES translator backed by Intel Iris Xe; this is not physical-phone GPU performance.

After startup, Android `gfxinfo` statistics were reset separately for each short workload:

| Workload | Frames | Reported janky frames | Median | 95th percentile | 99th percentile |
| --- | ---: | ---: | ---: | ---: | ---: |
| Eight horizontal map pans, approximately 6.8 s | 361 | 24 (6.65%) | 16 ms | 20 ms | 24 ms |
| Four double-tap zoom gestures | 83 | 18 (21.69%) | 16 ms | 32 ms | 97 ms |
| Four alternating sheet-area swipes | 90 | 19 (21.11%) | 17 ms | 34 ms | 93 ms |

These are exploratory single runs, not controlled benchmark averages. No per-frame screen recording verified every sheet gesture; the final screenshot confirms zoom changed and the sheet was collapsed. Small sample sizes, differing map viewports, automatic location work, and residual tile loading can affect comparisons. `gfxinfo` describes the app window's rendering and must not be treated as a complete profile of the Maps GL renderer. Its legacy jank counter was not used.

**Interpretation:** loaded-map panning was substantially less disruptive than initial startup in this session. Zoom and sheet-area interactions showed longer frame-time tails than panning. This is consistent with further work needed around redraws/layout, but does not isolate marker rebuilding as the measured cause. GPU frame-time 95th percentiles were approximately 14–15 ms; CPU/UI and draw-command counters also registered slow frames, so a GPU-only explanation is not established.

Additional findings:

- Maps logged a specific 63 ms main-thread parameter wait during `MapView.onCreate`, reached through MAUI Shell fragment creation and native layout. Initial SDK setup is a confirmed contributor, but the log does not account for the complete multi-second startup stalls.
- Source review confirms the sheet currently translates native views during drag. Its HeightRequest is updated outside the active gesture; the map is the background child of a full Grid. The former every-drag-frame row-resizing diagnosis does not describe the current implementation.
- A pre-test process snapshot reported approximately 314 MiB PSS and 422 MiB RSS for the entire app. This includes runtime, pages, SDKs, and map allocations; it is neither map-only memory nor evidence of a leak. No repeated-run growth test was performed.
- A post-test thread snapshot showed no continuously busy Maps worker at that instant. This does not establish battery consumption. The emulator had about 326 MiB swap in use system-wide, and earlier system CPU history included substantial System UI/Play Services activity. Host/emulator load remains a confounder.
- No crash entries were returned for the tested PID. No malformed-map-cache error was found in the captured cold-launch log; the earlier historical cache corruption should not be assumed to explain this run.

Artifacts: `obj/map-startup-investigation/` contains cold-launch logs, independent gfxinfo samples, and before/after screenshots. Next useful measurements are Release on the physical phone and timed marker-application/cluster/layout spans. The current evidence supports prioritizing startup restructuring, then differential marker updates and cached icons; it does not justify disabling map features or background geofencing.

## First three optimizations implemented — 15 October 2026

- MainPage canonicalizes place snapshots independently of distance order and reuses unchanged individual/cluster pin objects. It reconciles collection membership instead of clearing all pins. A changed individual pin is replaced; unchanged pins survive. Explore candidates are only enumerated in Explore mode.
- Clustering now operates on place snapshots before allocating UI pins. Only the final visible single pins and cluster pins are created. Cluster identity includes grid cell/zoom, member count, centroid, and public/private styling.
- Android's handler reconciles native markers by retained pin reference. Pending image loads are tracked so unrelated collection changes do not restart them; removed pins and disconnected map generations cannot add stale markers. Native markers and cached descriptors are released on disconnect.
- Cluster bitmap descriptors are cached by count, public/private styling, and display density, with a maximum of 64 entries. Ordinary image loading remains unchanged for newly created individual markers; unchanged markers do not reload images.
- Selected geofence circles remain in place when geometry is unchanged; changed geometry updates the retained circle. Removing selection or entering Explore removes it. Sheet HeightRequest is assigned only when its computed value changes.

Validation: the Android C#/XAML pipeline compiled successfully. Diagnostic DLL/PDB deployment to the emulator was hash-verified. Screenshots under `obj/map-incremental-check/` show saved pins, Explore hiding saved pins, a three-place cluster after zooming out, and individual pins after tapping the cluster to zoom in. Returning from Places retained the map markers. No crash or disposed-object exception was found in the inspected process log (PID 10433). The final full Android build completed with 0 errors and 83 existing XAML/compiler warnings; output is in `obj/map-incremental-final-build.log`. No speedup percentage is claimed; a controlled Release-device comparison remains outstanding. Location acquisition, geofencing, and cloud persistence were not modified.
