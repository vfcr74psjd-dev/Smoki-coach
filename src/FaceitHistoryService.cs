using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class FaceitHistorySyncResult
{
    public int HistoryCount { get; set; }
    public int DemoReadyCount { get; set; }
    public int DetailsCheckedCount { get; set; }
    public int ApiErrors { get; set; }
}

public sealed class FaceitHistoryService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://open.faceit.com/data/v4/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<FaceitHistorySyncResult> SyncAsync(
        string nickname,
        int maxMatches,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var key = FaceitSettingsStore.LoadKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("FACEIT API key še ni nastavljen.");

        maxMatches = Math.Clamp(maxMatches, 1, 1000);
        nickname = (nickname ?? "").Trim();
        if (nickname.Length < 2)
            throw new InvalidOperationException("FACEIT nickname ni veljaven.");

        progress?.Report("Resolving FACEIT player…");
        var player = await new FaceitService().LoadAsync(nickname, cancellationToken);
        if (string.IsNullOrWhiteSpace(player.PlayerId))
            throw new InvalidOperationException("FACEIT player_id ni bil najden.");

        var game = string.IsNullOrWhiteSpace(player.GameId) ? "cs2" : player.GameId;
        var matches = new List<FaceitHistoryMatch>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int offset = 0; offset < maxMatches; offset += 100)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageSize = Math.Min(100, maxMatches - offset);
            progress?.Report($"History {offset}/{maxMatches}…");

            using var request = NewRequest(
                $"players/{Uri.EscapeDataString(player.PlayerId)}/history" +
                $"?game={Uri.EscapeDataString(game)}&from=0&to={now}&offset={offset}&limit={pageSize}",
                key);

            using var response = await Http.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, raw, "history");

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                break;

            int count = 0;
            foreach (var item in items.EnumerateArray())
            {
                var parsed = ParseMatch(item, nickname, game, false);
                if (!string.IsNullOrWhiteSpace(parsed.MatchId))
                {
                    matches.Add(parsed);
                    count++;
                }
            }

            if (count < pageSize)
                break;
        }

        matches = matches
            .GroupBy(x => x.MatchId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.FinishedUtc)
            .Take(maxMatches)
            .ToList();

        FaceitHistoryStore.UpsertRange(nickname, matches);

        // Player history does not include demo_url in the current Data API schema,
        // so match details are checked separately. Keep concurrency intentionally low.
        int completed = 0;
        int errors = 0;
        int ready = 0;
        using var gate = new SemaphoreSlim(2, 2);

        progress?.Report($"Checking demo availability 0/{matches.Count}…");

        var tasks = matches.Select(async match =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var detailed = await LoadDetailsWithRetryAsync(
                    match.MatchId, nickname, game, key, cancellationToken);

                lock (matches)
                {
                    CopyDetails(match, detailed);
                    completed++;
                    if (match.DemoReady) ready++;
                }

                progress?.Report($"Demo check {completed}/{matches.Count} • ready {ready}");
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                lock (matches)
                {
                    match.DetailsChecked = true;
                    completed++;
                    errors++;
                }
                progress?.Report($"Demo check {completed}/{matches.Count} • {errors} skipped");
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        FaceitHistoryStore.UpsertRange(nickname, matches);

        return new FaceitHistorySyncResult
        {
            HistoryCount = matches.Count,
            DemoReadyCount = matches.Count(x => x.DemoReady),
            DetailsCheckedCount = matches.Count(x => x.DetailsChecked),
            ApiErrors = errors
        };
    }

    private static async Task<FaceitHistoryMatch> LoadDetailsWithRetryAsync(
        string matchId,
        string nickname,
        string game,
        string key,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using var request = NewRequest("matches/" + Uri.EscapeDataString(matchId), key);
            using var response = await Http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if ((int)response.StatusCode == 429 && attempt < 3)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 + attempt * 3);
                await Task.Delay(wait, ct);
                continue;
            }

            EnsureSuccess(response, raw, "match details");
            using var doc = JsonDocument.Parse(raw);
            return ParseMatch(doc.RootElement, nickname, game, true);
        }

        throw new InvalidOperationException("FACEIT demo availability check failed.");
    }

    private static void CopyDetails(FaceitHistoryMatch target, FaceitHistoryMatch source)
    {
        if (!string.IsNullOrWhiteSpace(source.Map)) target.Map = source.Map;
        if (!string.IsNullOrWhiteSpace(source.FaceitUrl)) target.FaceitUrl = source.FaceitUrl;
        if (!string.IsNullOrWhiteSpace(source.Status)) target.Status = source.Status;
        if (source.FinishedUtc != default) target.FinishedUtc = source.FinishedUtc;
        target.DemoReady = source.DemoReady;
        target.DemoResources = source.DemoResources;
        target.DetailsChecked = true;
    }

    private static FaceitHistoryMatch ParseMatch(
        JsonElement root,
        string nickname,
        string game,
        bool detailsChecked)
    {
        var demos = new List<string>();

        if (root.TryGetProperty("demo_url", out var demoUrl))
        {
            if (demoUrl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in demoUrl.EnumerateArray())
                    if (d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString()))
                        demos.Add(d.GetString()!);
            }
            else if (demoUrl.ValueKind == JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(demoUrl.GetString()))
            {
                demos.Add(demoUrl.GetString()!);
            }
        }

        if (root.TryGetProperty("instances", out var instances) &&
            instances.ValueKind == JsonValueKind.Array)
        {
            foreach (var instance in instances.EnumerateArray())
            {
                if (!instance.TryGetProperty("demos", out var ds) || ds.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var d in ds.EnumerateArray())
                    if (d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString()))
                        demos.Add(d.GetString()!);
            }
        }

        var finished = GetLong(root, "finished_at");
        var started = GetLong(root, "started_at");
        var when = finished > 0 ? finished : started;

        return new FaceitHistoryMatch
        {
            MatchId = GetString(root, "match_id") ?? "",
            PlayerNickname = nickname,
            GameId = GetString(root, "game") ?? GetString(root, "game_id") ?? game,
            FinishedUtc = when > 0
                ? DateTimeOffset.FromUnixTimeSeconds(when).UtcDateTime
                : DateTime.MinValue,
            Map = TryGetMap(root),
            FaceitUrl = GetString(root, "faceit_url") ?? "",
            Status = GetString(root, "status") ?? "",
            DemoReady = demos.Count > 0,
            DetailsChecked = detailsChecked,
            DemoResources = demos.Distinct().ToList(),
            SyncedUtc = DateTime.UtcNow
        };
    }

    private static string TryGetMap(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("voting", out var voting) &&
                voting.ValueKind == JsonValueKind.Object &&
                voting.TryGetProperty("map", out var map) &&
                map.ValueKind == JsonValueKind.Object &&
                map.TryGetProperty("pick", out var pick))
            {
                var value = FirstString(pick);
                if (!string.IsNullOrWhiteSpace(value))
                    return NormalizeMap(value);
            }
        }
        catch { }

        var direct = GetString(root, "map");
        return string.IsNullOrWhiteSpace(direct) ? "" : NormalizeMap(direct);
    }

    private static string FirstString(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
            return e.GetString() ?? "";

        if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString() ?? "";

        return "";
    }

    private static string NormalizeMap(string map)
    {
        map = map.Trim();
        if (map.StartsWith("de_", StringComparison.OrdinalIgnoreCase))
            return map.ToLowerInvariant();

        var simple = map.ToLowerInvariant().Replace(" ", "").Replace("-", "");
        return simple switch
        {
            "mirage" => "de_mirage",
            "inferno" => "de_inferno",
            "nuke" => "de_nuke",
            "ancient" => "de_ancient",
            "anubis" => "de_anubis",
            "dust2" or "dustii" => "de_dust2",
            "overpass" => "de_overpass",
            "train" => "de_train",
            "vertigo" => "de_vertigo",
            _ => map
        };
    }

    private static HttpRequestMessage NewRequest(string url, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.UserAgent.ParseAdd("Sm0kiSoloCoach/" + AppUpdater.CurrentVersion);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string raw, string stage)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException($"FACEIT {stage}: API key ni sprejet.");

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"FACEIT {stage}: podatki niso bili najdeni.");

        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException($"FACEIT {stage}: rate limit.");

        var shortRaw = raw.Length > 180 ? raw[..180] + "…" : raw;
        throw new InvalidOperationException($"FACEIT {stage} {(int)response.StatusCode}: {shortRaw}");
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    private static long GetLong(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var l)) return l;
        return long.TryParse(el.ToString(), out var parsed) ? parsed : 0;
    }
}
