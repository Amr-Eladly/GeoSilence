using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.Graphics.Drawables;
using GeoSilence.Controls;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Maps.Handlers;
using Microsoft.Maui.Platform;
using IMap = Microsoft.Maui.Maps.IMap;

namespace GeoSilence.Platforms.Droid
{
    public class PlaceMapHandler : MapHandler
    {
        private readonly Dictionary<IMapPin, Marker?> _markers = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(int Count, bool Public, float Density), BitmapDescriptor> _clusterIcons = new();
        private GoogleMap? _markerMap;
        private int _generation;
        private bool _refreshScheduled;

        public static new void MapPins(IMapHandler handler, IMap map)
        {
            if (handler is not PlaceMapHandler mapHandler)
                return;

            // Pins.Clear followed by N additions used to rebuild all native
            // markers N+1 times. Coalesce that burst into one dispatcher pass.
            if (mapHandler._refreshScheduled)
                return;
            mapHandler._refreshScheduled = true;
            ((Microsoft.Maui.Controls.BindableObject)map).Dispatcher.Dispatch(() =>
            {
                mapHandler._refreshScheduled = false;
                if (mapHandler.VirtualView != map || mapHandler.Map == null)
                    return;
                mapHandler.RefreshMarkers(map);
            });
        }

        private void RefreshMarkers(IMap map)
        {
            if (!ReferenceEquals(_markerMap, Map))
            {
                ClearMarkers();
                _markerMap = Map;
            }
            var desired = new HashSet<IMapPin>(map.Pins, ReferenceEqualityComparer.Instance);
            foreach (var pin in _markers.Keys.Where(p => !desired.Contains(p)).ToArray())
            {
                _markers[pin]?.Remove();
                _markers[pin]?.Dispose();
                _markers.Remove(pin);
                pin.MarkerId = null;
            }
            AddPins(map.Pins.Where(p => !_markers.ContainsKey(p)).ToArray(), _generation);
        }

        private void ClearMarkers()
        {
            _generation++;
            foreach (var pair in _markers)
            {
                pair.Value?.Remove();
                pair.Value?.Dispose();
                pair.Key.MarkerId = null;
            }
            _markers.Clear();
            foreach (var icon in _clusterIcons.Values) icon.Dispose();
            _clusterIcons.Clear();
        }

        protected override void DisconnectHandler(MapView platformView)
        {
            ClearMarkers();
            _markerMap = null;
            base.DisconnectHandler(platformView);
        }

        private void AddPins(IEnumerable<IMapPin> pins, int renderVersion)
        {
            if (Map == null || MauiContext == null)
                return;

            foreach (var pin in pins)
            {
                if (pin.ToHandler(MauiContext) is not IMapPinHandler pinHandler)
                    continue;

                var options = pinHandler.PlatformView;
                // Reserve this pin while its image loads; unrelated updates must not
                // start duplicate loads or invalidate an unchanged pending marker.
                _markers.Add(pin, null);

                if (pin is PlaceMapPin { ClusterCount: > 1 } cluster)
                {
                    var density = Context.Resources?.DisplayMetrics?.Density ?? 1;
                    var iconKey = (cluster.ClusterCount, cluster.IsPublicCluster, density);
                    if (!_clusterIcons.TryGetValue(iconKey, out var icon))
                    {
                        var size = (int)(52 * density);
                        using var bitmap = Android.Graphics.Bitmap.CreateBitmap(size, size, Android.Graphics.Bitmap.Config.Argb8888!);
                        using var canvas = new Android.Graphics.Canvas(bitmap);
                        using var paint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias);
                        paint.Color = Android.Graphics.Color.White;
                        canvas.DrawCircle(size / 2f, size / 2f, size / 2f, paint);
                        paint.Color = Android.Graphics.Color.ParseColor(cluster.IsPublicCluster ? "#0F766E" : "#6D28D9");
                        canvas.DrawCircle(size / 2f, size / 2f, size / 2f - 3 * density, paint);
                        paint.Color = Android.Graphics.Color.White;
                        paint.TextSize = (cluster.ClusterCount >= 1000 ? 12 : 16) * density;
                        paint.TextAlign = Android.Graphics.Paint.Align.Center;
                        paint.FakeBoldText = true;
                        canvas.DrawText(cluster.ClusterCount.ToString(), size / 2f,
                            size / 2f - (paint.Ascent() + paint.Descent()) / 2, paint);
                        icon = BitmapDescriptorFactory.FromBitmap(bitmap);
                        // Bound descriptor retention during long zoom/explore sessions.
                        if (_clusterIcons.Count >= 64)
                        {
                            var oldest = _clusterIcons.Keys.First();
                            _clusterIcons[oldest].Dispose();
                            _clusterIcons.Remove(oldest);
                        }
                        _clusterIcons.Add(iconKey, icon);
                    }
                    options.SetIcon(icon);
                    options.Anchor(0.5f, 0.5f);
                    AddMarker(pin, options, renderVersion);
                }
                else if (pin is PlaceMapPin { ImageSource: not null } placePin)
                {
                    placePin.ImageSource.LoadImage(MauiContext, result =>
                    {
                        if (renderVersion != _generation || !_markers.TryGetValue(pin, out var existing) || existing != null)
                            return;
                        using var icon = result?.Value is BitmapDrawable { Bitmap: not null } drawable
                            ? BitmapDescriptorFactory.FromBitmap(drawable.Bitmap) : null;
                        if (icon != null) options.SetIcon(icon);
                        AddMarker(pin, options, renderVersion);
                    });
                }
                else
                {
                    AddMarker(pin, options, renderVersion);
                }
            }
        }

        private void AddMarker(IMapPin pin, MarkerOptions options, int renderVersion)
        {
            if (renderVersion != _generation || !_markers.TryGetValue(pin, out var existing) || existing != null)
                return;

            var marker = Map?.AddMarker(options);
            if (marker == null)
                return;

            pin.MarkerId = marker.Id;
            _markers[pin] = marker;
        }
    }
}
