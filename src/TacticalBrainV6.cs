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

        var effectiveIntent = manualIntent
            ? CoachEngine.NormalizeRoundIntent(intent)
            : CoachEngine.AutoRoundIntent(snapshot, rounds);

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

            var roundType = CoachEngine.ClassifyRound(snapshot);
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
        var expect = CoachEngine.EnemyExpectation(snapshot, rounds);
        var firstMove = FirstMove(
            snapshot,
            best.Route,
            role,
            focus,
            mode,
            brain);

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

        if (manualIntent)
            whyParts.Add("manual " + effectiveIntent);

        var why = string.Join(" • ", whyParts.Take(4));

        return new TacticalPlanV6(
            sideMode,
            effectiveIntent,
            buy,
            best.Route,
            firstMove,
            expect,
            fallback,
            brain.OneFocus,
            why,
            confidence,
            (int)Math.Round(best.Score));
    }

    private static string FirstMove(
        GameSnapshot s,
        string route,
        string role,
        string focus,
        string mode,
        SmartMatchBrainReport brain)
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

        var roleStep = role switch
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
