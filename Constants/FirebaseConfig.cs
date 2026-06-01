namespace GeoSilence.Constants
{
    public static class FirebaseConfig
    {
        private const string UnconfiguredApiKey = "REPLACE_WITH_FIREBASE_WEB_API_KEY";

        public const string ApiKey = "AIzaSyDQkgghcqMJj9n6d5MVavts--R1BeOJ8zk";
        public const string ProjectId = "geosilence-2f35c";

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) &&
            ApiKey != UnconfiguredApiKey &&
            !string.IsNullOrWhiteSpace(ProjectId);
    }
}
