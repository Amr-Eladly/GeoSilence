using GeoSilence.Models;

namespace GeoSilence.Services
{
    public interface IAuthenticationService
    {
        GeoSilenceUser? CurrentUser { get; }
        bool IsSignedIn { get; }
        event EventHandler? AuthStateChanged;

        Task InitializeAsync();
        Task<GeoSilenceUser> RegisterAsync(string email, string password, string displayName);
        Task<GeoSilenceUser> LoginAsync(string email, string password);
        Task LogoutAsync();
        Task<string?> GetIdTokenAsync(bool forceRefresh = false);
    }
}
