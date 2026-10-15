# Background location and battery investigation — 15 October 2026

Analysis only; application code, battery statistics, saved places, and sound settings were not changed. Only the Android emulator was connected, so these observations cannot quantify physical-phone battery drain.

## Direct answer

GeoSilence intentionally keeps an Android location foreground service running when private geofences have been registered. This means ongoing background service/location subscriptions, not continuous CPU execution. It is not simply one location fetch per minute.

| Component | Current behavior |
| --- | --- |
| Background LocationHeartbeatService | Balanced-power fused-location subscription; requested interval 180 seconds, fastest interval 60 seconds; sticky service with ongoing notification |
| HomeViewModel tracking while Map appears | Requests a fresh medium-accuracy location, waits for completion (timeout five seconds), updates distances/order, then delays ten seconds |
| Map user-location layer | IsShowingUser remains true; independently enables native Maps location behavior |
| Weekly schedule timer | One shared in-process timer while schedules exist; checks time at least once a minute and at nearer boundaries; no GPS request or cloud read |
| Geofences | Play Services registrations with no expiry and notification responsiveness set to zero |

Requested fused-location intervals are not measurements of GPS hardware duty cycle. Android/Play Services can combine subscriptions and share fixes. See [Google LocationRequest documentation](https://developers.google.com/android/reference/com/google/android/gms/location/LocationRequest).

## Live evidence

Emulator PID 17182 / app UID 10174. At the first inspection Android's launcher was foreground, but dumpsys activity services reported LocationHeartbeatService with `isForeground=true`, `startRequested=true`, and an age of approximately 1 hour 24 minutes.

The location service snapshot contained:

```text
ProviderRequest[@+5s0ms, HIGH_ACCURACY, WorkSource{10174 com.companyname.geosilence}]
com.google.android.gms[fused_location_provider] Request[@+5s0ms HIGH_ACCURACY,
  minUpdateInterval=0, hiddenFromAppOps,
  WorkSource{10174 com.companyname.geosilence}]
```

This is a live high-accuracy request attributed to GeoSilence, not just a historical statistics row. It remained present after opening GeoSilence, switching to Places, waiting seven seconds, returning Home, and waiting another seven seconds. The log confirms Places.OnAppearing during that test. The seven-second waits exceed the app's five-second one-shot location timeout, although they are not a long idle/Doze study.

Recent heartbeat callback timestamps were 20:07:16.304, 20:08:10.156, 20:09:04.210, 20:09:58.253, and 20:10:52.274: approximately 54 seconds apart. The requested three-minute interval therefore does not describe all actual callbacks. This does not prove that the heartbeat alone requested the underlying five-second high-accuracy feed.

The emulator supplies synthetic locations. Frequent provider deliveries there do not establish real GPS energy use on a physical phone. No battery percentage, power estimate, or expected savings was calculated.

## Likely causes and code gaps

1. **Native map location remains enabled offscreen.** MainPage.xaml sets IsShowingUser=True. MainPage.OnDisappearing stops the custom tracking loop but never disables the native map user-location layer. The application also lacks explicit Android Activity/app stop hooks for this location layer. In the inspected MAUI 9.0.120 map handler, CreatePlatformView calls MapView.OnResume; its shown disconnect path does not pair that with OnPause/OnDestroy. The live five-second request surviving Places/Home is consistent with a retained native location subscription. Attribution to that exact native component is still an inference, not a traced call stack.

   Source: [MAUI 9.0.120 Android MapHandler](https://github.com/dotnet/maui/blob/9.0.120/src/Core/maps/src/Handlers/Map/MapHandler.Android.cs).

2. **Custom tracking has no explicit app-background lifecycle stop.** It stops on page disappearance. App.xaml.cs/MainActivity.cs do not explicitly stop it on window stop/activity pause. App backgrounding must not be assumed to produce the same lifecycle events as a tab change. In-flight GetLocationAsync also receives no tracking cancellation token, so it can finish after StopTracking.

3. **Persistent heartbeat is unconditional after registration.** BackgroundGeofenceService starts it after successful geofence registration. Its visible stop paths handle zero private places/deleting the last private place. Logout clears authentication and stops the view-model loop but does not unregister geofences or stop this service. A boot/package-update receiver can register private database rows and restart it. Scheduled places outside their active windows still keep geofences/heartbeat registered.

4. **Geofences request zero notification responsiveness.** The comment claiming this means a default approximately 30-second cadence is not a reliable interpretation. It requests aggressive responsiveness; it does not guarantee a fixed arrival delay. Android recommends higher responsiveness values to conserve power. Any increase trades reaction latency against power and needs a physical-device reliability test.

   Sources: [Android background location and battery guidance](https://developer.android.com/develop/sensors-and-location/location/battery), [Geofence.Builder responsiveness](https://developers.google.com/android/reference/com/google/android/gms/location/Geofence.Builder).

The heartbeat was introduced as a geofence-reliability workaround. Its source comments asserting that geofences only work when another app requests location should not be treated as a universal Android requirement; the native geofencing API is designed for battery-efficient background monitoring. Validate whether this workaround is necessary on the actual phone before keeping it permanently enabled or removing it.

## Recommended order

1. Explicitly disable map user-location tracking and suspend custom Map polling when the app/map is not visible; restore them when it returns. Verify the five-second high-accuracy subscription disappears while native geofencing continues.
2. Reuse recent fixes for Map/list updates and pass cancellation through in-flight location requests instead of requesting a fresh fix on every return and every short refresh cycle.
3. Stop/unregister user-specific background work on logout and when no monitored private places remain.
4. Test native geofencing without the permanent heartbeat on the real phone, or make the fallback adaptive. Tune responsiveness with measured entry/exit latency rather than simply increasing all intervals.
5. Compare equal screen-off periods on the same phone with Android battery/location diagnostics. Emulator snapshots cannot establish the user's actual battery cost.

Artifacts: `obj/battery-investigation/` contains location snapshots for the original Home state, Places, and Home after the test, plus sanitized heartbeat times. The test ended on the launcher as it began.
