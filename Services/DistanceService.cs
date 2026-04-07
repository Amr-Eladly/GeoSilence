using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class DistanceService
    {
        public double CalculateMeters(Models.Location loc, Place place)
        {
            const double R = 6371000;

            double dLat = ToRadians(place.Latitude - loc.Latitude);
            double dLon = ToRadians(place.Longitude - loc.Longitude);

            double lat1 = ToRadians(loc.Latitude);
            double lat2 = ToRadians(place.Latitude);

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private double ToRadians(double deg)
            => deg * (Math.PI / 180);
    }
}
