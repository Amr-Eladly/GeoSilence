namespace GeoSilence.Pages
{
    public partial class ChangePasswordPage : ContentPage
    {
        private readonly AccountProfileService _accountProfileService;

        public ChangePasswordPage(AccountProfileService accountProfileService)
        {
            InitializeComponent();
            _accountProfileService = accountProfileService;
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var currentPassword = CurrentPasswordEntry.Text ?? string.Empty;
            var newPassword = NewPasswordEntry.Text ?? string.Empty;
            var confirmPassword = ConfirmPasswordEntry.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                await DisplayAlert("Password", "Fill in every password field.", "OK");
                return;
            }

            if (newPassword.Length < 6)
            {
                await DisplayAlert("Password", "New password must be at least 6 characters.", "OK");
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                await DisplayAlert("Password", "New password and confirmation must match.", "OK");
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Text = "Updating...";

            try
            {
                await _accountProfileService.ChangePasswordAsync(currentPassword, newPassword);
                await DisplayAlert("Password", "Password updated. Please sign in again.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Password", ex.Message, "OK");
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Text = "Update Password";
            }
        }
    }
}
