namespace GeoSilence
{
    public partial class App : Application
    {
        public App(IServiceProvider services)
        {
            InitializeComponent(); //  resources loaded FIRST

            MainPage = services.GetRequiredService<AppShell>(); // THEN resolve
        }
    }
}