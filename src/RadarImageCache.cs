using System.Security.Cryptography;
using System.Text;

namespace Sm0kiSoloCoach;

public static class RadarImageCache
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly string CacheDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach",
            "RadarCache");

    public static async Task<byte[]> GetAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Radar URL is empty.", nameof(url));

        Directory.CreateDirectory(CacheDir);

        var file = Path.Combine(CacheDir, FileNameFor(url));
        try
        {
            if (File.Exists(file))
            {
                var info = new FileInfo(file);
                if (info.Length > 1024)
                    return await File.ReadAllBytesAsync(file, cancellationToken);
            }
        }
        catch { }

        var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
        if (bytes.Length > 1024)
        {
            try
            {
                var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                File.Move(temp, file, overwrite: true);
            }
            catch
            {
                // Caching is best effort. The downloaded image can still be used.
            }
        }

        return bytes;
    }

    private static string FileNameFor(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash) + ".png";
    }
}
