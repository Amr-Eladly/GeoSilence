# Startup investigation — 13 October 2026

## Evidence and measurement limits

The user reports approximately 45 seconds from tapping the icon: 20 seconds on the native .NET splash and 25 seconds on the loading screen.

The connected physical phone's retained log contained:

```text
23:42:37.585 MAIN local places ready: 0 ms
23:42:41.673 PAGE PREPARATION completed: 16781 ms
```

The preparation measurement starts after session restoration and the startup page's initial layout. It is not total startup time. The 0 ms message is a later, cached HomeViewModel initialization on Map; it does not measure the original database read.

Android package metadata confirms the installed application is debuggable, with arm64-v8a as its primary ABI. The phone disconnected before a fresh cold launch could be measured. Consequently, neither the reported 20-second splash nor the full 45-second launch has been independently timed in this investigation.

### Reconnected phone: one fresh cold launch

After the phone reconnected, a force-stop followed by an explicit Activity launch produced these timestamps for PID 22017:

- Activity launch request: 23:44:38.730; process started: 23:44:38.871.
- Preparation timer start inferred from completion minus duration: 23:44:50.688.
- Places constructed: 23:44:51.857.
- Map's cached local initialization: 23:45:03.850.
- Preparation complete: 23:45:11.229, reporting 20,541 ms.

Thus this run took approximately **32.5 seconds from launch request to preparation completion**: about 12.0 seconds before the preparation timer and 20.5 seconds within it. The initial 12 seconds includes startup/session/startup-page layout; it is not an isolated measurement of native splash visibility. Overlay dismissal follows the completion log.

Android reported 297 skipped frames (approximately 4.97 seconds) during preparation, and another 183 (approximately 3.06 seconds) near Map startup. This confirms substantial UI-thread stalls, though it does not identify the individual expensive methods. `am start -W` itself timed out after 10.3 seconds, so its WaitTime must not be used as the successful launch duration. These are one-run observations, not an average or a measured improvement.

## Confirmed loading-path problem

`AppShell.RouteForSessionAsync` blocks access to the app while it performs this sequence:

1. Restore the saved session from secure storage.
2. Display and lay out StartupPage.
3. Initialize HomeViewModel and load local saved places.
4. Construct PlacesPage, navigate to it, and wait for visible layout.
5. Construct AccountPage, navigate to it, and wait for visible layout.
6. Construct MainPage, navigate to it, and wait for visible layout.
7. Remove the native preparation overlay.

This preload design moves all tabs' first-use UI construction onto the startup path, with Map last. It was introduced to improve initial tab switching, but makes the application unavailable while hidden pages render. The measured 16.8 seconds covers the local initialization and three-tab preparation together; it does not isolate the removable overhead of Places and Account.

`WaitForFirstLayoutAsync` polls for layout for up to four seconds and then waits another 150 ms. The four seconds is a polling limit, not a fixed delay per page. Four successful calls add 600 ms of explicit settling delays across startup; they do not explain the entire launch duration. Busy UI work can also delay polling continuations, so this is not a strict total-time bound.

Account's `OnAppearing` starts account lookup, remote profile loading, and possibly avatar fetching. Shell preparation does not explicitly await completion of those requests: it waits for layout. They can compete with startup work, and visiting Account does not guarantee its data will be ready when the overlay disappears.

HomeViewModel awaits local SQLite initialization and reading, then starts cloud synchronization, geofence registration, and public-place loading in a background task. Session restoration reads secure storage without requesting token refresh. There is no evidence that startup explicitly waits for the entire Firestore synchronization.

## Initial native splash: still needs a trace

The existing logs do not separate runtime initialization, MAUI builder creation, global XAML resources, Shell construction, and first Activity layout. MainApplication's existing startup log occurs after `base.OnCreate`, leaving a substantial early interval unmeasured.

The installed Debug build is a relevant variable, but its cost has not been established by a same-device Release comparison. Do not assign all 20 seconds to Debug mode or promise a particular Release speedup.

The locally generated Android manifest includes Mono runtime, AndroidX startup, and Firebase initialization providers. The project references Plugin.Firebase and Plugin.Firebase.Auth, while the inspected authentication service uses Firebase REST endpoints. No direct Plugin.Firebase/CrossFirebase references were found in application C# source. These dependencies merit an audit, but removing them safely requires checking their full use and validating the resulting build; their startup cost is not measured.

## Recommended order

1. Remove mandatory hidden navigation to Places and Account. Restore the session, read local places, and display Map. Preserve tab page reuse. Prepare optional data afterward without navigating away from the user's current tab. Native UI construction must remain on the UI thread; Task.Run is not a safe way to preload whole pages.
2. Measure the shortened path on the phone. Record process start, MainApplication entry, MAUI builder, App resources, Shell creation, session restoration, original SQLite read, Map construction/layout, and overlay dismissal. Measure first displayed frame separately from usable Map and loaded map tiles.
3. Compare complete Debug and Release builds on the same phone without an attached debugger, using several force-stopped launches. Preserve sign-in and saved places; do not clear app data. Record medians and ranges rather than a single best launch.
4. Use those measurements to target the remaining splash cost: audit unused auto-initializing dependencies and global resources. Keep cloud synchronization and geofencing functioning; do not disable core services just to reduce a startup number.

No application behavior was changed during this analysis. No performance improvement is claimed yet.

## Additional emulator measurements — 14 October 2026

Only the Android 13 x86_64 emulator was connected. Installed package is Debug, last updated 02:32:26 on October 14. No rebuild, reinstall, data clearing, or application-code modification was performed. These timings must not be compared with the earlier physical-phone numbers as a speedup.

One force-stopped launch (PID 5347) returned `LaunchState: COLD` and Android `TotalTime: 5706 ms`. The script observed preparation completion approximately 16.98 seconds after starting the launch command (includes command/polling overhead). First Activity display is therefore substantially earlier than app preparation completion.

| Emulator log timestamp | Event |
| --- | --- |
| 15:53:20.240 | MainApplication's post-base.OnCreate log |
| 15:53:20.623 | MapsInitializer begins selecting LATEST renderer |
| 15:53:20.971 | MapsInitializer reports loadedRenderer LATEST |
| 15:53:22.642 | Preparation timer start, inferred from duration |
| 15:53:23.447 | Places page constructed |
| 15:53:28.518 | MainPage reports cached local initialization: 1 ms |
| 15:53:29.572 | Maps reports a 63 ms main-thread feature-parameter wait |
| 15:53:33.274 | Preparation completes: 10,632 ms |

An earlier retained emulator launch reported 11,278 ms preparation and a 43 ms Maps parameter wait. The fresh launch contains skipped-frame bursts before and during page preparation. Maps' captured stack runs through `MapView.onCreate`, MAUI `ShellFragmentContainer.onCreateView`, and ViewPager/RecyclerView layout. This establishes native map creation inside a UI layout path, but the 63 ms feature wait is only one measured sub-operation, not the total Map initialization cost. Renderer initialization also begins before the Places warmup and before MainPage's appearance; moving tab warmup alone will not eliminate all Maps startup work.

There are numerous Java concurrent GC messages during initialization. Most listed stop-the-world pauses are sub-millisecond to a few milliseconds, with one approximately 16 ms pause. Do not interpret a concurrent collection's total duration as a main-thread pause or claim that GC alone explains seconds of delay.

Evidence is in `obj/map-startup-investigation/launch.txt` and `cold-launch.log`. Exact per-method managed costs remain unmeasured. The highest-confidence architectural fix remains shortening the mandatory tab-preparation path, followed by a same-device Release comparison and method-level tracing.

## Step 1 implemented — 14 October 2026

Removed construction, hidden navigation, and layout waits for Places and Account from AppShell's startup route. After session restoration and local-place initialization, startup opens Map and dismisses the existing cover after Map layout. Other tabs retain Shell's lazy page creation/cache. Cloud initialization remains asynchronous; account profile loading happens on first Account appearance. This deliberately trades first-tab native construction for earlier access to Map.

Emulator diagnostic deployment used the managed DLL/PDB produced by the full MAUI build's completed XAML pipeline, copied into the existing debug override directory and verified by SHA-256. No app data was cleared. PID 8023 logged preparation complete in 8,675 ms; Android first-display TotalTime was 5,528 ms. No Places construction occurred before preparation completion. Places construction appeared only after tapping the tab; navigation back to Map succeeded. No crash entries were returned during the startup check. The earlier 10,632 ms preparation sample is not a controlled baseline, so no guaranteed percentage improvement is claimed. APK build results are recorded in `obj/map-first-build.log`.
