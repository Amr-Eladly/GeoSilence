namespace GeoSilence.Constants
{
    public static class FirebaseConfig
    {
        private const string UnconfiguredApiKey = "REPLACE_WITH_FIREBASE_WEB_API_KEY";

        public const string ApiKey = "AIzaSyAKhlqATIz6DM0L0wRUPtesoSyM7EWi9Hg";
        public const string ProjectId = "geosilence-2f35c";
        public const string StorageBucket = "";

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) &&
            ApiKey != UnconfiguredApiKey &&
            !string.IsNullOrWhiteSpace(ProjectId);
    }
}
