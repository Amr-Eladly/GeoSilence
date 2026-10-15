# Vibrate activates but a silent indicator appears

Investigated 14 October 2026. Documentation only; application code and device settings were not changed. ADB listed no connected devices, so the reported indicator was not reproduced. It is not yet clear whether the sign is Android's status-bar/volume/DND indicator, a GeoSilence notification icon, or a badge inside the app.

## Confirmed code behavior

1. **Vibrate is not mapped directly to Silent.** `Services/ModeService.cs:106` requests `InterruptionFilter.All`, then sets `AudioManager.RingerMode` to `RingerMode.Vibrate`. Silent has a separate branch setting `RingerMode.Silent` and `InterruptionFilter.None`. The explicit enum branches do not rely on Android and application enum values matching.
2. **Ringer state and DND state are handled separately, but only ringer state is logged after applying a mode.** The `done mode=... ringerAfter=...` log cannot establish that DND was cleared or that the system indicator matches. The interruption-filter helper checks permission and calls Android without verifying the resulting filter.
3. **GeoSilence notification icons do not identify sound mode.** `PlaceActivationNotificationService.cs:62` and `LocationHeartbeatService.cs:180` both use `Android.Resource.Drawable.StatSysWarning`. Neither chooses a vibrate-specific icon. The confirmation text does include the requested mode; the ongoing notification only says background location is active. These are not notifications confirming successful mode activation. If the reported sign is an app notification icon, this fixed icon is a confirmed presentation limitation, although it is a generic warning resource, not an explicitly selected silent icon.
4. **The background notification itself is deliberately silent.** Its channel disables sound and vibration. A system label describing that notification as silent would describe notification delivery, not the phone's ringer mode.
5. **In-app place badges describe configuration.** `Models/Place.cs` maps Vibrate to its Vibrate label, and `Pages/MainPage.xaml` has separate Vibrate color triggers. These do not read the actual Android audio/DND state. No reversed Vibrate/Silent badge mapping was found in these paths.

## Important Android-version caveat

Android documents that apps targeting API 35 and above generally use an app-associated automatic DND rule through `setInterruptionFilter`, rather than directly changing the global interruption filter. The previously generated project manifest targets API 35. On affected Android versions, requesting All must not be assumed to clear every other active DND rule. A remaining DND indicator is therefore a plausible explanation for a different icon alongside Vibrate, but this requires the phone's Android version and actual rule state to verify. It is not a confirmed diagnosis of this report.

Source: [Android NotificationManager.setInterruptionFilter](https://developer.android.com/reference/android/app/NotificationManager#setInterruptionFilter(int)).

## Separate overlap/state-management risk

- Foreground `HomeViewModel.UpdateModeForLocation` chooses the nearest eligible automatic private place from a distance-sorted list.
- Background `GeofenceBroadcastReceiver.FindFirstMatchingPlace` chooses the first database item matching the triggered/active IDs, without the same nearest-place ordering.
- The foreground view model, geofence receiver, and confirmation-action receiver can each apply modes. They do not use a shared serialized mode decision. Overlapping Silent and Vibrate places can therefore produce conflicting selections or successive writes. This is a real inconsistency in the code, but no runtime evidence connects it to the reported icon.
- `SetMode` returns void and can return early when policy access is missing. The foreground caller still records its intended place/mode as active afterward. Intended state is not necessarily verified device state. This is another reliability issue, not proof that the observed successful Vibrate activation failed.

## Minimal next verification, without code changes

1. Identify the exact sign and phone Android version. Distinguish a DND symbol, ringer icon, app notification, and in-app badge.
2. At the reported transition, read existing MODE logs and filtered `adb shell dumpsys audio` output for internal/external ringer state. Inspect relevant DND/Zen fields in `dumpsys notification` locally; avoid collecting or sharing unrelated notification content.
3. Check for `done mode=Vibrate ringerAfter=Vibrate`, then any subsequent Silent application or restoration. Correlate timestamps with geofence ENTER/EXIT and foreground mode logs.
4. Compare a single isolated Vibrate place with overlapping Silent/Vibrate places, and compare starting with DND off versus a pre-existing DND rule. Any live state-changing reproduction should be coordinated with the user.

## Conclusion

No direct Vibrate-to-Silent coding error was found. The strongest conditional leads are an ambiguous fixed app notification icon, a separate DND indicator, or conflicting overlapping-zone decisions. Confirm the actual indicator and simultaneous device states before selecting a fix. No fix is claimed.

## Emulator verification — 14 October 2026

Read-only inspection of `emulator-5554`, running Android 13, with installed GeoSilence targeting API 35. No code, sound settings, or simulated coordinates were changed. The following are emulator log timestamps.

- At 02:36:12.387, the geofence receiver received ENTER for place ID 68 (BME Q).
- At 02:36:12.432, it logged `applying mode=Vibrate ringerBefore=Normal`.
- At 02:36:12.460, it logged `done mode=Vibrate ringerAfter=Vibrate`.
- At 02:36:15.368, another application of Vibrate completed with `ringerAfter=Vibrate`. No conflicting Silent application appeared in the inspected recent MODE logs.
- Independent `dumpsys audio` inspection reported both internal and external ringer modes as **VIBRATE**, with `ZEN_MODE_OFF`.
- Notification service inspection agreed: `mZenMode=ZEN_MODE_OFF`, `mInterruptionFilter=1` (All). The Android 15 DND-rule caveat above does not explain this Android 13 observation.
- The emulator reports `mHasVibrator=true`. Its vibrator service recorded a completed System UI TOUCH vibration around the transition. That is evidence of a virtual vibration event, not a physical vibration test or proof of incoming-call vibration.
- The screenshot at approximately 02:37 shows BME Q with the correct orange **Vibrate** badge. The left status bar shows a warning triangle matching the generic notification icon selected in the app's source; it does not show an identifiable Silent/DND indicator in that capture. The screenshot alone does not identify which notification owns the triangle.

Screenshot: `obj/vibrate-investigation/emulator.png` (local diagnostic artifact).

**Updated finding:** entering the observed Vibrate place worked on this emulator. The reported Silent indication was not reproduced in the inspected state. A muted ring stream is expected in Vibrate and is not by itself evidence of Silent mode. The ambiguous generic notification icon remains a presentation issue; these observations do not prove it was the user's original sign. Exit/restoration, incoming calls, and other zones were not tested in this inspection.
