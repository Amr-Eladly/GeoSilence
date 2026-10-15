using System.Security.Cryptography;
using System.Text;

namespace GeoSilence.Utilities;

internal static class ProfilePhotoFiles
{
    public static string CachePath(string directory, string userId, string? storagePath)
    {
        var version = string.IsNullOrWhiteSpace(storagePath) ? string.Empty
            : "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storagePath)));
        return Path.Combine(directory, $"{userId}{version}.jpg");
    }

    public static async Task<string> CaptureAsync(Stream source, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"capture-{Guid.NewGuid():N}.jpg");
        try
        {
            await using var target = File.Create(path);
            await source.CopyToAsync(target);
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }
}
