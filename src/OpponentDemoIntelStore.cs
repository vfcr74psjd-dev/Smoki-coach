using System.Text.Json;

namespace Sm0kiSoloCoach;

public static class OpponentDemoIntelStore
{
    private static readonly object Gate = new();
    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach");
    private static readonly string FilePath =
        Path.Combine(Dir, "opponent-demo-intel.json");

    public static void Save(OpponentDemoIntelReport report)
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

    public static OpponentDemoIntelReport? LoadLatest(
        string map,
        TimeSpan? maxAge = null)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;

                var result =
                    JsonSerializer.Deserialize<OpponentDemoIntelReport>(
                        File.ReadAllText(FilePath));

                if (result == null)
                    return null;

                if (DateTime.UtcNow - result.ScannedUtc >
                    (maxAge ?? TimeSpan.FromHours(8)))
                    return null;

                if (!string.IsNullOrWhiteSpace(map) &&
                    !result.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase))
                    return null;

                return result;
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
