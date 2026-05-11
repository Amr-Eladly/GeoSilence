using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using Android.Provider;
using Android.Views;

namespace GeoSilence
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, WindowSoftInputMode = SoftInput.AdjustNothing, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Window?.SetSoftInputMode(SoftInput.AdjustNothing);

            var notificationManager =
                (NotificationManager)GetSystemService(NotificationService);

            if (!notificationManager.IsNotificationPolicyAccessGranted)
            {
                Intent intent =
                    new Intent(Settings.ActionNotificationPolicyAccessSettings);

                StartActivity(intent);
            }
        }
    }
}
