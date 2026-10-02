using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class AiCoachService
{
    public const string ModelName = "qwen3:4b-instruct";
    private const string BaseUrl = "http://127.0.0.1:11434";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public bool IsConfigured => FindOllamaExe() != null;

    public static string? FindOllamaExe()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Ollama", "ollama.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Ollama", "ollama.exe")
        };

        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var path = Path.Combine(dir.Trim(), "ollama.exe");
                if (File.Exists(path)) return path;
            }
            catch { }
        }

        return null;
    }

    private static async Task<bool> ServerIsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(BaseUrl + "/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureServerAsync(CancellationToken cancellationToken)
    {
        if (await ServerIsReadyAsync(cancellationToken)) return;

        var exe = FindOllamaExe();
        if (exe == null)
            throw new InvalidOperationException(
                "Local AI ni nameščen. Odpri AI Settings in klikni Install Ollama.");

        try
        {
            Process.Start(new ProcessStartInfo(exe, "serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }

        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(500, cancellationToken);
            if (await ServerIsReadyAsync(cancellationToken)) return;
        }

        throw new InvalidOperationException(
            "Ollama je nameščen, vendar lokalnega AI strežnika ni bilo mogoče zagnati.");
    }

    public static async Task PrepareLocalAiAsync(CancellationToken cancellationToken = default)
    {
        var exe = FindOllamaExe();
        if (exe == null)
            throw new InvalidOperationException(
                "Najprej namesti Ollama. Klikni Install Ollama, nato ponovno odpri ta meni.");

        await EnsureServerAsync(cancellationToken);

        using var process = Process.Start(new ProcessStartInfo(exe, $"pull {ModelName}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process == null)
            throw new InvalidOperationException("Prenosa lokalnega AI modela ni bilo mogoče zagnati.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err) ? "Prenos lokalnega AI modela ni uspel." : err.Trim());
        }
    }

    public static async Task<string> TestLocalAiAsync(CancellationToken cancellationToken = default)
    {
        await EnsureServerAsync(cancellationToken);

        var payload = new
        {
            model = ModelName,
            prompt = "Odgovori samo: LOCAL AI OK",
            stream = false,
            options = new
            {
                temperature = 0.0,
                num_predict = 16
            }
        };

        using var response = await Http.PostAsJsonAsync(BaseUrl + "/api/generate", payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildLocalError(response.StatusCode, raw);

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.TryGetProperty("response", out var value)
            ? value.GetString()
            : null;

        return string.IsNullOrWhiteSpace(text) ? "LOCAL AI OK" : text.Trim();
    }

    public async Task<string> GenerateRoundAdviceAsync(
        GameSnapshot s,
        IReadOnlyList<RoundRecord> rounds,
        string mode,
        CancellationToken cancellationToken = default)
    {
        await EnsureServerAsync(cancellationToken);

        var recent = rounds.TakeLast(6)
            .Select(r => $"R{r.Round + 1}: {r.Side}, +{r.KillsRound}K/+{r.DeathsRound}D, end USD {r.MoneyEnd}")
            .ToArray();

        var state =
            $"Map: {CoachEngine.PrettyMap(s.Map)} ({s.Map})\n" +
            $"Side: {s.Team}\n" +
            $"Round: {(s.Round is int roundNo ? roundNo + 1 : 0)}\n" +
            $"Score CT:T: {s.CtScore ?? 0}:{s.TScore ?? 0}\n" +
            $"Money: USD {s.Money ?? 0}\n" +
            $"Weapon: {s.Weapon}\n" +
            $"Armor: {s.Armor ?? 0}, helmet: {s.Helmet == true}\n" +
            $"Match K/D/A: {s.Kills ?? 0}/{s.Deaths ?? 0}/{s.Assists ?? 0}\n" +
            $"Round type: {CoachEngine.ClassifyRound(s)}\n" +
            $"Coach mode: {mode}\n" +
            $"Recent rounds: {(recent.Length == 0 ? "none tracked yet" : string.Join(" | ", recent))}";

        const string instructions =
            "Ti si kratek CS2 solo-queue performance coach za igralca Sm0ki_72. " +
            "Uporabi SAMO podatke, ki so podani v stanju igre. Ne ugibaj lokacij ali informacij o nasprotnikih. " +
            "Ne predlagaj cheatov, memory readanja, avtomatizacije inputa ali skritih podatkov. " +
            "Cilj je izboljšati odločitve, trade timing, survival po prvem killu in dolgoročni kill output. " +
            "Odgovori v slovenščini, zelo kratko in praktično, največ 5 vrstic. " +
            "Uporabi točno format PLAN:, OPENING:, AFTER KILL:, AVOID:, FOCUS:. " +
            "Brez uvoda, brez dolge razlage. Če je podatkov premalo, uporabi varen splošen plan glede na mapo, side in economy.";

        var payload = new
        {
            model = ModelName,
            prompt = instructions + "\n\nSTANJE TEKME:\n" + state,
            stream = false,
            keep_alive = "15m",
            options = new
            {
                temperature = 0.35,
                num_predict = 220
            }
        };

        using var response = await Http.PostAsJsonAsync(BaseUrl + "/api/generate", payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildLocalError(response.StatusCode, raw);

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("response", out var output) &&
            output.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(output.GetString()))
            return output.GetString()!.Trim();

        throw new InvalidOperationException("Local AI odgovor ni vseboval besedila.");
    }

    private static Exception BuildLocalError(System.Net.HttpStatusCode status, string raw)
    {
        if (raw.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (raw.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             raw.Contains("pull", StringComparison.OrdinalIgnoreCase)))
        {
            return new InvalidOperationException(
                $"Model {ModelName} še ni pripravljen. Odpri AI Settings in klikni Prepare Local AI.");
        }

        var shortError = raw.Length > 240 ? raw[..240] + "…" : raw;
        return new InvalidOperationException($"Local AI {(int)status}: {shortError}");
    }
}
