using Android.App;
using Android.Content;
using Android.Gms.Location;
using GeoSilence.Models;
using GeoSilence.Platforms.Droid;
using GeoSilence.Services;
using SQLite;

namespace GeoSilence
{
    // Exported = false because Play Services delivers via the explicit
    // PendingIntent (component name set). Marking it exported would be
    // a surface-area mistake on API 31+.
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
            // Make sure logging is up — this receiver may be the very first
            // thing that runs after the process is woken by Play Services.
            if (context != null)
                GeoLog.Init(context);

            GeoLog.Write("RX", $"OnReceive action={intent?.Action ?? "null"} pid={Android.OS.Process.MyPid()}");

            if (context == null || intent == null)
            {
                GeoLog.Write("RX", "ABORT: context or intent null");
                return;
            }

            var pendingResult = GoAsync();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await HandleGeofenceTransition(context, intent);
                }
                catch (Exception ex)
                {
                    GeoLog.Error("RX", ex);
                }
                finally
                {
                    pendingResult.Finish();
                    GeoLog.Write("RX", "OnReceive complete");
                }
            });
        }

        private static async System.Threading.Tasks.Task HandleGeofenceTransition(
            Context context,
            Intent intent)
        {
            var geofencingEvent = GeofencingEvent.FromIntent(intent);

            if (geofencingEvent == null)
            {
                GeoLog.Write("RX", "ABORT: GeofencingEvent.FromIntent returned null");
                return;
            }

            if (geofencingEvent.HasError)
            {
                var code = geofencingEvent.ErrorCode;
                GeoLog.Write("RX", $"ABORT: GeofencingEvent error code={code} ({DescribeError(code)})");
                return;
            }

            var transition = geofencingEvent.GeofenceTransition;
            var triggeringList = geofencingEvent.TriggeringGeofences;

            GeoLog.Write("RX",
                $"transition={DescribeTransition(transition)} triggered={triggeringList?.Count ?? 0}");

            var triggeredIds = triggeringList?
                .Select(geofence => geofence.RequestId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            if (triggeredIds == null || triggeredIds.Count == 0)
            {
                GeoLog.Write("RX", "ABORT: no triggered ids");
                return;
            }

            foreach (var id in triggeredIds)
                GeoLog.Write("RX", $"  triggered id={id}");

            var activeIds = ReadActiveIds(context);

            if (transition == Geofence.GeofenceTransitionEnter)
            {
                foreach (var id in triggeredIds)
                    activeIds.Add(id);
            }
            else if (transition == Geofence.GeofenceTransitionExit)
            {
                foreach (var id in triggeredIds)
                    activeIds.Remove(id);
            }

            SaveActiveIds(context, activeIds);
            GeoLog.Write("RX", $"activeIds now: [{string.Join(",", activeIds)}]");

            List<PlaceEntity> places;
            try
            {
                places = await LoadPlaces();
                GeoLog.Write("RX", $"loaded {places.Count} place(s) from DB");
            }
            catch (Exception ex)
            {
                GeoLog.Error("RX-DB", ex);
                return;
            }

            var modeService = new ModeService();

            if (transition == Geofence.GeofenceTransitionEnter)
            {
                var enteredPlace = FindFirstMatchingPlace(places, triggeredIds);
                if (enteredPlace != null)
                {
                    GeoLog.Write("MODE", $"ENTER -> applying {enteredPlace.Mode} for '{enteredPlace.Name}'");
                    modeService.ApplyZoneMode(enteredPlace.Mode);
                }
                else
                {
                    GeoLog.Write("MODE", "ENTER but no matching place found in DB");
                }
                return;
            }

            if (transition == Geofence.GeofenceTransitionExit)
            {
                if (activeIds.Count == 0)
                {
                    GeoLog.Write("MODE", "EXIT (no active zones) -> restoring original mode");
                    modeService.RestoreOriginalMode();
                    return;
                }

                var activePlace = FindFirstMatchingPlace(places, activeIds.ToList());
                if (activePlace != null)
                {
                    GeoLog.Write("MODE",
                        $"EXIT but still inside '{activePlace.Name}' -> applying {activePlace.Mode}");
                    modeService.ApplyZoneMode(activePlace.Mode);
                }
            }
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

        private static async System.Threading.Tasks.Task<List<PlaceEntity>> LoadPlaces()
        {
            // FileSystem.AppDataDirectory is safe in receiver context as long as
            // MAUI essentials is initialized — which it is via MainApplication.
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

        private static string DescribeTransition(int t) => t switch
        {
            1 => "ENTER",
            2 => "EXIT",
            4 => "DWELL",
            _ => $"UNKNOWN({t})"
        };

        private static string DescribeError(int code) => code switch
        {
            1000 => "GEOFENCE_NOT_AVAILABLE",
            1001 => "GEOFENCE_TOO_MANY_GEOFENCES",
            1002 => "GEOFENCE_TOO_MANY_PENDING_INTENTS",
            1004 => "GEOFENCE_INSUFFICIENT_LOCATION_PERMISSION",
            _ => "unknown"
        };
    }
}
