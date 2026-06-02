using GeoSilence.Models;

namespace GeoSilence.Pages
{
    public partial class ProfilePage : ContentPage
    {
        private readonly AccountProfileService _accountProfileService;

        public ProfilePage(AccountProfileService accountProfileService)
        {
            InitializeComponent();
            _accountProfileService = accountProfileService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            var profile = await _accountProfileService.EnsureLoadedAsync();
            var locationsCount = await _accountProfileService.GetLocationsCountAsync();

            FirstNameEntry.Text = profile.FirstName;
            LastNameEntry.Text = profile.LastName;
            DateOfBirthEntry.Text = profile.DateOfBirthIso;
            EmailLabel.Text = $"Email Address: {profile.Email}";
            CreatedAtLabel.Text = $"Account Creation Date: {FormatDate(profile.CreatedAtUtcMs)}";
            LocationsCountLabel.Text = $"Locations Count: {locationsCount}";
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var firstName = FirstNameEntry.Text?.Trim() ?? string.Empty;
            var lastName = LastNameEntry.Text?.Trim() ?? string.Empty;
            var dateText = DateOfBirthEntry.Text?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(dateText) &&
                !DateTime.TryParse(dateText, out _))
            {
                await DisplayAlert("Profile", "Enter date of birth as YYYY-MM-DD.", "OK");
                return;
            }

            try
            {
                DateTime? dateOfBirth = string.IsNullOrWhiteSpace(dateText)
                    ? null
                    : DateTime.Parse(dateText);

                await _accountProfileService.SaveProfileAsync(firstName, lastName, dateOfBirth);
                await DisplayAlert("Profile", "Your profile was updated.", "OK");
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Profile", ex.Message, "OK");
            }
        }

        private static string FormatDate(long unixMs)
        {
            if (unixMs <= 0)
                return "Unavailable";

            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd");
        }
    }
}
