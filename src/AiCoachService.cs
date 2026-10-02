using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class AiCoachService
{
    public const string ModelName = "qwen3:1.7b";
    public const string LegacyModelName = "qwen3:4b-instruct";
    private const string BaseUrl = "http://127.0.0.1:11434";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
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

    private static async Task<HashSet<string>> GetInstalledModelsAsync(
        CancellationToken cancellationToken)
    {
        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var response = await Http.GetAsync(BaseUrl + "/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return models;

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty("models", out var list) ||
                list.ValueKind != JsonValueKind.Array)
                return models;

            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                    models.Add(name.GetString()!);

                if (item.TryGetProperty("model", out var model) &&
                    model.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(model.GetString()))
                    models.Add(model.GetString()!);
            }
        }
        catch { }

        return models;
    }

    private static async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        await EnsureServerAsync(cancellationToken);
        var models = await GetInstalledModelsAsync(cancellationToken);

        if (models.Contains(ModelName)) return ModelName;
        if (models.Contains(LegacyModelName)) return LegacyModelName;

        // Returning the fast model gives the normal Ollama "model not found" message,
        // which our error handler converts into a clear Prepare Fast AI instruction.
        return ModelName;
    }

    public static async Task<bool> IsFastModelReadyAsync(
        CancellationToken cancellationToken = default)
    {
        if (FindOllamaExe() == null)
            return false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));

            await EnsureServerAsync(timeout.Token);
            var models = await GetInstalledModelsAsync(timeout.Token);
            return models.Contains(ModelName) || models.Contains(LegacyModelName);
        }
        catch
        {
            return false;
        }
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
            throw new InvalidOperationException("Prenosa Fast AI modela ni bilo mogoče zagnati.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        _ = await stdoutTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? "Prenos Fast AI modela ni uspel."
                    : stderr.Trim());
    }

    public static async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (FindOllamaExe() == null) return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            var model = await ResolveModelAsync(timeout.Token);
            var payload = new
            {
                model,
                prompt = "/no_think\nOdgovori samo: OK",
                stream = false,
                keep_alive = "30m",
                options = new
                {
                    temperature = 0.0,
                    num_ctx = 512,
                    num_predict = 4
                }
            };

            using var response = await Http.PostAsJsonAsync(
                BaseUrl + "/api/generate", payload, timeout.Token);
            _ = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch
        {
            // Warm-up is best-effort. The instant coach works even if Local AI is unavailable.
        }
    }

    public static async Task<string> TestLocalAiAsync(CancellationToken cancellationToken = default)
    {
        var model = await ResolveModelAsync(cancellationToken);

        var payload = new
        {
            model,
            prompt = "/no_think\nOdgovori samo: LOCAL AI OK",
            stream = false,
            keep_alive = "30m",
            options = new
            {
                temperature = 0.0,
                num_ctx = 512,
                num_predict = 16
            }
        };

        using var response = await Http.PostAsJsonAsync(
            BaseUrl + "/api/generate", payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildLocalError(response.StatusCode, raw);

        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.TryGetProperty("response", out var value)
            ? value.GetString()
            : null;

        return string.IsNullOrWhiteSpace(text) ? "LOCAL AI OK" : StripThinking(text).Trim();
    }

    public async Task<string> GenerateRoundAdviceAsync(
        GameSnapshot s,
        IReadOnlyList<RoundRecord> rounds,
        string mode,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var model = await ResolveModelAsync(cancellationToken);

        var recent = rounds.TakeLast(3)
            .Select(r => $"R{r.Round + 1}:{r.KillsRound}K/{r.DeathsRound}D")
            .ToArray();

        var safePlayerName = string.IsNullOrWhiteSpace(playerName)
            ? "player"
            : playerName.Trim();

        var roundNumber = s.Round is int currentRound ? currentRound + 1 : 0;
        var variationSeed =
            $"{roundNumber}:{s.CtScore ?? 0}:{s.TScore ?? 0}:{rounds.LastOrDefault()?.KillsRound ?? -1}:{rounds.LastOrDefault()?.DeathsRound ?? -1}";

        var prompt =
            "/no_think\n" +
            $"CS2 solo coach for {safePlayerName}. Slovenian. Give ONLY minimal commands for the NEXT round. " +
            "Use ONLY supplied state; never guess enemy positions. Adapt strongly to the previous round outcome, score, side and money. " +
            "Exactly 3 lines and nothing else: BUY:, DO:, ADAPT:. " +
            "BUY must be a realistic short purchase/save recommendation from current money. " +
            "DO must be one short sequence using arrows, e.g. mid control → trade → reposition. " +
            "ADAPT must explicitly react to the recent round result. Max 35 words total. No explanations.\n" +
            $"STATE map={CoachEngine.PrettyMap(s.Map)}({s.Map}); side={s.Team}; " +
            $"round={roundNumber}; score={s.CtScore ?? 0}:{s.TScore ?? 0}; " +
            $"money={s.Money ?? 0}; weapon={s.Weapon}; hp={s.Health ?? 0}; armor={s.Armor ?? 0}; " +
            $"type={CoachEngine.ClassifyRound(s)}; mode={mode}; " +
            $"recent={(recent.Length == 0 ? "none" : string.Join(",", recent))}; variation={variationSeed}.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            keep_alive = "30m",
            options = new
            {
                temperature = 0.24,
                top_p = 0.80,
                num_ctx = 1024,
                num_predict = 64
            }
        };

        using var response = await Http.PostAsJsonAsync(
            BaseUrl + "/api/generate", payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildLocalError(response.StatusCode, raw);

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("response", out var output) &&
            output.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(output.GetString()))
            return NormalizeRoundAdvice(StripThinking(output.GetString()!));

        throw new InvalidOperationException("Local AI odgovor ni vseboval besedila.");
    }

    private static string NormalizeRoundAdvice(string text)
    {
        var lines = text
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x =>
                x.StartsWith("BUY:", StringComparison.OrdinalIgnoreCase) ||
                x.StartsWith("DO:", StringComparison.OrdinalIgnoreCase) ||
                x.StartsWith("ADAPT:", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        if (lines.Count == 3)
            return string.Join("\n", lines);

        // Small local models occasionally ignore formatting. Keep the UI compact
        // instead of showing a paragraph in the live-round card.
        var compact = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (compact.Length > 180)
            compact = compact[..180].TrimEnd() + "…";

        return compact;
    }

    private static string StripThinking(string text)
    {
        var end = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return end >= 0 ? text[(end + "</think>".Length)..] : text;
    }

    private static Exception BuildLocalError(System.Net.HttpStatusCode status, string raw)
    {
        if (raw.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (raw.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             raw.Contains("pull", StringComparison.OrdinalIgnoreCase)))
        {
            return new InvalidOperationException(
                $"Fast model {ModelName} še ni pripravljen. Odpri Local AI in klikni Prepare Fast AI.");
        }

        var shortError = raw.Length > 240 ? raw[..240] + "…" : raw;
        return new InvalidOperationException($"Local AI {(int)status}: {shortError}");
    }
}
