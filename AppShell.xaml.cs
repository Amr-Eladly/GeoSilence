using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Font = Microsoft.Maui.Font;

namespace GeoSilence
{
    public partial class AppShell : Shell
    {
        private readonly IAuthenticationService _authenticationService;
        private bool _initialized;

        public AppShell(IAuthenticationService authenticationService)
        {
            _authenticationService = authenticationService;
            InitializeComponent();
            Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
            _authenticationService.AuthStateChanged += OnAuthStateChanged;

            var currentTheme = Application.Current!.RequestedTheme;
            //ThemeSegmentedControl.SelectedIndex = currentTheme == AppTheme.Light ? 0 : 1;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_initialized)
                return;

            _initialized = true;

            await _authenticationService.InitializeAsync();
            await GoToAsync(_authenticationService.IsSignedIn ? "//main" : "//login");
        }

        private async void OnAuthStateChanged(object? sender, EventArgs e)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await GoToAsync(_authenticationService.IsSignedIn ? "//main" : "//login");
            });
        }

        public static async Task DisplaySnackbarAsync(string message)
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            var snackbarOptions = new SnackbarOptions
            {
                BackgroundColor = Color.FromArgb("#FF3300"),
                TextColor = Colors.White,
                ActionButtonTextColor = Colors.Yellow,
                CornerRadius = new CornerRadius(0),
                Font = Font.SystemFontOfSize(18),
                ActionButtonFont = Font.SystemFontOfSize(14)
            };

            var snackbar = Snackbar.Make(message, visualOptions: snackbarOptions);

            await snackbar.Show(cancellationTokenSource.Token);
        }

        public static async Task DisplayToastAsync(string message)
        {
            // Toast is currently not working in MCT on Windows
            if (OperatingSystem.IsWindows())
                return;

            var toast = Toast.Make(message, textSize: 18);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await toast.Show(cts.Token);
        }

        //private void SfSegmentedControl_SelectionChanged(object sender, Syncfusion.Maui.Toolkit.SegmentedControl.SelectionChangedEventArgs e)
        //{
        //    Application.Current!.UserAppTheme = e.NewIndex == 0 ? AppTheme.Light : AppTheme.Dark;
        //}
    }
}
