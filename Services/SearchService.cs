using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoSilence.Constants;

namespace GeoSilence.Services
{
    public class SearchService
    {
        private static readonly HttpClient Client = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<PlaceSearchResult?> SearchPlaceAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var encodedQuery = Uri.EscapeDataString(query.Trim());
            var url =
                $"https://maps.googleapis.com/maps/api/place/textsearch/json?query={encodedQuery}&key={GoogleConfig.ApiKey}";

            var response = await Client.GetStringAsync(url);
            var searchResponse = JsonSerializer.Deserialize<GooglePlaceSearchResponse>(
                response,
                JsonOptions);

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
