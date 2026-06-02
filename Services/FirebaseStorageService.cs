using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GeoSilence.Constants;

namespace GeoSilence.Services
{
    public sealed class FirebaseStorageService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthenticationService _authenticationService;

        public FirebaseStorageService(HttpClient httpClient, IAuthenticationService authenticationService)
        {
            _httpClient = httpClient;
            _authenticationService = authenticationService;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(FirebaseConfig.StorageBucket);

        public async Task<(string PhotoUrl, string StoragePath)> UploadProfilePhotoAsync(
            string userId,
            string localFilePath,
            string contentType,
            string? existingStoragePath = null)
        {
            EnsureConfigured();

            if (!string.IsNullOrWhiteSpace(existingStoragePath))
                await DeleteFileAsync(existingStoragePath);

            var storagePath = $"profile-images/{userId}/{Guid.NewGuid():N}{Path.GetExtension(localFilePath)}";
            var token = await RequireTokenAsync();

            using var stream = File.OpenRead(localFilePath);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://firebasestorage.googleapis.com/v0/b/{FirebaseConfig.StorageBucket}/o?uploadType=media&name={Uri.EscapeDataString(storagePath)}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StreamContent(stream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Profile photo upload failed: {TryReadError(payload)}");

            return (BuildMediaUrl(storagePath), storagePath);
        }

        public async Task DownloadProfilePhotoAsync(string storagePath, string localFilePath)
        {
            EnsureConfigured();

            var token = await RequireTokenAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildMediaUrl(storagePath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Profile photo download failed: {TryReadError(payload)}");
            }

            var directory = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(localFilePath);
            await source.CopyToAsync(target);
        }

        public async Task DeleteFileAsync(string storagePath)
        {
            if (string.IsNullOrWhiteSpace(storagePath) || !IsConfigured)
                return;

            var token = await RequireTokenAsync();
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"https://firebasestorage.googleapis.com/v0/b/{FirebaseConfig.StorageBucket}/o/{Uri.EscapeDataString(storagePath)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return;

            var payload = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Profile photo delete failed: {TryReadError(payload)}");
        }

        private async Task<string> RequireTokenAsync()
        {
            var token = await _authenticationService.GetIdTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("A signed-in user is required to access profile photos.");

            return token;
        }

        private static string BuildMediaUrl(string storagePath) =>
            $"https://firebasestorage.googleapis.com/v0/b/{FirebaseConfig.StorageBucket}/o/{Uri.EscapeDataString(storagePath)}?alt=media";

        private static string TryReadError(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.TryGetProperty("message", out var message))
                        return message.GetString() ?? "unknown error";

                    if (error.TryGetProperty("errors", out var errors) &&
                        errors.ValueKind == JsonValueKind.Array &&
                        errors.GetArrayLength() > 0 &&
                        errors[0].TryGetProperty("message", out var nestedMessage))
                    {
                        return nestedMessage.GetString() ?? "unknown error";
                    }
                }
            }
            catch (JsonException)
            {
            }

            return "unknown error";
        }

        private void EnsureConfigured()
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Firebase Storage is not configured.");
        }
    }
}
