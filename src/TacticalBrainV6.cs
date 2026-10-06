namespace Sm0kiSoloCoach;

public sealed record TacticalPlanV6(
    string SideMode,
    string Intent,
    string Buy,
    string Route,
    string FirstMove,
    string Expect,
    string Fallback,
    string Focus,
    string Why,
    string Confidence,
    int DecisionScore)
{
    public string SpawnBias { get; init; } = "";
    public string RoundType { get; init; } = "";

    public string PlanKey =>
        $"{SideMode}|{Intent}|{Route}|{FirstMove}|{Fallback}|{Focus}";

    public string RenderLegacyCompatible()
    {
        return
            $"BUY: {Buy}\n" +
            $"POSITION: {Route}\n" +
            $"EXPECT: {Expect}\n" +
            $"DO: {FirstMove}\n" +
            $"ADAPT: {Fallback}\n" +
            $"FOCUS: {Focus}\n" +
            $"WHY: {Why}\n" +
            $"CONFIDENCE: {Confidence}";
    }
}

public static class TacticalBrainV6
{
    private sealed record Candidate(
        string Route,
        int Index,
        double Score,
        AdaptiveRouteStat? Personal,
        int RecentWins,
        int RecentLosses,
        int RecentZeroDeaths);

    public static TacticalPlanV6 Generate(
        GameSnapshot snapshot,
        string mode,
        string role,
        string focus,
        IReadOnlyList<RoundRecord> rounds,
        string intent)
    {
        var ct = string.Equals(
            snapshot.Team,
            "CT",
            StringComparison.OrdinalIgnoreCase);

        var sideMode = ct ? "CT" : "T";
        var spawn = CoachEngine.SpawnProfile(snapshot);
        var manualIntent =
            !string.IsNullOrWhiteSpace(CoachEngine.NormalizeRoundIntent(intent));
        var demoIntel = OpponentDemoIntelStore.LoadLatest(snapshot.Map);

        var effectiveIntent = manualIntent
            ? CoachEngine.NormalizeRoundIntent(intent)
            : CoachEngine.AutoRoundIntent(snapshot, rounds);

        // Early CT rounds can use pre-match opponent demo evidence as a small
        // site bias. It never overrides a manual call, and after the current
        // match produces enough evidence the live match takes priority.
        var sameSideRounds = rounds.Count(r =>
            string.Equals(
                r.Side,
                sideMode,
                StringComparison.OrdinalIgnoreCase));

        if (ct &&
            !manualIntent &&
            sameSideRounds < 3 &&
            demoIntel != null &&
            demoIntel.OpeningSamples >= 6 &&
            demoIntel.TTopZoneShare >= 0.55 &&
            demoIntel.TopTOpeningZone is "A" or "B")
        {
            effectiveIntent = demoIntel.TopTOpeningZone;
        }

        var options = CoachEngine.GetPositionOptions(
            snapshot.Map,
            sideMode,
            effectiveIntent);

        if (options.Length == 0)
        {
            options = new[]
            {
                ct
                    ? $"{effectiveIntent} • safe first contact + fall"
                    : $"{effectiveIntent} • tradeable second contact"
            };
        }

        var brain = SmartMatchBrainEngine.Analyze(rounds);
        var scout = OpponentScoutStore.LoadLatest(snapshot.Map);
        var halftime = HalftimeBrainV6.Analyze(snapshot.Team, rounds);
        var recurring = MistakeLibraryStore.TopRecurring(snapshot.Map, 10);
        var twin = DigitalTwinStore.Analyze(snapshot.Map, sideMode);
        var mission = TrainingMissionStore.Current(snapshot.Map);
        var roundType = CoachEngine.ClassifyRound(snapshot);
        var candidates = new List<Candidate>();

        for (var i = 0; i < options.Length; i++)
        {
            var route = options[i];
            var score = 50.0;

            if (spawn.Known)
            {
                if (spawn.Bias == effectiveIntent)
                {
                    score += i switch
                    {
                        0 => 25,
                        1 => 13,
                        _ => 5
                    };
                }
                else if (spawn.Bias == "NEUTRAL")
                {
                    score += i == Math.Min(1, options.Length - 1) ? 10 : 3;
                }
                else
                {
                    score += i == options.Length - 1 ? 13 : i == 0 ? -16 : 2;
                }
            }

            var personal = AdaptivePlaybookStore.Find(
                snapshot.Map,
                sideMode,
                route);

            if (personal != null && personal.Attempts >= 2)
            {
                var confidenceFactor =
                    Math.Min(1.0, personal.Attempts / 8.0);

                score +=
                    (personal.SuccessScore - 0.5) *
                    42.0 *
                    confidenceFactor;
            }

            var recent = rounds
                .TakeLast(10)
                .Where(r =>
                    string.Equals(
                        r.Side,
                        sideMode,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        r.PositionPlan,
                        route,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            var recentWins = recent.Count(r => r.Won == true);
            var recentLosses = recent.Count(r => r.Won == false);
            var recentZeroDeaths = recent.Count(r =>
                r.DeathsRound > 0 &&
                r.KillsRound == 0);

            score += recentWins * 5;
            score -= recentLosses * 6;
            score -= recentZeroDeaths * 5;

            var last = rounds.LastOrDefault();
            if (last != null &&
                string.Equals(
                    last.PositionPlan,
                    route,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (last.Won == true && last.Survived)
                    score += 4;
                else if (last.Won == false || last.DeathsRound > 0)
                    score -= 12;
            }

            if (brain.MistakeKey is "discipline" or "post_impact")
            {
                if (i == 0) score -= 7;
                if (i == options.Length - 1) score += 6;
            }
            else if (brain.MistakeKey == "impact" && i == 0)
            {
                score += 5;
            }

            if (roundType is "Eco" or "Force / light")
            {
                // Light buys should avoid the highest-risk direct route unless
                // the spawn is clearly favorable.
                if (i == 0 &&
                    !(spawn.Known && spawn.Bias == effectiveIntent))
                    score -= 5;
            }

            if (manualIntent)
                score += 3;

            if (scout != null)
            {
                var strongEnemyMap =
                    scout.TeamMapWinRate is double scoutWr &&
                    scoutWr >= 58;

                var strongEnemyForm =
                    scout.TeamRecentKd is double scoutKd &&
                    scoutKd >= 1.10;

                if (strongEnemyMap || strongEnemyForm)
                {
                    // Against a strong-scoped opponent profile, reduce hero-route
                    // bias slightly and prefer trade/support variants.
                    if (i == 0) score -= 4;
                    if (i == options.Length - 1) score += 3;
                }
            }

            if (!ct &&
                demoIntel != null &&
                demoIntel.OpeningSamples >= 6)
            {
                var routeZone = RouteZone(route);
                var hotCtZone = demoIntel.TopCtOpeningZone;

                if (!string.IsNullOrWhiteSpace(routeZone) &&
                    routeZone == hotCtZone &&
                    demoIntel.CtTopZoneShare >= 0.45)
                {
                    // If their historical CT opening contacts repeatedly happen
                    // here and they win more of those openings than they lose,
                    // avoid making the most direct variant the default.
                    if (demoIntel.CtOpeningKills >
                        demoIntel.CtOpeningDeaths)
                    {
                        if (i == 0) score -= 8;
                        if (i == options.Length - 1) score += 4;
                    }
                    else if (demoIntel.CtOpeningDeaths >
                             demoIntel.CtOpeningKills)
                    {
                        // A repeatedly weak opening zone is a small positive
                        // signal, but never strong enough to override spawn or
                        // the player's own playbook.
                        if (i == 0) score += 4;
                    }
                }
            }

            if (recurring.Matches >= 3)
            {
                if (recurring.Key is "discipline" or "post_impact")
                {
                    if (i == 0) score -= 5;
                    if (i == options.Length - 1) score += 5;
                }
                else if (recurring.Key == "impact" && i == 0)
                {
                    score += 3;
                }
            }

            if (halftime.Active)
            {
                // New side = new job. Favor the middle/support variant unless
                // spawn clearly makes the direct option valuable.
                if (i == Math.Min(1, options.Length - 1))
                    score += 4;

                if (i == 0 &&
                    !(spawn.Known && spawn.Bias == effectiveIntent))
                    score -= 3;
            }

            // v7 Digital Twin: personal context + calibration of the coach's
            // own past recommendations. Repeated over-predictions get
            // automatically de-weighted in the same context.
            score += DigitalTwinStore.RouteAdjustment(
                snapshot.Map,
                sideMode,
                effectiveIntent,
                route,
                spawn.Bias,
                roundType);

            candidates.Add(
                new Candidate(
                    route,
                    i,
                    score,
                    personal,
                    recentWins,
                    recentLosses,
                    recentZeroDeaths));
        }

        var ranked = candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .ToList();

        var best = ranked[0];
        var fallbackCandidate = ranked
            .Skip(1)
            .FirstOrDefault();

        var buy = CoachEngine.RoundBuyPlan(snapshot);
        var expect = BuildExpectation(
            snapshot,
            rounds,
            demoIntel,
            CoachEngine.EnemyExpectation(snapshot, rounds));
        var firstMove = FirstMove(
            snapshot,
            best.Route,
            role,
            focus,
            mode,
            brain,
            scout?.TeamFitRole ?? "BALANCED FLEX");

        var fallback = Fallback(
            snapshot,
            best.Route,
            fallbackCandidate?.Route,
            effectiveIntent,
            ct);

        var confidencePoints = 10;
        if (spawn.Known) confidencePoints += 20;
        confidencePoints += Math.Min(30, rounds.Count * 4);
        if (best.Personal != null)
            confidencePoints += Math.Min(30, best.Personal.Attempts * 4);
        if (scout != null) confidencePoints += 8;
        if (demoIntel != null)
            confidencePoints += demoIntel.Confidence == "HIGH"
                ? 12
                : demoIntel.Confidence == "MEDIUM"
                    ? 7
                    : 3;
        if (twin.Samples >= 8)
            confidencePoints += twin.Confidence == "HIGH"
                ? 12
                : twin.Confidence == "MEDIUM"
                    ? 7
                    : 3;
        if (manualIntent) confidencePoints += 8;

        var confidence =
            confidencePoints >= 70
                ? "HIGH"
                : confidencePoints >= 40
                    ? "MEDIUM"
                    : "LOW";

        var whyParts = new List<string>();

        if (spawn.Known)
            whyParts.Add(spawn.Label);
        else
            whyParts.Add("spawn unknown");

        if (best.Personal != null && best.Personal.Attempts >= 2)
        {
            whyParts.Add(
                $"personal {best.Personal.SuccessScore * 100:0}% / {best.Personal.Attempts}x");
        }

        if (best.RecentWins + best.RecentLosses > 0)
        {
            whyParts.Add(
                $"recent {best.RecentWins}W/{best.RecentLosses}L");
        }

        if (brain.Priority != "KEEP PLAN")
            whyParts.Add("brain: " + brain.Priority);

        if (scout != null)
        {
            var scoutSignal =
                scout.TeamMapWinRate is double scoutWr
                    ? $"scout map WR {scoutWr:0}%"
                    : scout.TeamRecentKd is double scoutKd
                        ? $"scout KD {scoutKd:0.00}"
                        : "scout loaded";

            whyParts.Add(scoutSignal);

            if (!string.Equals(
                    scout.TeamFitRole,
                    "BALANCED FLEX",
                    StringComparison.OrdinalIgnoreCase))
            {
                whyParts.Add(
                    "team fit " +
                    scout.TeamFitRole);
            }
        }

        if (demoIntel != null)
        {
            var demoSignal = ct
                ? $"demo T-open {demoIntel.TopTOpeningZone} {demoIntel.TTopZoneShare * 100:0}%"
                : $"demo CT-open {demoIntel.TopCtOpeningZone} {demoIntel.CtTopZoneShare * 100:0}%";

            whyParts.Add(demoSignal);
        }

        if (manualIntent)
            whyParts.Add("manual " + effectiveIntent);

        if (halftime.Active)
            whyParts.Add($"half {halftime.FromSide}→{halftime.ToSide}");

        if (recurring.Matches >= 3)
            whyParts.Add($"memory {recurring.Label}");

        if (twin.Samples >= 4)
            whyParts.Add($"twin {twin.Archetype} • {twin.Samples}R");

        var calibration = DigitalTwinStore.FindCalibration(
            snapshot.Map,
            sideMode,
            effectiveIntent,
            best.Route,
            spawn.Bias,
            roundType);

        if (calibration != null &&
            calibration.Samples >= 3)
        {
            whyParts.Add(
                $"coach trust {calibration.TrustAdjustment:+0;-0;0}");
        }

        var why = string.Join(" • ", whyParts.Take(6));

        var tacticalFocus =
            brain.Priority != "KEEP PLAN"
                ? brain.OneFocus
                : halftime.Active
                    ? halftime.Focus
                    : !string.IsNullOrWhiteSpace(mission.Key)
                        ? mission.Instruction
                        : recurring.Matches >= 3
                            ? recurring.CoachingFocus
                            : brain.OneFocus;

        return new TacticalPlanV6(
            sideMode,
            effectiveIntent,
            buy,
            best.Route,
            firstMove,
            expect,
            fallback,
            tacticalFocus,
            why,
            confidence,
            (int)Math.Round(best.Score))
        {
            SpawnBias = spawn.Bias,
            RoundType = roundType
        };
    }

    private static string BuildExpectation(
        GameSnapshot snapshot,
        IReadOnlyList<RoundRecord> rounds,
        OpponentDemoIntelReport? demoIntel,
        string liveExpectation)
    {
        if (demoIntel == null ||
            demoIntel.OpeningSamples < 6)
            return liveExpectation;

        var sameSideRounds = rounds.Count(r =>
            string.Equals(
                r.Side,
                snapshot.Team,
                StringComparison.OrdinalIgnoreCase));

        // Once enough current-match rounds exist, current evidence is more
        // valuable than pre-match demo history.
        if (sameSideRounds >= 3)
            return liveExpectation;

        var ct = string.Equals(
            snapshot.Team,
            "CT",
            StringComparison.OrdinalIgnoreCase);

        if (ct &&
            demoIntel.TTopZoneShare >= 0.55 &&
            demoIntel.TopTOpeningZone is "A" or "B" or "MID")
        {
            return
                $"pre-match demo tendency • T opening contact {demoIntel.TopTOpeningZone} " +
                $"{demoIntel.TTopZoneShare * 100:0}% • {demoIntel.Confidence}";
        }

        if (!ct &&
            demoIntel.CtTopZoneShare >= 0.45 &&
            demoIntel.TopCtOpeningZone is "A" or "B" or "MID")
        {
            return
                $"pre-match demo tendency • CT opening contact {demoIntel.TopCtOpeningZone} " +
                $"{demoIntel.CtTopZoneShare * 100:0}% • {demoIntel.Confidence}";
        }

        return liveExpectation;
    }

    private static string RouteZone(string route)
    {
        var r = (route ?? "").ToLowerInvariant();

        if (r.Contains("mid") ||
            r.Contains("short") ||
            r.Contains("cat") ||
            r.Contains("connector") ||
            r.Contains("water"))
            return "MID";

        if (r.Contains("b ") ||
            r.StartsWith("b") ||
            r.Contains("tunnel") ||
            r.Contains("banana") ||
            r.Contains("monster") ||
            r.Contains("cave"))
            return "B";

        if (r.Contains("a ") ||
            r.StartsWith("a") ||
            r.Contains("long") ||
            r.Contains("palace") ||
            r.Contains("ivy") ||
            r.Contains("hut") ||
            r.Contains("squeaky"))
            return "A";

        return "";
    }

    private static string FirstMove(
        GameSnapshot s,
        string route,
        string role,
        string focus,
        string mode,
        SmartMatchBrainReport brain,
        string teamFitRole)
    {
        var ct = string.Equals(
            s.Team,
            "CT",
            StringComparison.OrdinalIgnoreCase);

        var routeLower = route.ToLowerInvariant();

        if (ct)
        {
            if (routeLower.Contains("info"))
                return "vzemi info → brez repeeka → fall v trade/crossfire";

            if (routeLower.Contains("crossfire") ||
                routeLower.Contains("anchor") ||
                routeLower.Contains("site"))
                return "setup crossfire → utility na kontakt → preživi prvi val";

            return "prvi kontakt samo z escape potjo → info → reposition";
        }

        var roleStep =
            role == "Flex" &&
            teamFitRole == "SECOND CONTACT"
                ? "sledi najboljšemu openerju na trade razdalji"
                : role == "Flex" &&
                  teamFitRole == "IMPACT FLEX"
                    ? "če team obstane, ustvari 1 utility-supported opening"
                    : role switch
                    {
                        "Entry" => "vstopi samo s flash/trade podporo",
                        "Lurk" => "vzemi info → ne zamudi join timinga",
                        "Support" => "utility pred kontaktom → takoj za trade",
                        _ => "ostani na trade razdalji"
                    };

        if (brain.MistakeKey == "post_impact")
            return $"{roleStep} → po prvem killu STOP + reposition";

        if (brain.MistakeKey == "discipline")
            return $"{roleStep} → en duel, brez chain-peeka";

        if (focus == "Utility impact")
            return "1 uporaben nade pred duelom → " + roleStep;

        if (mode == "Aggressive")
            return "en spawn-timed opening → " + roleStep + " → brez drugega dry peeka";

        return routeLower.Contains("late") || routeLower.Contains("support")
            ? "počakaj prvi kontakt ekipe → utility/trade → commit"
            : roleStep + " → po kontaktu takoj odloči commit ali reset";
    }

    private static string Fallback(
        GameSnapshot s,
        string route,
        string? alternate,
        string intent,
        bool ct)
    {
        if (ct)
        {
            return alternate == null
                ? "če ni kontakta → ostani rotate-ready, brez lovljenja"
                : $"če ni kontakta/si potisnjen → fall → {alternate}";
        }

        if (!string.IsNullOrWhiteSpace(alternate))
            return $"če je route blokiran → STOP → reset → {alternate}";

        var other = intent == "A" ? "B" : "A";
        return $"če je route blokiran → reset → vzemi info mid → odloči {other}";
    }
}
