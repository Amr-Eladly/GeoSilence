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
        private readonly List<Marker> _markers = [];
        private int _renderVersion;

        public static new void MapPins(IMapHandler handler, IMap map)
        {
            if (handler is not PlaceMapHandler mapHandler)
                return;

            mapHandler._renderVersion++;

            foreach (var marker in mapHandler._markers)
                marker.Remove();

            mapHandler._markers.Clear();
            mapHandler.AddPins(map.Pins, mapHandler._renderVersion);
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

                if (pin is PlaceMapPin { ImageSource: not null } placePin)
                {
                    placePin.ImageSource.LoadImage(MauiContext, result =>
                    {
                        if (result?.Value is BitmapDrawable { Bitmap: not null } drawable)
                            options.SetIcon(BitmapDescriptorFactory.FromBitmap(drawable.Bitmap));

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
            if (renderVersion != _renderVersion)
                return;

            var marker = Map?.AddMarker(options);
            if (marker == null)
                return;

            pin.MarkerId = marker.Id;
            _markers.Add(marker);
        }
    }
}
