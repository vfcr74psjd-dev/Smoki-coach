using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class CoachPreferences
{
    public string Mode { get; set; } = "Balanced";
    public string Role { get; set; } = "Flex";
    public string Focus { get; set; } = "More kills";
    public bool AutoAi { get; set; } = true;
    public string OverlayMode { get; set; } = "Off";
    public bool OverlayCustomPlacement { get; set; }
    public int OverlayX { get; set; } = -1;
    public int OverlayY { get; set; } = -1;
    public float OverlayScale { get; set; } = 1.0f;
}

public static class UserSettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string FilePath = Path.Combine(Dir, "coach-settings.json");

    public static CoachPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new CoachPreferences();
            return JsonSerializer.Deserialize<CoachPreferences>(File.ReadAllText(FilePath))
                   ?? new CoachPreferences();
        }
        catch { return new CoachPreferences(); }
    }

    public static void Save(CoachPreferences p)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
