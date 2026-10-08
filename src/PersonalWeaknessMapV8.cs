namespace Sm0kiSoloCoach;

public sealed record PersonalWeaknessMapV8Report(
    string Map,
    string Side,
    string Route,
    string Leak,
    string Evidence,
    string Focus,
    int Samples,
    string Confidence)
{
    public string Compact =>
        Samples <= 0
            ? "Collecting route + Ground Truth evidence"
            : $"{Side} • {Route} • {Leak} • {Confidence}";
}

public static class PersonalWeaknessMapV8
{
    public static PersonalWeaknessMapV8Report Analyze(
        string map)
        => Build(
            map,
            DigitalTwinStore.LoadRoutes(map),
            GroundTruthStore.Load()
                .Where(x =>
                    x.Map.Equals(
                        map,
                        StringComparison.OrdinalIgnoreCase))
                .Take(12)
                .ToList());

    public static PersonalWeaknessMapV8Report Build(
        string map,
        IReadOnlyList<DigitalTwinRouteNode> routes,
        IReadOnlyList<GroundTruthMatchReport> groundTruth)
    {
        var routeLeak =
            routes
                .Where(x =>
                    x.Attempts >= 2)
                .GroupBy(x =>
                    new
                    {
                        Side =
                            (x.Side ?? "")
                                .ToUpperInvariant(),
                        Route =
                            x.Route ?? ""
                    })
                .Select(g =>
                {
                    var attempts =
                        g.Sum(x => x.Attempts);

                    var wins =
                        g.Sum(x => x.Wins);

                    var survived =
                        g.Sum(x => x.Survived);

                    var zeroImpact =
                        g.Sum(x =>
                            x.ZeroImpactDeaths);

                    var outcome =
                        g.Sum(x =>
                            x.OutcomeScoreSum) /
                        Math.Max(
                            1,
                            attempts);

                    var weakness =
                        (1 -
                         (double)wins /
                         Math.Max(1, attempts)) *
                        0.34 +
                        (1 -
                         (double)survived /
                         Math.Max(1, attempts)) *
                        0.22 +
                        (double)zeroImpact /
                        Math.Max(1, attempts) *
                        0.22 +
                        (1 - outcome) *
                        0.22;

                    return new
                    {
                        g.Key.Side,
                        g.Key.Route,
                        Attempts = attempts,
                        Weakness = weakness
                    };
                })
                .OrderByDescending(x =>
                    x.Weakness)
                .ThenByDescending(x =>
                    x.Attempts)
                .FirstOrDefault();

        var gtRounds =
            groundTruth
                .SelectMany(x =>
                    x.Rounds ??
                    new List<GroundTruthRoundVerdict>())
                .Where(x =>
                    x.GroundTruthEligible)
                .ToList();

        var diagnosis =
            gtRounds
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.Diagnosis) &&
                    x.Diagnosis !=
                        "COACH_PLAN_CONFIRMED" &&
                    x.Diagnosis !=
                        "LOW_EVIDENCE")
                .GroupBy(x =>
                    x.Diagnosis,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                    new
                    {
                        Key = g.Key,
                        Count = g.Count()
                    })
                .OrderByDescending(x =>
                    x.Count)
                .FirstOrDefault();

        if (routeLeak == null &&
            diagnosis == null)
        {
            return new PersonalWeaknessMapV8Report(
                map,
                "—",
                "—",
                "LEARNING",
                "Need repeated route outcomes or verified Ground Truth.",
                "Keep playing normal matches while the Digital Twin collects evidence.",
                0,
                "LOW");
        }

        var leak =
            DiagnosisLabel(
                diagnosis?.Key);

        var evidenceParts =
            new List<string>();

        if (routeLeak != null)
        {
            evidenceParts.Add(
                $"{routeLeak.Attempts} route samples");
        }

        if (diagnosis != null)
        {
            evidenceParts.Add(
                $"{diagnosis.Count} verified {leak.ToLowerInvariant()} events");
        }

        var samples =
            (routeLeak?.Attempts ?? 0) +
            (diagnosis?.Count ?? 0);

        var confidence =
            samples >= 14 &&
            gtRounds.Count >= 6
                ? "HIGH"
                : samples >= 6
                    ? "MEDIUM"
                    : "LOW";

        var side =
            routeLeak?.Side ??
            gtRounds
                .GroupBy(x => x.Side)
                .OrderByDescending(x => x.Count())
                .Select(x => x.Key)
                .FirstOrDefault() ??
            "—";

        var route =
            routeLeak?.Route ??
            gtRounds
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.RecommendedRoute))
                .GroupBy(x =>
                    x.RecommendedRoute,
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .Select(x => x.Key)
                .FirstOrDefault() ??
            "—";

        if (leak == "ROUTE / POSITION" &&
            routeLeak != null)
        {
            leak =
                routeLeak.Weakness >= 0.62
                    ? "ROUTE CONVERSION"
                    : "ROUTE CONSISTENCY";
        }

        return new PersonalWeaknessMapV8Report(
            map,
            side,
            route,
            leak,
            string.Join(
                " • ",
                evidenceParts),
            FocusFor(
                side,
                leak),
            samples,
            confidence);
    }

    private static string DiagnosisLabel(
        string? diagnosis)
        => diagnosis switch
        {
            "EXECUTION_ROUTE_DIVERGENCE" =>
                "ROUTE / POSITION",
            "POST_IMPACT_OVEREXTEND" =>
                "POST-KILL DISCIPLINE",
            "OPENING_DUEL_LOSS" =>
                "OPENING DUEL",
            "COACH_PLAN_UNDERPERFORMING" =>
                "PLAN FIT",
            "COACH_PLAN_REVIEW" =>
                "PLAN CONSISTENCY",
            "MIXED_RESULT" =>
                "ROUND CONVERSION",
            _ =>
                "ROUTE / POSITION"
        };

    private static string FocusFor(
        string side,
        string leak)
    {
        if (leak.Contains(
                "POST-KILL",
                StringComparison.OrdinalIgnoreCase))
        {
            return "After first impact: stop, reposition, and make the next duel come to you.";
        }

        if (leak.Contains(
                "OPENING",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Make the first duel tradeable or utility-assisted; avoid isolated dry openings.";
        }

        if (side.Equals(
                "CT",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Change the first hold angle after contact; preserve a fall-back and rotate path.";
        }

        if (side.Equals(
                "T",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Use utility or a teammate before the first committed contact; keep a reset route.";
        }

        return "Vary the first contact and preserve a safe second option.";
    }
}
