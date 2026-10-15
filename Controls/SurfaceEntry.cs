namespace GeoSilence.Controls;

// Used inside a styled input Border instead of the platform's underline.
public class SurfaceEntry : Entry
{
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is Android.Widget.EditText entry)
            entry.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
        if (Handler?.PlatformView is UIKit.UITextField entry)
            entry.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
    }
}
