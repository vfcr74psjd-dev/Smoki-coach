using System.Text;

namespace Sm0kiSoloCoach;

public static class GsiInstaller
{
    public const string Token = "sm0ki72_native_csharp_v1";
    public static int Port { get; set; } = 31972;

    public static string ConfigText =>
        "\"Sm0ki Solo Coach C#\"\n" +
        "{\n" +
        $" \"uri\" \"http://127.0.0.1:{Port}/gsi\"\n" +
        " \"timeout\" \"5.0\"\n" +
        " \"buffer\" \"0.1\"\n" +
        " \"throttle\" \"0.1\"\n" +
        " \"heartbeat\" \"10.0\"\n" +
        $" \"auth\" {{ \"token\" \"{Token}\" }}\n" +
        " \"data\"\n" +
        " {\n" +
        "  \"provider\" \"1\"\n" +
        "  \"map\" \"1\"\n" +
        "  \"map_round_wins\" \"1\"\n" +
        "  \"round\" \"1\"\n" +
        "  \"player_id\" \"1\"\n" +
        "  \"player_state\" \"1\"\n" +
        "  \"player_weapons\" \"1\"\n" +
        "  \"player_match_stats\" \"1\"\n" +
        "  \"player_position\" \"1\"\n" +
        "  \"phase_countdowns\" \"1\"\n" +
        "  \"bomb\" \"1\"\n" +
        " }\n" +
        " \"output\"\n" +
        " {\n" +
        "  \"precision_position\" \"2\"\n" +
        "  \"precision_vector\" \"3\"\n" +
        " }\n" +
        "}\n";

    public static string? FindConfigDirectory()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var candidates = new[]
        {
            Path.Combine(pf86, @"Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg"),
            Path.Combine(pf, @"Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg"),
            @"C:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg",
            @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg",
            @"E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg",
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    public static string? TryInstall()
    {
        var dir = FindConfigDirectory();
        if (string.IsNullOrWhiteSpace(dir))
            return null;

        try
        {
            var path = Path.Combine(dir, "gamestate_integration_sm0ki72.cfg");
            File.WriteAllText(path, ConfigText, Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static string? TryEnableChatLog()
    {
        var cfgDir = FindConfigDirectory();
        if (string.IsNullOrWhiteSpace(cfgDir))
            return null;

        try
        {
            var coachCfg = Path.Combine(cfgDir, "sm0ki_coach_chat.cfg");
            File.WriteAllText(
                coachCfg,
                "con_logfile \"console.log\"\n",
                Encoding.UTF8);

            var autoexec = Path.Combine(cfgDir, "autoexec.cfg");
            var current = File.Exists(autoexec)
                ? File.ReadAllText(autoexec, Encoding.UTF8)
                : "";

            const string marker = "exec sm0ki_coach_chat.cfg";
            if (!current.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                var prefix = current.Length == 0 || current.EndsWith("\n") ? "" : "\n";
                File.AppendAllText(
                    autoexec,
                    prefix +
                    "\n// Sm0ki Solo Coach: local chat intent log\n" +
                    marker + "\n",
                    Encoding.UTF8);
            }

            var csgoDir = Directory.GetParent(cfgDir)?.FullName;
            return string.IsNullOrWhiteSpace(csgoDir)
                ? null
                : Path.Combine(csgoDir, "console.log");
        }
        catch
        {
            return null;
        }
    }
}
