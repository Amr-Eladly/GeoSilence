# Weekly active times prototype

Each place's existing Add/Edit form has one new collapsible Active times section above Save. Any time is the default. Enabling a weekly schedule allows multiple windows, each with Monday–Sunday selections and start/end times. An earlier end time continues into the following day. The editor is built only when expanded. Its helper label reads Device local time.

Schedule changes are stored in memory by the current session, not in SQLite, Firestore, public place documents, or cloud sync. Existing ordinary place saving still works as before. Process termination or signing out discards schedules. This intentionally remains a prototype until database integration is requested.

The session engine combines location presence and local-time eligibility. It reevaluates when location/geofence events arrive, a schedule changes, or the next time boundary is reached. One shared timer is active only while schedules exist; it checks the clock at least once per minute and does not poll GPS, fetch cloud data, or refresh map pins. It can activate while already inside a place, stop at the end of a window, fall back to another eligible place, and honor Confirm First. App suspension/Doze can delay in-process timer execution; no persistent Android alarm scheduling was added.

## Tests

Run:

```powershell
dotnet run --project Tests/WeeklySchedules/GeoSilence.ScheduleTests.csproj
```

The dependency-free unit runner compiles the actual model and session-engine source with controlled time and fake audio/notification callbacks. It passed 21,838 assertions covering every weekday mask, 2,000 randomized schedules checked against absolute date intervals, tick-level boundaries, overnight/week rollover, overlapping windows and places, edits, deletion, confirmation/expiry/reentry, clock jumps, and account isolation/reset.

The expanded checks found and fixed pre-owner observations from another account being eligible. The session now drops those observations when selecting its owner and rejects cross-account schedule edits until explicitly reset. The tests are excluded from the MAUI application's compilation.

These tests exercise schedule decisions; they do not validate native sound-permission behavior, Doze timer delivery, or UI rendering. The user reported the prototype UI working before the helper-label change. The full Android build for the final label and session fix passed with 0 errors and 83 warnings; output is in `obj/weekly-schedule-tested-build.log`.
