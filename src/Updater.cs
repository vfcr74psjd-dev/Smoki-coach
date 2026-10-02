using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Sm0kiSoloCoach;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string SetupUrl { get; set; } = "";
    public string SetupSha256 { get; set; } = "";
    public string Notes { get; set; } = "";
}

public static class AppUpdater
{
    public static string CurrentVersion =>
        typeof(AppUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private const string ManifestUrl =
        "https://raw.githubusercontent.com/vfcr74psjd-dev/Smoki-coach/main/update.json";

    public static async Task<UpdateManifest?> CheckAsync()
    {
        using var http = CreateHttp(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };

        var url = ManifestUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await http.GetFromJsonAsync<UpdateManifest>(url);
    }

    public static bool IsNewer(string? remote)
    {
        if (!Version.TryParse(remote, out var r)) return false;
        if (!Version.TryParse(CurrentVersion, out var c)) return false;
        return r > c;
    }

    public static async Task DownloadAndInstallAsync(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("Update manifest nima verzije.");

        // Preferred path: use the signed/packaged installer instead of trying
        // to overwrite the EXE that is currently running.
        if (!string.IsNullOrWhiteSpace(manifest.SetupUrl))
        {
            await InstallViaSetupAsync(manifest);
            return;
        }

        // Backward-compatible fallback for old manifests.
        await InstallPortableFallbackAsync(manifest);
    }

    private static async Task InstallViaSetupAsync(UpdateManifest manifest)
    {
        var token = Guid.NewGuid().ToString("N");
        var tempSetup = Path.Combine(
            Path.GetTempPath(),
            $"Sm0kiSoloCoach-Setup-{manifest.Version}-{token}.exe");
        var helper = Path.Combine(
            Path.GetTempPath(),
            $"Sm0kiSoloCoach-update-{token}.cmd");
        var log = Path.Combine(
            Path.GetTempPath(),
            $"Sm0kiSoloCoach-update-{manifest.Version}.log");

        try
        {
            var bytes = await DownloadAsync(manifest.SetupUrl, "installer");
            VerifySha(bytes, manifest.SetupSha256, "installer");
            await File.WriteAllBytesAsync(tempSetup, bytes);

            if (!File.Exists(tempSetup) || new FileInfo(tempSetup).Length == 0)
                throw new IOException("Preneseni installer ni bil pravilno shranjen.");

            var pid = Environment.ProcessId;
            var installedExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Sm0kiSoloCoach", "Sm0kiSoloCoach.exe");

            var cmd =
                "@echo off\r\n" +
                "setlocal\r\n" +
                $":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL\r\n" +
                "if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
                $"start \"\" /wait \"{tempSetup}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LOG=\"{log}\"\r\n" +
                "set EXITCODE=%ERRORLEVEL%\r\n" +
                "if not \"%EXITCODE%\"==\"0\" goto failed\r\n" +
                $"if exist \"{installedExe}\" start \"\" \"{installedExe}\"\r\n" +
                $"del /Q \"{tempSetup}\" >NUL 2>&1\r\n" +
                "del /Q \"%~f0\" >NUL 2>&1\r\n" +
                "exit /b 0\r\n" +
                ":failed\r\n" +
                $"echo Installer exit code %EXITCODE% >> \"{log}\"\r\n" +
                $"start \"\" notepad.exe \"{log}\"\r\n" +
                "exit /b %EXITCODE%\r\n";

            await File.WriteAllTextAsync(helper, cmd);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(helper);

            var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Update helperja ni bilo mogoče zagnati.");

            Application.Exit();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Updater stage 'installer': {ex.Message}", ex);
        }
    }

    private static async Task InstallPortableFallbackAsync(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Url))
            throw new InvalidOperationException("Update manifest nima download URL-ja.");

        var token = Guid.NewGuid().ToString("N");
        var currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
        var tempExe = Path.Combine(
            Path.GetTempPath(),
            $"Sm0kiSoloCoach-{manifest.Version}-{token}.exe");
        var helper = Path.Combine(
            Path.GetTempPath(),
            $"Sm0kiSoloCoach-portable-update-{token}.cmd");

        try
        {
            var bytes = await DownloadAsync(manifest.Url, "portable EXE");
            VerifySha(bytes, manifest.Sha256, "portable EXE");
            await File.WriteAllBytesAsync(tempExe, bytes);

            var pid = Environment.ProcessId;
            var cmd =
                "@echo off\r\n" +
                "setlocal\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL\r\n" +
                "if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
                $"copy /Y \"{tempExe}\" \"{currentExe}\" >NUL\r\n" +
                "if errorlevel 1 exit /b 5\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                $"del /Q \"{tempExe}\" >NUL 2>&1\r\n" +
                "del /Q \"%~f0\" >NUL 2>&1\r\n";

            await File.WriteAllTextAsync(helper, cmd);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(helper);

            var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Portable update helperja ni bilo mogoče zagnati.");

            Application.Exit();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Updater stage 'portable': {ex.Message}", ex);
        }
    }

    private static async Task<byte[]> DownloadAsync(string url, string stage)
    {
        try
        {
            using var http = CreateHttp(TimeSpan.FromMinutes(5));
            using var response = await http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Prenos ({stage}) ni uspel: {ex.Message}", ex);
        }
    }

    private static void VerifySha(byte[] bytes, string expectedSha, string stage)
    {
        if (string.IsNullOrWhiteSpace(expectedSha)) return;

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!hash.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"SHA256 preverjanje za {stage} ni uspelo.");
    }

    private static HttpClient CreateHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Sm0kiSoloCoach/" + CurrentVersion);
        return http;
    }
}
