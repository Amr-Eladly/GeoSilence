using GeoSilence.Models;
using GeoSilence.Services;

var checks = 0;
var monday = new DateTime(2026, 10, 19);
var work = Rule(31, 540, 1020);
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Throws<T>(Action action, string description) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, description); return; }
    Check(false, description);
}

// Compare to absolute intervals, rather than reproducing Contains' minute/day logic.
IEnumerable<(DateTime Start, DateTime End)> Intervals(WeeklySchedule rule, DateTime now)
{
    var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    for (var offset = -1; offset <= 8; offset++)
    {
        var date = now.Date.AddDays(offset);
        foreach (var window in rule.Windows)
        {
            var selectedDays = weekdays.Where((_, index) => (window.Days & (1 << index)) != 0);
            if (!window.IsValid || !selectedDays.Contains(date.DayOfWeek)) continue;
            var start = date.AddMinutes(window.StartMinute);
            var duration = TimeSpan.FromMinutes((window.EndMinute - window.StartMinute + 1440) % 1440);
            yield return (start, start + duration);
        }
    }
}

Check(work.IsActive(monday.AddHours(9)), "start is inclusive");
Check(!work.IsActive(monday.AddHours(9).AddTicks(-1)), "one tick before start");
Check(work.IsActive(monday.AddHours(17).AddTicks(-1)), "one tick before end");
Check(!work.IsActive(monday.AddHours(17)), "end is exclusive");
Check(!work.IsActive(monday.AddDays(5).AddHours(12)), "weekend excluded");
Check(work.NextBoundary(monday.AddDays(4).AddHours(18)) == monday.AddDays(7).AddHours(9), "wrap to next week");
Check(Rule(64, 1320, 120).IsActive(monday.AddHours(1)), "Sunday overnight continues Monday");
Check(!Rule(64, 1320, 120).IsActive(monday.AddHours(2)), "overnight excludes end");
Check(Rule(1, 1380, 0).IsActive(monday.AddHours(23.5)), "midnight end before boundary");
Check(!Rule(1, 1380, 0).IsActive(monday.AddDays(1)), "midnight end excluded");
foreach (var invalid in new[] { new WeeklyTimeWindow(0, 1, 2), new(128, 1, 2), new(1, -1, 2),
    new WeeklyTimeWindow(1, 1440, 2), new(1, 1, 1440), new(1, 1, -1), new(1, 540, 540) })
{
    Check(!invalid.IsValid, "invalid editor input rejected");
    Check(!invalid.Contains(monday), "invalid window never activates");
}
Check(!new WeeklySchedule([]).IsValid, "empty weekly schedule invalid");
Check(new WeeklySchedule([]).NextBoundary(monday) == null, "empty schedule has no boundary");
var draft = new[] { new WeeklyTimeWindow(1, 540, 720) };
var immutable = new WeeklySchedule(draft);
draft[0] = new(64, 1, 2);
Check(immutable.IsActive(monday.AddHours(10)), "draft changes cannot mutate saved schedule");

// Every weekday mask, including weekend and overnight combinations.
for (var days = 1; days <= 127; days++)
foreach (var (start, end) in new[] { (0, 1), (540, 1020), (1320, 120), (1380, 0), (0, 1439) })
for (var day = 0; day < 7; day++)
{
    var rule = Rule(days, start, end);
    foreach (var instant in new[] { monday.AddDays(day).AddMinutes(start).AddTicks(-1),
        monday.AddDays(day).AddMinutes(start), monday.AddDays(day).AddMinutes(end), monday.AddDays(day).AddHours(23.5) })
        Check(rule.IsActive(instant) == Intervals(rule, instant).Any(w => instant >= w.Start && instant < w.End),
            $"mask {days}, day {day}, {start}-{end}, {instant:O}");
}
var random = new Random(1710);
for (var example = 0; example < 2000; example++)
{
    var windows = Enumerable.Range(0, random.Next(1, 7)).Select(_ =>
    {
        var start = random.Next(1440);
        return new WeeklyTimeWindow(random.Next(1, 128), start, (start + random.Next(1, 1440)) % 1440);
    }).ToArray();
    var rule = new WeeklySchedule(windows);
    var now = monday.AddDays(random.Next(40)).AddSeconds(random.Next(86400));
    var intervals = Intervals(rule, now).ToArray();
    Check(rule.IsActive(now) == intervals.Any(w => now >= w.Start && now < w.End), "random interval membership");
    Check(rule.NextBoundary(now) == intervals.SelectMany(w => new[] { w.Start, w.End }).Where(t => t > now).Min(),
        "random next boundary");
}

var zone = new ScheduleZone(1, "user-a", "Work", ModeType.Silent, ActivationType.Automatic, 5, true);
using (var f = new Fixture(monday.AddHours(8)))
{
    f.Session.Observe(zone, true); f.Session.Set(zone, work);
    Check(f.Applied.Count == 0, "inside before window does not activate");
    f.At(monday.AddHours(9));
    Check(f.Applied.SequenceEqual(new[] { ModeType.Silent }), "window activates while already inside");
    for (var i = 0; i < 100; i++) f.Session.Evaluate();
    Check(f.Applied.Count == 1, "unchanged reevaluation does not rewrite mode");
    var restores = f.Restores;
    f.At(monday.AddHours(17));
    Check(f.Restores == restores + 1, "window end restores once");
    f.Session.Evaluate();
    Check(f.Restores == restores + 1, "inactive repeats do not restore repeatedly");
    f.Session.Observe(zone, false); f.At(monday.AddDays(1).AddHours(9));
    Check(f.Applied.Count == 1, "outside place never activates on time alone");
}
using (var f = new Fixture(monday.AddHours(10)))
{
    var fallback = zone with { Id = 2, Mode = ModeType.Vibrate, Distance = 30 };
    f.Session.Observe(zone, true); f.Session.Observe(fallback, true); f.Session.Set(zone, work);
    Check(f.Applied.Last() == ModeType.Silent, "nearest active place chosen");
    f.At(monday.AddHours(17));
    Check(f.Applied.Last() == ModeType.Vibrate, "falls back to unscheduled active place");
    f.Session.Observe(fallback, false); f.Session.Evaluate();
    Check(f.Restores == 1, "leaving last eligible place restores");
    f.Session.Observe(zone, true); f.At(monday.AddHours(10));
    f.Session.Observe(zone with { Mode = ModeType.Vibrate }); f.Session.Evaluate();
    Check(f.Applied.Last() == ModeType.Vibrate, "changing active place mode is applied");
    f.Session.Observe(zone with { Enabled = false }); f.Session.Evaluate();
    Check(f.Restores == 2, "disabled place cannot hold a mode");
}
using (var f = new Fixture(monday.AddHours(10)))
{
    f.Session.Observe(zone, true); f.Session.Set(zone, work);
    f.Session.Set(zone, Rule(31, 1080, 1200));
    Check(f.Restores == 1, "editing current window to inactive stops effect");
    f.Session.Set(zone, null);
    Check(!f.Session.HasSchedules && f.Applied.Count == 2, "Any time removes restriction");
    f.Session.Remove(zone.Id);
    Check(f.Restores == 2 && f.Session.Get(zone.Id) == null, "deleting active place restores and removes rule");
    f.Session.Remove(999);
    Check(f.Restores == 2, "deleting unknown place has no audio side effect");
}
using (var f = new Fixture(monday.AddHours(9)))
{
    var overlap = new WeeklySchedule([new(31, 540, 720), new(31, 660, 1020)]);
    f.Session.Observe(zone, true); f.Session.Set(zone, overlap);
    f.At(monday.AddHours(11)); f.At(monday.AddHours(12));
    Check(f.Applied.Count == 1 && f.Restores == 0, "overlap boundaries do not toggle mode");
    f.At(monday.AddHours(17));
    Check(f.Restores == 1, "union end restores");
}
using (var f = new Fixture(monday.AddHours(9)))
{
    var confirm = zone with { Activation = ActivationType.ConfirmFirst };
    f.Session.Observe(confirm, true); f.Session.Set(confirm, work); f.Session.Evaluate();
    Check(f.Prompts.Count == 1 && f.Applied.Count == 0, "Confirm First prompts once");
    f.Session.HandleConfirmation(1, false); f.Session.Evaluate();
    Check(f.Prompts.Count == 1 && f.Applied.Count == 0, "Ignore is respected for current visit");
    f.Session.Observe(confirm, false); f.Session.Evaluate();
    f.Session.Observe(confirm, true); f.Session.Evaluate();
    Check(f.Prompts.Count == 2, "reentry permits a fresh confirmation");
    f.Session.HandleConfirmation(1, true);
    Check(f.Applied.Count == 1, "confirmation activates");
    f.At(monday.AddHours(17)); f.Session.HandleConfirmation(1, true);
    Check(f.Applied.Count == 1, "expired notification cannot activate");
    f.At(monday.AddDays(1).AddHours(9));
    Check(f.Prompts.Count == 3, "new occurrence requires confirmation again");
    f.Session.Clear();
    f.Session.HandleConfirmation(1, true);
    Check(!f.Session.HasSchedules && f.Applied.Count == 1, "logout rejects old confirmation");
}
using (var f = new Fixture(monday.AddHours(10)))
{
    f.Session.Observe(zone, true); f.Session.Set(zone, work);
    f.At(monday.AddHours(20));
    Check(f.Restores == 1, "clock jump past end releases mode");
    f.At(monday.AddHours(10));
    Check(f.Applied.Count == 2, "clock jump back reevaluates eligibility");
    f.Session.Clear(); f.Session.Clear();
    Check(f.Restores == 2, "session reset restores at most once");
    Throws<ArgumentException>(() => f.Session.Set(zone, new WeeklySchedule([])), "invalid rule rejected by session");
}
using (var f = new Fixture(monday.AddHours(10)))
{
    f.Session.Observe(zone, true);
    f.Session.Observe(zone with { Id = 2, OwnerId = "user-b", Distance = 0, Mode = ModeType.Vibrate }, true);
    f.Session.Set(zone, work);
    Check(f.Applied.SequenceEqual(new[] { ModeType.Silent }), "preexisting other-user state cannot participate");
    Throws<InvalidOperationException>(() => f.Session.Set(zone with { OwnerId = "user-b" }, work), "cross-account edit rejected");
    f.Session.Clear();
    var nextUser = zone with { OwnerId = "user-b" };
    f.Session.Observe(nextUser, true); f.Session.Set(nextUser, work);
    Check(f.Session.Get(1) == work, "new account starts after explicit session reset");
}

Console.WriteLine($"PASS: {checks:N0} assertions; 127 weekday masks, 2,000 randomized schedules, and session lifecycle scenarios.");
static WeeklySchedule Rule(int days, int start, int end) => new([new(days, start, end)]);

sealed class Fixture : IDisposable
{
    public DateTime Clock;
    public List<ModeType> Applied = new();
    public List<int> Prompts = new();
    public int Restores;
    public WeeklyScheduleSession Session;
    public Fixture(DateTime now)
    {
        Clock = now;
        Session = new WeeklyScheduleSession(Applied.Add, () => Restores++, p => Prompts.Add(p.Id), _ => { }, () => Clock, false);
    }
    public void At(DateTime time) { Clock = time; Session.Evaluate(); }
    public void Dispose() => Session.Dispose();
}
