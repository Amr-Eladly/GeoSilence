using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using Android.Provider;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace GeoSilence
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, WindowSoftInputMode = SoftInput.AdjustNothing, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Window?.SetSoftInputMode(SoftInput.AdjustNothing);

            // Request POST_NOTIFICATIONS permission on Android 13+ to show activation prompts
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(
                    this,
                    new[] { Android.Manifest.Permission.PostNotifications },
                    1001);
            }

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
