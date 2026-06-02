using Android.App;
using Android.Content;
using GeoSilence.Models;
using GeoSilence.Platforms.Droid;
using GeoSilence.Services;
using SQLite;

namespace GeoSilence
{
    [BroadcastReceiver(Enabled = true, Exported = true,
        Permission = "android.permission.RECEIVE_BOOT_COMPLETED")]
    [IntentFilter(new[]
    {
        Intent.ActionBootCompleted,
        Intent.ActionLockedBootCompleted,
        Intent.ActionMyPackageReplaced,
        "android.intent.action.QUICKBOOT_POWERON"
    })]
    public class BootCompletedReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context != null)
                GeoLog.Init(context);

            var action = intent?.Action ?? "null";
            GeoLog.Write("BOOT", $"OnReceive action={action}");

            if (context == null || intent == null)
                return;

            if (action != Intent.ActionBootCompleted &&
                action != Intent.ActionLockedBootCompleted &&
                action != Intent.ActionMyPackageReplaced &&
                action != "android.intent.action.QUICKBOOT_POWERON")
                return;

            var pendingResult = GoAsync();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var places = await LoadPlaces();
                    GeoLog.Write("BOOT", $"loaded {places.Count} place(s) — re-registering geofences");

                    var service = new BackgroundGeofenceService();
                    await service.RegisterPlacesAsync(places.Select(place => new Place
                    {
                        Id = place.Id,
                        CloudId = place.CloudId,
                        OwnerId = place.OwnerId,
                        Name = place.Name,
                        Latitude = place.Latitude,
                        Longitude = place.Longitude,
                        Radius = place.Radius,
                        Mode = (ModeType)place.Mode,
                        ActivationType = (ActivationType)place.ActivationType,
                        Visibility = (PlaceVisibility)place.Visibility,
                        IsActive = true
                    }));
                }
                catch (Exception ex)
                {
                    GeoLog.Error("BOOT", ex);
                }
                finally
                {
                    pendingResult.Finish();
                    GeoLog.Write("BOOT", "complete");
                }
            });
        }

        private static async System.Threading.Tasks.Task<List<PlaceEntity>> LoadPlaces()
        {
            var path = Path.Combine(
                FileSystem.AppDataDirectory,
                "geosilence.db");

            var db = new SQLiteAsyncConnection(path);
            await PlaceDatabaseSchema.EnsureMigratedAsync(db);
            return await db.Table<PlaceEntity>().Where(place => !place.IsDeleted).ToListAsync();
        }
    }
}
