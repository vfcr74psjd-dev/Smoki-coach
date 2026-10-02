using System.Security.Cryptography;
using System.Text;

namespace Sm0kiSoloCoach;

public static class AiSettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string KeyPath = Path.Combine(Dir, "openai.key");

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(LoadApiKey());

    public static void SaveApiKey(string apiKey)
    {
        apiKey = (apiKey ?? "").Trim();
        if (apiKey.Length < 12)
            throw new ArgumentException("API key ni veljaven.");

        Directory.CreateDirectory(Dir);
        var plain = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyPath, encrypted);
    }

    public static string? LoadApiKey()
    {
        try
        {
            if (!File.Exists(KeyPath)) return null;
            var encrypted = File.ReadAllBytes(KeyPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearApiKey()
    {
        try
        {
            if (File.Exists(KeyPath)) File.Delete(KeyPath);
        }
        catch { }
    }
}
