namespace GeoSilence.Models
{
    public sealed class GeoSilenceUser
    {
        public required string Uid { get; init; }
        public required string Email { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }
}
