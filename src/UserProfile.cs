using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class UserProfile
{
    public string Nickname { get; set; } = "";
    public bool SetupComplete { get; set; }
}

public static class UserProfileStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string FilePath = Path.Combine(Dir, "profile.json");

    public static UserProfile Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UserProfile();
            return JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(FilePath))
                   ?? new UserProfile();
        }
        catch { return new UserProfile(); }
    }

    public static void Save(UserProfile profile)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }
}
