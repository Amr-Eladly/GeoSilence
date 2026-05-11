using GeoSilence.Models;

#if ANDROID
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Gms.Location;
using Android.Locations;
using Android.OS;
using AndroidX.Core.Content;
using GeoSilence.Platforms.Droid;
using GmsTask = Android.Gms.Tasks.Task;
using IGmsOnCompleteListener = Android.Gms.Tasks.IOnCompleteListener;
#endif

namespace GeoSilence.Services
{
    public class BackgroundGeofenceService
    {
        public async System.Threading.Tasks.Task RegisterPlacesAsync(IEnumerable<Place> places)
        {
#if ANDROID
            try
            {
                await RegisterAndroidAsync(places);
            }
            catch (Exception ex)
            {
                GeoLog.Error("REG", ex);
            }
#else
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

#if ANDROID
        private static async System.Threading.Tasks.Task RegisterAndroidAsync(IEnumerable<Place> places)
        {
            var context = Android.App.Application.Context;

            // 1. Pre-flight: Play Services
            var availability = GoogleApiAvailability.Instance
                .IsGooglePlayServicesAvailable(context);
            if (availability != ConnectionResult.Success)
            {
                GeoLog.Write("REG", $"ABORT: Play Services unavailable code={availability}");
                return;
            }

            // 2. Pre-flight: Fine location
            if (ContextCompat.CheckSelfPermission(context,
                    Manifest.Permission.AccessFineLocation) != Permission.Granted)
            {
                GeoLog.Write("REG", "ABORT: ACCESS_FINE_LOCATION not granted");
                return;
            }

            // 3. Pre-flight: Background location (Q+)
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q &&
                ContextCompat.CheckSelfPermission(context,
                    Manifest.Permission.AccessBackgroundLocation) != Permission.Granted)
            {
                GeoLog.Write("REG", "ABORT: ACCESS_BACKGROUND_LOCATION not granted");
                return;
            }

            // 4. Pre-flight: Location services enabled (geofencing fails with
            //    1000 GEOFENCE_NOT_AVAILABLE if the OS location toggle is off)
            var locationManager = context.GetSystemService(Context.LocationService)
                as LocationManager;
            if (locationManager != null &&
                !locationManager.IsProviderEnabled(LocationManager.GpsProvider) &&
                !locationManager.IsProviderEnabled(LocationManager.NetworkProvider))
            {
                GeoLog.Write("REG", "WARN: Location services disabled — registration may fail");
            }

            var activePlaces = places
                .Where(place => place.IsActive)
                .Take(100)
                .ToList();

            GeoLog.Write("REG", $"Registering {activePlaces.Count} place(s)");

            // Prune any stale entries in the activeIds prefs that no longer
            // correspond to a registered place. Without this, a deleted place
            // leaves a phantom id behind that prevents mode restoration on
            // exit (because activeIds.Count never reaches 0).
            PruneStaleActiveIds(context, activePlaces.Select(p => p.Id.ToString()).ToHashSet());

            var client = LocationServices.GetGeofencingClient(context);
            var pendingIntent = GetGeofencePendingIntent(context);

            // 5. Remove old fences first AND WAIT for completion
            try
            {
                await ToTaskAsync(client.RemoveGeofences(pendingIntent));
                GeoLog.Write("REG", "Old geofences removed");
            }
            catch (ApiException ex)
            {
                GeoLog.Write("REG", $"RemoveGeofences failed status={ex.StatusCode} {ex.Message}");
            }
            catch (Exception ex)
            {
                GeoLog.Error("REG-REMOVE", ex);
            }

            if (activePlaces.Count == 0)
            {
                GeoLog.Write("REG", "No active places — stopping heartbeat");
                LocationHeartbeatService.Stop(context);
                return;
            }

            // 6. Build request
            var requestBuilder = new GeofencingRequest.Builder()
                .SetInitialTrigger(GeofencingRequest.InitialTriggerEnter |
                                   GeofencingRequest.InitialTriggerDwell);

            foreach (var place in activePlaces)
            {
                var geofence = new GeofenceBuilder()
                    .SetRequestId(place.Id.ToString())
                    .SetCircularRegion(
                        place.Latitude,
                        place.Longitude,
                        (float)place.Radius)
                    .SetExpirationDuration(Geofence.NeverExpire)
                    .SetTransitionTypes(
                        Geofence.GeofenceTransitionEnter |
                        Geofence.GeofenceTransitionExit)
                    .SetNotificationResponsiveness(0) // 0 = OS default (~30s), more responsive than 60_000
                    .Build();

                requestBuilder.AddGeofence(geofence);
                GeoLog.Write("REG", $"  - id={place.Id} name={place.Name} r={place.Radius}m mode={place.Mode}");
            }

            // 7. Add fences AND WAIT for the success/failure callback
            try
            {
                await ToTaskAsync(client.AddGeofences(requestBuilder.Build(), pendingIntent));
                GeoLog.Write("REG", $"SUCCESS: {activePlaces.Count} geofence(s) registered");

                // Start the location heartbeat so FusedLocationProvider keeps
                // producing fixes for Play Services to evaluate against.
                // Without this, geofences are only checked when SOME OTHER app
                // requests location (Maps, weather, etc).
                LocationHeartbeatService.Start(context);
            }
            catch (ApiException ex)
            {
                GeoLog.Write("REG",
                    $"FAILURE status={ex.StatusCode} ({DescribeStatus(ex.StatusCode)}) msg={ex.Message}");
            }
            catch (Exception ex)
            {
                GeoLog.Error("REG-ADD", ex);
            }
        }

        // Bridge Android Gms Task -> .NET Task
        private static System.Threading.Tasks.Task ToTaskAsync(GmsTask gmsTask)
        {
            var tcs = new TaskCompletionSource<bool>();
            var listener = new TaskCompletionListener(tcs);
            gmsTask.AddOnCompleteListener(listener);
            return tcs.Task;
        }

        private sealed class TaskCompletionListener : Java.Lang.Object,
            IGmsOnCompleteListener
        {
            private readonly TaskCompletionSource<bool> _tcs;

            public TaskCompletionListener(TaskCompletionSource<bool> tcs)
            {
                _tcs = tcs;
            }

            public void OnComplete(GmsTask task)
            {
                if (task.IsSuccessful)
                    _tcs.TrySetResult(true);
                else if (task.Exception != null)
                    _tcs.TrySetException(task.Exception);
                else
                    _tcs.TrySetException(new Exception("Gms Task failed (no exception)"));
            }
        }

        private static string DescribeStatus(int code) => code switch
        {
            1000 => "GEOFENCE_NOT_AVAILABLE — location off / Play Services issue",
            1001 => "GEOFENCE_TOO_MANY_GEOFENCES",
            1002 => "GEOFENCE_TOO_MANY_PENDING_INTENTS",
            1004 => "GEOFENCE_INSUFFICIENT_LOCATION_PERMISSION (background location missing)",
            13   => "ERROR — generic, often background-location not granted",
            _    => "see CommonStatusCodes / GeofenceStatusCodes"
        };

        private static void PruneStaleActiveIds(Context context, HashSet<string> validIds)
        {
            const string ActivePrefsName = "GeoSilenceActiveGeofences";
            const string ActiveIdsKey = "ActiveIds";

            var prefs = context.GetSharedPreferences(ActivePrefsName, FileCreationMode.Private);
            if (prefs == null)
                return;

            var raw = prefs.GetString(ActiveIdsKey, string.Empty) ?? string.Empty;
            var current = raw.Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            var pruned = current.Where(id => validIds.Contains(id)).ToHashSet();

            if (pruned.Count == current.Count)
                return;

            var dropped = current.Except(pruned);
            GeoLog.Write("REG", $"Pruning stale activeIds: dropped=[{string.Join(",", dropped)}] kept=[{string.Join(",", pruned)}]");

            prefs.Edit()!
                .PutString(ActiveIdsKey, string.Join("|", pruned))
                .Apply();
        }

        internal static PendingIntent GetGeofencePendingIntent(Context context)
        {
            var intent = new Intent(context, typeof(GeofenceBroadcastReceiver));
            intent.SetAction(GeofenceBroadcastReceiver.ActionGeofenceTransition);

            // FLAG_UPDATE_CURRENT keeps the same PendingIntent identity
            // FLAG_MUTABLE is REQUIRED on API 31+ for geofencing to fill in the trigger extras
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                flags |= PendingIntentFlags.Mutable;

            return PendingIntent.GetBroadcast(context, 0, intent, flags)!;
        }
#endif
    }
}
