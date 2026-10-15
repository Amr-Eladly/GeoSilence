namespace GeoSilence.Models;

// Monday is bit 0; Sunday is bit 6. Times are minutes after local midnight.
public sealed record WeeklyTimeWindow(int Days, int StartMinute, int EndMinute)
{
    public bool IsValid => Days > 0 && Days < 128 && StartMinute >= 0 && StartMinute < 1440
        && EndMinute >= 0 && EndMinute < 1440 && StartMinute != EndMinute;

    public bool Contains(DateTime localTime)
    {
        if (!IsValid) return false;
        var day = ((int)localTime.DayOfWeek + 6) % 7;
        var minute = localTime.TimeOfDay.TotalMinutes;
        bool Selected(int index) => (Days & (1 << index)) != 0;
        return StartMinute < EndMinute
            ? Selected(day) && minute >= StartMinute && minute < EndMinute
            : (Selected(day) && minute >= StartMinute) ||
              (Selected((day + 6) % 7) && minute < EndMinute);
    }
}

public sealed class WeeklySchedule
{
    public IReadOnlyList<WeeklyTimeWindow> Windows { get; }
    public WeeklySchedule(IEnumerable<WeeklyTimeWindow> windows) => Windows = Array.AsReadOnly(windows.ToArray());
    public bool IsValid => Windows.Count > 0 && Windows.All(w => w.IsValid);
    public bool IsActive(DateTime localTime) => Windows.Any(w => w.Contains(localTime));

    public DateTime? NextBoundary(DateTime localTime)
    {
        DateTime? next = null;
        for (var offset = -1; offset <= 7; offset++)
        {
            var date = localTime.Date.AddDays(offset);
            var day = ((int)date.DayOfWeek + 6) % 7;
            foreach (var window in Windows.Where(w => w.IsValid && (w.Days & (1 << day)) != 0))
            {
                var start = date.AddMinutes(window.StartMinute);
                var end = date.AddMinutes(window.EndMinute).AddDays(window.EndMinute < window.StartMinute ? 1 : 0);
                foreach (var candidate in new[] { start, end })
                    if (candidate > localTime && (next == null || candidate < next)) next = candidate;
            }
        }
        return next;
    }
}
