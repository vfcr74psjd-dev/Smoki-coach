using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class AdaptiveRouteStat
{
    public string Map { get; set; } = "";
    public string Side { get; set; } = "";
    public string Route { get; set; } = "";
    public int Attempts { get; set; }
    public int Wins { get; set; }
    public int Survived { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int ZeroImpactDeaths { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public double WinRate => Attempts > 0 ? (double)Wins / Attempts : 0.5;
    public double SurvivalRate => Attempts > 0 ? (double)Survived / Attempts : 0.5;
    public double ImpactRate => Attempts > 0
        ? Math.Clamp((double)Kills / Math.Max(1, Attempts * 2), 0, 1)
        : 0.5;

    public double SuccessScore
    {
        get
        {
            if (Attempts <= 0) return 0.5;

            var zeroDeathPenalty =
                Math.Clamp((double)ZeroImpactDeaths / Attempts, 0, 1);

            return Math.Clamp(
                WinRate * 0.50 +
                SurvivalRate * 0.20 +
                ImpactRate * 0.30 -
                zeroDeathPenalty * 0.12,
                0,
                1);
        }
    }
}

public static class AdaptivePlaybookStore
{
    private static readonly object Gate = new();
    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach");

    private static readonly string FilePath =
        Path.Combine(Dir, "adaptive-playbook.json");

    private static List<AdaptiveRouteStat>? _cache;

    private static List<AdaptiveRouteStat> LoadUnsafe()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (!File.Exists(FilePath))
                return _cache = new();

            _cache =
                JsonSerializer.Deserialize<List<AdaptiveRouteStat>>(
                    File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            _cache = new();
        }

        return _cache;
    }

    public static AdaptiveRouteStat? Find(
        string map,
        string side,
        string route)
    {
        if (string.IsNullOrWhiteSpace(map) ||
            string.IsNullOrWhiteSpace(side) ||
            string.IsNullOrWhiteSpace(route))
            return null;

        lock (Gate)
        {
            var found = LoadUnsafe()
                .FirstOrDefault(x =>
                    x.Map.Equals(map, StringComparison.OrdinalIgnoreCase) &&
                    x.Side.Equals(side, StringComparison.OrdinalIgnoreCase) &&
                    x.Route.Equals(route, StringComparison.OrdinalIgnoreCase));

            if (found == null)
                return null;

            return new AdaptiveRouteStat
            {
                Map = found.Map,
                Side = found.Side,
                Route = found.Route,
                Attempts = found.Attempts,
                Wins = found.Wins,
                Survived = found.Survived,
                Kills = found.Kills,
                Deaths = found.Deaths,
                ZeroImpactDeaths = found.ZeroImpactDeaths,
                UpdatedUtc = found.UpdatedUtc
            };
        }
    }

    public static void Record(
        string map,
        RoundRecord round,
        string route)
    {
        if (string.IsNullOrWhiteSpace(map) ||
            string.IsNullOrWhiteSpace(round.Side) ||
            string.IsNullOrWhiteSpace(route))
            return;

        lock (Gate)
        {
            var all = LoadUnsafe();
            var stat = all.FirstOrDefault(x =>
                x.Map.Equals(map, StringComparison.OrdinalIgnoreCase) &&
                x.Side.Equals(round.Side, StringComparison.OrdinalIgnoreCase) &&
                x.Route.Equals(route, StringComparison.OrdinalIgnoreCase));

            if (stat == null)
            {
                stat = new AdaptiveRouteStat
                {
                    Map = map,
                    Side = round.Side,
                    Route = route
                };
                all.Add(stat);
            }

            stat.Attempts++;
            if (round.Won == true) stat.Wins++;
            if (round.Survived) stat.Survived++;
            stat.Kills += Math.Max(0, round.KillsRound);
            stat.Deaths += Math.Max(0, round.DeathsRound);
            if (round.DeathsRound > 0 && round.KillsRound == 0)
                stat.ZeroImpactDeaths++;
            stat.UpdatedUtc = DateTime.UtcNow;

            // Keep a compact personal playbook. Old route experiments should
            // not grow the file forever.
            _cache = all
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(400)
                .ToList();

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(
                    FilePath,
                    JsonSerializer.Serialize(
                        _cache,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _cache = new();
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }
}
