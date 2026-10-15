using GeoSilence.Models;

namespace GeoSilence.Pages
{
    public partial class AccountPage : ContentPage
    {
        private readonly AccountProfileService _accountProfileService;
        private Task? _loadTask;
        private string? _renderedPhotoPath;
        private bool _updatingPhoto;

        public AccountPage(AccountProfileService accountProfileService)
        {
            InitializeComponent();
            _accountProfileService = accountProfileService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _accountProfileService.ProfileChanged += OnProfileChanged;
            try { await LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ACCOUNT LOAD: {ex}"); }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _accountProfileService.ProfileChanged -= OnProfileChanged;
        }

        private Task LoadAsync()
        {
            if (_loadTask is { IsCompleted: false })
                return _loadTask;
            return _loadTask = LoadCoreAsync();
        }

        private async Task LoadCoreAsync()
        {
            var profile = await _accountProfileService.EnsureLoadedAsync();

            DisplayNameLabel.Text = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Your Account" : profile.DisplayName;
            EmailLabel.Text = profile.Email;
            RenderAvatar(profile);
        }

        private void RenderAvatar(UserProfile profile)
        {
            var localPhotoPath = profile.LocalPhotoPath;
            var hasPhoto = !string.IsNullOrWhiteSpace(localPhotoPath) && File.Exists(localPhotoPath);

            AvatarBorder.Background = Color.FromArgb(_accountProfileService.GetAvatarColor(profile));
            AvatarImage.IsVisible = hasPhoto;
            AvatarInitialsLabel.IsVisible = !hasPhoto;

            if (hasPhoto)
            {
                if (_renderedPhotoPath != localPhotoPath)
                    AvatarImage.Source = ImageSource.FromStream(() => File.OpenRead(localPhotoPath));
                _renderedPhotoPath = localPhotoPath;
                AvatarInitialsLabel.Text = string.Empty;
            }
            else
            {
                AvatarImage.Source = null;
                _renderedPhotoPath = null;
                AvatarInitialsLabel.Text = _accountProfileService.GetInitials(profile);
            }
        }

        private async void OnEditPhotoClicked(object sender, EventArgs e)
        {
            if (_updatingPhoto) return;
            var action = await DisplayActionSheet(
                "Profile Photo",
                "Cancel",
                null,
                "Take Photo",
                "Choose From Gallery",
                "Remove Photo");

            if (action == "Cancel" || string.IsNullOrWhiteSpace(action))
                return;

            if (action == "Take Photo") await ChangePhotoAsync(() => _accountProfileService.UpdateProfilePhotoAsync(true));
            else if (action == "Choose From Gallery") await ChangePhotoAsync(() => _accountProfileService.UpdateProfilePhotoAsync(false));
            else if (action == "Remove Photo") await ChangePhotoAsync(_accountProfileService.RemoveProfilePhotoAsync);
        }

        private async void OnTakePhotoClicked(object sender, EventArgs e) =>
            await ChangePhotoAsync(() => _accountProfileService.UpdateProfilePhotoAsync(true));

        private async Task ChangePhotoAsync(Func<Task> update)
        {
            if (_updatingPhoto) return;
            _updatingPhoto = true;
            TakePhotoButton.IsEnabled = PhotoOptionsButton.IsEnabled = false;
            PhotoActivity.IsVisible = PhotoActivity.IsRunning = true;
            try
            {
                await update();
                // Also refresh local-only photos when cloud storage is unavailable.
                _renderedPhotoPath = null;
                AvatarImage.Source = null;
                await LoadAsync();
            }
            catch (PermissionException)
            {
                await DisplayAlert("Camera permission", "Allow camera access in your device settings to take a profile photo.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Profile Photo", ex.Message, "OK");
            }
            finally
            {
                PhotoActivity.IsVisible = PhotoActivity.IsRunning = false;
                TakePhotoButton.IsEnabled = PhotoOptionsButton.IsEnabled = true;
                _updatingPhoto = false;
            }
        }

        private async void OnProfileTapped(object sender, TappedEventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(ProfilePage));
        }

        private async void OnChangePasswordTapped(object sender, TappedEventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(ChangePasswordPage));
        }

        private async void OnLoginOptionsTapped(object sender, TappedEventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(LoginOptionsPage));
        }

        private async void OnLogoutTapped(object sender, TappedEventArgs e)
        {
            var confirm = await DisplayAlert("Logout", "Sign out of GeoSilence now?", "Logout", "Cancel");
            if (!confirm)
                return;

            await _accountProfileService.LogoutAsync();
        }

        private async void OnDeleteAccountTapped(object sender, TappedEventArgs e)
        {
            var confirm = await DisplayAlert(
                "Delete Account?",
                "This action is permanent.\n\nThe following will be removed:\nAccount\nProfile\nPlaces\nCloud backups",
                "Delete",
                "Cancel");

            if (!confirm)
                return;

            await Shell.Current.GoToAsync(nameof(DeleteAccountPage));
        }

        private async void OnProfileChanged(object? sender, EventArgs e)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // A photo update may overwrite the same local filename.
                    _renderedPhotoPath = null;
                    return LoadAsync();
                });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ACCOUNT LOAD: {ex}"); }
        }
    }
}
