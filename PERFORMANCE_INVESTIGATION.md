# Android ANR investigation — 10 October 2026

## Findings

The connected Android 13 emulator recorded an ANR at 19:18:18 for `LocationHeartbeatService` (PID 3357): **executing service timeout**. This was not reported as a search-bar or bottom-sheet input timeout.

- The service performed Google Play Services initialization and location requests on the main thread. Its original start log at 19:17:41.313 was followed by the location-request log at 19:17:48.065: approximately 6.75 seconds. This measures the whole intervening path, not one proven slow call.
- `GeoLog` synchronously checked, rotated, and appended an external-storage file while holding a shared lock. Service callbacks and other main-thread callers could wait for storage or another writer.
- The emulator was also heavily overloaded: the ANR report recorded 97% total CPU usage, substantial memory pressure, and another application's service ANR. The app's full stack dump timed out, so the exact blocking instruction cannot be established from this report.
- Google Maps repeatedly reported `database disk image is malformed` from its tile-cache implementation. This was its external `cache/diskcache/map_cache.db`, separate from `files/geosilence.db` containing saved places.

## Changes

- `Platforms/Android/LocationHeartbeatService.cs`: keep foreground notification promotion in the service callback; queue location initialization, removal, and location callbacks on a dedicated `HandlerThread`. Queue cleanup before shutting down that thread, and avoid redundant foreground promotion.
- `Platforms/Android/GeoLog.cs`: keep immediate logcat output; resolve storage and write/rotate files on one asynchronous consumer. Bound the queue to 512 lines, dropping oldest queued diagnostics if overloaded. Recent queued file lines may be lost on process termination; logging cannot block the UI waiting for disk.
- `Services/BackgroundGeofenceService.cs`: snapshot the input collection, serialize registration operations, perform platform work off the caller's UI thread, and dispatch task continuations asynchronously.
- Emulator repair: stopped the app and renamed only the Maps disk-cache directory, allowing fresh tiles to be generated. Android/Maps subsequently removed the external cache contents, including the renamed backup. Saved places and sign-in data were not cleared. This repair is local to this emulator, not an automatic cache-reset feature shipped in the app.

## Validation and limits

- Android Debug build and installation succeeded: 0 errors, 82 warnings.
- Two subsequent launches successfully registered three active private geofences and received location callbacks on the dedicated worker.
- The heartbeat start callback completed in approximately 86 ms and 27 ms in those runs. Conditions differed from the original overloaded run; these are observations, not a controlled speedup benchmark.
- Exercised Map, Places, Account, scrolling, and sheet gestures. The Places page still showed all seven saved locations.
- No additional ANR appeared in Android's retained ANR records during the test window. No malformed Maps database errors appeared in the post-repair process log.
- Startup still showed dropped frames. A short successful test does not establish that every ANR cause is eliminated, and Debug/emulator timings should not be presented as production performance.
- Next performance measurement should use a Release build on a physical device, with separate cold-start and repeatable gesture traces. Avoid comparing accumulated pre-fix frame statistics with a different post-fix workload as a percentage improvement.

Raw logs, screenshots, and build output are under `obj/anr-investigation/` (local diagnostic artifacts).

Android's guidance distinguishes system overload from application blocking and recommends moving non-UI work off the main thread: [Diagnose and fix ANRs](https://developer.android.com/topic/performance/anrs/diagnose-and-fix-anrs).
