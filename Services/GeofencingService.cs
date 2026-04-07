using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class GeofencingService
    {
        private readonly DistanceService _distanceService;

        public GeofencingService(DistanceService distanceService)
        {
            _distanceService = distanceService;
        }

        public bool IsInside(Models.Location location, Place place)
        {
            double distance = _distanceService.CalculateMeters(location, place);
            return distance <= place.Radius;
        }
    }
}
