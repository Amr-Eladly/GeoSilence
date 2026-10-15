using Microsoft.Maui.Controls.Maps;
using MapLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace GeoSilence.Controls;

public class PlaceMap : Microsoft.Maui.Controls.Maps.Map
{
    public event EventHandler<MapClickedEventArgs>? MapLongPressed;

#if ANDROID
    private Android.Gms.Maps.GoogleMap? _nativeMap;
    private MapLongPressListener? _longPressListener;
#elif IOS || MACCATALYST
    private MapKit.MKMapView? _nativeMap;
    private UIKit.UILongPressGestureRecognizer? _longPressRecognizer;
#endif

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

#if ANDROID
        if (Handler?.PlatformView is Android.Gms.Maps.MapView mapView)
        {
            _longPressListener = new MapLongPressListener(this);
            mapView.GetMapAsync(_longPressListener);
        }
#elif IOS || MACCATALYST
        if (Handler?.PlatformView is MapKit.MKMapView mapView)
        {
            _nativeMap = mapView;
            _longPressRecognizer = new UIKit.UILongPressGestureRecognizer(OnLongPress)
            {
                MinimumPressDuration = 0.6,
                CancelsTouchesInView = false
            };
            mapView.AddGestureRecognizer(_longPressRecognizer);
        }
#endif
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
#if ANDROID
        _nativeMap?.SetOnMapLongClickListener(null);
        _nativeMap = null;
        // A map-ready callback can arrive after this handler has disconnected.
        _longPressListener = null;
#elif IOS || MACCATALYST
        if (_longPressRecognizer != null)
        {
            _nativeMap?.RemoveGestureRecognizer(_longPressRecognizer);
            _longPressRecognizer.Dispose();
            _longPressRecognizer = null;
        }
        _nativeMap = null;
#endif
        base.OnHandlerChanging(args);
    }

    private void RaiseMapLongPressed(double latitude, double longitude)
    {
        MapLongPressed?.Invoke(this, new MapClickedEventArgs(new MapLocation(latitude, longitude)));
    }

#if ANDROID
    private sealed class MapLongPressListener : Java.Lang.Object,
        Android.Gms.Maps.IOnMapReadyCallback,
        Android.Gms.Maps.GoogleMap.IOnMapLongClickListener
    {
        private readonly WeakReference<PlaceMap> _owner;

        public MapLongPressListener(PlaceMap owner) => _owner = new(owner);

        public void OnMapReady(Android.Gms.Maps.GoogleMap googleMap)
        {
            if (!_owner.TryGetTarget(out var owner) || !ReferenceEquals(owner._longPressListener, this))
                return;

            owner._nativeMap = googleMap;
            googleMap.UiSettings.MyLocationButtonEnabled = false;
            googleMap.SetOnMapLongClickListener(this);
        }

        public void OnMapLongClick(Android.Gms.Maps.Model.LatLng point)
        {
            if (_owner.TryGetTarget(out var owner) && ReferenceEquals(owner._longPressListener, this))
                owner.RaiseMapLongPressed(point.Latitude, point.Longitude);
        }
    }
#elif IOS || MACCATALYST
    private void OnLongPress(UIKit.UILongPressGestureRecognizer recognizer)
    {
        if (recognizer.State != UIKit.UIGestureRecognizerState.Began || _nativeMap == null)
            return;

        var coordinate = _nativeMap.ConvertPoint(recognizer.LocationInView(_nativeMap), _nativeMap);
        RaiseMapLongPressed(coordinate.Latitude, coordinate.Longitude);
    }
#endif
}
