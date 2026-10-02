using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class AiCoachService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(18)
    };

    public bool IsConfigured => AiSettingsStore.HasApiKey;

    public async Task<string> GenerateRoundAdviceAsync(
        GameSnapshot s,
        IReadOnlyList<RoundRecord> rounds,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var key = AiSettingsStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OpenAI API key ni nastavljen.");

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
            "Uporabi format: PLAN:, OPENING:, AFTER KILL:, AVOID:, FOCUS:. " +
            "Če je podatkov premalo, povej varen splošen plan glede na mapo, side in economy.";

        var payload = new
        {
            model = "gpt-6-luna",
            instructions,
            input = state,
            max_output_tokens = 220
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.UserAgent.ParseAdd("Sm0kiSoloCoach/" + AppUpdater.CurrentVersion);
        req.Content = JsonContent.Create(payload);

        using var response = await Http.SendAsync(req, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortError = raw.Length > 220 ? raw[..220] + "…" : raw;
            throw new InvalidOperationException($"OpenAI API {(int)response.StatusCode}: {shortError}");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.TryGetProperty("output_text", out var direct) &&
            direct.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(direct.GetString()))
            return direct.GetString()!.Trim();

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) &&
                        type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(text.GetString()))
                        return text.GetString()!.Trim();
                }
            }
        }

        throw new InvalidOperationException("AI odgovor ni vseboval besedila.");
    }
}
