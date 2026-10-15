using GeoSilence.Controls;
using GeoSilence.PageModels;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Font = Microsoft.Maui.Font;

namespace GeoSilence
{
    public partial class AppShell : Shell
    {
        private readonly IAuthenticationService _authenticationService;
        private bool _initialized;
        private readonly IServiceProvider _services;
        private readonly SemaphoreSlim _sessionRouteGate = new(1, 1);
        private int _sessionRouteVersion;
        private bool _preparingPages;
        private string? _preparedUserId;
        private StartupPage? _startupPage;

        public AppShell(IAuthenticationService authenticationService, IServiceProvider services)
        {
            _authenticationService = authenticationService;
            _services = services;
            InitializeComponent();
            Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));


            Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));
            Routing.RegisterRoute(nameof(LoginOptionsPage), typeof(LoginOptionsPage));
            Routing.RegisterRoute(nameof(ChangePasswordPage), typeof(ChangePasswordPage));
            Routing.RegisterRoute(nameof(DeleteAccountPage), typeof(DeleteAccountPage));
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

            await RouteForSessionAsync(restoreSession: true);
        }

        private async void OnAuthStateChanged(object? sender, EventArgs e) =>
            await MainThread.InvokeOnMainThreadAsync(() => RouteForSessionAsync());

        protected override bool OnBackButtonPressed() => _preparingPages || base.OnBackButtonPressed();

        private static T GetPage<T>(ShellContent content) where T : ContentPage
        {
            // Shell templates keep their own page cache. Resolve that exact instance;
            // assigning Content while ContentTemplate is set creates a second, unused page.
            return (T)((IShellContentController)content).GetOrCreateContent();
        }

        private StartupPage GetStartupPage()
        {
            var page = GetPage<StartupPage>(StartupContent);
            if (!ReferenceEquals(_startupPage, page))
            {
                _startupPage = page;
                page.RetryRequested += async (_, _) => await RouteForSessionAsync();
            }
            return page;
        }

        private static async Task WaitForFirstLayoutAsync(ContentPage page)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!HasVisibleLayout(page) && timer.ElapsedMilliseconds < 4000)
                await Task.Delay(25);
            if (!HasVisibleLayout(page))
                throw new TimeoutException($"{page.GetType().Name} did not finish its initial layout.");
            // Native Shell fragments can commit after GoToAsync returns.
            // Keep the window cover until the destination has rendered.
            await Task.Delay(150);
        }

        private static bool HasVisibleLayout(ContentPage page)
        {
            if (page.Content?.IsLoaded != true || page.Content.Width <= 0 || page.Content.Height <= 0)
                return false;
#if ANDROID
            return page.Handler?.PlatformView is Android.Views.View view && view.IsAttachedToWindow && view.IsShown;
#else
            return true;
#endif
        }

        private async Task RouteForSessionAsync(bool restoreSession = false)
        {
            var version = ++_sessionRouteVersion;
            await _sessionRouteGate.WaitAsync();
            PreparationScreen? loadingScreen = null;
            try
            {
                if (version != _sessionRouteVersion) return;
                _preparingPages = true;
                if (restoreSession) await _authenticationService.InitializeAsync();
                var userId = _authenticationService.CurrentUser?.Uid;
                if (userId == null)
                {
                    _preparedUserId = null;
                    await GoToAsync("//login", false);
                    return;
                }
                if (_preparedUserId == userId)
                {
                    await GoToAsync("//home/map/main", false);
                    return;
                }

                loadingScreen = PreparationScreen.TryShow();
                var startup = GetStartupPage();
                startup.ShowLoading();
                await GoToAsync("//startup", false);
                await WaitForFirstLayoutAsync(startup);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                await _services.GetRequiredService<HomeViewModel>().InitializeAsync();
                if (!SessionIsCurrent()) return;

                // Only Map is required before dismissing the startup cover. Shell
                // creates and caches the other tab pages when the user opens them.
                // HomeViewModel already refreshes cloud data asynchronously.
                var map = GetPage<Pages.MainPage>(MapContent);
                await GoToAsync("//home/map/main", false);
                await WaitForFirstLayoutAsync(map);
                if (!SessionIsCurrent()) return;

                _preparedUserId = userId;
                System.Diagnostics.Debug.WriteLine($"PAGE PREPARATION completed: {timer.ElapsedMilliseconds} ms");

                bool SessionIsCurrent() => version == _sessionRouteVersion &&
                    _authenticationService.CurrentUser?.Uid == userId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PAGE PREPARATION ERROR: {ex}");
                if (version == _sessionRouteVersion)
                {
                    var startup = GetStartupPage();
                    startup.ShowError();
                    await GoToAsync("//startup", false);
                }
            }
            finally
            {
                loadingScreen?.Dispose();
                _preparingPages = false;
                _sessionRouteGate.Release();
            }
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
