namespace Sm0kiSoloCoach;

public sealed record StrategyCandidateV7(
    string Intent,
    string Route,
    int Variant,
    double SpawnFit,
    double TwinFit,
    double GroundTruthFit,
    double EnemyFit,
    double OpponentDnaFit,
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

        var opponentDna =
            OpponentDnaEngine.Build(
                snapshot.Map,
                scout,
                demoIntel);

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

                var dnaFit =
                    OpponentDnaEngine.ScorePlan(
                        opponentDna,
                        side,
                        intent,
                        route);

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
                        side,
                        brain,
                        mission,
                        i,
                        options.Length);

                // v8 Predictive Match Engine: CT and T are not the same job.
                // CT weights verified hold/survival/enemy-pressure evidence more;
                // T weights spawn/route conversion and entry-trade opportunity more.
                var score =
                    side == "CT"
                        ? spawnFit * 0.10 +
                          twinFit * 0.18 +
                          groundFit * 0.18 +
                          enemyFit * 0.10 +
                          dnaFit * 0.14 +
                          currentFit * 0.16 +
                          mapGraphFit * 0.07 +
                          economyFit * 0.03 +
                          disciplineFit * 0.04
                        : spawnFit * 0.20 +
                          twinFit * 0.17 +
                          groundFit * 0.17 +
                          enemyFit * 0.10 +
                          dnaFit * 0.12 +
                          currentFit * 0.11 +
                          mapGraphFit * 0.07 +
                          economyFit * 0.03 +
                          disciplineFit * 0.03;

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
                        opponentDna,
                        side,
                        intent,
                        dnaFit,
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
                        dnaFit,
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

        var plants =
            recent.Count(x =>
                x.BombPlanted);

        var winRate =
            (double)wins /
            recent.Count;

        var survivalRate =
            (double)survived /
            recent.Count;

        var killRate =
            Math.Clamp(
                (double)kills /
                Math.Max(1, recent.Count) /
                1.5,
                0,
                1);

        var zeroRate =
            (double)zeroDeaths /
            recent.Count;

        double score;

        if (side.Equals(
                "CT",
                StringComparison.OrdinalIgnoreCase))
        {
            // CT: the best starting position must primarily convert rounds
            // while keeping the player alive for the second contact/rotate.
            score =
                34 +
                winRate * 30 +
                survivalRate * 22 +
                killRate * 10 -
                zeroRate * 18;
        }
        else
        {
            // T: prioritize conversion + useful impact. A plant is a positive
            // route signal even when the round itself is eventually lost.
            var plantRate =
                (double)plants /
                recent.Count;

            score =
                32 +
                winRate * 30 +
                killRate * 18 +
                survivalRate * 8 +
                plantRate * 8 -
                zeroRate * 16;
        }

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
        string side,
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

        var ct =
            side.Equals(
                "CT",
                StringComparison.OrdinalIgnoreCase);

        if (key is
            "discipline" or
            "post_impact")
        {
            if (variant == 0)
                return ct ? 32 : 40;

            if (variant ==
                count - 1)
                return ct ? 92 : 84;

            return ct ? 78 : 70;
        }

        if (key == "impact")
        {
            return variant == 0
                ? ct ? 74 : 86
                : ct ? 66 : 58;
        }

        return ct ? 68 : 64;
    }

    private static string BuildEvidence(
        SpawnProfile spawn,
        double twinAdjustment,
        PlanGroundTruthStats gt,
        OpponentDemoIntelReport? demo,
        OpponentDnaProfile opponentDna,
        string side,
        string intent,
        double dnaFit,
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

        if (dnaFit >= 65)
            parts.Add(
                "DNA +");
        else if (dnaFit <= 40)
            parts.Add(
                "DNA -");

        if (opponentDna.Confidence != "LOW" &&
            !string.IsNullOrWhiteSpace(
                opponentDna.Archetype))
        {
            parts.Add(
                "enemy " +
                opponentDna.Archetype);
        }

        if (currentFit >= 65)
            parts.Add("live +");
        else if (currentFit <= 40)
            parts.Add("live -");

        if (mapGraphFit >= 80)
            parts.Add("graph ✓");
        else if (mapGraphFit <= 45)
            parts.Add("graph ?");

        parts.Insert(
            0,
            side == "CT"
                ? "CT hold model"
                : "T conversion model");

        return string.Join(
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
