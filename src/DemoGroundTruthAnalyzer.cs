using System.IO.Compression;
using System.Text.Json;
using DemoFile;
using DemoFile.Game.Cs;

namespace Sm0kiSoloCoach;

public sealed class GroundTruthPathSample
{
    public int Tick { get; set; }
    public string PlaceName { get; set; } = "";
    public string Zone { get; set; } = "OTHER";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class GroundTruthDemoRound
{
    public int Round { get; set; }
    public string Side { get; set; } = "";
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public bool Survived { get; set; } = true;
    public bool? Won { get; set; }
    public bool OpeningDuel { get; set; }
    public string OpeningResult { get; set; } = "";
    public int RoundStartTick { get; set; } = -1;
    public int FirstContactTick { get; set; } = -1;
    public int FirstKillTick { get; set; } = -1;
    public int DeathTick { get; set; } = -1;
    public string FirstContactPlace { get; set; } = "";
    public string FirstContactZone { get; set; } = "";
    public string DeathPlace { get; set; } = "";
    public string DeathZone { get; set; } = "";
    public bool BombPlantedByPlayer { get; set; }
    public string BombSite { get; set; } = "";
    public int UtilityThrown { get; set; }

    public double? SecondsToFirstContact =>
        RoundStartTick >= 0 &&
        FirstContactTick >= RoundStartTick
            ? (FirstContactTick - RoundStartTick) / 64.0
            : null;

    public double? SecondsAfterFirstKillToDeath =>
        FirstKillTick >= 0 &&
        DeathTick >= FirstKillTick
            ? (DeathTick - FirstKillTick) / 64.0
            : null;

    public List<GroundTruthPathSample> Path { get; set; } = new();
}

public sealed class GroundTruthRoundVerdict
{
    public int DemoRound { get; set; }
    public int RecommendedRound { get; set; }
    public string Side { get; set; } = "";
    public string RecommendedRoute { get; set; } = "";
    public string FirstMove { get; set; } = "";
    public string ContextKey { get; set; } = "";
    public string RouteEvidence { get; set; } = "UNKNOWN";
    public string Verdict { get; set; } = "INSUFFICIENT_EVIDENCE";
    public string Confidence { get; set; } = "LOW";
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public bool? Won { get; set; }
    public bool Survived { get; set; }
    public bool OpeningDuel { get; set; }
    public string OpeningResult { get; set; } = "";
    public int FirstContactTick { get; set; } = -1;
    public int FirstKillTick { get; set; } = -1;
    public int DeathTick { get; set; } = -1;
    public double? SecondsToFirstContact { get; set; }
    public double? SecondsAfterFirstKillToDeath { get; set; }
    public string FirstContactPlace { get; set; } = "";
    public string DeathPlace { get; set; } = "";
    public string ActualRoute { get; set; } = "";
    public double OutcomeScore { get; set; }
    public List<GroundTruthPathSample> Path { get; set; } = new();
}

public sealed class GroundTruthMatchReport
{
    public string MatchId { get; set; } = "";
    public string Map { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string RecommendationSessionId { get; set; } = "";
    public DateTime MatchFinishedUtc { get; set; }
    public DateTime AnalyzedUtc { get; set; } = DateTime.UtcNow;
    public int RoundAlignmentOffset { get; set; }
    public int MatchedRounds { get; set; }
    public int RouteSupportedRounds { get; set; }
    public int ExecutionDivergedRounds { get; set; }
    public int PlanReviewRounds { get; set; }
    public int PlanWorkedRounds { get; set; }
    public int UnknownRounds { get; set; }
    public string BiggestFinding { get; set; } = "";
    public List<GroundTruthRoundVerdict> Rounds { get; set; } = new();

    public string Compact =>
        $"{MatchedRounds} matched • {RouteSupportedRounds} route-confirmed • " +
        $"{ExecutionDivergedRounds} diverged • {PlanReviewRounds} plan-review";
}

public sealed record PlanGroundTruthStats(
    string ContextKey,
    int SupportedSamples,
    int PositiveOutcomes,
    int PoorOutcomes,
    int DivergedSamples)
{
    public double PoorRate =>
        SupportedSamples > 0
            ? (double)PoorOutcomes / SupportedSamples
            : 0;
}

public static class GroundTruthStore
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
            "ground-truth-v7.json");

    private static List<GroundTruthMatchReport>? _cache;

    public static void Save(
        GroundTruthMatchReport report)
    {
        lock (Gate)
        {
            var all = LoadUnsafe();
            var existing = all.FindIndex(x =>
                x.MatchId.Equals(
                    report.MatchId,
                    StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
                all[existing] = report;
            else
                all.Insert(0, report);

            _cache = all
                .OrderByDescending(x => x.MatchFinishedUtc)
                .Take(100)
                .ToList();

            SaveUnsafe();
        }
    }

    public static IReadOnlyList<GroundTruthMatchReport> Load()
    {
        lock (Gate)
            return LoadUnsafe()
                .OrderByDescending(x => x.MatchFinishedUtc)
                .ToList();
    }

    public static GroundTruthMatchReport? Find(
        string matchId)
    {
        lock (Gate)
            return LoadUnsafe()
                .FirstOrDefault(x =>
                    x.MatchId.Equals(
                        matchId,
                        StringComparison.OrdinalIgnoreCase));
    }

    public static PlanGroundTruthStats ContextStats(
        string contextKey)
    {
        lock (Gate)
        {
            var rounds = LoadUnsafe()
                .SelectMany(x => x.Rounds)
                .Where(x =>
                    x.ContextKey.Equals(
                        contextKey,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            var supported = rounds
                .Where(x =>
                    x.RouteEvidence == "SUPPORTED")
                .ToList();

            return new PlanGroundTruthStats(
                contextKey,
                supported.Count,
                supported.Count(x =>
                    x.OutcomeScore >= 0.62),
                supported.Count(x =>
                    x.OutcomeScore <= 0.35),
                rounds.Count(x =>
                    x.RouteEvidence == "CONTRADICTED"));
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

    private static List<GroundTruthMatchReport> LoadUnsafe()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (!File.Exists(FilePath))
                return _cache = new();

            return _cache =
                JsonSerializer.Deserialize<List<GroundTruthMatchReport>>(
                    File.ReadAllText(FilePath))
                ?? new();
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
}

public static class DemoGroundTruthAnalyzer
{
    public static async Task<GroundTruthMatchReport> AnalyzeSourceAsync(
        string sourcePath,
        string matchId,
        DateTime matchFinishedUtc,
        string nickname,
        string? steamId64,
        RecommendationSessionTrace? recommendationSession,
        CancellationToken cancellationToken = default)
    {
        var prepared =
            await PrepareDemoAsync(
                sourcePath,
                cancellationToken);

        try
        {
            var rounds =
                await ParseDemoAsync(
                    prepared.DemoPath,
                    nickname,
                    steamId64,
                    cancellationToken);

            var map =
                prepared.Map;

            if (string.IsNullOrWhiteSpace(map))
                map = rounds.Map;

            return BuildReport(
                matchId,
                matchFinishedUtc,
                nickname,
                map,
                rounds.Rounds,
                recommendationSession);
        }
        finally
        {
            prepared.Dispose();
        }
    }

    private sealed class ParsedDemo
    {
        public string Map { get; set; } = "";
        public List<GroundTruthDemoRound> Rounds { get; set; } = new();
    }

    private sealed class PreparedDemo : IDisposable
    {
        public string DemoPath { get; init; } = "";
        public string Map { get; init; } = "";
        public string? TempFile { get; init; }
        public string? TempDir { get; init; }

        public void Dispose()
        {
            if (!string.IsNullOrWhiteSpace(TempFile))
            {
                try { File.Delete(TempFile); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(TempDir))
            {
                try { Directory.Delete(TempDir, true); } catch { }
            }
        }
    }

    private static async Task<ParsedDemo> ParseDemoAsync(
        string demoPath,
        string nickname,
        string? steamId64,
        CancellationToken ct)
    {
        var parsed = new ParsedDemo();
        var demo = new CsDemoParser();
        var rounds =
            new Dictionary<int,GroundTruthDemoRound>();
        var firstDeathSeen =
            new HashSet<int>();

        var round = 0;
        var hasSteamId =
            ulong.TryParse(
                steamId64,
                out var targetSteamId);

        bool IsTarget(
            CCSPlayerController? player)
        {
            if (player == null)
                return false;

            if (hasSteamId &&
                player.SteamID == targetSteamId)
                return true;

            return player.PlayerName.Equals(
                nickname,
                StringComparison.OrdinalIgnoreCase);
        }

        static string SideOf(
            CCSPlayerController? player)
            => player?.CSTeamNum switch
            {
                CSTeamNumber.CounterTerrorist => "CT",
                CSTeamNumber.Terrorist => "T",
                _ => ""
            };

        static bool IsEnemy(
            CCSPlayerController? a,
            CCSPlayerController? b)
            => a != null &&
               b != null &&
               a.SteamID != b.SteamID &&
               a.CSTeamNum != b.CSTeamNum;

        GroundTruthDemoRound Current()
        {
            var r =
                Math.Max(1, round);

            if (!rounds.TryGetValue(
                    r,
                    out var value))
            {
                value =
                    new GroundTruthDemoRound
                    {
                        Round = r
                    };

                rounds[r] = value;
            }

            return value;
        }

        void AddPath(
            GroundTruthDemoRound target,
            int tick,
            CCSPlayerController? controller)
        {
            var pawn = controller?.Pawn;
            if (pawn == null)
                return;

            var csPawn = pawn as CCSPlayerPawn;
            var pos = pawn.Origin;
            var place =
                csPawn?.LastPlaceName ?? "";

            if (target.Side.Length == 0)
                target.Side =
                    SideOf(controller);

            var zone =
                OpponentDemoPatternAnalyzer
                    .NormalizeZone(place);

            var last =
                target.Path.LastOrDefault();

            if (last != null)
            {
                var dx =
                    pos.X - last.X;
                var dy =
                    pos.Y - last.Y;
                var dz =
                    pos.Z - last.Z;

                var moved =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);

                if (moved < 90 &&
                    place.Equals(
                        last.PlaceName,
                        StringComparison.OrdinalIgnoreCase))
                    return;
            }

            if (target.Path.Count >= 90)
                return;

            target.Path.Add(
                new GroundTruthPathSample
                {
                    Tick = tick,
                    PlaceName = place,
                    Zone = zone,
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z
                });
        }

        demo.PacketEvents.SvcServerInfo += e =>
        {
            if (!string.IsNullOrWhiteSpace(
                    e.MapName))
            {
                parsed.Map = e.MapName;
            }
        };

        demo.Source1GameEvents.RoundStart += _ =>
        {
            if (demo.GameRules.CSGamePhase ==
                CSGamePhase.WarmupRound)
                return;

            round++;
            var current = Current();
            current.RoundStartTick =
                demo.CurrentDemoTick.Value;
        };

        demo.Source1GameEvents.PlayerHurt += e =>
        {
            if (demo.GameRules.CSGamePhase ==
                    CSGamePhase.WarmupRound ||
                round <= 0)
                return;

            var targetVictim =
                IsTarget(e.Player);
            var targetAttacker =
                IsTarget(e.Attacker);

            if (!targetVictim &&
                !targetAttacker)
                return;

            if (!IsEnemy(
                    e.Player,
                    e.Attacker))
                return;

            var current = Current();

            if (current.FirstContactTick >= 0)
                return;

            current.FirstContactTick =
                demo.CurrentDemoTick.Value;

            var actor =
                targetAttacker
                    ? e.Attacker
                    : e.Player;

            if (current.Side.Length == 0)
                current.Side =
                    SideOf(actor);

            var pawn =
                actor?.Pawn;

            var csPawn =
                pawn as CCSPlayerPawn;

            current.FirstContactPlace =
                csPawn?.LastPlaceName ?? "";

            current.FirstContactZone =
                OpponentDemoPatternAnalyzer
                    .NormalizeZone(
                        current.FirstContactPlace);

            AddPath(
                current,
                current.FirstContactTick,
                actor);
        };

        demo.Source1GameEvents.PlayerDeath += e =>
        {
            if (demo.GameRules.CSGamePhase ==
                    CSGamePhase.WarmupRound ||
                round <= 0)
                return;

            var current = Current();
            var targetVictim =
                IsTarget(e.Player);
            var targetAttacker =
                IsTarget(e.Attacker);

            var isOpening =
                !firstDeathSeen.Contains(round);

            firstDeathSeen.Add(round);

            if (!targetVictim &&
                !targetAttacker)
                return;

            var actor =
                targetAttacker
                    ? e.Attacker
                    : e.Player;

            if (current.Side.Length == 0)
                current.Side =
                    SideOf(actor);

            var pawn =
                targetAttacker
                    ? e.Attacker?.Pawn
                    : e.PlayerPawn;

            var csPawn =
                pawn as CCSPlayerPawn;

            if (pawn != null)
            {
                var place =
                    csPawn?.LastPlaceName ?? "";

                if (current.FirstContactTick < 0)
                {
                    current.FirstContactTick =
                        demo.CurrentDemoTick.Value;
                }

                if (string.IsNullOrWhiteSpace(
                        current.FirstContactPlace))
                {
                    current.FirstContactPlace =
                        place;
                    current.FirstContactZone =
                        OpponentDemoPatternAnalyzer
                            .NormalizeZone(place);
                }
            }

            if (targetAttacker)
            {
                current.Kills++;

                if (current.FirstKillTick < 0)
                    current.FirstKillTick =
                        demo.CurrentDemoTick.Value;
            }

            if (targetVictim)
            {
                current.Deaths++;
                current.Survived = false;
                current.DeathTick =
                    demo.CurrentDemoTick.Value;

                if (pawn != null)
                {
                    current.DeathPlace =
                        csPawn?.LastPlaceName ?? "";
                    current.DeathZone =
                        OpponentDemoPatternAnalyzer
                            .NormalizeZone(
                                current.DeathPlace);
                }
            }

            if (isOpening)
            {
                current.OpeningDuel = true;
                current.OpeningResult =
                    targetAttacker
                        ? "KILL"
                        : "DEATH";
            }

            AddPath(
                current,
                0,
                actor);
        };

        demo.Source1GameEvents.HegrenadeDetonate += e =>
        {
            if (round > 0 &&
                IsTarget(e.Player))
                Current().UtilityThrown++;
        };

        demo.Source1GameEvents.FlashbangDetonate += e =>
        {
            if (round > 0 &&
                IsTarget(e.Player))
                Current().UtilityThrown++;
        };

        demo.Source1GameEvents.SmokegrenadeDetonate += e =>
        {
            if (round > 0 &&
                IsTarget(e.Player))
                Current().UtilityThrown++;
        };

        demo.Source1GameEvents.MolotovDetonate += e =>
        {
            if (round > 0 &&
                IsTarget(e.Player))
                Current().UtilityThrown++;
        };

        demo.Source1GameEvents.BombPlanted += e =>
        {
            if (round <= 0 ||
                !IsTarget(e.Player))
                return;

            var current = Current();
            current.BombPlantedByPlayer = true;

            var pawn =
                e.Player?.Pawn;

            if (pawn != null)
            {
                var pos = pawn.Origin;
                current.BombSite =
                    RadarCatalog.BombSiteFromWorld(
                        parsed.Map,
                        pos.X,
                        pos.Y,
                        pos.Z);

                AddPath(
                    current,
                    0,
                    e.Player);
            }

            if (string.IsNullOrWhiteSpace(
                    current.BombSite))
            {
                current.BombSite =
                    e.Site.ToString();
            }
        };

        demo.Source1GameEvents.RoundEnd += e =>
        {
            if (round <= 0)
                return;

            var current = Current();

            if (current.Side.Length == 0)
                return;

            var winner =
                WinnerSide(
                    e.Winner.ToString());

            if (winner.Length > 0)
                current.Won =
                    winner.Equals(
                        current.Side,
                        StringComparison.OrdinalIgnoreCase);
        };

        using var fs =
            File.OpenRead(demoPath);

        var reader =
            DemoFileReader.Create(
                demo,
                fs);

        await reader.StartReadingAsync(ct);

        var lastSampleTick =
            int.MinValue;

        while (await reader.MoveNextAsync(ct))
        {
            if (round <= 0 ||
                demo.GameRules.CSGamePhase ==
                    CSGamePhase.WarmupRound)
                continue;

            var tickNumber =
                demo.CurrentDemoTick.Value;

            if (tickNumber < 0 ||
                (lastSampleTick != int.MinValue &&
                 tickNumber - lastSampleTick < 64))
                continue;

            var target =
                demo.Players
                    .FirstOrDefault(IsTarget);

            if (target == null)
                continue;

            AddPath(
                Current(),
                tickNumber,
                target);

            lastSampleTick =
                tickNumber;
        }

        parsed.Rounds = rounds.Values
            .OrderBy(x => x.Round)
            .ToList();

        return parsed;
    }

    private static GroundTruthMatchReport BuildReport(
        string matchId,
        DateTime matchFinishedUtc,
        string nickname,
        string map,
        IReadOnlyList<GroundTruthDemoRound> demoRounds,
        RecommendationSessionTrace? session)
    {
        var report =
            new GroundTruthMatchReport
            {
                MatchId = matchId,
                Map = map,
                Nickname = nickname,
                MatchFinishedUtc = matchFinishedUtc,
                RecommendationSessionId =
                    session?.Id ?? ""
            };

        if (session == null ||
            session.Rounds.Count == 0)
        {
            report.UnknownRounds =
                demoRounds.Count;
            report.BiggestFinding =
                "Demo parsed, but no matching recommendation session was found.";
            return report;
        }

        var offset =
            FindRoundOffset(
                session.Rounds,
                demoRounds);

        report.RoundAlignmentOffset =
            offset;

        foreach (var trace in session.Rounds)
        {
            var demoRound =
                demoRounds.FirstOrDefault(x =>
                    x.Round ==
                    trace.Round + offset);

            if (demoRound == null)
                continue;

            var evidence =
                EvaluateRoute(
                    map,
                    trace.Route,
                    demoRound.Path,
                    demoRound.FirstContactPlace,
                    demoRound.DeathPlace);

            var outcome =
                OutcomeScore(demoRound);

            var verdict =
                Verdict(
                    trace.ContextKey,
                    evidence,
                    outcome,
                    demoRound,
                    GroundTruthStore.ContextStats(
                        trace.ContextKey));

            var confidence =
                EvidenceConfidence(
                    evidence,
                    demoRound.Path.Count,
                    demoRound.FirstContactPlace);

            var routeText =
                BuildActualRoute(
                    demoRound.Path);

            report.Rounds.Add(
                new GroundTruthRoundVerdict
                {
                    DemoRound = demoRound.Round,
                    RecommendedRound = trace.Round,
                    Side = demoRound.Side,
                    RecommendedRoute = trace.Route,
                    FirstMove = trace.FirstMove,
                    ContextKey = trace.ContextKey,
                    RouteEvidence = evidence,
                    Verdict = verdict,
                    Confidence = confidence,
                    Kills = demoRound.Kills,
                    Deaths = demoRound.Deaths,
                    Won = demoRound.Won,
                    Survived = demoRound.Survived,
                    OpeningDuel =
                        demoRound.OpeningDuel,
                    OpeningResult =
                        demoRound.OpeningResult,
                    FirstContactTick =
                        demoRound.FirstContactTick,
                    FirstKillTick =
                        demoRound.FirstKillTick,
                    DeathTick =
                        demoRound.DeathTick,
                    SecondsToFirstContact =
                        demoRound.SecondsToFirstContact,
                    SecondsAfterFirstKillToDeath =
                        demoRound.SecondsAfterFirstKillToDeath,
                    FirstContactPlace =
                        demoRound.FirstContactPlace,
                    DeathPlace =
                        demoRound.DeathPlace,
                    ActualRoute = routeText,
                    OutcomeScore = outcome,
                    Path = demoRound.Path
                        .ToList()
                });
        }

        report.MatchedRounds =
            report.Rounds.Count;

        report.RouteSupportedRounds =
            report.Rounds.Count(x =>
                x.RouteEvidence ==
                "SUPPORTED");

        report.ExecutionDivergedRounds =
            report.Rounds.Count(x =>
                x.RouteEvidence ==
                "CONTRADICTED");

        report.PlanReviewRounds =
            report.Rounds.Count(x =>
                x.Verdict is
                    "PLAN_REVIEW" or
                    "PLAN_UNDERPERFORMING");

        report.PlanWorkedRounds =
            report.Rounds.Count(x =>
                x.Verdict ==
                "PLAN_WORKED");

        report.UnknownRounds =
            report.Rounds.Count(x =>
                x.RouteEvidence ==
                "UNKNOWN");

        report.BiggestFinding =
            BuildFinding(report);

        return report;
    }

    private static int FindRoundOffset(
        IReadOnlyList<RecommendationRoundTrace> traces,
        IReadOnlyList<GroundTruthDemoRound> demoRounds)
    {
        var bestOffset = 0;
        var bestScore =
            double.MinValue;

        for (var offset = -2;
             offset <= 2;
             offset++)
        {
            double score = 0;
            int matched = 0;

            foreach (var trace in traces)
            {
                var demo =
                    demoRounds.FirstOrDefault(x =>
                        x.Round ==
                        trace.Round + offset);

                if (demo == null)
                    continue;

                matched++;

                if (!string.IsNullOrWhiteSpace(
                        trace.Side) &&
                    trace.Side.Equals(
                        demo.Side,
                        StringComparison.OrdinalIgnoreCase))
                    score += 3;

                if (trace.KillsRound ==
                    demo.Kills)
                    score += 1.5;

                if (trace.DeathsRound ==
                    demo.Deaths)
                    score += 1;

                if (trace.Won.HasValue &&
                    demo.Won.HasValue &&
                    trace.Won.Value ==
                    demo.Won.Value)
                    score += 1.5;
            }

            score +=
                matched * 0.2;

            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }

    private static string EvaluateRoute(
        string map,
        string recommended,
        IReadOnlyList<GroundTruthPathSample> path,
        string firstContactPlace,
        string deathPlace)
    {
        if (string.IsNullOrWhiteSpace(
                recommended))
            return "UNKNOWN";

        var graphEvidence =
            MapKnowledgeGraphV7.RouteEvidence(
                map,
                recommended,
                path);

        if (graphEvidence is
            "SUPPORTED" or
            "CONTRADICTED")
            return graphEvidence;

        var route =
            Normalize(recommended);

        var places = path
            .Select(x => Normalize(x.PlaceName))
            .Where(x => x.Length > 0)
            .ToList();

        if (!string.IsNullOrWhiteSpace(
                firstContactPlace))
        {
            places.Add(
                Normalize(
                    firstContactPlace));
        }

        if (!string.IsNullOrWhiteSpace(
                deathPlace))
        {
            places.Add(
                Normalize(
                    deathPlace));
        }

        var tokens =
            RouteTokens(route);

        if (tokens.Count > 0 &&
            tokens.Any(token =>
                places.Any(place =>
                    place.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase))))
        {
            return "SUPPORTED";
        }

        var expectedZones =
            ExpectedZones(route);

        var observedZones = path
            .Select(x => x.Zone)
            .Where(x =>
                x is "A" or "B" or "MID")
            .ToList();

        if (expectedZones.Count > 0 &&
            observedZones.Any(x =>
                expectedZones.Contains(x)))
        {
            return "SUPPORTED";
        }

        if (expectedZones.Count == 1 &&
            observedZones.Count >= 3 &&
            !observedZones.Any(x =>
                expectedZones.Contains(x)))
        {
            var dominant =
                observedZones
                    .GroupBy(x => x)
                    .OrderByDescending(x => x.Count())
                    .First();

            if (dominant.Count() >= 2)
                return "CONTRADICTED";
        }

        return "UNKNOWN";
    }

    private static List<string> RouteTokens(
        string route)
    {
        var known = new[]
        {
            "long",
            "short",
            "cat",
            "connector",
            "window",
            "tunnel",
            "banana",
            "apps",
            "apart",
            "palace",
            "monster",
            "water",
            "cave",
            "donut",
            "heaven",
            "hell",
            "lobby",
            "hut",
            "squeaky",
            "ivy",
            "popdog",
            "toilet",
            "ramp",
            "main",
            "garage"
        };

        return known
            .Where(route.Contains)
            .ToList();
    }

    private static HashSet<string> ExpectedZones(
        string route)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (route.Contains("mid") ||
            route.Contains("short") ||
            route.Contains("cat") ||
            route.Contains("connector") ||
            route.Contains("window") ||
            route.Contains("water"))
            result.Add("MID");

        if (route.Contains("tunnel") ||
            route.Contains("banana") ||
            route.Contains("monster") ||
            route.Contains("bapps") ||
            route.Contains("bmain") ||
            route.StartsWith("b"))
            result.Add("B");

        if (route.Contains("long") ||
            route.Contains("palace") ||
            route.Contains("amain") ||
            route.Contains("ivy") ||
            route.Contains("hut") ||
            route.Contains("squeaky") ||
            route.StartsWith("a"))
            result.Add("A");

        return result;
    }

    private static string Verdict(
        string contextKey,
        string evidence,
        double outcome,
        GroundTruthDemoRound round,
        PlanGroundTruthStats prior)
    {
        if (evidence == "CONTRADICTED")
            return "EXECUTION_DIVERGED";

        if (evidence != "SUPPORTED")
            return "INSUFFICIENT_EVIDENCE";

        // Route was followed and produced impact, but the player died almost
        // immediately after their first kill. This is a measurable execution
        // leak signal; it is not evidence that the route itself was bad.
        if (round.Kills >= 1 &&
            round.Deaths > 0 &&
            round.SecondsAfterFirstKillToDeath is double postKill &&
            postKill >= 0 &&
            postKill <= 5.0)
        {
            return "POST_IMPACT_EXECUTION";
        }

        if (outcome >= 0.62)
            return "PLAN_WORKED";

        if (outcome > 0.35)
            return "PLAN_MIXED";

        var supported =
            prior.SupportedSamples + 1;

        var poor =
            prior.PoorOutcomes + 1;

        if (supported >= 3 &&
            (double)poor / supported >= 0.65)
            return "PLAN_UNDERPERFORMING";

        return "PLAN_REVIEW";
    }

    private static string EvidenceConfidence(
        string evidence,
        int pathSamples,
        string firstContact)
    {
        if (evidence == "UNKNOWN")
            return "LOW";

        if (pathSamples >= 8 &&
            !string.IsNullOrWhiteSpace(
                firstContact))
            return "HIGH";

        if (pathSamples >= 4)
            return "MEDIUM";

        return "LOW";
    }

    private static double OutcomeScore(
        GroundTruthDemoRound round)
    {
        var score = 0.15;

        if (round.Won == true)
            score += 0.40;

        if (round.Survived)
            score += 0.16;

        if (round.Kills >= 1)
            score += 0.18;

        if (round.Kills >= 2)
            score += 0.10;

        if (round.BombPlantedByPlayer &&
            round.Side.Equals(
                "T",
                StringComparison.OrdinalIgnoreCase))
            score += 0.06;

        if (round.Deaths > 0 &&
            round.Kills == 0)
            score -= 0.14;

        return Math.Clamp(
            score,
            0,
            1);
    }

    private static string BuildActualRoute(
        IReadOnlyList<GroundTruthPathSample> path)
    {
        var places =
            new List<string>();

        foreach (var sample in path)
        {
            var place =
                (sample.PlaceName ?? "").Trim();

            if (place.Length == 0)
                continue;

            if (places.Count == 0 ||
                !places[^1].Equals(
                    place,
                    StringComparison.OrdinalIgnoreCase))
            {
                places.Add(place);
            }
        }

        return places.Count == 0
            ? "—"
            : string.Join(
                " → ",
                places.Take(8));
    }

    private static string BuildFinding(
        GroundTruthMatchReport report)
    {
        if (report.MatchedRounds == 0)
            return "No recommendation rounds could be aligned to this demo.";

        var postImpact =
            report.Rounds.Count(x =>
                x.Verdict ==
                "POST_IMPACT_EXECUTION");

        if (postImpact >= 3)
        {
            return
                $"{postImpact} route-confirmed rounds show a death within 5s after first kill; plan created impact, post-impact execution is the stronger leak signal.";
        }

        if (report.ExecutionDivergedRounds >=
            Math.Max(
                3,
                report.MatchedRounds / 3))
        {
            return
                $"Execution diverged from the recommended route in {report.ExecutionDivergedRounds}/{report.MatchedRounds} matched rounds.";
        }

        if (report.PlanReviewRounds >= 3)
        {
            return
                $"{report.PlanReviewRounds} route-confirmed rounds need plan review; repeated contexts will be de-weighted by the Digital Twin.";
        }

        if (report.PlanWorkedRounds >=
            Math.Max(
                3,
                report.MatchedRounds / 2))
        {
            return
                $"Coach route was supported by demo evidence and produced positive outcomes in {report.PlanWorkedRounds} rounds.";
        }

        return
            "Ground Truth is mixed; keep collecting verified demo rounds before making a strong plan/execution judgment.";
    }

    private static string WinnerSide(
        string raw)
    {
        var normalized =
            (raw ?? "")
            .Trim()
            .ToLowerInvariant();

        if (normalized.Contains(
                "counter") ||
            normalized == "3" ||
            normalized == "ct")
            return "CT";

        if (normalized.Contains(
                "terror") ||
            normalized == "2" ||
            normalized == "t")
            return "T";

        return "";
    }

    private static string Normalize(
        string value)
        => (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "");

    private static async Task<PreparedDemo> PrepareDemoAsync(
        string sourcePath,
        CancellationToken ct)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(
                "Demo source ni bil najden.",
                sourcePath);

        if (sourcePath.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            var dir =
                Path.Combine(
                    Path.GetTempPath(),
                    "smoki-gt-" +
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dir);

            try
            {
                using var archive =
                    ZipFile.OpenRead(sourcePath);

                var entry =
                    archive.Entries
                        .Where(x =>
                            x.FullName.EndsWith(
                                ".dem",
                                StringComparison.OrdinalIgnoreCase) ||
                            x.FullName.EndsWith(
                                ".dem.gz",
                                StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.Length)
                        .FirstOrDefault();

                if (entry == null)
                    throw new InvalidOperationException(
                        "ZIP nima CS2 demo datoteke.");

                var extracted =
                    Path.Combine(
                        dir,
                        Path.GetFileName(
                            entry.FullName));

                await using (var input = entry.Open())
                await using (var output = File.Create(extracted))
                    await input.CopyToAsync(output, ct);

                if (extracted.EndsWith(
                        ".gz",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var expanded =
                        Path.Combine(
                            dir,
                            "ground-truth.dem");

                    await ExpandGzipAsync(
                        extracted,
                        expanded,
                        ct);

                    return new PreparedDemo
                    {
                        DemoPath = expanded,
                        TempDir = dir
                    };
                }

                return new PreparedDemo
                {
                    DemoPath = extracted,
                    TempDir = dir
                };
            }
            catch
            {
                try
                {
                    Directory.Delete(
                        dir,
                        true);
                }
                catch { }

                throw;
            }
        }

        if (sourcePath.EndsWith(
                ".gz",
                StringComparison.OrdinalIgnoreCase))
        {
            var expanded =
                Path.Combine(
                    Path.GetTempPath(),
                    "smoki-gt-" +
                    Guid.NewGuid().ToString("N") +
                    ".dem");

            await ExpandGzipAsync(
                sourcePath,
                expanded,
                ct);

            return new PreparedDemo
            {
                DemoPath = expanded,
                TempFile = expanded
            };
        }

        return new PreparedDemo
        {
            DemoPath = sourcePath
        };
    }

    private static async Task ExpandGzipAsync(
        string source,
        string destination,
        CancellationToken ct)
    {
        await using var input =
            File.OpenRead(source);

        await using var gzip =
            new GZipStream(
                input,
                CompressionMode.Decompress);

        await using var output =
            File.Create(destination);

        await gzip.CopyToAsync(
            output,
            ct);
    }
}
