using GeoSilence.Models;

namespace GeoSilence.Services;

internal sealed record ScheduleZone(int Id, string OwnerId, string Name, ModeType Mode,
    ActivationType Activation, double Distance, bool Enabled);

// Prototype state only. No database access, GPS polling, map updates, or per-place timers.
internal sealed class WeeklyScheduleSession : IDisposable
{
    private sealed class State(ScheduleZone zone)
    {
        public ScheduleZone Zone = zone;
        public WeeklySchedule? Schedule;
        public bool Inside, Confirmed, Prompted;
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, State> _places = new();
    private readonly Action<ModeType> _apply;
    private readonly Action _restore;
    private readonly Action<ScheduleZone> _requestConfirmation;
    private readonly Action<int> _cancelConfirmation;
    private readonly Func<DateTime> _clock;
    private readonly Timer? _timer;
    private (int Id, ModeType Mode)? _applied;
    private string? _owner;

    public WeeklyScheduleSession(Action<ModeType> apply, Action restore,
        Action<ScheduleZone> requestConfirmation, Action<int> cancelConfirmation,
        Func<DateTime>? clock = null, bool useTimer = true)
    {
        _apply = apply; _restore = restore;
        _requestConfirmation = requestConfirmation; _cancelConfirmation = cancelConfirmation;
        _clock = clock ?? (() => DateTime.Now);
        if (useTimer) _timer = new Timer(_ =>
        {
            try { Evaluate(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SCHEDULE: {ex}"); }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool HasSchedules { get { lock (_gate) return _places.Values.Any(p => p.Schedule != null); } }
    public WeeklySchedule? Get(int id) { lock (_gate) return _places.GetValueOrDefault(id)?.Schedule; }

    public void Observe(ScheduleZone zone, bool? inside = null)
    {
        lock (_gate)
        {
            if (_owner != null && zone.OwnerId != _owner) return;
            if (!_places.TryGetValue(zone.Id, out var state)) _places.Add(zone.Id, state = new State(zone));
            state.Zone = zone.Distance == double.MaxValue ? zone with { Distance = state.Zone.Distance } : zone;
            if (inside.HasValue) state.Inside = inside.Value;
        }
    }

    public void Set(ScheduleZone zone, WeeklySchedule? schedule)
    {
        if (schedule != null && !schedule.IsValid) throw new ArgumentException("Choose days and distinct start/end times.");
        lock (_gate)
        {
            if (_owner != null && _owner != zone.OwnerId)
                throw new InvalidOperationException("Reset the schedule session before switching accounts.");
            if (_owner == null)
            {
                _owner = zone.OwnerId;
                foreach (var id in _places.Where(p => p.Value.Zone.OwnerId != _owner).Select(p => p.Key).ToArray())
                    _places.Remove(id);
            }
            Observe(zone);
            var state = _places[zone.Id];
            state.Schedule = schedule;
            state.Confirmed = state.Prompted = false;
            _cancelConfirmation(zone.Id);
            Evaluate(forceRestore: true);
        }
    }

    public void Remove(int id)
    {
        lock (_gate)
        {
            _places.Remove(id);
            _cancelConfirmation(id);
            Evaluate(forceRestore: _applied?.Id == id);
        }
    }

    public void HandleConfirmation(int id, bool accepted)
    {
        lock (_gate)
        {
            if (_places.TryGetValue(id, out var state) && state.Inside && state.Zone.Enabled &&
                (state.Schedule?.IsActive(_clock()) ?? true)) state.Confirmed = accepted;
            Evaluate();
        }
    }

    public void Evaluate(bool forceRestore = false)
    {
        lock (_gate)
        {
            var now = _clock();
            var eligible = new List<State>();
            foreach (var state in _places.Values)
            {
                if (!state.Zone.Enabled || !state.Inside || !(state.Schedule?.IsActive(now) ?? true))
                {
                    if (state.Prompted) _cancelConfirmation(state.Zone.Id);
                    state.Confirmed = state.Prompted = false;
                    continue;
                }
                if (state.Zone.Activation == ActivationType.ConfirmFirst && !state.Confirmed)
                {
                    if (!state.Prompted)
                    {
                        state.Prompted = true;
                        _requestConfirmation(state.Zone);
                    }
                    continue;
                }
                eligible.Add(state);
            }
            var selected = eligible.OrderBy(p => p.Zone.Distance).ThenBy(p => p.Zone.Id).FirstOrDefault();
            if (selected != null)
            {
                var key = (selected.Zone.Id, selected.Zone.Mode);
                if (_applied != key) { _apply(selected.Zone.Mode); _applied = key; }
            }
            else if (_applied != null || forceRestore)
            {
                _restore();
                _applied = null;
            }
            var boundary = _places.Values.Where(p => p.Schedule != null)
                .Select(p => p.Schedule!.NextBoundary(now)).Where(t => t.HasValue).Min();
            // Recheck the local clock at least once a minute (including time-zone changes).
            var delay = boundary.HasValue ? boundary.Value - now : Timeout.InfiniteTimeSpan;
            if (delay > TimeSpan.FromMinutes(1)) delay = TimeSpan.FromMinutes(1);
            _timer?.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (_applied != null) _restore();
            foreach (var state in _places.Values.Where(p => p.Prompted)) _cancelConfirmation(state.Zone.Id);
            _places.Clear(); _applied = null; _owner = null;
        }
    }
    public void Dispose() { Clear(); _timer?.Dispose(); }
}
