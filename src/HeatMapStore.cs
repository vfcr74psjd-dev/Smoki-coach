using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class HeatMapMatchRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DemoFingerprint { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;
    public string PlayerNickname { get; set; } = "";
    public string Map { get; set; } = "";
    public List<DemoDeathPoint> Deaths { get; set; } = new();
}

public static class HeatMapStore
{
    private static readonly object Sync = new();
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string PathName = Path.Combine(Dir, "heatmap-history.json");

    public static List<HeatMapMatchRecord> Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(PathName))
                    return new List<HeatMapMatchRecord>();

                var json = File.ReadAllText(PathName, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<HeatMapMatchRecord>>(json)
                       ?? new List<HeatMapMatchRecord>();
            }
            catch
            {
                return new List<HeatMapMatchRecord>();
            }
        }
    }

    public static IReadOnlyList<HeatMapMatchRecord> LoadForPlayer(string nickname)
    {
        nickname = (nickname ?? "").Trim();
        return Load()
            .Where(x => x.PlayerNickname.Equals(nickname, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ImportedUtc)
            .ToList();
    }

    public static bool ContainsFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;

        return Load().Any(x =>
            x.DemoFingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));
    }

    public static HeatMapMatchRecord? AddFromResult(
        string sourcePath,
        string playerNickname,
        DemoReviewResult result)
    {
        if (result.Deaths.Count == 0 || string.IsNullOrWhiteSpace(result.Map))
            return null;

        var fingerprint = FingerprintFile(sourcePath);

        lock (Sync)
        {
            var all = LoadUnlocked();
            var existing = all.FirstOrDefault(x =>
                x.DemoFingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            var record = new HeatMapMatchRecord
            {
                DemoFingerprint = fingerprint,
                SourceFileName = System.IO.Path.GetFileName(sourcePath),
                ImportedUtc = DateTime.UtcNow,
                PlayerNickname = playerNickname.Trim(),
                Map = result.Map,
                Deaths = result.Deaths.ToList()
            };

            all.Insert(0, record);
            if (all.Count > 100)
                all = all.Take(100).ToList();

            SaveUnlocked(all);
            return record;
        }
    }

    public static string FingerprintFile(string path)
    {
        var info = new FileInfo(path);
        using var sha = SHA256.Create();
        using var ms = new MemoryStream();

        var lengthBytes = BitConverter.GetBytes(info.Length);
        ms.Write(lengthBytes, 0, lengthBytes.Length);

        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[256 * 1024];

        int read = fs.Read(buffer, 0, buffer.Length);
        if (read > 0)
            ms.Write(buffer, 0, read);

        if (fs.Length > buffer.Length)
        {
            fs.Seek(Math.Max(0, fs.Length - buffer.Length), SeekOrigin.Begin);
            read = fs.Read(buffer, 0, buffer.Length);
            if (read > 0)
                ms.Write(buffer, 0, read);
        }

        ms.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(ms));
    }

    private static List<HeatMapMatchRecord> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(PathName))
                return new List<HeatMapMatchRecord>();

            var json = File.ReadAllText(PathName, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<HeatMapMatchRecord>>(json)
                   ?? new List<HeatMapMatchRecord>();
        }
        catch
        {
            return new List<HeatMapMatchRecord>();
        }
    }

    private static void SaveUnlocked(List<HeatMapMatchRecord> items)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(PathName, json, Encoding.UTF8);
    }
}
