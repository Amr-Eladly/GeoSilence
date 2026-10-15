using System.Text;
using System.Threading.Channels;
using Android.Content;

namespace GeoSilence.Platforms.Droid
{
    // Logcat is immediate. A single bounded worker owns file I/O and rotation,
    // so slow external storage cannot stall service callbacks or UI gestures.
    public static class GeoLog
    {
        private const string FileName = "geofence.log";
        private const long MaxBytes = 256 * 1024;
        private static readonly Channel<string> Lines = Channel.CreateBounded<string>(
            new BoundedChannelOptions(512)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });
        private static Context? _context;
        private static readonly Task Writer = Task.Run(WriteLinesAsync);

        public static void Init(Context context)
        {
            if (Interlocked.CompareExchange(ref _context, context.ApplicationContext ?? context, null) == null)
                Write("INIT", "GeoLog file writer initialized");
        }

        public static void Write(string tag, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}";
            try { Android.Util.Log.Info("GeoSilence", line); }
            catch { }
            Lines.Writer.TryWrite(line);
        }

        private static async Task WriteLinesAsync()
        {
            string? path = null;
            await foreach (var line in Lines.Reader.ReadAllAsync())
            {
                try
                {
                    var context = Volatile.Read(ref _context);
                    if (path == null && context != null)
                    {
                        var dir = context.GetExternalFilesDir(null)?.AbsolutePath
                                  ?? context.FilesDir?.AbsolutePath;
                        if (dir != null) path = Path.Combine(dir, FileName);
                    }
                    if (path == null) continue;
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        var backup = path + ".1";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(path, backup);
                    }
                    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Diagnostics must never interrupt geofencing or the UI.
                }
            }
        }

        public static void Error(string tag, Exception ex) =>
            Write(tag, $"EXCEPTION {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
