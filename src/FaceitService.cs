using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class FaceitSnapshot
{
    public string Nickname { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string GameId { get; set; } = "";
    public int Elo { get; set; }
    public int SkillLevel { get; set; }
    public string Region { get; set; } = "";
    public string Matches { get; set; } = "—";
    public string WinRate { get; set; } = "—";
    public string LifetimeKd { get; set; } = "—";
    public double? RecentAverageKills { get; set; }
    public double? RecentAverageDeaths { get; set; }
    public double? RecentAverageKd { get; set; }
    public int RecentMatchesRead { get; set; }
    public double? Recent5AverageKills { get; set; }
    public double? Previous5AverageKills { get; set; }
    public double? RecentKillsTrendDelta { get; set; }

    public string RecentKillsTrendLabel
    {
        get
        {
            if (RecentKillsTrendDelta is not double delta)
                return "Trend: —";

            if (Math.Abs(delta) < 0.25)
                return "Trend: stable";

            return delta > 0
                ? $"Trend: +{delta:0.0} kills"
                : $"Trend: {delta:0.0} kills";
        }
    }
}

public sealed class FaceitService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://open.faceit.com/data/v4/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<FaceitSnapshot> LoadAsync(
        string nickname,
        CancellationToken cancellationToken = default)
    {
        var key = FaceitSettingsStore.LoadKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("FACEIT API key še ni nastavljen.");

        nickname = (nickname ?? "").Trim();
        if (nickname.Length < 2)
            throw new InvalidOperationException("FACEIT nickname ni veljaven.");

        using var playerDoc = await LoadPlayerDocumentAsync(nickname, key, cancellationToken);
        var root = playerDoc.RootElement;

        var snapshot = new FaceitSnapshot
        {
            Nickname = GetString(root, "nickname") ?? nickname,
            PlayerId = GetString(root, "player_id") ?? ""
        };

        if (root.TryGetProperty("games", out var games) &&
            games.ValueKind == JsonValueKind.Object)
        {
            JsonProperty? selected = null;

            foreach (var prop in games.EnumerateObject())
                if (prop.Name.Equals("cs2", StringComparison.OrdinalIgnoreCase))
                    selected = prop;

            if (selected == null)
                foreach (var prop in games.EnumerateObject())
                    if (prop.Name.Equals("csgo", StringComparison.OrdinalIgnoreCase))
                        selected = prop;

            if (selected == null)
                foreach (var prop in games.EnumerateObject())
                    if (prop.Name.Contains("cs", StringComparison.OrdinalIgnoreCase))
                    {
                        selected = prop;
                        break;
                    }

            if (selected is JsonProperty game)
            {
                snapshot.GameId = game.Name;
                snapshot.Elo = GetInt(game.Value, "faceit_elo");
                snapshot.SkillLevel = GetInt(game.Value, "skill_level");
                snapshot.Region = GetString(game.Value, "region") ?? "";
            }
        }

        if (string.IsNullOrWhiteSpace(snapshot.PlayerId))
            throw new InvalidOperationException(
                $"FACEIT profil '{nickname}' je bil najden, vendar player_id manjka.");

        if (string.IsNullOrWhiteSpace(snapshot.GameId))
            throw new InvalidOperationException(
                $"FACEIT profil '{snapshot.Nickname}' nima CS2/CS profila v Data API.");

        // Profile/ELO is still useful even if one stats endpoint has no data.
        await LoadLifetimeAsync(snapshot, key, cancellationToken);
        await LoadRecentAsync(snapshot, key, cancellationToken);

        return snapshot;
    }

    private static async Task<JsonDocument> LoadPlayerDocumentAsync(
        string nickname,
        string key,
        CancellationToken cancellationToken)
    {
        using (var directRequest = NewRequest(
            "players?nickname=" + Uri.EscapeDataString(nickname), key))
        using (var directResponse = await Http.SendAsync(directRequest, cancellationToken))
        {
            var directRaw = await directResponse.Content.ReadAsStringAsync(cancellationToken);
            if (directResponse.IsSuccessStatusCode)
                return JsonDocument.Parse(directRaw);

            if (directResponse.StatusCode != HttpStatusCode.NotFound)
                EnsureSuccess(directResponse, directRaw, "player lookup");
        }

        // FACEIT also exposes an official player search endpoint. This makes the
        // app more tolerant when the stored coach profile differs in case/format.
        using var searchRequest = NewRequest(
            "search/players?nickname=" + Uri.EscapeDataString(nickname) +
            "&game=cs2&offset=0&limit=20", key);
        using var searchResponse = await Http.SendAsync(searchRequest, cancellationToken);
        var searchRaw = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(searchResponse, searchRaw, "player search");

        using var searchDoc = JsonDocument.Parse(searchRaw);
        if (!searchDoc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"FACEIT profil '{nickname}' ni bil najden. Preveri FACEIT NICKNAME.");
        }

        JsonElement? selected = null;
        foreach (var item in items.EnumerateArray())
        {
            var foundName = GetString(item, "nickname");
            if (string.Equals(foundName, nickname, StringComparison.OrdinalIgnoreCase))
            {
                selected = item;
                break;
            }
        }
        selected ??= items[0];

        var playerId = GetString(selected.Value, "player_id");
        if (string.IsNullOrWhiteSpace(playerId))
            throw new InvalidOperationException(
                $"FACEIT search je našel '{GetString(selected.Value, "nickname") ?? nickname}', vendar player_id manjka.");

        using var byIdRequest = NewRequest(
            "players/" + Uri.EscapeDataString(playerId), key);
        using var byIdResponse = await Http.SendAsync(byIdRequest, cancellationToken);
        var byIdRaw = await byIdResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(byIdResponse, byIdRaw, "player details");
        return JsonDocument.Parse(byIdRaw);
    }

    private static async Task LoadLifetimeAsync(
        FaceitSnapshot snapshot,
        string key,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(
            $"players/{Uri.EscapeDataString(snapshot.PlayerId)}/stats/{Uri.EscapeDataString(snapshot.GameId)}",
            key);
        using var response = await Http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        EnsureSuccess(response, raw, "lifetime stats");

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("lifetime", out var lifetime) ||
            lifetime.ValueKind != JsonValueKind.Object)
            return;

        snapshot.Matches = FindStat(lifetime, "Matches", "Total Matches") ?? "—";
        snapshot.WinRate = FindStat(lifetime, "Win Rate %", "Win Rate") ?? "—";
        snapshot.LifetimeKd = FindStat(
            lifetime,
            "Average K/D Ratio",
            "K/D Ratio",
            "Average K/D") ?? "—";
    }

    private static async Task LoadRecentAsync(
        FaceitSnapshot snapshot,
        string key,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(
            $"players/{Uri.EscapeDataString(snapshot.PlayerId)}/games/{Uri.EscapeDataString(snapshot.GameId)}/stats?offset=0&limit=20",
            key);
        using var response = await Http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        EnsureSuccess(response, raw, "recent match stats");

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return;

        var kills = new List<double>();
        var deaths = new List<double>();
        var kds = new List<double>();

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("stats", out var stats) ||
                stats.ValueKind != JsonValueKind.Object)
                continue;

            if (TryFindDouble(stats, out var k, "Kills")) kills.Add(k);
            if (TryFindDouble(stats, out var d, "Deaths")) deaths.Add(d);
            if (TryFindDouble(stats, out var kd, "K/D Ratio", "K/D")) kds.Add(kd);
        }

        snapshot.RecentMatchesRead = Math.Max(
            kills.Count,
            Math.Max(deaths.Count, kds.Count));

        if (kills.Count > 0)
        {
            snapshot.RecentAverageKills = kills.Average();

            var recent5 = kills.Take(5).ToList();
            if (recent5.Count > 0)
                snapshot.Recent5AverageKills = recent5.Average();

            var previous5 = kills.Skip(5).Take(5).ToList();
            if (previous5.Count > 0)
                snapshot.Previous5AverageKills = previous5.Average();

            if (snapshot.Recent5AverageKills is double latest &&
                snapshot.Previous5AverageKills is double previous)
            {
                snapshot.RecentKillsTrendDelta = latest - previous;
            }
        }

        if (deaths.Count > 0) snapshot.RecentAverageDeaths = deaths.Average();

        if (kds.Count > 0)
            snapshot.RecentAverageKd = kds.Average();
        else if (kills.Count > 0 && deaths.Count > 0)
            snapshot.RecentAverageKd = kills.Sum() / Math.Max(1.0, deaths.Sum());
    }

    private static HttpRequestMessage NewRequest(string url, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.UserAgent.ParseAdd(
            "Sm0kiSoloCoach/" + AppUpdater.CurrentVersion);
        return request;
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string raw,
        string stage)
    {
        if (response.IsSuccessStatusCode) return;

        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
            throw new InvalidOperationException(
                $"FACEIT {stage}: API key ni sprejet ali nima dovoljenja.");

        if ((int)response.StatusCode == 404)
            throw new InvalidOperationException(
                $"FACEIT {stage}: podatki niso bili najdeni.");

        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException(
                $"FACEIT {stage}: API rate limit je trenutno dosežen.");

        var shortRaw = raw.Length > 220 ? raw[..220] + "…" : raw;
        throw new InvalidOperationException(
            $"FACEIT {stage} API {(int)response.StatusCode}: {shortRaw}");
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    private static int GetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
            return i;
        return int.TryParse(el.ToString(), out var parsed) ? parsed : 0;
    }

    private static string? FindStat(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
            foreach (var name in names)
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();

        return null;
    }

    private static bool TryFindDouble(
        JsonElement obj,
        out double value,
        params string[] names)
    {
        value = 0;
        var raw = FindStat(obj, names);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        raw = raw.Replace("%", "").Trim();
        return double.TryParse(
                   raw,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value) ||
               double.TryParse(
                   raw,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out value);
    }
}
