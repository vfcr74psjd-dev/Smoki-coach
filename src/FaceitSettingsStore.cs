using System.Security.Cryptography;
using System.Text;

namespace Sm0kiSoloCoach;

public static class FaceitSettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string KeyPath = Path.Combine(Dir, "faceit.key");
    private static readonly string NicknamePath = Path.Combine(Dir, "faceit.nickname");

    public static bool HasKey => !string.IsNullOrWhiteSpace(LoadKey());

    public static void SaveKey(string apiKey)
    {
        apiKey = (apiKey ?? "").Trim();
        if (apiKey.Length < 12)
            throw new ArgumentException("FACEIT API key ni veljaven.");

        Directory.CreateDirectory(Dir);
        var plain = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyPath, encrypted);
    }

    public static string? LoadKey()
    {
        try
        {
            if (!File.Exists(KeyPath)) return null;
            var encrypted = File.ReadAllBytes(KeyPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch { return null; }
    }

    public static void SaveNickname(string nickname)
    {
        nickname = (nickname ?? "").Trim();
        if (nickname.Length < 2)
            throw new ArgumentException("FACEIT nickname ni veljaven.");

        Directory.CreateDirectory(Dir);
        File.WriteAllText(NicknamePath, nickname, Encoding.UTF8);
    }

    public static string? LoadNickname()
    {
        try
        {
            if (!File.Exists(NicknamePath)) return null;
            var nickname = File.ReadAllText(NicknamePath, Encoding.UTF8).Trim();
            return string.IsNullOrWhiteSpace(nickname) ? null : nickname;
        }
        catch { return null; }
    }

    public static void ClearKey()
    {
        try { if (File.Exists(KeyPath)) File.Delete(KeyPath); } catch { }
    }
}
