using GeoSilence.Services;

namespace GeoSilence.Pages
{
    public partial class LoginPage : ContentPage
    {
        private readonly IAuthenticationService _authenticationService;

        public LoginPage(IAuthenticationService authenticationService)
        {
            InitializeComponent();
            _authenticationService = authenticationService;
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            var email = EmailEntry.Text?.Trim() ?? string.Empty;
            var password = PasswordEntry.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlert("Login", "Enter your email and password.", "OK");
                return;
            }

            await SetBusyAsync(true);

            try
            {
                await _authenticationService.LoginAsync(email, password);
                await Shell.Current.GoToAsync("//main");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Login failed", ex.Message, "OK");
            }
            finally
            {
                await SetBusyAsync(false);
            }
        }

        private async void OnCreateAccountClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(RegisterPage));
        }

        private async Task SetBusyAsync(bool isBusy)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoginButton.IsEnabled = !isBusy;
                LoginButton.Text = isBusy ? "Logging in..." : "Login";
            });
        }
    }
}
