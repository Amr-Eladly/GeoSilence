using GeoSilence.Models;

namespace GeoSilence.Pages
{
    public partial class AccountPage : ContentPage
    {
        private readonly AccountProfileService _accountProfileService;

        public AccountPage(AccountProfileService accountProfileService)
        {
            InitializeComponent();
            _accountProfileService = accountProfileService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _accountProfileService.ProfileChanged += OnProfileChanged;
            await LoadAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _accountProfileService.ProfileChanged -= OnProfileChanged;
        }

        private async Task LoadAsync()
        {
            var profile = await _accountProfileService.EnsureLoadedAsync();
            var locationsCount = await _accountProfileService.GetLocationsCountAsync();

            DisplayNameLabel.Text = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Your Account" : profile.DisplayName;
            EmailLabel.Text = profile.Email;
            LocationsCountLabel.Text = $"Locations Saved: {locationsCount}";
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
                AvatarImage.Source = ImageSource.FromFile(localPhotoPath);
                AvatarInitialsLabel.Text = string.Empty;
            }
            else
            {
                AvatarImage.Source = null;
                AvatarInitialsLabel.Text = _accountProfileService.GetInitials(profile);
            }
        }

        private async void OnEditPhotoClicked(object sender, EventArgs e)
        {
            var action = await DisplayActionSheet(
                "Profile Photo",
                "Cancel",
                null,
                "Take Photo",
                "Choose From Gallery",
                "Remove Photo");

            if (action == "Cancel" || string.IsNullOrWhiteSpace(action))
                return;

            try
            {
                switch (action)
                {
                    case "Take Photo":
                        await _accountProfileService.UpdateProfilePhotoAsync(useCamera: true);
                        break;
                    case "Choose From Gallery":
                        await _accountProfileService.UpdateProfilePhotoAsync(useCamera: false);
                        break;
                    case "Remove Photo":
                        await _accountProfileService.RemoveProfilePhotoAsync();
                        break;
                }

                await LoadAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Profile Photo", ex.Message, "OK");
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
            await MainThread.InvokeOnMainThreadAsync(LoadAsync);
        }
    }
}
