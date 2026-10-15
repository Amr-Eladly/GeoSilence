# Weekly schedule unit checks

Run `dotnet run --project Tests/WeeklySchedules/GeoSilence.ScheduleTests.csproj` from the repository root.

This dependency-free runner tests the actual schedule model and session engine with a controlled clock and fake audio/notification callbacks. It needs no emulator, Firebase connection, test account, or real sound-mode changes. Failures throw and return a nonzero exit code.

Coverage includes every weekday mask, randomized comparisons to absolute calendar intervals, exact boundaries, overnight/week rollover, overlapping windows/places, mode changes, deletion, confirmation expiry/reentry, clock jumps, and account/session isolation. It does not test Android timer delivery in Doze or native UI rendering. Schedule persistence is intentionally outside this prototype.
