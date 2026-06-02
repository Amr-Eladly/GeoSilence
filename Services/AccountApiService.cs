using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoSilence.Constants;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public sealed class AccountApiService
    {
        private const string IdentityBaseUrl = "https://identitytoolkit.googleapis.com/v1";

        private readonly HttpClient _httpClient;
        private readonly IAuthenticationService _authenticationService;

        public AccountApiService(HttpClient httpClient, IAuthenticationService authenticationService)
        {
            _httpClient = httpClient;
            _authenticationService = authenticationService;
        }

        public async Task<FirebaseAccountMetadata> LookupCurrentAccountAsync()
        {
            var idToken = await _authenticationService.GetIdTokenAsync()
                ?? throw new InvalidOperationException("A signed-in user is required.");

            var response = await PostIdentityAsync<LookupResponse>(
                "accounts:lookup",
                new { idToken });

            var user = response.Users.FirstOrDefault()
                ?? throw new InvalidOperationException("Unable to load account metadata.");

            return new FirebaseAccountMetadata
            {
                Email = user.Email ?? _authenticationService.CurrentUser?.Email ?? string.Empty,
                DisplayName = user.DisplayName ?? _authenticationService.CurrentUser?.DisplayName ?? string.Empty,
                PhotoUrl = user.PhotoUrl ?? string.Empty,
                CreatedAtUtcMs = ParseUnixMs(user.CreatedAt)
            };
        }

        public async Task ChangePasswordAsync(string currentPassword, string newPassword)
        {
            var session = await ReauthenticateAsync(currentPassword);

            await PostIdentityAsync<object>(
                "accounts:update",
                new
                {
                    idToken = session.IdToken,
                    password = newPassword,
                    returnSecureToken = true
                });
        }

        public async Task DeleteCurrentAccountAsync(string currentPassword)
        {
            var session = await ReauthenticateAsync(currentPassword);

            await PostIdentityAsync<object>(
                "accounts:delete",
                new
                {
                    idToken = session.IdToken
                });
        }

        private async Task<AuthSession> ReauthenticateAsync(string currentPassword)
        {
            var email = _authenticationService.CurrentUser?.Email
                ?? throw new InvalidOperationException("A signed-in user is required.");

            return await PostIdentityAsync<AuthSession>(
                "accounts:signInWithPassword",
                new
                {
                    email = email.Trim(),
                    password = currentPassword,
                    returnSecureToken = true
                });
        }

        private async Task<T> PostIdentityAsync<T>(string method, object body)
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{IdentityBaseUrl}/{method}?key={FirebaseConfig.ApiKey}",
                body,
                JsonOptions());

            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw CreateAuthException(json);

            if (typeof(T) == typeof(object))
                return (T)(object)new object();

            return JsonSerializer.Deserialize<T>(json, JsonOptions()) ??
                throw new InvalidOperationException("Firebase returned an empty account response.");
        }

        private static Exception CreateAuthException(string json)
        {
            try
            {
                var error = JsonSerializer.Deserialize<FirebaseErrorResponse>(json, JsonOptions());
                var message = error?.Error?.Message?.Replace('_', ' ') ?? "Account operation failed.";

                return new InvalidOperationException(message switch
                {
                    "INVALID PASSWORD" => "The current password is incorrect.",
                    "WEAK PASSWORD : Password should be at least 6 characters" => "Password should be at least 6 characters.",
                    "CREDENTIAL TOO OLD LOGIN AGAIN" => "Please sign in again before making this change.",
                    _ => message
                });
            }
            catch (JsonException)
            {
                return new InvalidOperationException("Account operation failed.");
            }
        }

        private static long ParseUnixMs(string? rawValue)
        {
            return long.TryParse(rawValue, out var parsed) ? parsed : 0;
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        private sealed record AuthSession(
            [property: JsonPropertyName("idToken")] string IdToken,
            [property: JsonPropertyName("refreshToken")] string RefreshToken);

        private sealed record LookupResponse(
            [property: JsonPropertyName("users")] List<LookupUser> Users);

        private sealed record LookupUser(
            [property: JsonPropertyName("email")] string? Email,
            [property: JsonPropertyName("displayName")] string? DisplayName,
            [property: JsonPropertyName("photoUrl")] string? PhotoUrl,
            [property: JsonPropertyName("createdAt")] string? CreatedAt);

        private sealed record FirebaseErrorResponse(
            [property: JsonPropertyName("error")] FirebaseError? Error);

        private sealed record FirebaseError(
            [property: JsonPropertyName("message")] string? Message);
    }
}
