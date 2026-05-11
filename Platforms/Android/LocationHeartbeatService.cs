using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Location;
using Android.OS;
using AndroidX.Core.App;
using GeoSilence.Platforms.Droid;

namespace GeoSilence
{
    // Foreground service whose only job is to keep FusedLocationProvider warm
    // so Play Services has fresh fixes to evaluate geofences against.
    // Without this, on devices/emulators where no other app is requesting
    // location, geofence transitions may never be delivered.
    [Service(
        Enabled = true,
        Exported = false,
        ForegroundServiceType = ForegroundService.TypeLocation)]
    public class LocationHeartbeatService : Service
    {
        public const string ActionStart = "com.companyname.geosilence.HEARTBEAT_START";
        public const string ActionStop  = "com.companyname.geosilence.HEARTBEAT_STOP";

        private const int NotificationId = 4711;
        private const string ChannelId = "geosilence_location_channel";

        // 3 min interval — well under typical Doze window, far more responsive
        // than passive cadence, cheap at BalancedPowerAccuracy.
        private const long IntervalMs = 3 * 60 * 1000;
        private const long FastestIntervalMs = 60 * 1000;

        private IFusedLocationProviderClient? _client;
        private HeartbeatCallback? _callback;
        private bool _started;

        public override IBinder? OnBind(Intent? intent) => null;

        public override void OnCreate()
        {
            base.OnCreate();
            GeoLog.Init(this);
            GeoLog.Write("HB", "Service.OnCreate");
            EnsureNotificationChannel();
        }

        public override StartCommandResult OnStartCommand(
            Intent? intent, StartCommandFlags flags, int startId)
        {
            var action = intent?.Action ?? ActionStart;
            GeoLog.Write("HB", $"OnStartCommand action={action}");

            if (action == ActionStop)
            {
                StopHeartbeat();
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            try
            {
                StartForeground(NotificationId, BuildNotification());
            }
            catch (Exception ex)
            {
                GeoLog.Error("HB-FG", ex);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            StartHeartbeat();
            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            GeoLog.Write("HB", "Service.OnDestroy");
            StopHeartbeat();
            base.OnDestroy();
        }

        private void StartHeartbeat()
        {
            if (_started)
                return;

            try
            {
                _client = LocationServices.GetFusedLocationProviderClient(this);
                _callback = new HeartbeatCallback();

                // Older-style LocationRequest construction works in
                // Xamarin.GooglePlayServices.Location 121.x bindings without
                // depending on LocationRequest.Builder.
                var request = LocationRequest.Create()!
                    .SetPriority(LocationRequest.PriorityBalancedPowerAccuracy)
                    .SetInterval(IntervalMs)
                    .SetFastestInterval(FastestIntervalMs);

                _client.RequestLocationUpdates(
                    request,
                    _callback,
                    Looper.MainLooper);

                _started = true;
                GeoLog.Write("HB", $"location updates requested every {IntervalMs}ms");
            }
            catch (Exception ex)
            {
                GeoLog.Error("HB-START", ex);
            }
        }

        private void StopHeartbeat()
        {
            if (!_started)
                return;
            try
            {
                if (_client != null && _callback != null)
                    _client.RemoveLocationUpdates(_callback);
                GeoLog.Write("HB", "location updates removed");
            }
            catch (Exception ex)
            {
                GeoLog.Error("HB-STOP", ex);
            }
            finally
            {
                _started = false;
            }
        }

        private void EnsureNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            if (manager.GetNotificationChannel(ChannelId) != null)
                return;

            var channel = new NotificationChannel(
                ChannelId,
                "GeoSilence location",
                NotificationImportance.Min)
            {
                Description = "Keeps location fresh so zone changes work in the background"
            };
            channel.SetShowBadge(false);
            manager.CreateNotificationChannel(channel);
        }

        private Notification BuildNotification()
        {
            var builder = new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("GeoSilence")
                .SetContentText("Watching for sound zones")
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetOngoing(true)
                .SetPriority(NotificationCompat.PriorityMin)
                .SetCategory(NotificationCompat.CategoryService);

            return builder.Build()!;
        }

        private sealed class HeartbeatCallback : LocationCallback
        {
            public override void OnLocationResult(LocationResult result)
            {
                var loc = result.LastLocation;
                if (loc == null)
                {
                    GeoLog.Write("HB", "tick: no location in result");
                    return;
                }
                GeoLog.Write("HB",
                    $"tick lat={loc.Latitude:F5} lng={loc.Longitude:F5} acc={loc.Accuracy:F0}m");
            }
        }

        public static void Start(Context context)
        {
            var intent = new Intent(context, typeof(LocationHeartbeatService))
                .SetAction(ActionStart);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);

            GeoLog.Write("HB", "Start() requested");
        }

        public static void Stop(Context context)
        {
            var intent = new Intent(context, typeof(LocationHeartbeatService))
                .SetAction(ActionStop);
            context.StartService(intent);
            GeoLog.Write("HB", "Stop() requested");
        }
    }
}
