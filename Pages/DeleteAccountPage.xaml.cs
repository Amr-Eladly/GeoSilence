namespace GeoSilence.Pages
{
    public partial class DeleteAccountPage : ContentPage
    {
        private readonly AccountProfileService _accountProfileService;

        public DeleteAccountPage(AccountProfileService accountProfileService)
        {
            InitializeComponent();
            _accountProfileService = accountProfileService;
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
            var currentPassword = CurrentPasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                await DisplayAlert("Delete Account", "Enter your current password to continue.", "OK");
                return;
            }

            var confirm = await DisplayAlert(
                "Delete Account?",
                "This action cannot be undone.",
                "Delete",
                "Cancel");

            if (!confirm)
                return;

            DeleteButton.IsEnabled = false;
            DeleteButton.Text = "Deleting...";

            try
            {
                await _accountProfileService.DeleteAccountAsync(currentPassword);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Delete Account", ex.Message, "OK");
            }
            finally
            {
                DeleteButton.IsEnabled = true;
                DeleteButton.Text = "Delete Permanently";
            }
        }
    }
}
