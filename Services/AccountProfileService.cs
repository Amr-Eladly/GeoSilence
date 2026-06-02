using GeoSilence.Models;
using GeoSilence.Utilities;

namespace GeoSilence.Services
{
    public sealed class AccountProfileService
    {
        private readonly IAuthenticationService _authenticationService;
        private readonly FirestoreService _firestoreService;
        private readonly FirebaseStorageService _firebaseStorageService;
        private readonly AccountApiService _accountApiService;
        private readonly CloudPlaceRepository _cloudPlaceRepository;
        private readonly PlaceRepository _placeRepository;
        private readonly DatabaseService _databaseService;

        private UserProfile? _currentProfile;
        private string? _loadedUserId;

        public AccountProfileService(
            IAuthenticationService authenticationService,
            FirestoreService firestoreService,
            FirebaseStorageService firebaseStorageService,
            AccountApiService accountApiService,
            CloudPlaceRepository cloudPlaceRepository,
            PlaceRepository placeRepository,
            DatabaseService databaseService)
        {
            _authenticationService = authenticationService;
            _firestoreService = firestoreService;
            _firebaseStorageService = firebaseStorageService;
            _accountApiService = accountApiService;
            _cloudPlaceRepository = cloudPlaceRepository;
            _placeRepository = placeRepository;
            _databaseService = databaseService;

            _authenticationService.AuthStateChanged += OnAuthStateChanged;
        }

        public event EventHandler? ProfileChanged;

        public UserProfile? CurrentProfile => _currentProfile;

        public async Task<UserProfile> EnsureLoadedAsync(bool forceRefresh = false)
        {
            var user = RequireCurrentUser();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!forceRefresh &&
                _currentProfile != null &&
                string.Equals(_loadedUserId, user.Uid, StringComparison.Ordinal))
            {
                return _currentProfile;
            }

            FirebaseAccountMetadata metadata;
            try
            {
                metadata = await _accountApiService.LookupCurrentAccountAsync();
            }
            catch
            {
                metadata = new FirebaseAccountMetadata
                {
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    CreatedAtUtcMs = now
                };
            }

            UserProfile? remoteProfile = null;
            try
            {
                remoteProfile = await _firestoreService.DownloadUserProfileAsync(user.Uid);
            }
            catch
            {
            }

            var names = AvatarHelper.SplitDisplayName(remoteProfile?.DisplayName ?? metadata.DisplayName ?? user.DisplayName);
            var profile = remoteProfile ?? new UserProfile
            {
                Uid = user.Uid,
                Email = user.Email,
                DisplayName = string.IsNullOrWhiteSpace(metadata.DisplayName) ? user.DisplayName : metadata.DisplayName,
                FirstName = names.FirstName,
                LastName = names.LastName,
                DateOfBirthIso = string.Empty,
                CreatedAtUtcMs = metadata.CreatedAtUtcMs > 0 ? metadata.CreatedAtUtcMs : now,
                UpdatedAtUtcMs = now,
                PhotoUrl = string.Empty,
                PhotoStoragePath = string.Empty
            };

            profile.Email = string.IsNullOrWhiteSpace(profile.Email) ? user.Email : profile.Email;
            profile.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? (string.IsNullOrWhiteSpace(metadata.DisplayName) ? user.DisplayName : metadata.DisplayName)
                : profile.DisplayName;

            if (string.IsNullOrWhiteSpace(profile.FirstName) && string.IsNullOrWhiteSpace(profile.LastName))
            {
                profile.FirstName = names.FirstName;
                profile.LastName = names.LastName;
            }

            if (profile.CreatedAtUtcMs <= 0)
                profile.CreatedAtUtcMs = metadata.CreatedAtUtcMs > 0 ? metadata.CreatedAtUtcMs : now;

            if (profile.UpdatedAtUtcMs <= 0)
                profile.UpdatedAtUtcMs = now;

            profile.LocalPhotoPath = await ResolveLocalPhotoPathAsync(profile);

            _currentProfile = profile;
            _loadedUserId = user.Uid;

            if (remoteProfile == null)
            {
                try
                {
                    await _firestoreService.UpsertUserProfileAsync(profile);
                }
                catch
                {
                }
            }

            ProfileChanged?.Invoke(this, EventArgs.Empty);
            return profile;
        }

        public async Task<int> GetLocationsCountAsync()
        {
            var places = await _placeRepository.GetPlacesAsync();
            return places.Count;
        }

        public async Task SaveProfileAsync(string firstName, string lastName, DateTime? dateOfBirth)
        {
            if (string.IsNullOrWhiteSpace(firstName))
                throw new InvalidOperationException("First name is required.");

            if (string.IsNullOrWhiteSpace(lastName))
                throw new InvalidOperationException("Last name is required.");

            var profile = await EnsureLoadedAsync();
            profile.FirstName = firstName.Trim();
            profile.LastName = lastName.Trim();
            profile.DisplayName = AvatarHelper.BuildDisplayName(profile.FirstName, profile.LastName);
            profile.DateOfBirthIso = dateOfBirth?.ToString("yyyy-MM-dd") ?? string.Empty;
            profile.UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await _firestoreService.UpsertUserProfileAsync(profile);
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task UpdateProfilePhotoAsync(bool useCamera)
        {
            var profile = await EnsureLoadedAsync();
            FileResult? file = null;

            file = useCamera
                ? await MediaPicker.Default.CapturePhotoAsync()
                : await MediaPicker.Default.PickPhotoAsync();

            if (file == null)
                return;

            var localPath = GetLocalPhotoPath(profile.Uid);
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using (var source = await file.OpenReadAsync())
            await using (var target = File.Create(localPath))
            {
                await source.CopyToAsync(target);
            }

            profile.LocalPhotoPath = localPath;

            if (_firebaseStorageService.IsConfigured)
            {
                var upload = await _firebaseStorageService.UploadProfilePhotoAsync(
                    profile.Uid,
                    localPath,
                    file.ContentType ?? "image/jpeg",
                    profile.PhotoStoragePath);

                profile.PhotoUrl = upload.PhotoUrl;
                profile.PhotoStoragePath = upload.StoragePath;
            }

            profile.UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _firestoreService.UpsertUserProfileAsync(profile);
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task RemoveProfilePhotoAsync()
        {
            var profile = await EnsureLoadedAsync();

            if (File.Exists(profile.LocalPhotoPath))
                File.Delete(profile.LocalPhotoPath);

            if (!string.IsNullOrWhiteSpace(profile.PhotoStoragePath))
                await _firebaseStorageService.DeleteFileAsync(profile.PhotoStoragePath);

            profile.LocalPhotoPath = string.Empty;
            profile.PhotoUrl = string.Empty;
            profile.PhotoStoragePath = string.Empty;
            profile.UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await _firestoreService.UpsertUserProfileAsync(profile);
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task ChangePasswordAsync(string currentPassword, string newPassword)
        {
            await _accountApiService.ChangePasswordAsync(currentPassword, newPassword);
            await _authenticationService.LogoutAsync();
        }

        public async Task LogoutAsync()
        {
            _currentProfile = null;
            _loadedUserId = null;
            await _authenticationService.LogoutAsync();
        }

        public async Task DeleteAccountAsync(string currentPassword)
        {
            var user = RequireCurrentUser();
            var profile = await EnsureLoadedAsync();
            var remotePlaces = await _cloudPlaceRepository.DownloadPrivatePlacesAsync(user.Uid);
            var ownedPublicPlaces = (await _cloudPlaceRepository.DownloadPublicPlacesAsync())
                .Where(place => string.Equals(place.OwnerId, user.Uid, StringComparison.Ordinal))
                .ToList();

            foreach (var place in remotePlaces)
                await _cloudPlaceRepository.DeletePrivatePlaceAsync(user.Uid, place.Id, ignoreNotFound: true);

            foreach (var place in ownedPublicPlaces)
                await _cloudPlaceRepository.DeletePublicPlaceAsync(place.Id, ignoreNotFound: true);

            await _firestoreService.DeleteUserProfileAsync(user.Uid);

            if (!string.IsNullOrWhiteSpace(profile.PhotoStoragePath))
                await _firebaseStorageService.DeleteFileAsync(profile.PhotoStoragePath);

            if (File.Exists(profile.LocalPhotoPath))
                File.Delete(profile.LocalPhotoPath);

            await DeleteLocalPlacesAsync(user.Uid);
            await _accountApiService.DeleteCurrentAccountAsync(currentPassword);

            _currentProfile = null;
            _loadedUserId = null;
            await _authenticationService.LogoutAsync();
        }

        public string GetInitials(UserProfile? profile = null)
        {
            profile ??= _currentProfile;

            return AvatarHelper.GetInitials(
                profile?.FirstName,
                profile?.LastName,
                profile?.DisplayName,
                profile?.Email);
        }

        public string GetAvatarColor(UserProfile? profile = null)
        {
            profile ??= _currentProfile;

            var seed = profile?.Uid ??
                profile?.DisplayName ??
                profile?.Email ??
                "user";

            return AvatarHelper.GetAvatarColor(seed);
        }

        private async Task DeleteLocalPlacesAsync(string userId)
        {
            var localPlaces = await _databaseService.GetAllAsync();
            foreach (var place in localPlaces.Where(p => string.Equals(p.OwnerId, userId, StringComparison.Ordinal)))
                await _databaseService.DeleteAsync(place.Id);
        }

        private async Task<string> ResolveLocalPhotoPathAsync(UserProfile profile)
        {
            var localPath = GetLocalPhotoPath(profile.Uid);
            if (File.Exists(localPath))
                return localPath;

            if (!string.IsNullOrWhiteSpace(profile.PhotoStoragePath) && _firebaseStorageService.IsConfigured)
            {
                try
                {
                    await _firebaseStorageService.DownloadProfilePhotoAsync(profile.PhotoStoragePath, localPath);
                    return localPath;
                }
                catch
                {
                }
            }

            return string.Empty;
        }

        private static string GetLocalPhotoPath(string userId)
        {
            return Path.Combine(FileSystem.AppDataDirectory, "profile-images", $"{userId}.jpg");
        }

        private GeoSilenceUser RequireCurrentUser()
        {
            return _authenticationService.CurrentUser
                ?? throw new InvalidOperationException("A signed-in user is required.");
        }

        private void OnAuthStateChanged(object? sender, EventArgs e)
        {
            if (_authenticationService.IsSignedIn)
                return;

            _currentProfile = null;
            _loadedUserId = null;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
