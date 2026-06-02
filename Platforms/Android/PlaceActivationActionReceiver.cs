using Android.App;
using Android.Content;
using GeoSilence.Models;
using GeoSilence.Platforms.Droid;
using GeoSilence.Services;

namespace GeoSilence
{
    [BroadcastReceiver(Enabled = true, Exported = false)]
    [IntentFilter(new[]
    {
        PlaceActivationNotificationService.ActionActivate,
        PlaceActivationNotificationService.ActionIgnore
    })]
    public class PlaceActivationActionReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null)
                return;

            GeoLog.Init(context);

            if (!PlaceActivationNotificationService.TryReadActivationIntent(intent, out var placeId, out var mode))
                return;

            PlaceActivationNotificationService.CancelActivationRequest(context, placeId);

            if (intent.Action == PlaceActivationNotificationService.ActionActivate)
            {
                GeoLog.Write("NOTIFY", $"Activation confirmed for place {placeId}");
                try
                {
                    new ModeService().ApplyZoneMode(mode);
                }
                catch (Exception ex)
                {
                    GeoLog.Error("NOTIFY-APPLY", ex);
                }
                return;
            }

            GeoLog.Write("NOTIFY", $"Activation ignored for place {placeId}");
        }
    }
}
