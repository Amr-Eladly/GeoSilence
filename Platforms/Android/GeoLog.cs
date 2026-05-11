using System.Text;
using Android.Content;

namespace GeoSilence.Platforms.Droid
{
    // File-based logger that survives process death so background geofence
    // events (registration, transitions, errors) can be inspected after the
    // app is killed. Path: /Android/data/<package>/files/geofence.log
    public static class GeoLog
    {
        private const string FileName = "geofence.log";
        private const long MaxBytes = 256 * 1024;
        private static readonly object Sync = new();
        private static string? _path;

        public static void Init(Context context)
        {
            try
            {
                var dir = context.GetExternalFilesDir(null)?.AbsolutePath
                          ?? context.FilesDir?.AbsolutePath;

                if (dir == null)
                    return;

                _path = Path.Combine(dir, FileName);
                Write("INIT", $"GeoLog initialized at {_path}");
            }
            catch
            {
            }
        }

        public static void Write(string tag, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}";

            try
            {
                Android.Util.Log.Info("GeoSilence", line);
            }
            catch
            {
            }

            if (_path == null)
                return;

            lock (Sync)
            {
                try
                {
                    if (File.Exists(_path) &&
                        new FileInfo(_path).Length > MaxBytes)
                    {
                        var backup = _path + ".1";
                        if (File.Exists(backup))
                            File.Delete(backup);
                        File.Move(_path, backup);
                    }

                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                }
            }
        }

        public static void Error(string tag, Exception ex) =>
            Write(tag, $"EXCEPTION {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
