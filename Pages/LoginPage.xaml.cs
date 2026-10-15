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
            if (!LoginButton.IsEnabled)
                return;

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

        private async void OnForgotPasswordClicked(object sender, EventArgs e)
        {
            if (!ForgotPasswordButton.IsEnabled) return;
            await SetBusyAsync(true, resettingPassword: true);
            try
            {
                var email = await DisplayPromptAsync("Reset password",
                    "Enter the email address you use for GeoSilence.",
                    accept: "Send reset email", cancel: "Cancel",
                    placeholder: "you@example.com", keyboard: Keyboard.Email,
                    initialValue: EmailEntry.Text?.Trim() ?? string.Empty);
                if (email == null) return;
                email = email.Trim();
                if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) ||
                    !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
                {
                    await DisplayAlert("Reset password", "Enter a valid email address.", "OK");
                    return;
                }

                EmailEntry.Text = email;
                ResetLoadingIndicator.IsVisible = ResetLoadingIndicator.IsRunning = true;
                ForgotPasswordButton.Text = "Sending...";
                await _authenticationService.SendPasswordResetEmailAsync(email);
                ResetLoadingIndicator.IsVisible = ResetLoadingIndicator.IsRunning = false;
                await DisplayAlert("Check your inbox",
                    "If an account exists for this email, you will receive a password reset link. Check your spam folder too. After resetting your password, return here to sign in.", "OK");
            }
            catch (OperationCanceledException)
            {
                await DisplayAlert("Reset password", "The request timed out. Please try again.", "OK");
            }
            catch (HttpRequestException)
            {
                await DisplayAlert("Reset password", "Couldn't connect. Check your connection and try again.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Reset password", ex.Message, "OK");
            }
            finally
            {
                ResetLoadingIndicator.IsVisible = ResetLoadingIndicator.IsRunning = false;
                ForgotPasswordButton.Text = "Forgot password?";
                await SetBusyAsync(false);
            }
        }

        private async void OnCreateAccountClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(RegisterPage));
        }

        private void OnEmailCompleted(object sender, EventArgs e) => PasswordEntry.Focus();

        private async Task SetBusyAsync(bool isBusy, bool resettingPassword = false)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoginButton.IsEnabled = !isBusy;
                LoginButton.Text = isBusy && !resettingPassword ? "Signing in..." : "Sign in";
                CreateAccountButton.IsEnabled = !isBusy;
                ForgotPasswordButton.IsEnabled = !isBusy;
            });
        }
    }
}
