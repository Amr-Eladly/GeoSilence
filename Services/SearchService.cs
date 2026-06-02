using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoSilence.Constants;

namespace GeoSilence.Services
{
    public class SearchService
    {
        private static readonly HttpClient Client = new();
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<PlaceSearchResult?> SearchPlaceAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var encodedQuery = Uri.EscapeDataString(query.Trim());
            var url =
                $"https://maps.googleapis.com/maps/api/place/textsearch/json?query={encodedQuery}&key={GoogleConfig.ApiKey}";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request, timeoutCts.Token);
            }
            catch
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            GooglePlaceSearchResponse? searchResponse;

            try
            {
                searchResponse = JsonSerializer.Deserialize<GooglePlaceSearchResponse>(
                    payload,
                    JsonOptions);
            }
            catch
            {
                return null;
            }

            if (!string.Equals(searchResponse?.Status, "OK", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(searchResponse?.Status, "ZERO_RESULTS", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var result = searchResponse?.Results?.FirstOrDefault();

            if (result?.Geometry?.Location == null)
                return null;

            return new PlaceSearchResult(
                result.Name ?? query.Trim(),
                result.Geometry.Location.Latitude,
                result.Geometry.Location.Longitude);
        }
    }

    public sealed record PlaceSearchResult(
        string Name,
        double Latitude,
        double Longitude);

    internal sealed class GooglePlaceSearchResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("results")]
        public List<GooglePlaceResult>? Results { get; set; }
    }

    internal sealed class GooglePlaceResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("geometry")]
        public GooglePlaceGeometry? Geometry { get; set; }
    }

    internal sealed class GooglePlaceGeometry
    {
        [JsonPropertyName("location")]
        public GooglePlaceLocation? Location { get; set; }
    }

    internal sealed class GooglePlaceLocation
    {
        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lng")]
        public double Longitude { get; set; }
    }
}
