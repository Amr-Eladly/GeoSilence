using GeoSilence.Services;

namespace GeoSilence.Pages
{
    public partial class RegisterPage : ContentPage
    {
        private readonly IAuthenticationService _authenticationService;

        public RegisterPage(IAuthenticationService authenticationService)
        {
            InitializeComponent();
            _authenticationService = authenticationService;
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            var displayName = DisplayNameEntry.Text?.Trim() ?? string.Empty;
            var email = EmailEntry.Text?.Trim() ?? string.Empty;
            var password = PasswordEntry.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(displayName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlert("Register", "Enter your display name, email, and password.", "OK");
                return;
            }

            await SetBusyAsync(true);

            try
            {
                await _authenticationService.RegisterAsync(email, password, displayName);
                await Shell.Current.GoToAsync("//main");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Registration failed", ex.Message, "OK");
            }
            finally
            {
                await SetBusyAsync(false);
            }
        }

        private async Task SetBusyAsync(bool isBusy)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                RegisterButton.IsEnabled = !isBusy;
                RegisterButton.Text = isBusy ? "Creating..." : "Register";
            });
        }
    }
}
