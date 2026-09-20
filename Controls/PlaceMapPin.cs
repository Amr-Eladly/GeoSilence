using Microsoft.Maui.Controls.Maps;

namespace GeoSilence.Controls
{
    public class PlaceMapPin : Pin
    {
        public static readonly BindableProperty ImageSourceProperty = BindableProperty.Create(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(PlaceMapPin));

        public ImageSource? ImageSource
        {
            get => (ImageSource?)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }
    }
}
