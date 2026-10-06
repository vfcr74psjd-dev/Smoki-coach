namespace Sm0kiSoloCoach;

public sealed record StrategyCandidateV7(
    string Intent,
    string Route,
    int Variant,
    double SpawnFit,
    double TwinFit,
    double GroundTruthFit,
    double EnemyFit,
    double CurrentMatchFit,
    double MapGraphFit,
    double EconomyFit,
    double DisciplineFit,
    double Score,
    string Evidence);

public sealed record StrategySimulationV7(
    StrategyCandidateV7 Winner,
    IReadOnlyList<StrategyCandidateV7> Candidates,
    string Confidence,
    double Margin)
{
    public string Compact =>
        $"{Candidates.Count} plans • {Winner.Intent} {Winner.Route} • " +
        $"{Winner.Score:0}/100 • {Confidence}";
}

public static class StrategySimulatorV7
{
    public static StrategySimulationV7 Simulate(
        GameSnapshot snapshot,
        IReadOnlyList<RoundRecord> rounds,
        string requestedIntent,
        bool manualIntent)
    {
        var side =
            string.Equals(
                snapshot.Team,
                "CT",
                StringComparison.OrdinalIgnoreCase)
                ? "CT"
                : "T";

        var spawn =
            CoachEngine.SpawnProfile(
                snapshot);

        var roundType =
            CoachEngine.ClassifyRound(
                snapshot);

        var brain =
            SmartMatchBrainEngine.Analyze(
                rounds);

        var mission =
            TrainingMissionStore.Current(
                snapshot.Map);

        var demoIntel =
            OpponentDemoIntelStore.LoadLatest(
                snapshot.Map);

        var scout =
            OpponentScoutStore.LoadLatest(
                snapshot.Map);

        var autoIntent =
            CoachEngine.AutoRoundIntent(
                snapshot,
                rounds);

        var normalizedRequested =
            CoachEngine.NormalizeRoundIntent(
                requestedIntent);

        var intents =
            manualIntent &&
            normalizedRequested is "A" or "B"
                ? new[]
                {
                    normalizedRequested
                }
                : new[]
                {
                    "A",
                    "B"
                };

        var candidates =
            new List<StrategyCandidateV7>();

        foreach (var intent in intents)
        {
            var options =
                CoachEngine.GetPositionOptions(
                    snapshot.Map,
                    side,
                    intent);

            for (var i = 0;
                 i < options.Length;
                 i++)
            {
                var route =
                    options[i];

                var spawnFit =
                    ScoreSpawn(
                        spawn,
                        intent,
                        i,
                        options.Length);

                var twinAdj =
                    DigitalTwinStore.RouteAdjustment(
                        snapshot.Map,
                        side,
                        intent,
                        route,
                        spawn.Bias,
                        roundType);

                var twinFit =
                    Math.Clamp(
                        50 +
                        twinAdj * 2.5,
                        5,
                        95);

                var contextKey =
                    RecommendationTraceStore
                        .BuildContextKey(
                            snapshot.Map,
                            side,
                            intent,
                            route,
                            spawn.Bias,
                            roundType);

                var gt =
                    GroundTruthStore.ContextStats(
                        contextKey);

                var groundFit =
                    ScoreGroundTruth(gt);

                var enemyFit =
                    ScoreEnemy(
                        side,
                        intent,
                        route,
                        demoIntel,
                        scout);

                var currentFit =
                    ScoreCurrentMatch(
                        side,
                        route,
                        rounds);

                var mapGraphFit =
                    MapKnowledgeGraphV7.ConnectivityScore(
                        snapshot.Map,
                        route);

                var economyFit =
                    ScoreEconomy(
                        roundType,
                        i,
                        options.Length,
                        spawn,
                        intent);

                var disciplineFit =
                    ScoreDiscipline(
                        brain,
                        mission,
                        i,
                        options.Length);

                var score =
                    spawnFit * 0.20 +
                    twinFit * 0.18 +
                    groundFit * 0.17 +
                    enemyFit * 0.14 +
                    currentFit * 0.13 +
                    mapGraphFit * 0.10 +
                    economyFit * 0.04 +
                    disciplineFit * 0.04;

                if (!manualIntent &&
                    intent.Equals(
                        autoIntent,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 3;
                }

                if (manualIntent)
                    score += 3;

                var evidence =
                    BuildEvidence(
                        spawn,
                        twinAdj,
                        gt,
                        demoIntel,
                        side,
                        intent,
                        currentFit,
                        mapGraphFit);

                candidates.Add(
                    new StrategyCandidateV7(
                        intent,
                        route,
                        i,
                        spawnFit,
                        twinFit,
                        groundFit,
                        enemyFit,
                        currentFit,
                        mapGraphFit,
                        economyFit,
                        disciplineFit,
                        Math.Clamp(
                            score,
                            0,
                            100),
                        evidence));
            }
        }

        if (candidates.Count == 0)
        {
            var fallback =
                new StrategyCandidateV7(
                    normalizedRequested is "A" or "B"
                        ? normalizedRequested
                        : autoIntent,
                    "safe tradeable opening",
                    0,
                    50,
                    50,
                    50,
                    50,
                    50,
                    50,
                    50,
                    50,
                    50,
                    "fallback");

            return new StrategySimulationV7(
                fallback,
                new[] { fallback },
                "LOW",
                0);
        }

        var ranked =
            candidates
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Variant)
                .ToList();

        var winner =
            ranked[0];

        var margin =
            ranked.Count > 1
                ? winner.Score -
                  ranked[1].Score
                : 0;

        var evidencePoints = 0;

        if (spawn.Known)
            evidencePoints += 2;

        var twin =
            DigitalTwinStore.Analyze(
                snapshot.Map,
                side);

        if (twin.Samples >= 8)
            evidencePoints += 2;

        var winnerKey =
            RecommendationTraceStore
                .BuildContextKey(
                    snapshot.Map,
                    side,
                    winner.Intent,
                    winner.Route,
                    spawn.Bias,
                    roundType);

        var winnerGt =
            GroundTruthStore.ContextStats(
                winnerKey);

        if (winnerGt.SupportedSamples >= 3)
            evidencePoints += 2;

        if (demoIntel != null &&
            demoIntel.OpeningSamples >= 10)
            evidencePoints += 1;

        if (rounds.Count >= 5)
            evidencePoints += 1;

        if (margin >= 8)
            evidencePoints += 1;

        var confidence =
            evidencePoints >= 7
                ? "HIGH"
                : evidencePoints >= 4
                    ? "MEDIUM"
                    : "LOW";

        return new StrategySimulationV7(
            winner,
            ranked,
            confidence,
            margin);
    }

    private static double ScoreSpawn(
        SpawnProfile spawn,
        string intent,
        int variant,
        int count)
    {
        if (!spawn.Known)
            return 55;

        if (spawn.Bias == "NEUTRAL")
        {
            return variant ==
                   Math.Min(
                       1,
                       count - 1)
                ? 70
                : 60;
        }

        if (spawn.Bias.Equals(
                intent,
                StringComparison.OrdinalIgnoreCase))
        {
            return variant switch
            {
                0 => 95,
                1 => 82,
                _ => 72
            };
        }

        return variant switch
        {
            0 => 28,
            1 => 52,
            _ => 70
        };
    }

    private static double ScoreGroundTruth(
        PlanGroundTruthStats stats)
    {
        if (stats.SupportedSamples == 0)
            return 50;

        var positiveRate =
            (double)stats.PositiveOutcomes /
            stats.SupportedSamples;

        var poorRate =
            (double)stats.PoorOutcomes /
            stats.SupportedSamples;

        var sampleWeight =
            Math.Min(
                1.0,
                stats.SupportedSamples / 8.0);

        var raw =
            50 +
            (positiveRate - poorRate) *
            45 *
            sampleWeight;

        return Math.Clamp(
            raw,
            10,
            95);
    }

    private static double ScoreEnemy(
        string side,
        string intent,
        string route,
        OpponentDemoIntelReport? demo,
        OpponentScoutReport? scout)
    {
        var score = 55.0;

        if (demo != null &&
            demo.OpeningSamples >= 6)
        {
            if (side == "CT")
            {
                if (demo.TopTOpeningZone ==
                    intent &&
                    demo.TTopZoneShare >= 0.55)
                {
                    score += 22;
                }
                else if (demo.TTopZoneShare >= 0.65)
                {
                    score -= 4;
                }
            }
            else
            {
                var zone =
                    RouteZone(route);

                if (zone.Length > 0 &&
                    zone ==
                    demo.TopCtOpeningZone &&
                    demo.CtTopZoneShare >= 0.45)
                {
                    if (demo.CtOpeningKills >
                        demo.CtOpeningDeaths)
                    {
                        score -= 22;
                    }
                    else
                    {
                        score += 10;
                    }
                }
            }
        }

        if (scout != null)
        {
            var strong =
                (scout.TeamMapWinRate ?? 0) >= 58 ||
                (scout.TeamRecentKd ?? 0) >= 1.10;

            if (strong &&
                IsDirectRoute(route))
            {
                score -= 5;
            }
        }

        return Math.Clamp(
            score,
            10,
            95);
    }

    private static double ScoreCurrentMatch(
        string side,
        string route,
        IReadOnlyList<RoundRecord> rounds)
    {
        var recent =
            rounds
                .TakeLast(10)
                .Where(x =>
                    x.Side.Equals(
                        side,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.PositionPlan.Equals(
                        route,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (recent.Count == 0)
            return 50;

        var wins =
            recent.Count(x =>
                x.Won == true);

        var survived =
            recent.Count(x =>
                x.Survived);

        var kills =
            recent.Sum(x =>
                x.KillsRound);

        var zeroDeaths =
            recent.Count(x =>
                x.DeathsRound > 0 &&
                x.KillsRound == 0);

        var score =
            40 +
            25.0 * wins / recent.Count +
            12.0 * survived / recent.Count +
            12.0 *
            Math.Clamp(
                (double)kills /
                Math.Max(1, recent.Count),
                0,
                1.5) /
            1.5 -
            18.0 * zeroDeaths / recent.Count;

        return Math.Clamp(
            score,
            10,
            95);
    }

    private static double ScoreEconomy(
        string roundType,
        int variant,
        int count,
        SpawnProfile spawn,
        string intent)
    {
        if (roundType is "Eco" or
            "Force / light")
        {
            if (variant == 0 &&
                !(spawn.Known &&
                  spawn.Bias == intent))
                return 35;

            if (variant ==
                count - 1)
                return 78;

            return 60;
        }

        if (roundType == "Pistol")
        {
            return variant == 0
                ? 65
                : 72;
        }

        return 70;
    }

    private static double ScoreDiscipline(
        SmartMatchBrainReport brain,
        TrainingMissionState mission,
        int variant,
        int count)
    {
        var key =
            !string.IsNullOrWhiteSpace(
                mission.Key)
                ? mission.Key
                : brain.MistakeKey;

        if (key is
            "discipline" or
            "post_impact")
        {
            if (variant == 0)
                return 38;

            if (variant ==
                count - 1)
                return 88;

            return 72;
        }

        if (key == "impact")
        {
            return variant == 0
                ? 82
                : 60;
        }

        return 65;
    }

    private static string BuildEvidence(
        SpawnProfile spawn,
        double twinAdjustment,
        PlanGroundTruthStats gt,
        OpponentDemoIntelReport? demo,
        string side,
        string intent,
        double currentFit,
        double mapGraphFit)
    {
        var parts =
            new List<string>();

        if (spawn.Known)
            parts.Add(
                "spawn " +
                spawn.Bias);

        if (Math.Abs(
                twinAdjustment) >= 2)
        {
            parts.Add(
                $"twin {twinAdjustment:+0;-0}");
        }

        if (gt.SupportedSamples > 0)
        {
            parts.Add(
                $"GT {gt.SupportedSamples}x");
        }

        if (demo != null &&
            demo.OpeningSamples >= 6)
        {
            var zone =
                side == "CT"
                    ? demo.TopTOpeningZone
                    : demo.TopCtOpeningZone;

            if (!string.IsNullOrWhiteSpace(
                    zone))
            {
                parts.Add(
                    $"enemy {zone}");
            }
        }

        if (currentFit >= 65)
            parts.Add("live +");
        else if (currentFit <= 40)
            parts.Add("live -");

        if (mapGraphFit >= 80)
            parts.Add("graph ✓");
        else if (mapGraphFit <= 45)
            parts.Add("graph ?");

        return parts.Count == 0
            ? "limited evidence"
            : string.Join(
                " • ",
                parts.Take(4));
    }

    private static string RouteZone(
        string route)
    {
        var r =
            (route ?? "")
            .ToLowerInvariant();

        if (r.Contains("mid") ||
            r.Contains("short") ||
            r.Contains("cat") ||
            r.Contains("connector") ||
            r.Contains("window") ||
            r.Contains("water"))
            return "MID";

        if (r.Contains("tunnel") ||
            r.Contains("banana") ||
            r.Contains("monster") ||
            r.Contains("b apps") ||
            r.Contains("b main") ||
            r.StartsWith("b"))
            return "B";

        if (r.Contains("long") ||
            r.Contains("palace") ||
            r.Contains("a main") ||
            r.Contains("ivy") ||
            r.Contains("hut") ||
            r.Contains("squeaky") ||
            r.StartsWith("a"))
            return "A";

        return "";
    }

    private static bool IsDirectRoute(
        string route)
    {
        var r =
            (route ?? "")
            .ToLowerInvariant();

        return r.Contains("first contact") ||
               r.Contains("drugi kontakt") ||
               r.Contains("long") ||
               r.Contains("ramp") ||
               r.Contains("banana") ||
               r.Contains("main") ||
               r.Contains("tunnel");
    }
}
