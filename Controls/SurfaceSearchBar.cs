namespace GeoSilence.Controls;

// The containing Border supplies the surface; keep the native search controls intact.
public class SurfaceSearchBar : SearchBar
{
    public SurfaceSearchBar()
    {
        Loaded += (_, _) => ClearNativeBackground();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        ClearNativeBackground();
    }

    private void ClearNativeBackground()
    {
#if ANDROID
        if (Handler?.PlatformView is AndroidX.AppCompat.Widget.SearchView search)
        {
            search.SetBackgroundColor(Android.Graphics.Color.Transparent);
            foreach (var name in new[] { "search_bar", "search_edit_frame", "search_plate", "search_src_text", "submit_area" })
            {
                var id = search.Context?.Resources?.GetIdentifier(name, "id", search.Context.PackageName) ?? 0;
                if (id != 0)
                    search.FindViewById(id)?.SetBackgroundColor(Android.Graphics.Color.Transparent);
            }
        }
#endif
    }
}
