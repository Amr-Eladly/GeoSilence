using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using GeoSilence.Models;
using GeoSilence.Platforms.Droid;

namespace GeoSilence
{
    internal static class PlaceActivationNotificationService
    {
        internal const string ActionActivate = "com.companyname.geosilence.ACTION_ACTIVATE_PLACE_MODE";
        internal const string ActionIgnore = "com.companyname.geosilence.ACTION_IGNORE_PLACE_MODE";

        private const string ChannelId = "geosilence_place_activation";
        private const string ChannelName = "GeoSilence place activation";
        private const string ExtraPlaceId = "place_id";
        private const string ExtraMode = "mode";

        internal static void ShowActivationRequest(Context context, PlaceEntity place)
        {
            EnsureChannel(context);

            // POST_NOTIFICATIONS is required on Android 13+. If missing, log and silently
            // fail (the notification will not show, but the app won't crash).
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                ContextCompat.CheckSelfPermission(context, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                GeoLog.Write("NOTIFY", $"POST_NOTIFICATIONS missing; skipping activation prompt for place {place.Id}");
                return;
            }

            var activateIntent = new Intent(context, typeof(PlaceActivationActionReceiver))
                .SetAction(ActionActivate)
                .PutExtra(ExtraPlaceId, place.Id)
                .PutExtra(ExtraMode, place.Mode);

            var ignoreIntent = new Intent(context, typeof(PlaceActivationActionReceiver))
                .SetAction(ActionIgnore)
                .PutExtra(ExtraPlaceId, place.Id);

            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                flags |= PendingIntentFlags.Immutable;

            var activatePendingIntent = PendingIntent.GetBroadcast(
                context,
                place.Id * 2,
                activateIntent,
                flags);

            var ignorePendingIntent = PendingIntent.GetBroadcast(
                context,
                place.Id * 2 + 1,
                ignoreIntent,
                flags);

            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetContentTitle("GeoSilence")
                .SetContentText($"You entered {place.Name}. Activate {((ModeType)place.Mode)} mode?")
                .SetStyle(new NotificationCompat.BigTextStyle()
                    .BigText($"You entered {place.Name}. Activate {((ModeType)place.Mode)} mode?"))
                .SetAutoCancel(true)
                .SetPriority((int)NotificationPriority.High)
                .SetCategory(NotificationCompat.CategoryReminder)
                .AddAction(0, "Activate", activatePendingIntent)
                .AddAction(0, "Ignore", ignorePendingIntent);

            NotificationManagerCompat.From(context).Notify(GetNotificationId(place.Id), builder.Build());
            GeoLog.Write("NOTIFY", $"Activation prompt shown for place {place.Id} ({place.Name})");
        }

        internal static void CancelActivationRequest(Context context, int placeId)
        {
            NotificationManagerCompat.From(context).Cancel(GetNotificationId(placeId));
        }

        internal static bool TryReadActivationIntent(Intent intent, out int placeId, out ModeType mode)
        {
            placeId = intent.GetIntExtra(ExtraPlaceId, -1);
            mode = (ModeType)intent.GetIntExtra(ExtraMode, (int)ModeType.Silent);
            return placeId > 0;
        }

        private static void EnsureChannel(Context context)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            if (manager.GetNotificationChannel(ChannelId) != null)
                return;

            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High)
            {
                Description = "Lets you confirm sound mode changes when entering selected places."
            };
            channel.SetShowBadge(true);
            manager.CreateNotificationChannel(channel);
        }

        private static int GetNotificationId(int placeId) => 8000 + placeId;
    }
}
