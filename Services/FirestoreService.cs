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

        public async Task<IReadOnlyList<CloudPlaceDto>> DownloadPlacesAsync(string userId)
        {
            var token = await RequireTokenAsync();
            var url = CollectionUrl(userId);

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

        public async Task UploadPlaceAsync(string userId, CloudPlaceDto place)
        {
            var token = await RequireTokenAsync();
            var url = DocumentUrl(userId, place.Id);
            var body = JsonSerializer.Serialize(new { fields = CreateFields(place) });

            _logger.LogInformation("Firestore upload started for place {CloudId}", place.Id);

            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "upload");

            _logger.LogInformation("Firestore upload completed for place {CloudId}", place.Id);
        }

        public async Task DeletePlaceAsync(string userId, string cloudId)
        {
            var token = await RequireTokenAsync();
            var url = DocumentUrl(userId, cloudId);

            _logger.LogInformation("Firestore delete started for place {CloudId}", cloudId);

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, payload, "delete");

            _logger.LogInformation("Firestore delete completed for place {CloudId}", cloudId);
        }

        private async Task<string> RequireTokenAsync()
        {
            var token = await _authenticationService.GetIdTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("The user must be signed in before syncing with Firestore.");

            return token;
        }

        private static void EnsureSuccess(HttpResponseMessage response, string payload, string action)
        {
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
                IsActive = ReadBool(fields, "isActive"),
                IsPublic = ReadBool(fields, "isPublic"),
                Deleted = ReadBool(fields, "deleted"),
                CreatedAtUtcMs = ReadTimestamp(fields, "createdAt"),
                UpdatedAtUtcMs = ReadTimestamp(fields, "updatedAt"),
                Version = ReadInt(fields, "version")
            };
        }

        private static Dictionary<string, object> CreateFields(CloudPlaceDto place)
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
                ["isActive"] = BoolValue(place.IsActive),
                ["isPublic"] = BoolValue(place.IsPublic),
                ["deleted"] = BoolValue(place.Deleted),
                ["createdAt"] = TimestampValue(place.CreatedAtUtcMs),
                ["updatedAt"] = TimestampValue(place.UpdatedAtUtcMs),
                ["version"] = IntegerValue(place.Version)
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

        private static bool ReadBool(JsonElement fields, string name)
        {
            var value = fields.GetProperty(name);
            if (value.TryGetProperty("booleanValue", out var boolValue))
                return boolValue.GetBoolean();

            return false;
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

        private static int ReadInt(JsonElement fields, string name)
        {
            var value = fields.GetProperty(name);
            if (value.TryGetProperty("integerValue", out var intValue) &&
                int.TryParse(intValue.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 0;
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

        private static string ProjectUrl =>
            $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents";

        private static string CollectionUrl(string userId) =>
            $"{ProjectUrl}/users/{userId}/places";

        private static string DocumentUrl(string userId, string cloudId) =>
            $"{CollectionUrl(userId)}/{cloudId}";
    }
}
