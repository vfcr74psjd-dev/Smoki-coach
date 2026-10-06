using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class OpponentScoutPlayer
{
    public string Nickname { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public int Elo { get; set; }
    public int SkillLevel { get; set; }
    public double? RecentKd { get; set; }
    public double? RecentKills { get; set; }
    public int RecentMatches { get; set; }
    public int MapMatches { get; set; }
    public double? MapWinRate { get; set; }
    public double? MapKd { get; set; }
    public double? MapAverageKills { get; set; }
    public List<string> RecentMatchIds { get; set; } = new();

    public double ThreatScore
    {
        get
        {
            var eloPart = Math.Clamp((Elo - 800) / 20.0, 0, 100);
            var kdPart = Math.Clamp(((MapKd ?? RecentKd ?? 1.0) - 0.70) * 120, 0, 100);
            var winPart = Math.Clamp(MapWinRate ?? 50, 0, 100);
            return eloPart * 0.35 + kdPart * 0.40 + winPart * 0.25;
        }
    }
}

public sealed class OpponentScoutReport
{
    public string MatchId { get; set; } = "";
    public string Map { get; set; } = "";
    public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
    public List<OpponentScoutPlayer> Opponents { get; set; } = new();
    public double TeamAverageElo { get; set; }
    public double? TeamRecentKd { get; set; }
    public double? TeamMapWinRate { get; set; }
    public string PremadeSignal { get; set; } = "No shared-match signal";
    public string BiggestThreat { get; set; } = "";
    public string Confidence { get; set; } = "LOW";

    public string CompactSummary
    {
        get
        {
            var mapPart = TeamMapWinRate is double wr
                ? $"map WR {wr:0}%"
                : "map WR —";
            var kdPart = TeamRecentKd is double kd
                ? $"recent KD {kd:0.00}"
                : "recent KD —";
            var threat = string.IsNullOrWhiteSpace(BiggestThreat)
                ? "threat —"
                : $"watch {BiggestThreat}";

            return $"{mapPart} • {kdPart} • {threat} • {Confidence}";
        }
    }
}

public sealed class OpponentScoutService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://open.faceit.com/data/v4/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<OpponentScoutReport> ScanMatchAsync(
        string matchUrlOrId,
        string ownNickname,
        string fallbackMap,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var key = FaceitSettingsStore.LoadKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("FACEIT API key še ni nastavljen.");

        var matchId = ParseMatchId(matchUrlOrId);
        if (string.IsNullOrWhiteSpace(matchId))
            throw new InvalidOperationException(
                "FACEIT match URL/ID ni veljaven.");

        progress?.Report("Loading FACEIT match roster…");

        using var request = NewRequest(
            "matches/" + Uri.EscapeDataString(matchId),
            key);
        using var response = await Http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, raw, "match details");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var map = ReadMap(root);
        if (string.IsNullOrWhiteSpace(map))
            map = NormalizeMap(fallbackMap);

        var factions = ReadFactions(root);
        if (factions.Count < 2)
            throw new InvalidOperationException(
                "FACEIT match details nimajo dveh rosterjev.");

        var ownFaction = factions
            .FirstOrDefault(x => x.Value.Any(p =>
                p.Nickname.Equals(
                    ownNickname,
                    StringComparison.OrdinalIgnoreCase)));

        KeyValuePair<string,List<(string Nickname,string PlayerId)>> enemyFaction;

        if (!string.IsNullOrWhiteSpace(ownFaction.Key))
        {
            enemyFaction = factions
                .First(x => !x.Key.Equals(
                    ownFaction.Key,
                    StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            throw new InvalidOperationException(
                $"V match rosterju ne najdem profila '{ownNickname}'. " +
                "Preveri FACEIT nickname oziroma match URL.");
        }

        var report = new OpponentScoutReport
        {
            MatchId = matchId,
            Map = map,
            ScannedUtc = DateTime.UtcNow
        };

        using var gate = new SemaphoreSlim(3, 3);
        var scanned = new List<OpponentScoutPlayer>();

        var tasks = enemyFaction.Value.Select(async rosterPlayer =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                progress?.Report($"Scouting {rosterPlayer.Nickname}…");
                var player = await ScanPlayerAsync(
                    rosterPlayer.Nickname,
                    rosterPlayer.PlayerId,
                    map,
                    key,
                    cancellationToken);

                lock (scanned)
                    scanned.Add(player);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        report.Opponents = scanned
            .OrderByDescending(x => x.ThreatScore)
            .ToList();

        if (report.Opponents.Count > 0)
        {
            report.TeamAverageElo =
                report.Opponents.Average(x => x.Elo);

            var kds = report.Opponents
                .Where(x => x.RecentKd.HasValue)
                .Select(x => x.RecentKd!.Value)
                .ToList();
            if (kds.Count > 0)
                report.TeamRecentKd = kds.Average();

            var mapWrs = report.Opponents
                .Where(x =>
                    x.MapWinRate.HasValue &&
                    x.MapMatches >= 3)
                .Select(x => x.MapWinRate!.Value)
                .ToList();
            if (mapWrs.Count > 0)
                report.TeamMapWinRate = mapWrs.Average();

            report.BiggestThreat =
                report.Opponents[0].Nickname;
        }

        report.PremadeSignal =
            BuildPremadeSignal(report.Opponents);

        var mapSamples = report.Opponents.Sum(x => x.MapMatches);
        report.Confidence =
            report.Opponents.Count >= 5 && mapSamples >= 25
                ? "HIGH"
                : report.Opponents.Count >= 4 && mapSamples >= 10
                    ? "MEDIUM"
                    : "LOW";

        OpponentScoutStore.Save(report);
        return report;
    }

    private static async Task<OpponentScoutPlayer> ScanPlayerAsync(
        string nickname,
        string rosterPlayerId,
        string map,
        string key,
        CancellationToken ct)
    {
        var baseSnapshot = await new FaceitService()
            .LoadAsync(nickname, ct);

        var playerId = !string.IsNullOrWhiteSpace(baseSnapshot.PlayerId)
            ? baseSnapshot.PlayerId
            : rosterPlayerId;

        var game = string.IsNullOrWhiteSpace(baseSnapshot.GameId)
            ? "cs2"
            : baseSnapshot.GameId;

        var result = new OpponentScoutPlayer
        {
            Nickname = baseSnapshot.Nickname,
            PlayerId = playerId,
            Elo = baseSnapshot.Elo,
            SkillLevel = baseSnapshot.SkillLevel,
            RecentKd = baseSnapshot.RecentAverageKd,
            RecentKills = baseSnapshot.RecentAverageKills,
            RecentMatches = baseSnapshot.RecentMatchesRead
        };

        await LoadMapRecentAsync(result, playerId, game, map, key, ct);
        result.RecentMatchIds =
            await LoadRecentMatchIdsAsync(
                playerId,
                game,
                key,
                ct);

        return result;
    }

    private static async Task LoadMapRecentAsync(
        OpponentScoutPlayer player,
        string playerId,
        string game,
        string map,
        string key,
        CancellationToken ct)
    {
        using var request = NewRequest(
            $"players/{Uri.EscapeDataString(playerId)}/games/{Uri.EscapeDataString(game)}/stats?offset=0&limit=50",
            key);
        using var response = await Http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        EnsureSuccess(response, raw, "player recent stats");

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return;

        var kills = new List<double>();
        var deaths = new List<double>();
        int wins = 0;
        int resolved = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("stats", out var stats) ||
                stats.ValueKind != JsonValueKind.Object)
                continue;

            var statMap = FindStat(
                stats,
                "Map",
                "Map Name",
                "map");

            if (!MapMatches(statMap, map))
                continue;

            player.MapMatches++;

            if (TryFindDouble(stats, out var k, "Kills"))
                kills.Add(k);
            if (TryFindDouble(stats, out var d, "Deaths"))
                deaths.Add(d);

            var result = FindStat(
                stats,
                "Result",
                "Match Result",
                "Win");

            if (!string.IsNullOrWhiteSpace(result))
            {
                resolved++;
                if (IsWin(result))
                    wins++;
            }
        }

        if (kills.Count > 0)
            player.MapAverageKills = kills.Average();

        if (kills.Count > 0 && deaths.Count > 0)
            player.MapKd =
                kills.Sum() / Math.Max(1.0, deaths.Sum());

        if (resolved > 0)
            player.MapWinRate =
                100.0 * wins / resolved;
    }

    private static async Task<List<string>> LoadRecentMatchIdsAsync(
        string playerId,
        string game,
        string key,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var request = NewRequest(
            $"players/{Uri.EscapeDataString(playerId)}/history?game={Uri.EscapeDataString(game)}&from=0&to={now}&offset=0&limit=20",
            key);
        using var response = await Http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new();

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return new();

        var result = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            var id = GetString(item, "match_id");
            if (!string.IsNullOrWhiteSpace(id))
                result.Add(id);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildPremadeSignal(
        IReadOnlyList<OpponentScoutPlayer> players)
    {
        if (players.Count < 2)
            return "No shared-match signal";

        var bestPair = "";
        var bestShared = 0;

        for (var i = 0; i < players.Count; i++)
        for (var j = i + 1; j < players.Count; j++)
        {
            var shared = players[i].RecentMatchIds
                .Intersect(
                    players[j].RecentMatchIds,
                    StringComparer.OrdinalIgnoreCase)
                .Count();

            if (shared <= bestShared)
                continue;

            bestShared = shared;
            bestPair =
                $"{players[i].Nickname} + {players[j].Nickname}";
        }

        return bestShared >= 3
            ? $"Likely premade • {bestPair} • {bestShared} shared recent matches"
            : bestShared >= 1
                ? $"Some shared history • {bestPair} • {bestShared}x"
                : "No shared recent-match signal";
    }

    private static Dictionary<string,List<(string Nickname,string PlayerId)>> ReadFactions(
        JsonElement root)
    {
        var result =
            new Dictionary<string,List<(string,string)>>(
                StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("teams", out var teams) ||
            teams.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var faction in teams.EnumerateObject())
        {
            if (faction.Value.ValueKind != JsonValueKind.Object ||
                !faction.Value.TryGetProperty("roster", out var roster) ||
                roster.ValueKind != JsonValueKind.Array)
                continue;

            var players = new List<(string,string)>();

            foreach (var p in roster.EnumerateArray())
            {
                var nickname =
                    GetString(p, "nickname") ??
                    GetString(p, "game_player_name") ??
                    "";

                var id =
                    GetString(p, "player_id") ??
                    GetString(p, "user_id") ??
                    "";

                if (!string.IsNullOrWhiteSpace(nickname))
                    players.Add((nickname, id));
            }

            if (players.Count > 0)
                result[faction.Name] = players;
        }

        return result;
    }

    private static string ParseMatchId(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0)
            return "";

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath
                .Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            var roomIndex = Array.FindIndex(
                segments,
                x => x.Equals(
                    "room",
                    StringComparison.OrdinalIgnoreCase));

            if (roomIndex >= 0 &&
                roomIndex + 1 < segments.Length)
                return segments[roomIndex + 1];

            if (segments.Length > 0)
                return segments[^1];
        }

        return raw
            .Split(
                new[] { '?', '#', '/' },
                StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "";
    }

    private static string ReadMap(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("voting", out var voting) &&
                voting.ValueKind == JsonValueKind.Object &&
                voting.TryGetProperty("map", out var mapObj) &&
                mapObj.ValueKind == JsonValueKind.Object &&
                mapObj.TryGetProperty("pick", out var pick))
            {
                if (pick.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in pick.EnumerateArray())
                        if (v.ValueKind == JsonValueKind.String)
                            return NormalizeMap(v.GetString() ?? "");
                }

                if (pick.ValueKind == JsonValueKind.String)
                    return NormalizeMap(pick.GetString() ?? "");
            }
        }
        catch { }

        return NormalizeMap(GetString(root, "map") ?? "");
    }

    private static string NormalizeMap(string map)
    {
        map = (map ?? "").Trim();
        if (map.StartsWith("de_", StringComparison.OrdinalIgnoreCase))
            return map.ToLowerInvariant();

        var simple = map
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "");

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

    private static bool MapMatches(string? raw, string target)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return NormalizeMap(raw)
            .Equals(
                NormalizeMap(target),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWin(string raw)
    {
        raw = raw.Trim();
        return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("win", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("won", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("w", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage NewRequest(
        string url,
        string key)
    {
        var request =
            new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", key);
        request.Headers.UserAgent.ParseAdd(
            "Sm0kiSoloCoach/" + AppUpdater.CurrentVersion);

        return request;
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string raw,
        string stage)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"FACEIT {stage}: API key ni sprejet.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"FACEIT {stage}: podatki niso bili najdeni.");

        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException(
                $"FACEIT {stage}: rate limit.");

        var shortRaw =
            raw.Length > 180
                ? raw[..180] + "…"
                : raw;

        throw new InvalidOperationException(
            $"FACEIT {stage} {(int)response.StatusCode}: {shortRaw}");
    }

    private static string? GetString(
        JsonElement obj,
        string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return null;

        return el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : el.ToString();
    }

    private static string? FindStat(
        JsonElement obj,
        params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        foreach (var name in names)
        {
            if (prop.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
            }
        }

        return null;
    }

    private static bool TryFindDouble(
        JsonElement obj,
        out double value,
        params string[] names)
    {
        value = 0;
        var raw = FindStat(obj, names);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

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
