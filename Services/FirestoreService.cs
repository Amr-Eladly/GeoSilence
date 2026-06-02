using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeoSilence.Constants;
using GeoSilence.Models;
using Microsoft.Extensions.Logging;

namespace GeoSilence.Services
{
    public sealed class FirestoreService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthenticationService _authenticationService;
        private readonly ILogger<FirestoreService> _logger;

        public FirestoreService(
            HttpClient httpClient,
            IAuthenticationService authenticationService,
            ILogger<FirestoreService> logger)
        {
            _httpClient = httpClient;
            _authenticationService = authenticationService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<CloudPlaceDto>> DownloadPrivatePlacesAsync(string userId)
        {
            var token = await RequireTokenAsync();
            var url = PrivateCollectionUrl(userId);

            _logger.LogInformation("Firestore download started for user {UserId}", userId);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "download");

            var places = new List<CloudPlaceDto>();
            using var doc = JsonDocument.Parse(payload);

            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var document in documents.EnumerateArray())
                {
                    places.Add(ParseDocument(document));
                }
            }

            _logger.LogInformation("Firestore download completed for user {UserId} with {Count} places", userId, places.Count);
            return places;
        }

        public async Task<IReadOnlyList<CloudPlaceDto>> DownloadPublicPlacesAsync()
        {
            var token = await RequireTokenAsync();
            var url = PublicCollectionUrl;

            _logger.LogInformation("Firestore public place download started");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "download public places");

            var places = new List<CloudPlaceDto>();
            using var doc = JsonDocument.Parse(payload);

            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var document in documents.EnumerateArray())
                {
                    places.Add(ParseDocument(document));
                }
            }

            _logger.LogInformation("Firestore public place download completed with {Count} places", places.Count);
            return places;
        }

        public async Task UploadPrivatePlaceAsync(string userId, CloudPlaceDto place)
        {
            var token = await RequireTokenAsync();
            var url = PrivateDocumentUrl(userId, place.Id);
            var body = JsonSerializer.Serialize(new { fields = CreatePrivateFields(place) });

            _logger.LogInformation("Firestore private upload started for place {CloudId}", place.Id);

            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "upload");

            _logger.LogInformation("Firestore private upload completed for place {CloudId}", place.Id);
        }

        public async Task UploadPublicPlaceAsync(CloudPlaceDto place)
        {
            var token = await RequireTokenAsync();
            var url = PublicDocumentUrl(place.Id);
            var body = JsonSerializer.Serialize(new { fields = CreatePublicFields(place) });

            _logger.LogInformation("Firestore public upload started for place {CloudId}", place.Id);

            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "upload public place");

            _logger.LogInformation("Firestore public upload completed for place {CloudId}", place.Id);
        }

        public async Task<UserProfile?> DownloadUserProfileAsync(string userId)
        {
            var token = await RequireTokenAsync();
            var url = UserDocumentUrl(userId);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            EnsureSuccess(response, payload, "download user profile");

            using var doc = JsonDocument.Parse(payload);
            return ParseUserProfile(doc.RootElement);
        }

        public async Task UpsertUserProfileAsync(UserProfile profile)
        {
            var token = await RequireTokenAsync();
            var url = UserDocumentUrl(profile.Uid);
            var body = JsonSerializer.Serialize(new { fields = CreateProfileFields(profile) });

            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "upsert user profile");
        }

        public async Task DeleteUserProfileAsync(string userId)
        {
            var token = await RequireTokenAsync();
            var url = UserDocumentUrl(userId);

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "delete user profile");
        }

        public async Task DeletePrivatePlaceAsync(string userId, string cloudId, bool ignoreNotFound = false)
        {
            var token = await RequireTokenAsync();
            var url = PrivateDocumentUrl(userId, cloudId);

            _logger.LogInformation("Firestore private delete started for place {CloudId}", cloudId);

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "delete private place", ignoreNotFound);

            _logger.LogInformation("Firestore private delete completed for place {CloudId}", cloudId);
        }

        public async Task DeletePublicPlaceAsync(string cloudId, bool ignoreNotFound = false)
        {
            var token = await RequireTokenAsync();
            var url = PublicDocumentUrl(cloudId);

            _logger.LogInformation("Firestore public delete started for place {CloudId}", cloudId);

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "delete public place", ignoreNotFound);

            _logger.LogInformation("Firestore public delete completed for place {CloudId}", cloudId);
        }

        private async Task<string> RequireTokenAsync()
        {
            var token = await _authenticationService.GetIdTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("The user must be signed in before syncing with Firestore.");

            return token;
        }

        private static void EnsureSuccess(HttpResponseMessage response, string payload, string action, bool ignoreNotFound = false)
        {
            if (ignoreNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return;

            if (response.IsSuccessStatusCode)
                return;

            var message = TryReadError(payload);
            throw new InvalidOperationException($"Firestore {action} failed: {message}");
        }

        private static string TryReadError(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? "unknown error";
                }
            }
            catch
            {
            }

            return "unknown error";
        }

        private static CloudPlaceDto ParseDocument(JsonElement document)
        {
            var fields = document.GetProperty("fields");
            return new CloudPlaceDto
            {
                Id = document.GetProperty("name").GetString()!.Split('/').Last(),
                OwnerId = ReadString(fields, "ownerId"),
                Name = ReadString(fields, "name"),
                Latitude = ReadDouble(fields, "latitude"),
                Longitude = ReadDouble(fields, "longitude"),
                Radius = ReadDouble(fields, "radius"),
                Mode = ReadString(fields, "mode"),
                ActivationType = ReadOptionalString(fields, "activationType", ActivationType.Automatic.ToString()),
                Visibility = ReadOptionalString(fields, "visibility", InferVisibility(document)),
                Deleted = ReadOptionalBool(fields, "deleted"),
                CreatedAtUtcMs = ReadTimestamp(fields, "createdAt"),
                UpdatedAtUtcMs = ReadTimestamp(fields, "updatedAt"),
                Version = ReadOptionalInt(fields, "version", 1)
            };
        }

        private static UserProfile ParseUserProfile(JsonElement document)
        {
            var fields = document.GetProperty("fields");

            return new UserProfile
            {
                Uid = ReadString(fields, "uid"),
                Email = ReadString(fields, "email"),
                DisplayName = ReadString(fields, "displayName"),
                FirstName = ReadOptionalString(fields, "firstName"),
                LastName = ReadOptionalString(fields, "lastName"),
                DateOfBirthIso = ReadOptionalString(fields, "dateOfBirth"),
                CreatedAtUtcMs = ReadTimestamp(fields, "createdAt"),
                UpdatedAtUtcMs = ReadTimestamp(fields, "updatedAt"),
                PhotoUrl = ReadOptionalString(fields, "photoUrl"),
                PhotoStoragePath = ReadOptionalString(fields, "photoStoragePath"),
                LocalPhotoPath = string.Empty
            };
        }

        private static Dictionary<string, object> CreatePrivateFields(CloudPlaceDto place)
        {
            return new Dictionary<string, object>
            {
                ["id"] = StringValue(place.Id),
                ["ownerId"] = StringValue(place.OwnerId),
                ["name"] = StringValue(place.Name),
                ["latitude"] = DoubleValue(place.Latitude),
                ["longitude"] = DoubleValue(place.Longitude),
                ["radius"] = DoubleValue(place.Radius),
                ["mode"] = StringValue(place.Mode),
                ["activationType"] = StringValue(place.ActivationType),
                ["visibility"] = StringValue(place.Visibility),
                ["deleted"] = BoolValue(place.Deleted),
                ["createdAt"] = TimestampValue(place.CreatedAtUtcMs),
                ["updatedAt"] = TimestampValue(place.UpdatedAtUtcMs),
                ["version"] = IntegerValue(place.Version)
            };
        }

        private static Dictionary<string, object> CreatePublicFields(CloudPlaceDto place)
        {
            return new Dictionary<string, object>
            {
                ["id"] = StringValue(place.Id),
                ["ownerId"] = StringValue(place.OwnerId),
                ["name"] = StringValue(place.Name),
                ["latitude"] = DoubleValue(place.Latitude),
                ["longitude"] = DoubleValue(place.Longitude),
                ["radius"] = DoubleValue(place.Radius),
                ["mode"] = StringValue(place.Mode),
                ["activationType"] = StringValue(place.ActivationType),
                ["createdAt"] = TimestampValue(place.CreatedAtUtcMs),
                ["updatedAt"] = TimestampValue(place.UpdatedAtUtcMs)
            };
        }

        private static Dictionary<string, object> CreateProfileFields(UserProfile profile)
        {
            return new Dictionary<string, object>
            {
                ["uid"] = StringValue(profile.Uid),
                ["displayName"] = StringValue(profile.DisplayName),
                ["email"] = StringValue(profile.Email),
                ["firstName"] = StringValue(profile.FirstName),
                ["lastName"] = StringValue(profile.LastName),
                ["dateOfBirth"] = StringValue(profile.DateOfBirthIso),
                ["photoUrl"] = StringValue(profile.PhotoUrl),
                ["photoStoragePath"] = StringValue(profile.PhotoStoragePath),
                ["createdAt"] = TimestampValue(profile.CreatedAtUtcMs),
                ["updatedAt"] = TimestampValue(profile.UpdatedAtUtcMs)
            };
        }

        private static Dictionary<string, object> StringValue(string value) =>
            new() { ["stringValue"] = value };

        private static Dictionary<string, object> DoubleValue(double value) =>
            new() { ["doubleValue"] = value };

        private static Dictionary<string, object> BoolValue(bool value) =>
            new() { ["booleanValue"] = value };

        private static Dictionary<string, object> IntegerValue(int value) =>
            new() { ["integerValue"] = value.ToString(CultureInfo.InvariantCulture) };

        private static Dictionary<string, object> TimestampValue(long unixMs) =>
            new() { ["timestampValue"] = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("O") };

        private static string ReadString(JsonElement fields, string name)
        {
            var value = fields.GetProperty(name);
            if (value.TryGetProperty("stringValue", out var stringValue))
                return stringValue.GetString() ?? string.Empty;

            return string.Empty;
        }

        private static string ReadOptionalString(JsonElement fields, string name, string defaultValue = "")
        {
            if (!fields.TryGetProperty(name, out var value))
                return defaultValue;

            if (value.TryGetProperty("stringValue", out var stringValue))
                return stringValue.GetString() ?? string.Empty;

            return defaultValue;
        }

        private static bool ReadOptionalBool(JsonElement fields, string name, bool defaultValue = false)
        {
            if (!fields.TryGetProperty(name, out var value))
                return defaultValue;

            if (value.TryGetProperty("booleanValue", out var boolValue))
                return boolValue.GetBoolean();

            return defaultValue;
        }

        private static double ReadDouble(JsonElement fields, string name)
        {
            var value = fields.GetProperty(name);
            if (value.TryGetProperty("doubleValue", out var doubleValue))
                return doubleValue.GetDouble();

            if (value.TryGetProperty("integerValue", out var intValue) &&
                double.TryParse(intValue.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static int ReadOptionalInt(JsonElement fields, string name, int defaultValue = 0)
        {
            if (!fields.TryGetProperty(name, out var value))
                return defaultValue;

            if (value.TryGetProperty("integerValue", out var intValue) &&
                int.TryParse(intValue.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static long ReadTimestamp(JsonElement fields, string name)
        {
            var value = fields.GetProperty(name);
            if (value.TryGetProperty("timestampValue", out var timestampValue) &&
                DateTimeOffset.TryParse(timestampValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed.ToUnixTimeMilliseconds();
            }

            return 0;
        }

        private static string InferVisibility(JsonElement document)
        {
            var name = document.GetProperty("name").GetString() ?? string.Empty;
            return name.Contains("/publicPlaces/", StringComparison.Ordinal)
                ? PlaceVisibility.Public.ToString()
                : PlaceVisibility.Private.ToString();
        }

        private static string ProjectUrl =>
            $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents";

        private static string UserDocumentUrl(string userId) =>
            $"{ProjectUrl}/users/{userId}";

        private static string PrivateCollectionUrl(string userId) =>
            $"{ProjectUrl}/users/{userId}/places";

        private static string PrivateDocumentUrl(string userId, string cloudId) =>
            $"{PrivateCollectionUrl(userId)}/{cloudId}";

        private static string PublicCollectionUrl =>
            $"{ProjectUrl}/publicPlaces";

        private static string PublicDocumentUrl(string cloudId) =>
            $"{PublicCollectionUrl}/{cloudId}";
    }
}
