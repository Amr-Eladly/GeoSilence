using Android.App;
using Android.Content;
using GeoSilence.Models;
using GeoSilence.Services;
using SQLite;

namespace GeoSilence
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted })]
    public class BootCompletedReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null ||
                intent?.Action != Intent.ActionBootCompleted)
                return;

            var pendingResult = GoAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    var places = await LoadPlaces();
                    var service = new BackgroundGeofenceService();

                    await service.RegisterPlacesAsync(places.Select(place => new Place
                    {
                        Id = place.Id,
                        Name = place.Name,
                        Latitude = place.Latitude,
                        Longitude = place.Longitude,
                        Radius = place.Radius,
                        Mode = (ModeType)place.Mode,
                        IsActive = true
                    }));
                }
                finally
                {
                    pendingResult.Finish();
                }
            });
        }

        private static async Task<List<PlaceEntity>> LoadPlaces()
        {
            var path = Path.Combine(
                FileSystem.AppDataDirectory,
                "geosilence.db");

            var db = new SQLiteAsyncConnection(path);
            await db.CreateTableAsync<PlaceEntity>();
            return await db.Table<PlaceEntity>().ToListAsync();
        }
    }
}
