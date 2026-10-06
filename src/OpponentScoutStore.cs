using System.Text.Json;

namespace Sm0kiSoloCoach;

public static class OpponentScoutStore
{
    private static readonly object Gate = new();

    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach");

    private static readonly string FilePath =
        Path.Combine(Dir, "opponent-scout.json");

    public static void Save(OpponentScoutReport report)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(
                    FilePath,
                    JsonSerializer.Serialize(
                        report,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
            }
            catch { }
        }
    }

    public static OpponentScoutReport? LoadLatest(
        string map,
        TimeSpan? maxAge = null)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;

                var report =
                    JsonSerializer.Deserialize<OpponentScoutReport>(
                        File.ReadAllText(FilePath));

                if (report == null)
                    return null;

                var ageLimit =
                    maxAge ?? TimeSpan.FromHours(8);

                if (DateTime.UtcNow - report.ScannedUtc > ageLimit)
                    return null;

                if (!string.IsNullOrWhiteSpace(map) &&
                    !report.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase))
                    return null;

                return report;
            }
            catch
            {
                return null;
            }
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }
}
