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

            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
            builder.Services.AddSingleton<FirestoreService>();
            builder.Services.AddSingleton<CloudPlaceRepository>();
            builder.Services.AddSingleton<FirebaseStorageService>();
            builder.Services.AddSingleton<AccountApiService>();
            builder.Services.AddSingleton<AccountProfileService>();
            builder.Services.AddSingleton<SyncService>();
            builder.Services.AddSingleton<DistanceService>();
            builder.Services.AddSingleton<GeofencingService>();
            builder.Services.AddSingleton<LocationService>();
            builder.Services.AddSingleton<ModeService>();
            builder.Services.AddSingleton<BackgroundGeofenceService>();
            builder.Services.AddSingleton<PlaceManagerService>();
            builder.Services.AddSingleton<HomeViewModel>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<RegisterPage>();
            builder.Services.AddTransient<AccountPage>();
            builder.Services.AddTransient<ProfilePage>();
            builder.Services.AddTransient<LoginOptionsPage>();
            builder.Services.AddTransient<ChangePasswordPage>();
            builder.Services.AddTransient<DeleteAccountPage>();
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<PlaceRepository>();

            return builder.Build();
        }
    }
}
