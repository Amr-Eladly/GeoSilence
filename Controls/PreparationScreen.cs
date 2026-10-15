namespace GeoSilence.Controls;

// One Android window-level surface stays in place across Shell tab transitions.
internal sealed class PreparationScreen : IDisposable
{
#if ANDROID
    private readonly Android.Views.ViewGroup _host;
    private readonly Android.Widget.FrameLayout _cover;

    private PreparationScreen(Android.Views.ViewGroup host, Android.Widget.FrameLayout cover)
    {
        _host = host;
        _cover = cover;
    }
#endif

    public static PreparationScreen? TryShow()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity?.FindViewById(Android.Resource.Id.Content) is not Android.Views.ViewGroup host)
            return null;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1;
        int Dp(int size) => (int)(size * density);
        var cover = new Android.Widget.FrameLayout(activity) { Clickable = true, Focusable = true, Elevation = Dp(100) };
        cover.SetBackgroundColor(Android.Graphics.Color.ParseColor(dark ? "#111116" : "#F8F7FC"));
        var stack = new Android.Widget.LinearLayout(activity) { Orientation = Android.Widget.Orientation.Vertical };
        stack.SetGravity(Android.Views.GravityFlags.Center);
        var title = new Android.Widget.TextView(activity) { Text = "GeoSilence", TextSize = 28, Gravity = Android.Views.GravityFlags.Center };
        title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        title.SetTextColor(Android.Graphics.Color.ParseColor(dark ? "#F4F4F7" : "#17171A"));
        stack.AddView(title);
        var spinner = new Android.Widget.ProgressBar(activity) { Indeterminate = true, ContentDescription = "Preparing your pages" };
        spinner.IndeterminateTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#7C3AED"));
        stack.AddView(spinner, new Android.Widget.LinearLayout.LayoutParams(Dp(36), Dp(36)) { TopMargin = Dp(18), BottomMargin = Dp(18) });
        var message = new Android.Widget.TextView(activity) { Text = "Getting things ready?", TextSize = 15, Gravity = Android.Views.GravityFlags.Center };
        message.SetTextColor(Android.Graphics.Color.ParseColor(dark ? "#A1A1AA" : "#6B7280"));
        stack.AddView(message);
        cover.AddView(stack, new Android.Widget.FrameLayout.LayoutParams(-1, -2, Android.Views.GravityFlags.Center));
        host.AddView(cover, new Android.Views.ViewGroup.LayoutParams(-1, -1));
        cover.BringToFront();
        return new PreparationScreen(host, cover);
#else
        return null;
#endif
    }

    public void Dispose()
    {
#if ANDROID
        _host.RemoveView(_cover);
        _cover.Dispose();
#endif
    }
}
