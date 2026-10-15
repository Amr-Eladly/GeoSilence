namespace GeoSilence.Pages;

public partial class StartupPage : ContentPage
{
    public event EventHandler? RetryRequested;
    public StartupPage() => InitializeComponent();
    public void ShowLoading()
    {
        LoadingIndicator.IsRunning = LoadingIndicator.IsVisible = true;
        StatusLabel.Text = "Getting things ready?";
        RetryButton.IsVisible = false;
    }
    public void ShowError()
    {
        LoadingIndicator.IsRunning = LoadingIndicator.IsVisible = false;
        StatusLabel.Text = "We couldn't prepare your pages. Please try again.";
        RetryButton.IsVisible = true;
    }
    private void OnRetryClicked(object sender, EventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);
}
