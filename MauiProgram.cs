using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace GeoSilence
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton<DistanceService>();
            builder.Services.AddSingleton<GeofencingService>();
            builder.Services.AddSingleton<LocationService>();
            builder.Services.AddSingleton<ModeService>();
            builder.Services.AddSingleton<PlaceManagerService>();
            builder.Services.AddSingleton<HomeViewModel>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<AppShell>();

            return builder.Build();
        }
    }
}