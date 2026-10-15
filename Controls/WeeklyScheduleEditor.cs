using GeoSilence.Models;
using Microsoft.Maui.Controls.Shapes;

namespace GeoSilence.Controls;

public sealed class WeeklyScheduleEditor : VerticalStackLayout
{
    private sealed class Draft
    {
        public int Days = 31;
        public TimeSpan Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(17);
    }
    private readonly List<Draft> _drafts = new();
    private readonly Button _summary;
    private readonly ContentView _bodyHost = new() { IsVisible = false };
    private VerticalStackLayout? _rows;
    private bool _weekly;

    public WeeklyScheduleEditor()
    {
        Spacing = 8;
        _summary = new Button { FontSize = 12, CornerRadius = 10, HorizontalOptions = LayoutOptions.Fill };
        _summary.SetAppThemeColor(Button.BackgroundColorProperty, Color.FromArgb("#F5F3FF"), Color.FromArgb("#202028"));
        _summary.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#17171A"), Color.FromArgb("#F4F4F7"));
        _summary.Clicked += (_, _) =>
        {
            if (_bodyHost.Content == null) BuildBody();
            _bodyHost.IsVisible = !_bodyHost.IsVisible;
            UpdateSummary();
        };
        Children.Add(_summary);
        Children.Add(_bodyHost);
        Load(null);
    }

    public void Load(WeeklySchedule? schedule)
    {
        _weekly = schedule != null;
        _drafts.Clear();
        if (schedule != null)
            _drafts.AddRange(schedule.Windows.Select(w => new Draft
            {
                Days = w.Days, Start = TimeSpan.FromMinutes(w.StartMinute), End = TimeSpan.FromMinutes(w.EndMinute)
            }));
        _bodyHost.Content = null;
        _bodyHost.IsVisible = false;
        _rows = null;
        UpdateSummary();
    }

    public bool TryGetSchedule(out WeeklySchedule? schedule, out string error)
    {
        schedule = _weekly ? new WeeklySchedule(_drafts.Select(d =>
            new WeeklyTimeWindow(d.Days, (int)d.Start.TotalMinutes, (int)d.End.TotalMinutes))) : null;
        error = schedule?.IsValid == false
            ? "Add at least one time window. Select its days and different start/end times." : string.Empty;
        return error.Length == 0;
    }

    private void BuildBody()
    {
        var body = new VerticalStackLayout { Spacing = 10 };
        var toggleRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        toggleRow.Add(new Label { Text = "Weekly schedule", FontSize = 12, VerticalOptions = LayoutOptions.Center });
        var toggle = new Switch { IsToggled = _weekly, OnColor = Color.FromArgb("#6D28D9") };
        toggleRow.Add(toggle, 1);
        body.Children.Add(toggleRow);
        body.Children.Add(new Label { Text = "Device local time", FontSize = 11 });
        var windows = new VerticalStackLayout { Spacing = 10, IsVisible = _weekly };
        _rows = new VerticalStackLayout { Spacing = 10 };
        windows.Children.Add(_rows);
        var add = new Button { Text = "+ Add time window", FontSize = 12, CornerRadius = 8 };
        add.Clicked += (_, _) => { _drafts.Add(new Draft()); RenderRows(); UpdateSummary(); };
        windows.Children.Add(add);
        windows.Children.Add(new Label { Text = "An end time before the start time continues into the next day.", FontSize = 11 });
        body.Children.Add(windows);
        toggle.Toggled += (_, e) =>
        {
            _weekly = e.Value;
            windows.IsVisible = _weekly;
            if (_weekly && _drafts.Count == 0) { _drafts.Add(new Draft()); RenderRows(); }
            UpdateSummary();
        };
        RenderRows();
        _bodyHost.Content = body;
    }

    private void RenderRows()
    {
        if (_rows == null) return;
        _rows.Children.Clear();
        var labels = new[] { "M", "T", "W", "T", "F", "S", "S" };
        var names = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        foreach (var draft in _drafts)
        {
            var row = new VerticalStackLayout { Spacing = 6 };
            var days = new Grid { ColumnSpacing = 3 };
            for (var day = 0; day < 7; day++)
            {
                days.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                var bit = 1 << day;
                var button = new Button { Text = labels[day], FontSize = 11, Padding = 0, HeightRequest = 34, CornerRadius = 8 };
                SemanticProperties.SetDescription(button, names[day]);
                void Style()
                {
                    var selected = (draft.Days & bit) != 0;
                    button.SetAppThemeColor(Button.BackgroundColorProperty,
                        Color.FromArgb(selected ? "#6D28D9" : "#F5F3FF"), Color.FromArgb(selected ? "#6D28D9" : "#202028"));
                    button.SetAppThemeColor(Button.TextColorProperty,
                        selected ? Colors.White : Color.FromArgb("#17171A"), Colors.White);
                    SemanticProperties.SetHint(button, selected ? "Selected" : "Not selected");
                }
                button.Clicked += (_, _) => { draft.Days ^= bit; Style(); UpdateSummary(); };
                Style();
                days.Add(button, day);
            }
            row.Children.Add(days);
            var times = new Grid { ColumnSpacing = 10,
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Auto) } };
            var start = new TimePicker { Time = draft.Start, Format = "HH:mm", FontSize = 13 };
            var end = new TimePicker { Time = draft.End, Format = "HH:mm", FontSize = 13 };
            SemanticProperties.SetDescription(start, "Start time");
            SemanticProperties.SetDescription(end, "End time");
            start.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimePicker.Time)) { draft.Start = start.Time; UpdateSummary(); } };
            end.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimePicker.Time)) { draft.End = end.Time; UpdateSummary(); } };
            var from = new VerticalStackLayout { Spacing = 0 };
            from.Children.Add(new Label { Text = "From", FontSize = 11 }); from.Children.Add(start);
            var until = new VerticalStackLayout { Spacing = 0 };
            until.Children.Add(new Label { Text = "Until", FontSize = 11 }); until.Children.Add(end);
            times.Add(from); times.Add(until, 1);
            var remove = new Button { Text = "Remove", FontSize = 11, Padding = 4, VerticalOptions = LayoutOptions.Center };
            remove.Clicked += (_, _) => { _drafts.Remove(draft); RenderRows(); UpdateSummary(); };
            times.Add(remove, 2);
            row.Children.Add(times);
            _rows.Children.Add(new Border { Content = row, Padding = 8, StrokeShape = new RoundRectangle { CornerRadius = 10 }, StrokeThickness = 1 });
        }
    }

    private void UpdateSummary()
    {
        var value = "Any time";
        if (_weekly)
        {
            if (_drafts.Count == 1)
            {
                var draft = _drafts[0];
                var days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                var selected = draft.Days == 31 ? "Mon–Fri" : draft.Days == 127 ? "Every day"
                    : string.Join(", ", days.Where((_, i) => (draft.Days & (1 << i)) != 0));
                value = $"{selected} · {draft.Start:hh\\:mm}–{draft.End:hh\\:mm}";
            }
            else value = $"{_drafts.Count} time windows";
        }
        _summary.Text = $"Active times · {value} {(_bodyHost.IsVisible ? "▴" : "▾")}";
    }
}
