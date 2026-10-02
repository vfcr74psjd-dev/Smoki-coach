using System.Globalization;
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

        using var playerRequest = NewRequest(
            "players?nickname=" + Uri.EscapeDataString(nickname), key);
        using var playerResponse = await Http.SendAsync(playerRequest, cancellationToken);
        var playerRaw = await playerResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(playerResponse, playerRaw);

        using var playerDoc = JsonDocument.Parse(playerRaw);
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
            throw new InvalidOperationException("FACEIT player_id ni bil vrnjen.");
        if (string.IsNullOrWhiteSpace(snapshot.GameId))
            throw new InvalidOperationException("Na FACEIT profilu nisem našel CS2/CS game profila.");

        await LoadLifetimeAsync(snapshot, key, cancellationToken);
        await LoadRecentAsync(snapshot, key, cancellationToken);

        return snapshot;
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
        EnsureSuccess(response, raw);

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("lifetime", out var lifetime) ||
            lifetime.ValueKind != JsonValueKind.Object)
            return;

        snapshot.Matches = FindStat(lifetime, "Matches", "Total Matches") ?? "—";
        snapshot.WinRate = FindStat(lifetime, "Win Rate %", "Win Rate") ?? "—";
        snapshot.LifetimeKd = FindStat(lifetime, "Average K/D Ratio", "K/D Ratio", "Average K/D") ?? "—";
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
        EnsureSuccess(response, raw);

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

        snapshot.RecentMatchesRead = Math.Max(kills.Count, Math.Max(deaths.Count, kds.Count));
        if (kills.Count > 0) snapshot.RecentAverageKills = kills.Average();
        if (deaths.Count > 0) snapshot.RecentAverageDeaths = deaths.Average();
        if (kds.Count > 0) snapshot.RecentAverageKd = kds.Average();
        else if (kills.Count > 0 && deaths.Count > 0)
            snapshot.RecentAverageKd = kills.Sum() / Math.Max(1.0, deaths.Sum());
    }

    private static HttpRequestMessage NewRequest(string url, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.UserAgent.ParseAdd("Sm0kiSoloCoach/" + AppUpdater.CurrentVersion);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string raw)
    {
        if (response.IsSuccessStatusCode) return;

        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
            throw new InvalidOperationException("FACEIT API key ni sprejet ali nima dovoljenja.");
        if ((int)response.StatusCode == 404)
            throw new InvalidOperationException("FACEIT profila ali statistik ni bilo mogoče najti.");
        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException("FACEIT API rate limit je trenutno dosežen.");

        var shortRaw = raw.Length > 180 ? raw[..180] + "…" : raw;
        throw new InvalidOperationException($"FACEIT API {(int)response.StatusCode}: {shortRaw}");
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    private static int GetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
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

    private static bool TryFindDouble(JsonElement obj, out double value, params string[] names)
    {
        value = 0;
        var raw = FindStat(obj, names);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        raw = raw.Replace("%", "").Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
