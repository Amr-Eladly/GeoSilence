namespace GeoSilence.Models
{
    public sealed class CloudPlaceDto
    {
        public string Id { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Radius { get; set; }
        public string Mode { get; set; } = "Silent";
        public bool IsActive { get; set; } = true;
        public bool IsPublic { get; set; } = false;
        public bool Deleted { get; set; }
        public long CreatedAtUtcMs { get; set; }
        public long UpdatedAtUtcMs { get; set; }
        public int Version { get; set; }
    }
}
