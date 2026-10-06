using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class RecommendationRoundTrace
{
    public int Round { get; set; }
    public string Side { get; set; } = "";
    public string Intent { get; set; } = "";
    public string Route { get; set; } = "";
    public string FirstMove { get; set; } = "";
    public string Fallback { get; set; } = "";
    public string Expect { get; set; } = "";
    public string Focus { get; set; } = "";
    public string SpawnBias { get; set; } = "";
    public string RoundType { get; set; } = "";
    public int DecisionScore { get; set; }
    public string ContextKey { get; set; } = "";
    public int KillsRound { get; set; }
    public int DeathsRound { get; set; }
    public bool? Won { get; set; }
    public bool Survived { get; set; }
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RecommendationSessionTrace
{
    public string Id { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Map { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public List<RecommendationRoundTrace> Rounds { get; set; } = new();
}

public static class RecommendationTraceStore
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
            "recommendation-trace-v7.json");

    private static List<RecommendationSessionTrace>? _cache;

    public static void RecordRound(
        DateTime sessionStartedUtc,
        string nickname,
        string map,
        RoundRecord round,
        TacticalPlanV6 plan)
    {
        if (string.IsNullOrWhiteSpace(map) ||
            round.Round <= 0 ||
            string.IsNullOrWhiteSpace(plan.Route))
            return;

        lock (Gate)
        {
            var all = LoadUnsafe();
            var id = BuildSessionId(
                nickname,
                map,
                sessionStartedUtc);

            var session = all.FirstOrDefault(x =>
                x.Id.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase));

            if (session == null)
            {
                session = new RecommendationSessionTrace
                {
                    Id = id,
                    Nickname = nickname,
                    Map = map,
                    StartedUtc = sessionStartedUtc
                };
                all.Add(session);
            }

            var existing = session.Rounds
                .FirstOrDefault(x =>
                    x.Round == round.Round);

            var trace = existing ??
                        new RecommendationRoundTrace
                        {
                            Round = round.Round
                        };

            trace.Side = round.Side;
            trace.Intent =
                CoachEngine.NormalizeRoundIntent(
                    round.Intent);
            trace.Route = plan.Route;
            trace.FirstMove = plan.FirstMove;
            trace.Fallback = plan.Fallback;
            trace.Expect = plan.Expect;
            trace.Focus = plan.Focus;
            trace.SpawnBias = plan.SpawnBias;
            trace.RoundType = plan.RoundType;
            trace.DecisionScore = plan.DecisionScore;
            trace.ContextKey = BuildContextKey(
                map,
                round.Side,
                trace.Intent,
                plan.Route,
                plan.SpawnBias,
                plan.RoundType);
            trace.KillsRound = round.KillsRound;
            trace.DeathsRound = round.DeathsRound;
            trace.Won = round.Won;
            trace.Survived = round.Survived;
            trace.RecordedUtc = DateTime.UtcNow;

            if (existing == null)
                session.Rounds.Add(trace);

            session.Rounds = session.Rounds
                .OrderBy(x => x.Round)
                .ToList();

            _cache = all
                .OrderByDescending(x => x.StartedUtc)
                .Take(80)
                .ToList();

            SaveUnsafe();
        }
    }

    public static void MarkEnded(
        DateTime sessionStartedUtc,
        string nickname,
        string map,
        DateTime endedUtc)
    {
        lock (Gate)
        {
            var id = BuildSessionId(
                nickname,
                map,
                sessionStartedUtc);

            var session = LoadUnsafe()
                .FirstOrDefault(x =>
                    x.Id.Equals(
                        id,
                        StringComparison.OrdinalIgnoreCase));

            if (session == null)
                return;

            if (session.EndedUtc == default ||
                endedUtc < session.EndedUtc)
            {
                session.EndedUtc = endedUtc;
            }

            SaveUnsafe();
        }
    }

    public static RecommendationSessionTrace? FindBestSession(
        string nickname,
        string map,
        DateTime matchFinishedUtc)
    {
        lock (Gate)
        {
            var candidates = LoadUnsafe()
                .Where(x =>
                    x.Rounds.Count >= 4 &&
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(nickname) ||
                     x.Nickname.Equals(
                         nickname,
                         StringComparison.OrdinalIgnoreCase)))
                .Select(x => new
                {
                    Session = x,
                    Anchor =
                        x.EndedUtc != default
                            ? x.EndedUtc
                            : x.StartedUtc.AddMinutes(45)
                })
                .Select(x => new
                {
                    x.Session,
                    Distance =
                        Math.Abs(
                            (x.Anchor -
                             matchFinishedUtc).TotalMinutes)
                })
                .Where(x => x.Distance <= 240)
                .OrderBy(x => x.Distance)
                .ThenByDescending(x =>
                    x.Session.Rounds.Count)
                .FirstOrDefault();

            return candidates == null
                ? null
                : Clone(candidates.Session);
        }
    }

    public static IReadOnlyList<RecommendationSessionTrace> Load()
    {
        lock (Gate)
            return LoadUnsafe()
                .OrderByDescending(x => x.StartedUtc)
                .Select(Clone)
                .ToList();
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

    public static string BuildContextKey(
        string map,
        string side,
        string intent,
        string route,
        string spawnBias,
        string roundType)
        => string.Join(
            "|",
            (map ?? "").Trim().ToLowerInvariant(),
            (side ?? "").Trim().ToUpperInvariant(),
            (intent ?? "").Trim().ToUpperInvariant(),
            (route ?? "").Trim().ToLowerInvariant(),
            (spawnBias ?? "").Trim().ToUpperInvariant(),
            (roundType ?? "").Trim().ToLowerInvariant());

    private static string BuildSessionId(
        string nickname,
        string map,
        DateTime startedUtc)
        => $"{nickname.Trim().ToLowerInvariant()}|" +
           $"{map.Trim().ToLowerInvariant()}|" +
           $"{startedUtc.ToUniversalTime():O}";

    private static List<RecommendationSessionTrace> LoadUnsafe()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (!File.Exists(FilePath))
                return _cache = new();

            var loaded =
                JsonSerializer.Deserialize<List<RecommendationSessionTrace>>(
                    File.ReadAllText(FilePath))
                ?? new();

            foreach (var session in loaded)
                session.Rounds ??= new();

            return _cache = loaded;
        }
        catch
        {
            return _cache = new();
        }
    }

    private static void SaveUnsafe()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    _cache ?? new(),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch { }
    }

    private static RecommendationSessionTrace Clone(
        RecommendationSessionTrace x)
        => new()
        {
            Id = x.Id,
            Nickname = x.Nickname,
            Map = x.Map,
            StartedUtc = x.StartedUtc,
            EndedUtc = x.EndedUtc,
            Rounds = x.Rounds
                .Select(r => new RecommendationRoundTrace
                {
                    Round = r.Round,
                    Side = r.Side,
                    Intent = r.Intent,
                    Route = r.Route,
                    FirstMove = r.FirstMove,
                    Fallback = r.Fallback,
                    Expect = r.Expect,
                    Focus = r.Focus,
                    SpawnBias = r.SpawnBias,
                    RoundType = r.RoundType,
                    DecisionScore = r.DecisionScore,
                    ContextKey = r.ContextKey,
                    KillsRound = r.KillsRound,
                    DeathsRound = r.DeathsRound,
                    Won = r.Won,
                    Survived = r.Survived,
                    RecordedUtc = r.RecordedUtc
                })
                .ToList()
        };
}
