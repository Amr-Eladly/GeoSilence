using GeoSilence.Models;
using Microsoft.Maui.Devices.Sensors;

namespace GeoSilence.Services
{
    public class LocationService
    {
        public async Task<Models.Location?> GetCurrentLocationAsync()
        {
            try
            {
                // 🔹 Check permission
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status != PermissionStatus.Granted)
                    return null;

#if ANDROID
                var backgroundStatus =
                    await Permissions.CheckStatusAsync<Permissions.LocationAlways>();

                if (backgroundStatus != PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<Permissions.LocationAlways>();
                }
#endif

                // 🔹 Get location
                var request = new GeolocationRequest(
                    GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(5));

                var response = await Geolocation.GetLocationAsync(request);

                if (response == null)
                    return null;

                return new Models.Location
                {
                    Latitude = response.Latitude,
                    Longitude = response.Longitude
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GPS ERROR: {ex.Message}");
                return null;
            }
        }
    }
}
