using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeoSilence.Models
{
    public class Place
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Radius { get; set; }
        public ModeType Mode { get; set; }
        public Boolean IsActive { get; set; } = true;
    }
}
