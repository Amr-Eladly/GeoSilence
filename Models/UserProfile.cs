namespace GeoSilence.Models
{
    public sealed class UserProfile
    {
        public required string Uid { get; init; }
        public required string Email { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string DateOfBirthIso { get; set; } = string.Empty;
        public long CreatedAtUtcMs { get; set; }
        public long UpdatedAtUtcMs { get; set; }
        public string PhotoUrl { get; set; } = string.Empty;
        public string PhotoStoragePath { get; set; } = string.Empty;
        public string LocalPhotoPath { get; set; } = string.Empty;
    }
}
