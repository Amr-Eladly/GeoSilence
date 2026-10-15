using System.Net.Http;
using System.Globalization;
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

        public async Task<IReadOnlyList<PlaceSuggestion>> GetSuggestionsAsync(
            string input, string sessionToken, SearchArea? area = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<PlaceSuggestion>();

            var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(input.Trim())}&sessiontoken={Uri.EscapeDataString(sessionToken)}&key={GoogleConfig.ApiKey}{FormatLocationBias(area)}";
            var response = await GetGoogleResponseAsync<GoogleAutocompleteResponse>(url, cancellationToken);
            var predictions = response.Predictions?
                .Where(prediction => !string.IsNullOrWhiteSpace(prediction.PlaceId))
                .Select(prediction => new PlaceSuggestion(
                    prediction.PlaceId!,
                    prediction.StructuredFormatting?.MainText ?? prediction.Description ?? input,
                    prediction.StructuredFormatting?.SecondaryText ?? string.Empty,
                    DistanceMeters: prediction.DistanceMeters))
                .ToList() ?? new List<PlaceSuggestion>();
            if (area != null)
            {
                var nearby = predictions.Where(item =>
                    item.DistanceMeters is double distance && distance <= area.RadiusMeters * 2).ToList();
                if (nearby.Count > 0) predictions = nearby;
            }
            return predictions.Take(5).ToArray();
        }

        public async Task<IReadOnlyList<PlaceSearchResult>> SearchPlacesAsync(
            string query, SearchArea? area = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<PlaceSearchResult>();

            var url = $"https://maps.googleapis.com/maps/api/place/textsearch/json?query={Uri.EscapeDataString(query.Trim())}&key={GoogleConfig.ApiKey}{FormatLocationBias(area)}";
            var response = await GetGoogleResponseAsync<GooglePlaceSearchResponse>(url, cancellationToken);
            var results = response.Results?
                .Where(place => place.Geometry?.Location != null)
                .Select(place => new PlaceSearchResult(
                    place.Name ?? query.Trim(),
                    place.Geometry!.Location!.Latitude,
                    place.Geometry.Location.Longitude,
                    place.FormattedAddress ?? string.Empty))
                .ToList() ?? new List<PlaceSearchResult>();

            if (area != null)
            {
                double DistanceMeters(PlaceSearchResult place) =>
                    Microsoft.Maui.Devices.Sensors.Location.CalculateDistance(
                        area.Latitude, area.Longitude, place.Latitude, place.Longitude,
                        Microsoft.Maui.Devices.Sensors.DistanceUnits.Kilometers) * 1000;
                var nearby = results.Where(place => DistanceMeters(place) <= area.RadiusMeters * 2).ToList();
                if (nearby.Count > 0) results = nearby;
                results = results.OrderBy(DistanceMeters).ToList();
            }
            return results.Take(8).ToArray();
        }

        private static string FormatLocationBias(SearchArea? area)
        {
            if (area == null) return string.Empty;
            var radius = Math.Clamp(area.RadiusMeters, 1000, 50000);
            var point = $"{area.Latitude.ToString(CultureInfo.InvariantCulture)},{area.Longitude.ToString(CultureInfo.InvariantCulture)}";
            return $"&location={point}&radius={radius.ToString("F0", CultureInfo.InvariantCulture)}&origin={point}";
        }

        public async Task<PlaceSearchResult> GetSuggestionPlaceAsync(
            PlaceSuggestion suggestion, string sessionToken, CancellationToken cancellationToken = default)
        {
            var url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={Uri.EscapeDataString(suggestion.PlaceId)}&fields=name,geometry&sessiontoken={Uri.EscapeDataString(sessionToken)}&key={GoogleConfig.ApiKey}";
            var response = await GetGoogleResponseAsync<GooglePlaceDetailsResponse>(url, cancellationToken);
            var result = response.Result;
            if (result?.Geometry?.Location == null)
                throw new PlaceSearchException("The selected place has no map location.");

            return new PlaceSearchResult(result.Name ?? suggestion.Name,
                result.Geometry.Location.Latitude, result.Geometry.Location.Longitude);
        }

        private static async Task<T> GetGoogleResponseAsync<T>(string url, CancellationToken cancellationToken)
            where T : GoogleStatusResponse
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            try
            {
                using var response = await Client.SendAsync(request, timeoutCts.Token);
                var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
                if (response.IsSuccessStatusCode &&
                    string.Equals(result?.Status, "ZERO_RESULTS", StringComparison.OrdinalIgnoreCase))
                    return result!;
                if (!response.IsSuccessStatusCode ||
                    !string.Equals(result?.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    throw CreateSearchFailure(result, (int)response.StatusCode);
                return result!;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PlaceSearchException("Place search timed out. Check your connection and try again.");
            }
            catch (HttpRequestException ex)
            {
                throw new PlaceSearchException("Place search could not connect. Check your connection and try again.", ex);
            }
            catch (JsonException ex)
            {
                throw new PlaceSearchException("Place search returned an invalid response.", ex);
            }
        }

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
            try
            {
                using var response = await Client.SendAsync(request, timeoutCts.Token);
                var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var searchResponse = JsonSerializer.Deserialize<GooglePlaceSearchResponse>(payload, JsonOptions);

                if (response.IsSuccessStatusCode &&
                    string.Equals(searchResponse?.Status, "ZERO_RESULTS", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (!response.IsSuccessStatusCode ||
                    !string.Equals(searchResponse?.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    throw CreateSearchFailure(searchResponse, (int)response.StatusCode);

                var result = searchResponse?.Results?.FirstOrDefault();
                if (result?.Geometry?.Location == null)
                    throw new PlaceSearchException("Place search returned a result without a location.");

                return new PlaceSearchResult(
                    result.Name ?? query.Trim(),
                    result.Geometry.Location.Latitude,
                    result.Geometry.Location.Longitude);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PlaceSearchException("Place search timed out. Check your connection and try again.");
            }
            catch (HttpRequestException ex)
            {
                throw new PlaceSearchException("Place search could not connect. Check your connection and try again.", ex);
            }
            catch (JsonException ex)
            {
                throw new PlaceSearchException("Place search returned an invalid response.", ex);
            }
        }

        private static PlaceSearchException CreateSearchFailure(GoogleStatusResponse? response, int httpStatus)
        {
            System.Diagnostics.Debug.WriteLine($"GOOGLE PLACE SEARCH: HTTP {httpStatus}, status={response?.Status}");

            if (response?.ErrorMessage?.Contains("billing", StringComparison.OrdinalIgnoreCase) == true)
                return new PlaceSearchException("Place search is unavailable because billing is not enabled for the Google Cloud project used by its Places API key.");

            return response?.Status?.ToUpperInvariant() switch
            {
                "REQUEST_DENIED" => new PlaceSearchException("Google Places rejected the search request. Check that Places API (Legacy) is enabled and the search API key is allowed to use Autocomplete and Place Details."),
                "OVER_QUERY_LIMIT" => new PlaceSearchException("Google Places has reached its quota or billing limit. Try again later or check the Cloud project."),
                "INVALID_REQUEST" => new PlaceSearchException("Google Places rejected this search as invalid."),
                _ => new PlaceSearchException($"Place search is unavailable (HTTP {httpStatus}, status {response?.Status ?? "unknown"}).")
            };
        }
    }

    public sealed class PlaceSearchException : Exception
    {
        public PlaceSearchException(string message) : base(message) { }
        public PlaceSearchException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed record PlaceSearchResult(
        string Name,
        double Latitude,
        double Longitude,
        string Address = "");

    public sealed record PlaceSuggestion(string PlaceId, string Name, string Description,
        double? Latitude = null, double? Longitude = null, bool IsSearchAction = false,
        double? DistanceMeters = null);

    public sealed record SearchArea(double Latitude, double Longitude, double RadiusMeters);

    internal class GoogleStatusResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

    }

    internal sealed class GooglePlaceSearchResponse : GoogleStatusResponse
    {
        [JsonPropertyName("results")]
        public List<GooglePlaceResult>? Results { get; set; }
    }

    internal sealed class GoogleAutocompleteResponse : GoogleStatusResponse
    {
        [JsonPropertyName("predictions")]
        public List<GooglePrediction>? Predictions { get; set; }
    }

    internal sealed class GooglePrediction
    {
        [JsonPropertyName("place_id")]
        public string? PlaceId { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("distance_meters")]
        public double? DistanceMeters { get; set; }
        [JsonPropertyName("structured_formatting")]
        public GoogleStructuredFormatting? StructuredFormatting { get; set; }
    }

    internal sealed class GoogleStructuredFormatting
    {
        [JsonPropertyName("main_text")]
        public string? MainText { get; set; }
        [JsonPropertyName("secondary_text")]
        public string? SecondaryText { get; set; }
    }

    internal sealed class GooglePlaceDetailsResponse : GoogleStatusResponse
    {
        [JsonPropertyName("result")]
        public GooglePlaceResult? Result { get; set; }
    }

    internal sealed class GooglePlaceResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

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
