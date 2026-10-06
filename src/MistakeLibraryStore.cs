using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class MistakeMatchEntry
{
    public string MatchKey { get; set; } = "";
    public DateTime EndedUtc { get; set; }
    public string Map { get; set; } = "";
    public int Rounds { get; set; }
    public Dictionary<string,int> Signals { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string PrimaryMistake { get; set; } = "";
}

public sealed record MistakeLibrarySummary(
    string Key,
    string Label,
    int Matches,
    int TotalSignals,
    int RecentMatchesRead,
    string Trend,
    string CoachingFocus)
{
    public string Compact =>
        Matches <= 0
            ? "No recurring leak yet."
            : $"{Label} • {Matches}/{RecentMatchesRead} recent matches • {Trend}";
}

public static class MistakeLibraryStore
{
    private static readonly object Gate = new();
    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach");
    private static readonly string FilePath =
        Path.Combine(
            Dir,
            "mistake-library.json");

    public static void RecordMatch(
        string matchKey,
        string map,
        IReadOnlyList<RoundRecord> rounds)
    {
        if (string.IsNullOrWhiteSpace(matchKey) ||
            string.IsNullOrWhiteSpace(map) ||
            rounds.Count < 4)
            return;

        var signals = DetectSignals(rounds);
        var primary = signals
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Select(x => x.Key)
            .FirstOrDefault() ?? "";

        lock (Gate)
        {
            var all = LoadUnsafe();

            if (all.Any(x =>
                    x.MatchKey.Equals(
                        matchKey,
                        StringComparison.OrdinalIgnoreCase)))
                return;

            all.Add(new MistakeMatchEntry
            {
                MatchKey = matchKey,
                EndedUtc = DateTime.UtcNow,
                Map = map,
                Rounds = rounds.Count,
                Signals = signals,
                PrimaryMistake = primary
            });

            all = all
                .OrderByDescending(x => x.EndedUtc)
                .Take(100)
                .ToList();

            SaveUnsafe(all);
        }
    }

    public static MistakeLibrarySummary TopRecurring(
        string? map = null,
        int recentMatches = 10)
    {
        recentMatches = Math.Clamp(
            recentMatches,
            3,
            30);

        List<MistakeMatchEntry> sample;

        lock (Gate)
        {
            sample = LoadUnsafe()
                .Where(x =>
                    string.IsNullOrWhiteSpace(map) ||
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.EndedUtc)
                .Take(recentMatches)
                .ToList();
        }

        if (sample.Count == 0)
            return Empty(0);

        var keys = new[]
        {
            "discipline",
            "post_impact",
            "impact",
            "conversion"
        };

        var ranked = keys
            .Select(key => new
            {
                Key = key,
                Matches = sample.Count(x =>
                    x.Signals.TryGetValue(
                        key,
                        out var count) &&
                    count >= ThresholdFor(
                        key,
                        x.Rounds)),
                TotalSignals = sample.Sum(x =>
                    x.Signals.TryGetValue(
                        key,
                        out var count)
                        ? count
                        : 0)
            })
            .OrderByDescending(x => x.Matches)
            .ThenByDescending(x => x.TotalSignals)
            .ToList();

        var best = ranked[0];
        if (best.Matches == 0)
            return Empty(sample.Count);

        var half = Math.Max(1, sample.Count / 2);
        var latest = sample.Take(half).ToList();
        var older = sample.Skip(half).ToList();

        int MatchCount(
            IEnumerable<MistakeMatchEntry> entries,
            string key)
            => entries.Count(x =>
                x.Signals.TryGetValue(
                    key,
                    out var count) &&
                count >= ThresholdFor(
                    key,
                    x.Rounds));

        var latestRate =
            (double)MatchCount(
                latest,
                best.Key) /
            Math.Max(1, latest.Count);

        var olderRate =
            older.Count == 0
                ? latestRate
                : (double)MatchCount(
                    older,
                    best.Key) /
                  older.Count;

        var trend =
            latestRate + 0.15 < olderRate
                ? "IMPROVING"
                : latestRate > olderRate + 0.15
                    ? "WORSENING"
                    : "PERSISTENT";

        return new MistakeLibrarySummary(
            best.Key,
            Label(best.Key),
            best.Matches,
            best.TotalSignals,
            sample.Count,
            trend,
            Focus(best.Key));
    }

    public static IReadOnlyList<MistakeLibrarySummary> TopAll(
        string? map = null,
        int recentMatches = 10)
    {
        List<MistakeMatchEntry> sample;

        lock (Gate)
        {
            sample = LoadUnsafe()
                .Where(x =>
                    string.IsNullOrWhiteSpace(map) ||
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.EndedUtc)
                .Take(Math.Clamp(recentMatches, 3, 30))
                .ToList();
        }

        if (sample.Count == 0)
            return Array.Empty<MistakeLibrarySummary>();

        return new[]
        {
            "discipline",
            "post_impact",
            "impact",
            "conversion"
        }
        .Select(key =>
        {
            var matches = sample.Count(x =>
                x.Signals.TryGetValue(
                    key,
                    out var count) &&
                count >= ThresholdFor(
                    key,
                    x.Rounds));

            var total = sample.Sum(x =>
                x.Signals.TryGetValue(
                    key,
                    out var count)
                    ? count
                    : 0);

            return new MistakeLibrarySummary(
                key,
                Label(key),
                matches,
                total,
                sample.Count,
                matches == 0
                    ? "CLEAR"
                    : "TRACKING",
                Focus(key));
        })
        .Where(x => x.Matches > 0)
        .OrderByDescending(x => x.Matches)
        .ThenByDescending(x => x.TotalSignals)
        .ToList();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }

    private static Dictionary<string,int> DetectSignals(
        IReadOnlyList<RoundRecord> rounds)
    {
        var resolved = rounds
            .Where(r => r.Won.HasValue)
            .ToList();

        return new Dictionary<string,int>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["discipline"] = rounds.Count(r =>
                r.DeathsRound > 0 &&
                r.KillsRound == 0),

            ["post_impact"] = rounds.Count(r =>
                r.KillsRound > 0 &&
                !r.Survived),

            ["impact"] = rounds.Count(r =>
                r.KillsRound == 0),

            ["conversion"] = resolved.Count(r =>
                r.KillsRound > 0 &&
                r.Won == false)
        };
    }

    private static int ThresholdFor(
        string key,
        int rounds)
        => key switch
        {
            "discipline" => Math.Max(2, (int)Math.Ceiling(rounds * 0.25)),
            "post_impact" => Math.Max(2, (int)Math.Ceiling(rounds * 0.25)),
            "impact" => Math.Max(3, (int)Math.Ceiling(rounds * 0.40)),
            "conversion" => Math.Max(2, (int)Math.Ceiling(rounds * 0.20)),
            _ => 2
        };

    private static string Label(
        string key)
        => key switch
        {
            "discipline" => "0-IMPACT DEATHS",
            "post_impact" => "DIE AFTER IMPACT",
            "impact" => "LOW-IMPACT ROUNDS",
            "conversion" => "ADVANTAGE CONVERSION",
            _ => key.ToUpperInvariant()
        };

    private static string Focus(
        string key)
        => key switch
        {
            "discipline" =>
                "First death must be tradeable. No isolated repeat peek.",
            "post_impact" =>
                "After first kill, break contact and reposition.",
            "impact" =>
                "Create one utility-supported or tradeable impact chance.",
            "conversion" =>
                "After impact, protect the advantage instead of hunting.",
            _ =>
                "Keep the round plan simple and repeatable."
        };

    private static MistakeLibrarySummary Empty(
        int matches)
        => new(
            "",
            "NO RECURRING LEAK",
            0,
            0,
            matches,
            "CLEAR",
            "Keep the current discipline.");

    private static List<MistakeMatchEntry> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new();

            var all =
                JsonSerializer.Deserialize<List<MistakeMatchEntry>>(
                    File.ReadAllText(FilePath))
                ?? new();

            foreach (var item in all)
            {
                item.Signals =
                    new Dictionary<string,int>(
                        item.Signals ??
                        new Dictionary<string,int>(),
                        StringComparer.OrdinalIgnoreCase);
            }

            return all;
        }
        catch
        {
            return new();
        }
    }

    private static void SaveUnsafe(
        List<MistakeMatchEntry> all)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    all,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch { }
    }
}
