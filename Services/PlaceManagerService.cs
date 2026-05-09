using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class PlaceManagerService
    {
        private readonly DistanceService _distanceService;

        public PlaceManagerService(DistanceService distanceService)
        {
            _distanceService = distanceService;
        }
        public List<Place> GetNearbyPlaces(Models.Location location, List<Place> places)
        {
            return places
                .Where(p => p.IsActive)
                .Select(p => new
                {
                    Place = p,
                    Distance = _distanceService.CalculateMeters(location, p)
                })
                .Where(x => x.Distance <= x.Place.Radius)
                .OrderBy(x => x.Distance)
                .Select(x => x.Place)
                .ToList();
        }
    }
}
