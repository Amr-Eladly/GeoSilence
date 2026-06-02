namespace GeoSilence.Models
{
    public sealed class FirebaseAccountMetadata
    {
        public string Email { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string PhotoUrl { get; init; } = string.Empty;
        public long CreatedAtUtcMs { get; init; }
    }
}
