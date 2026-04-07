using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class LocationService
    {
        //public async Task<Models.Location?> GetCurrentLocationAsync()
        //{
        //    try
        //    {
        //        var request = new GeolocationRequest(
        //            GeolocationAccuracy.Medium,
        //            TimeSpan.FromSeconds(5));

        //        var response = await Geolocation.GetLocationAsync(request);

        //        if (response == null)
        //            return null;

        //        return new Models.Location
        //        {
        //            Latitude = response.Latitude,
        //            Longitude = response.Longitude
        //        };
        //    }
        //    catch
        //    {
        //        return null;
        //    }
        //}
        public async Task<Models.Location?> GetCurrentLocationAsync()
        {
            await Task.Delay(100);

            return new Models.Location
            {
                Latitude = 47.4979,
                Longitude = 19.0402
            };
        }
    }
}
