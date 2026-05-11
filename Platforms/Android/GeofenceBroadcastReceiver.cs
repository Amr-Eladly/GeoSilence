using Android.App;
using Android.Content;
using Android.Gms.Location;
using GeoSilence.Models;
using GeoSilence.Services;
using SQLite;

namespace GeoSilence
{
    [BroadcastReceiver(Enabled = true, Exported = false)]
    [IntentFilter(new[] { ActionGeofenceTransition })]
    public class GeofenceBroadcastReceiver : BroadcastReceiver
    {
        public const string ActionGeofenceTransition =
            "com.companyname.geosilence.ACTION_GEOFENCE_TRANSITION";

        private const string ActivePrefsName = "GeoSilenceActiveGeofences";
        private const string ActiveIdsKey = "ActiveIds";

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null)
                return;

            var pendingResult = GoAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleGeofenceTransition(context, intent);
                }
                finally
                {
                    pendingResult.Finish();
                }
            });
        }

        private static async Task HandleGeofenceTransition(
            Context context,
            Intent intent)
        {
            var geofencingEvent = GeofencingEvent.FromIntent(intent);

            if (geofencingEvent == null || geofencingEvent.HasError)
                return;

            var triggeredIds = geofencingEvent.TriggeringGeofences?
                .Select(geofence => geofence.RequestId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            if (triggeredIds == null || triggeredIds.Count == 0)
                return;

            var activeIds = ReadActiveIds(context);

            if (geofencingEvent.GeofenceTransition ==
                Geofence.GeofenceTransitionEnter)
            {
                foreach (var id in triggeredIds)
                    activeIds.Add(id);
            }
            else if (geofencingEvent.GeofenceTransition ==
                     Geofence.GeofenceTransitionExit)
            {
                foreach (var id in triggeredIds)
                    activeIds.Remove(id);
            }

            SaveActiveIds(context, activeIds);

            var places = await LoadPlaces();
            var modeService = new ModeService();

            if (geofencingEvent.GeofenceTransition ==
                Geofence.GeofenceTransitionEnter)
            {
                var enteredPlace = FindFirstMatchingPlace(places, triggeredIds);

                if (enteredPlace != null)
                    modeService.ApplyZoneMode(enteredPlace.Mode);

                return;
            }

            if (activeIds.Count == 0)
            {
                modeService.RestoreOriginalMode();
                return;
            }

            var activePlace = FindFirstMatchingPlace(places, activeIds.ToList());

            if (activePlace != null)
                modeService.ApplyZoneMode(activePlace.Mode);
        }

        private static Place? FindFirstMatchingPlace(
            IEnumerable<PlaceEntity> places,
            IEnumerable<string> ids)
        {
            var idSet = ids.ToHashSet();
            var place = places.FirstOrDefault(place =>
                idSet.Contains(place.Id.ToString()));

            if (place == null)
                return null;

            return new Place
            {
                Id = place.Id,
                Name = place.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Radius = place.Radius,
                Mode = (ModeType)place.Mode,
                IsActive = true
            };
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

        private static HashSet<string> ReadActiveIds(Context context)
        {
            var prefs = context.GetSharedPreferences(
                ActivePrefsName,
                FileCreationMode.Private);

            var raw = prefs?.GetString(ActiveIdsKey, string.Empty)
                ?? string.Empty;

            return raw
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();
        }

        private static void SaveActiveIds(
            Context context,
            HashSet<string> activeIds)
        {
            var prefs = context.GetSharedPreferences(
                ActivePrefsName,
                FileCreationMode.Private);

            prefs!
                .Edit()!
                .PutString(ActiveIdsKey, string.Join("|", activeIds))
                .Apply();
        }
    }
}
