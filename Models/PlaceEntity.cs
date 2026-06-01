using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;

namespace GeoSilence.Models
{
    public class PlaceEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string CloudId { get; set; } = "";

        public string Name { get; set; } = "";

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public double Radius { get; set; }

        public int Mode { get; set; }

        public bool IsPublic { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsDirty { get; set; }

        public string OwnerId { get; set; } = "";
        public long CreatedAtUtcMs { get; set; }
        public long UpdatedAtUtcMs { get; set; }
        public long? LastSyncedAtUtcMs { get; set; }
        public int Version { get; set; } = 1;
    }
}
