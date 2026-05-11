using Android.App;
using Android.Runtime;
using GeoSilence.Platforms.Droid;

namespace GeoSilence
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            // Initialize regardless of whether process was started by user
            // tap, BOOT_COMPLETED, or geofence broadcast wake.
            GeoLog.Init(this);
            GeoLog.Write("APP", $"MainApplication.OnCreate pid={Android.OS.Process.MyPid()}");

            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch (Exception ex)
            {
                GeoLog.Error("APP-SQLITE", ex);
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
