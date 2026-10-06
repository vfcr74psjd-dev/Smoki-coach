using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class DigitalTwinRouteNode
{
    public string Map { get; set; } = "";
    public string Side { get; set; } = "";
    public string Intent { get; set; } = "";
    public string Route { get; set; } = "";
    public string SpawnBias { get; set; } = "";
    public string RoundType { get; set; } = "";
    public int Attempts { get; set; }
    public int Wins { get; set; }
    public int Survived { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int MultiKillRounds { get; set; }
    public int ZeroImpactDeaths { get; set; }
    public int ImpactLostRounds { get; set; }
    public double OutcomeScoreSum { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public double WinRate =>
        Attempts > 0
            ? (double)Wins / Attempts
            : 0.5;

    public double SurvivalRate =>
        Attempts > 0
            ? (double)Survived / Attempts
            : 0.5;

    public double KillsPerRound =>
        Attempts > 0
            ? (double)Kills / Attempts
            : 0;

    public double OutcomeScore =>
        Attempts > 0
            ? OutcomeScoreSum / Attempts
            : 0.5;
}

public sealed class CoachDecisionCalibration
{
    public string ContextKey { get; set; } = "";
    public string Map { get; set; } = "";
    public string Side { get; set; } = "";
    public string Route { get; set; } = "";
    public int Samples { get; set; }
    public double PredictedScoreSum { get; set; }
    public double ActualOutcomeSum { get; set; }
    public double AbsoluteErrorSum { get; set; }
    public int OverPredictedFailures { get; set; }
    public int UnderPredictedSuccesses { get; set; }
    public int GroundTruthVerified { get; set; }
    public int GroundTruthDiverged { get; set; }
    public int GroundTruthPositive { get; set; }
    public int GroundTruthPoor { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public double AveragePrediction =>
        Samples > 0
            ? PredictedScoreSum / Samples
            : 0.5;

    public double AverageOutcome =>
        Samples > 0
            ? ActualOutcomeSum / Samples
            : 0.5;

    public double CalibrationError =>
        Samples > 0
            ? AbsoluteErrorSum / Samples
            : 0;

    public double TrustAdjustment
    {
        get
        {
            if (Samples < 3)
                return 0;

            var delta =
                AverageOutcome -
                AveragePrediction;

            var effectiveSamples =
                Math.Max(
                    1,
                    Samples - GroundTruthDiverged);

            var sampleWeight =
                Math.Min(
                    1.0,
                    effectiveSamples / 10.0);

            // Before demo verification, live outcome correlation is useful but
            // deliberately weak. Ground Truth raises trust only when the demo
            // supports that the recommended route was actually followed.
            var verificationWeight =
                GroundTruthVerified > 0
                    ? Math.Clamp(
                        0.35 +
                        (double)GroundTruthVerified /
                        Math.Max(1, Samples) *
                        0.65,
                        0.35,
                        1.0)
                    : 0.35;

            var raw =
                delta *
                28.0 *
                sampleWeight *
                verificationWeight;

            if (GroundTruthPoor >= 3)
                raw -= Math.Min(
                    7,
                    GroundTruthPoor * 1.3);

            if (GroundTruthPositive >= 3)
                raw += Math.Min(
                    6,
                    GroundTruthPositive);

            // Legacy live-only indicators remain a small secondary signal.
            if (OverPredictedFailures >= 3)
                raw -= Math.Min(
                    3,
                    OverPredictedFailures * 0.5);

            if (UnderPredictedSuccesses >= 3)
                raw += Math.Min(
                    2,
                    UnderPredictedSuccesses * 0.35);

            return Math.Clamp(raw, -14, 12);
        }
    }
}

public sealed class DigitalTwinState
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<DigitalTwinRouteNode> Routes { get; set; } = new();
    public List<CoachDecisionCalibration> Decisions { get; set; } = new();
}

public sealed record DigitalTwinSnapshot(
    string Map,
    string Side,
    int Samples,
    string Archetype,
    string BestRoute,
    string RiskRoute,
    double BestRouteScore,
    double SurvivalRate,
    double KillsPerRound,
    string Confidence)
{
    public string Compact =>
        Samples <= 0
            ? "Digital Twin learning"
            : $"{Archetype} • {Samples} rounds • best {BestRoute} • {Confidence}";
}

public static class DigitalTwinStore
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
            "digital-twin-v7.json");

    private static DigitalTwinState? _cache;

    public static IReadOnlyList<DigitalTwinRouteNode> LoadRoutes(
        string? map = null,
        string? side = null)
    {
        lock (Gate)
        {
            return LoadUnsafe()
                .Routes
                .Where(x =>
                    (string.IsNullOrWhiteSpace(map) ||
                     x.Map.Equals(
                         map,
                         StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(side) ||
                     x.Side.Equals(
                         side,
                         StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new DigitalTwinRouteNode
                {
                    Map = x.Map,
                    Side = x.Side,
                    Intent = x.Intent,
                    Route = x.Route,
                    SpawnBias = x.SpawnBias,
                    RoundType = x.RoundType,
                    Attempts = x.Attempts,
                    Wins = x.Wins,
                    Survived = x.Survived,
                    Kills = x.Kills,
                    Deaths = x.Deaths,
                    MultiKillRounds = x.MultiKillRounds,
                    ZeroImpactDeaths = x.ZeroImpactDeaths,
                    ImpactLostRounds = x.ImpactLostRounds,
                    OutcomeScoreSum = x.OutcomeScoreSum,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToList();
        }
    }

    public static IReadOnlyList<CoachDecisionCalibration> LoadCalibrations(
        string? map = null)
    {
        lock (Gate)
        {
            return LoadUnsafe()
                .Decisions
                .Where(x =>
                    string.IsNullOrWhiteSpace(map) ||
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => Math.Abs(x.TrustAdjustment))
                .ThenByDescending(x => x.Samples)
                .Select(x => new CoachDecisionCalibration
                {
                    ContextKey = x.ContextKey,
                    Map = x.Map,
                    Side = x.Side,
                    Route = x.Route,
                    Samples = x.Samples,
                    PredictedScoreSum = x.PredictedScoreSum,
                    ActualOutcomeSum = x.ActualOutcomeSum,
                    AbsoluteErrorSum = x.AbsoluteErrorSum,
                    OverPredictedFailures = x.OverPredictedFailures,
                    UnderPredictedSuccesses = x.UnderPredictedSuccesses,
                    GroundTruthVerified = x.GroundTruthVerified,
                    GroundTruthDiverged = x.GroundTruthDiverged,
                    GroundTruthPositive = x.GroundTruthPositive,
                    GroundTruthPoor = x.GroundTruthPoor,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToList();
        }
    }

    public static DigitalTwinSnapshot Analyze(
        string map,
        string side)
    {
        lock (Gate)
        {
            var state = LoadUnsafe();

            var nodes = state.Routes
                .Where(x =>
                    (string.IsNullOrWhiteSpace(map) ||
                     x.Map.Equals(
                         map,
                         StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(side) ||
                     x.Side.Equals(
                         side,
                         StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (nodes.Count == 0)
            {
                return new DigitalTwinSnapshot(
                    map,
                    side,
                    0,
                    "LEARNING",
                    "—",
                    "—",
                    0.5,
                    0,
                    0,
                    "LOW");
            }

            var attempts = nodes.Sum(x => x.Attempts);
            var kills = nodes.Sum(x => x.Kills);
            var survived = nodes.Sum(x => x.Survived);

            var routeGroups = nodes
                .GroupBy(
                    x => x.Route,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Route = g.Key,
                    Attempts = g.Sum(x => x.Attempts),
                    Outcome =
                        g.Sum(x => x.OutcomeScoreSum) /
                        Math.Max(1, g.Sum(x => x.Attempts)),
                    Survival =
                        (double)g.Sum(x => x.Survived) /
                        Math.Max(1, g.Sum(x => x.Attempts)),
                    Kpr =
                        (double)g.Sum(x => x.Kills) /
                        Math.Max(1, g.Sum(x => x.Attempts)),
                    Zero =
                        (double)g.Sum(x => x.ZeroImpactDeaths) /
                        Math.Max(1, g.Sum(x => x.Attempts))
                })
                .Where(x => x.Attempts >= 2)
                .Select(x => new
                {
                    x.Route,
                    x.Attempts,
                    Score =
                        x.Outcome * 0.55 +
                        x.Survival * 0.20 +
                        Math.Clamp(x.Kpr / 1.5, 0, 1) * 0.25 -
                        x.Zero * 0.10
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var best =
                routeGroups.FirstOrDefault();
            var risk =
                routeGroups.LastOrDefault();

            var survivalRate =
                attempts > 0
                    ? (double)survived / attempts
                    : 0;

            var kpr =
                attempts > 0
                    ? (double)kills / attempts
                    : 0;

            var archetype =
                kpr >= 0.95 && survivalRate >= 0.48
                    ? "CONTROLLED IMPACT"
                    : kpr >= 0.95
                        ? "HIGH-RISK IMPACT"
                        : survivalRate >= 0.58
                            ? "DISCIPLINED SUPPORT"
                            : kpr >= 0.72
                                ? "BALANCED FLEX"
                                : "DEVELOPING IMPACT";

            var confidence =
                attempts >= 40
                    ? "HIGH"
                    : attempts >= 18
                        ? "MEDIUM"
                        : "LOW";

            return new DigitalTwinSnapshot(
                map,
                side,
                attempts,
                archetype,
                best?.Route ?? "—",
                risk?.Route ?? "—",
                best?.Score ?? 0.5,
                survivalRate,
                kpr,
                confidence);
        }
    }

    public static double RouteAdjustment(
        string map,
        string side,
        string intent,
        string route,
        string spawnBias,
        string roundType)
    {
        lock (Gate)
        {
            var state = LoadUnsafe();

            var candidates = state.Routes
                .Where(x =>
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Side.Equals(
                        side,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Route.Equals(
                        route,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
                return 0;

            double weightedOutcome = 0;
            double weightSum = 0;
            int totalAttempts = 0;

            foreach (var node in candidates)
            {
                var weight = 1.0;

                if (!string.IsNullOrWhiteSpace(intent) &&
                    node.Intent.Equals(
                        intent,
                        StringComparison.OrdinalIgnoreCase))
                    weight += 0.50;

                if (!string.IsNullOrWhiteSpace(spawnBias) &&
                    node.SpawnBias.Equals(
                        spawnBias,
                        StringComparison.OrdinalIgnoreCase))
                    weight += 0.75;

                if (!string.IsNullOrWhiteSpace(roundType) &&
                    node.RoundType.Equals(
                        roundType,
                        StringComparison.OrdinalIgnoreCase))
                    weight += 0.35;

                var sampleWeight =
                    Math.Min(
                        1.0,
                        node.Attempts / 8.0);

                weightedOutcome +=
                    node.OutcomeScore *
                    weight *
                    sampleWeight;
                weightSum +=
                    weight *
                    sampleWeight;

                totalAttempts += node.Attempts;
            }

            if (weightSum <= 0 ||
                totalAttempts < 2)
                return 0;

            var outcome =
                weightedOutcome /
                weightSum;

            var personal =
                (outcome - 0.5) *
                28.0;

            var contextKey = BuildDecisionKey(
                map,
                side,
                intent,
                route,
                spawnBias,
                roundType);

            var calibration = state.Decisions
                .FirstOrDefault(x =>
                    x.ContextKey.Equals(
                        contextKey,
                        StringComparison.OrdinalIgnoreCase));

            var calibrationAdj =
                calibration?.TrustAdjustment ?? 0;

            return Math.Clamp(
                personal + calibrationAdj,
                -18,
                18);
        }
    }

    public static CoachDecisionCalibration? FindCalibration(
        string map,
        string side,
        string intent,
        string route,
        string spawnBias,
        string roundType)
    {
        lock (Gate)
        {
            var key = BuildDecisionKey(
                map,
                side,
                intent,
                route,
                spawnBias,
                roundType);

            var found = LoadUnsafe()
                .Decisions
                .FirstOrDefault(x =>
                    x.ContextKey.Equals(
                        key,
                        StringComparison.OrdinalIgnoreCase));

            if (found == null)
                return null;

            return new CoachDecisionCalibration
            {
                ContextKey = found.ContextKey,
                Map = found.Map,
                Side = found.Side,
                Route = found.Route,
                Samples = found.Samples,
                PredictedScoreSum = found.PredictedScoreSum,
                ActualOutcomeSum = found.ActualOutcomeSum,
                AbsoluteErrorSum = found.AbsoluteErrorSum,
                OverPredictedFailures = found.OverPredictedFailures,
                UnderPredictedSuccesses = found.UnderPredictedSuccesses,
                GroundTruthVerified = found.GroundTruthVerified,
                GroundTruthDiverged = found.GroundTruthDiverged,
                GroundTruthPositive = found.GroundTruthPositive,
                GroundTruthPoor = found.GroundTruthPoor,
                UpdatedUtc = found.UpdatedUtc
            };
        }
    }

    public static void RecordOutcome(
        string map,
        RoundRecord round,
        TacticalPlanV6 plan,
        string spawnBias,
        string roundType)
    {
        if (string.IsNullOrWhiteSpace(map) ||
            string.IsNullOrWhiteSpace(round.Side) ||
            string.IsNullOrWhiteSpace(plan.Route))
            return;

        lock (Gate)
        {
            var state = LoadUnsafe();

            var normalizedIntent =
                CoachEngine.NormalizeRoundIntent(
                    round.Intent);

            var node = state.Routes
                .FirstOrDefault(x =>
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Side.Equals(
                        round.Side,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Intent.Equals(
                        normalizedIntent,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Route.Equals(
                        plan.Route,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.SpawnBias.Equals(
                        spawnBias,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.RoundType.Equals(
                        roundType,
                        StringComparison.OrdinalIgnoreCase));

            if (node == null)
            {
                node = new DigitalTwinRouteNode
                {
                    Map = map,
                    Side = round.Side,
                    Intent = normalizedIntent,
                    Route = plan.Route,
                    SpawnBias = spawnBias,
                    RoundType = roundType
                };

                state.Routes.Add(node);
            }

            var outcome = OutcomeScore(round);

            node.Attempts++;
            if (round.Won == true)
                node.Wins++;
            if (round.Survived)
                node.Survived++;
            node.Kills +=
                Math.Max(0, round.KillsRound);
            node.Deaths +=
                Math.Max(0, round.DeathsRound);
            if (round.KillsRound >= 2)
                node.MultiKillRounds++;
            if (round.DeathsRound > 0 &&
                round.KillsRound == 0)
                node.ZeroImpactDeaths++;
            if (round.KillsRound > 0 &&
                round.Won == false)
                node.ImpactLostRounds++;
            node.OutcomeScoreSum += outcome;
            node.UpdatedUtc = DateTime.UtcNow;

            var contextKey = BuildDecisionKey(
                map,
                round.Side,
                normalizedIntent,
                plan.Route,
                spawnBias,
                roundType);

            var decision = state.Decisions
                .FirstOrDefault(x =>
                    x.ContextKey.Equals(
                        contextKey,
                        StringComparison.OrdinalIgnoreCase));

            if (decision == null)
            {
                decision = new CoachDecisionCalibration
                {
                    ContextKey = contextKey,
                    Map = map,
                    Side = round.Side,
                    Route = plan.Route
                };
                state.Decisions.Add(decision);
            }

            var predicted =
                Math.Clamp(
                    0.5 +
                    (plan.DecisionScore - 50) / 100.0,
                    0.05,
                    0.95);

            decision.Samples++;
            decision.PredictedScoreSum += predicted;
            decision.ActualOutcomeSum += outcome;
            decision.AbsoluteErrorSum +=
                Math.Abs(predicted - outcome);

            if (predicted >= 0.68 &&
                outcome <= 0.35)
                decision.OverPredictedFailures++;

            if (predicted <= 0.42 &&
                outcome >= 0.68)
                decision.UnderPredictedSuccesses++;

            decision.UpdatedUtc = DateTime.UtcNow;

            state.Routes = state.Routes
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(1200)
                .ToList();

            state.Decisions = state.Decisions
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(1200)
                .ToList();

            state.UpdatedUtc = DateTime.UtcNow;
            SaveUnsafe(state);
        }
    }

    public static void ApplyGroundTruth(
        GroundTruthMatchReport report)
    {
        if (report == null ||
            report.Rounds.Count == 0)
            return;

        lock (Gate)
        {
            var state = LoadUnsafe();

            foreach (var round in report.Rounds)
            {
                if (string.IsNullOrWhiteSpace(
                        round.ContextKey))
                    continue;

                var decision = state.Decisions
                    .FirstOrDefault(x =>
                        x.ContextKey.Equals(
                            round.ContextKey,
                            StringComparison.OrdinalIgnoreCase));

                if (decision == null)
                    continue;

                if (round.RouteEvidence ==
                    "SUPPORTED")
                {
                    decision.GroundTruthVerified++;

                    if (round.OutcomeScore >= 0.62)
                        decision.GroundTruthPositive++;
                    else if (round.OutcomeScore <= 0.35)
                        decision.GroundTruthPoor++;
                }
                else if (round.RouteEvidence ==
                         "CONTRADICTED")
                {
                    decision.GroundTruthDiverged++;
                }

                decision.UpdatedUtc =
                    DateTime.UtcNow;
            }

            state.UpdatedUtc =
                DateTime.UtcNow;

            SaveUnsafe(state);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _cache = new DigitalTwinState();

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }

    private static double OutcomeScore(
        RoundRecord round)
    {
        var score = 0.15;

        if (round.Won == true)
            score += 0.40;

        if (round.Survived)
            score += 0.16;

        if (round.KillsRound >= 1)
            score += 0.18;

        if (round.KillsRound >= 2)
            score += 0.10;

        if (round.BombPlanted &&
            round.Side.Equals(
                "T",
                StringComparison.OrdinalIgnoreCase))
            score += 0.06;

        if (round.DeathsRound > 0 &&
            round.KillsRound == 0)
            score -= 0.14;

        return Math.Clamp(score, 0, 1);
    }

    private static string BuildDecisionKey(
        string map,
        string side,
        string intent,
        string route,
        string spawnBias,
        string roundType)
        => string.Join(
            "|",
            map.Trim().ToLowerInvariant(),
            side.Trim().ToUpperInvariant(),
            intent.Trim().ToUpperInvariant(),
            route.Trim().ToLowerInvariant(),
            spawnBias.Trim().ToUpperInvariant(),
            roundType.Trim().ToLowerInvariant());

    private static DigitalTwinState LoadUnsafe()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (!File.Exists(FilePath))
                return _cache =
                    new DigitalTwinState();

            var loaded =
                JsonSerializer.Deserialize<DigitalTwinState>(
                    File.ReadAllText(FilePath))
                ?? new DigitalTwinState();

            loaded.Routes ??= new();
            loaded.Decisions ??= new();
            return _cache = loaded;
        }
        catch
        {
            return _cache =
                new DigitalTwinState();
        }
    }

    private static void SaveUnsafe(
        DigitalTwinState state)
    {
        _cache = state;

        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch { }
    }
}
