using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoSilence.Constants;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public sealed class AuthenticationService : IAuthenticationService
    {
        private const string IdentityBaseUrl = "https://identitytoolkit.googleapis.com/v1";
        private const string SecureKeyUid = "auth_uid";
        private const string SecureKeyEmail = "auth_email";
        private const string SecureKeyDisplayName = "auth_display_name";
        private const string SecureKeyIdToken = "auth_id_token";
        private const string SecureKeyRefreshToken = "auth_refresh_token";
        private const string SecureKeyExpiresAt = "auth_expires_at";

        private readonly HttpClient _httpClient;
        private bool _initialized;
        private string? _idToken;
        private string? _refreshToken;
        private DateTimeOffset _expiresAt;

        public AuthenticationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public GeoSilenceUser? CurrentUser { get; private set; }
        public bool IsSignedIn => CurrentUser != null;
        public event EventHandler? AuthStateChanged;

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            _initialized = true;

            var uid = await SecureStorage.Default.GetAsync(SecureKeyUid);
            var email = await SecureStorage.Default.GetAsync(SecureKeyEmail);
            var displayName = await SecureStorage.Default.GetAsync(SecureKeyDisplayName);
            _idToken = await SecureStorage.Default.GetAsync(SecureKeyIdToken);
            _refreshToken = await SecureStorage.Default.GetAsync(SecureKeyRefreshToken);

            var expiresAtValue = await SecureStorage.Default.GetAsync(SecureKeyExpiresAt);
            if (DateTimeOffset.TryParse(expiresAtValue, out var expiresAt))
                _expiresAt = expiresAt;

            if (string.IsNullOrWhiteSpace(uid) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(_refreshToken))
            {
                await ClearSessionInternalAsync();
                return;
            }

            CurrentUser = new GeoSilenceUser
            {
                Uid = uid,
                Email = email,
                DisplayName = displayName ?? string.Empty
            };

            try
            {
                await GetIdTokenAsync();
                AuthStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                await ClearSessionInternalAsync();
            }
        }

        public async Task<GeoSilenceUser> RegisterAsync(string email, string password, string displayName)
        {
            EnsureConfigured();

            var signUp = await PostIdentityAsync<AuthResponse>(
                "accounts:signUp",
                new
                {
                    email = email.Trim(),
                    password,
                    returnSecureToken = true
                });

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                var update = await PostIdentityAsync<AuthResponse>(
                    "accounts:update",
                    new
                    {
                        idToken = signUp.IdToken,
                        displayName = displayName.Trim(),
                        returnSecureToken = true
                    });

                signUp = signUp with
                {
                    IdToken = update.IdToken ?? signUp.IdToken,
                    RefreshToken = update.RefreshToken ?? signUp.RefreshToken,
                    DisplayName = update.DisplayName ?? displayName.Trim()
                };
            }

            return await SaveSessionAsync(signUp, displayName.Trim());
        }

        public async Task<GeoSilenceUser> LoginAsync(string email, string password)
        {
            EnsureConfigured();

            var response = await PostIdentityAsync<AuthResponse>(
                "accounts:signInWithPassword",
                new
                {
                    email = email.Trim(),
                    password,
                    returnSecureToken = true
                });

            return await SaveSessionAsync(response, response.DisplayName ?? string.Empty);
        }

        public async Task LogoutAsync()
        {
            await ClearSessionInternalAsync();
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<string?> GetIdTokenAsync(bool forceRefresh = false)
        {
            if (string.IsNullOrWhiteSpace(_refreshToken))
                return null;

            if (!forceRefresh &&
                !string.IsNullOrWhiteSpace(_idToken) &&
                _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _idToken;
            }

            EnsureConfigured();

            var refreshResponse = await _httpClient.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={FirebaseConfig.ApiKey}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _refreshToken
                }));

            var json = await refreshResponse.Content.ReadAsStringAsync();
            if (!refreshResponse.IsSuccessStatusCode)
                throw CreateAuthException(json);

            var token = JsonSerializer.Deserialize<RefreshTokenResponse>(
                json,
                JsonOptions()) ?? throw new InvalidOperationException("Firebase returned an empty token response.");

            _idToken = token.IdToken;
            _refreshToken = token.RefreshToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            await SecureStorage.Default.SetAsync(SecureKeyIdToken, _idToken);
            await SecureStorage.Default.SetAsync(SecureKeyRefreshToken, _refreshToken);
            await SecureStorage.Default.SetAsync(SecureKeyExpiresAt, _expiresAt.ToString("O"));

            return _idToken;
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

            return JsonSerializer.Deserialize<T>(json, JsonOptions()) ??
                throw new InvalidOperationException("Firebase returned an empty authentication response.");
        }

        private async Task<GeoSilenceUser> SaveSessionAsync(AuthResponse response, string displayName)
        {
            var user = new GeoSilenceUser
            {
                Uid = response.LocalId,
                Email = response.Email,
                DisplayName = response.DisplayName ?? displayName
            };

            CurrentUser = user;
            _idToken = response.IdToken;
            _refreshToken = response.RefreshToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(ParseExpiresIn(response.ExpiresIn));

            await SecureStorage.Default.SetAsync(SecureKeyUid, user.Uid);
            await SecureStorage.Default.SetAsync(SecureKeyEmail, user.Email);
            await SecureStorage.Default.SetAsync(SecureKeyDisplayName, user.DisplayName);
            await SecureStorage.Default.SetAsync(SecureKeyIdToken, _idToken);
            await SecureStorage.Default.SetAsync(SecureKeyRefreshToken, _refreshToken);
            await SecureStorage.Default.SetAsync(SecureKeyExpiresAt, _expiresAt.ToString("O"));

            AuthStateChanged?.Invoke(this, EventArgs.Empty);
            return user;
        }

        private static void ClearPersistedSession()
        {
            SecureStorage.Default.Remove(SecureKeyUid);
            SecureStorage.Default.Remove(SecureKeyEmail);
            SecureStorage.Default.Remove(SecureKeyDisplayName);
            SecureStorage.Default.Remove(SecureKeyIdToken);
            SecureStorage.Default.Remove(SecureKeyRefreshToken);
            SecureStorage.Default.Remove(SecureKeyExpiresAt);
        }

        private async Task ClearSessionInternalAsync()
        {
            CurrentUser = null;
            _idToken = null;
            _refreshToken = null;
            _expiresAt = default;

            ClearPersistedSession();
            await Task.CompletedTask;
        }

        private static Exception CreateAuthException(string json)
        {
            try
            {
                var error = JsonSerializer.Deserialize<FirebaseErrorResponse>(json, JsonOptions());
                var message = error?.Error?.Message?.Replace('_', ' ') ?? "Authentication failed.";
                return new InvalidOperationException(ToFriendlyMessage(message));
            }
            catch (JsonException)
            {
                return new InvalidOperationException("Authentication failed.");
            }
        }

        private static string ToFriendlyMessage(string message)
        {
            return message switch
            {
                "EMAIL EXISTS" => "An account already exists for this email.",
                "EMAIL NOT FOUND" => "No account was found for this email.",
                "INVALID PASSWORD" => "The email or password is incorrect.",
                "USER DISABLED" => "This account has been disabled.",
                "WEAK PASSWORD : Password should be at least 6 characters" => "Password should be at least 6 characters.",
                _ => message
            };
        }

        private static int ParseExpiresIn(string? expiresIn)
        {
            return int.TryParse(expiresIn, out var seconds) ? seconds : 3600;
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        private static void EnsureConfigured()
        {
            if (!FirebaseConfig.IsConfigured)
                throw new InvalidOperationException("Firebase authentication is not configured. Set Constants/FirebaseConfig.cs ApiKey to your Firebase Web API key.");
        }

        private sealed record AuthResponse(
            [property: JsonPropertyName("localId")] string LocalId,
            [property: JsonPropertyName("email")] string Email,
            [property: JsonPropertyName("displayName")] string? DisplayName,
            [property: JsonPropertyName("idToken")] string IdToken,
            [property: JsonPropertyName("refreshToken")] string RefreshToken,
            [property: JsonPropertyName("expiresIn")] string? ExpiresIn);

        private sealed record RefreshTokenResponse(
            [property: JsonPropertyName("id_token")] string IdToken,
            [property: JsonPropertyName("refresh_token")] string RefreshToken,
            [property: JsonPropertyName("expires_in")] int ExpiresIn);

        private sealed record FirebaseErrorResponse(
            [property: JsonPropertyName("error")] FirebaseError? Error);

        private sealed record FirebaseError(
            [property: JsonPropertyName("message")] string? Message);
    }
}
