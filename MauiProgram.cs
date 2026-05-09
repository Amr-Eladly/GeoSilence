using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;

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

            builder.UseMauiMaps();
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
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<PlaceRepository>();

            return builder.Build();
        }
    }
}