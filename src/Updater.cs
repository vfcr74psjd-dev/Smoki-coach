using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Sm0kiSoloCoach;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
}

public static class AppUpdater
{
    public static string CurrentVersion => typeof(AppUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private const string ManifestUrl = "https://raw.githubusercontent.com/vfcr74psjd-dev/Smoki-coach/main/update.json";

    public static async Task<UpdateManifest?> CheckAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Sm0kiSoloCoach/" + CurrentVersion);
        return await http.GetFromJsonAsync<UpdateManifest>(ManifestUrl);
    }

    public static bool IsNewer(string? remote)
    {
        if (!Version.TryParse(remote, out var r)) return false;
        if (!Version.TryParse(CurrentVersion, out var c)) return false;
        return r > c;
    }

    public static async Task DownloadAndInstallAsync(UpdateManifest manifest)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Sm0kiSoloCoach/" + CurrentVersion);

        var bytes = await http.GetByteArrayAsync(manifest.Url);
        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update SHA256 preverjanje ni uspelo.");
        }

        var currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
        var tempExe = Path.Combine(Path.GetTempPath(), "Sm0kiSoloCoach.update.exe");
        File.WriteAllBytes(tempExe, bytes);

        var script = Path.Combine(Path.GetTempPath(), "Sm0kiSoloCoach_update.cmd");
        var pid = Environment.ProcessId;
        var cmd = $"@echo off\r\n" +
                  $":wait\r\n" +
                  $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul\r\n" +
                  $"if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                  $"copy /Y \"{tempExe}\" \"{currentExe}\" >nul\r\n" +
                  $"start \"\" \"{currentExe}\"\r\n" +
                  $"del /Q \"{tempExe}\"\r\n" +
                  $"del /Q \"%~f0\"\r\n";
        File.WriteAllText(script, cmd);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Exit();
    }
}
