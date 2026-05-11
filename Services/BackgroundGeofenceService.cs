using GeoSilence.Models;

#if ANDROID
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Location;
using Android.OS;
using AndroidX.Core.Content;
#endif

namespace GeoSilence.Services
{
    public class BackgroundGeofenceService
    {
        public Task RegisterPlacesAsync(IEnumerable<Place> places)
        {
#if ANDROID
            var context = Android.App.Application.Context;

            if (ContextCompat.CheckSelfPermission(
                    context,
                    Manifest.Permission.AccessFineLocation) !=
                Permission.Granted)
                return Task.CompletedTask;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q &&
                ContextCompat.CheckSelfPermission(
                    context,
                    Manifest.Permission.AccessBackgroundLocation) !=
                Permission.Granted)
                return Task.CompletedTask;

            var activePlaces = places
                .Where(place => place.IsActive)
                .Take(100)
                .ToList();

            var client = LocationServices.GetGeofencingClient(context);
            var pendingIntent = GetGeofencePendingIntent(context);

            client.RemoveGeofences(pendingIntent);

            if (activePlaces.Count == 0)
                return Task.CompletedTask;

            var requestBuilder = new GeofencingRequest.Builder()
                .SetInitialTrigger(GeofencingRequest.InitialTriggerEnter);

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
                    .SetNotificationResponsiveness(60_000)
                    .Build();

                requestBuilder.AddGeofence(geofence);
            }

            client.AddGeofences(requestBuilder.Build(), pendingIntent);
#endif
            return Task.CompletedTask;
        }

#if ANDROID
        internal static PendingIntent GetGeofencePendingIntent(Context context)
        {
            var intent = new Intent(context, typeof(GeofenceBroadcastReceiver));
            intent.SetAction(GeofenceBroadcastReceiver.ActionGeofenceTransition);

            var flags = PendingIntentFlags.UpdateCurrent;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                flags |= PendingIntentFlags.Mutable;

            return PendingIntent.GetBroadcast(context, 0, intent, flags)!;
        }
#endif
    }
}
