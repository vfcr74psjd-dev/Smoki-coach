namespace Sm0kiSoloCoach;

public sealed record HalftimeReportV6(
    bool Active,
    string FromSide,
    string ToSide,
    string Keep,
    string Change,
    string Opening,
    string Focus,
    string Confidence,
    int PreviousSideRounds)
{
    public string Compact =>
        !Active
            ? ""
            : $"{FromSide}→{ToSide} • KEEP {Keep} • CHANGE {Change} • OPEN {Opening}";
}

public static class HalftimeBrainV6
{
    public static HalftimeReportV6 Analyze(
        string currentSide,
        IReadOnlyList<RoundRecord> rounds)
    {
        currentSide = NormalizeSide(currentSide);
        if (currentSide.Length == 0 || rounds.Count < 6)
            return Empty(currentSide);

        var previousSide = rounds
            .LastOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.Side) &&
                !NormalizeSide(r.Side).Equals(
                    currentSide,
                    StringComparison.OrdinalIgnoreCase))
            ?.Side;

        previousSide = NormalizeSide(previousSide);
        if (previousSide.Length == 0 ||
            previousSide == currentSide)
            return Empty(currentSide);

        var currentSideRounds = rounds.Count(r =>
            NormalizeSide(r.Side) == currentSide);

        // Keep the halftime reset relevant only for the first two completed
        // rounds on the new side. After that, current-match evidence wins.
        if (currentSideRounds > 2)
            return Empty(currentSide);

        var previous = rounds
            .Where(r => NormalizeSide(r.Side) == previousSide)
            .TakeLast(13)
            .ToList();

        if (previous.Count < 5)
            return Empty(currentSide);

        var wins = previous.Count(r => r.Won == true);
        var survival =
            100.0 * previous.Count(r => r.Survived) /
            previous.Count;
        var zeroDeaths = previous.Count(r =>
            r.DeathsRound > 0 &&
            r.KillsRound == 0);
        var impactDeaths = previous.Count(r =>
            r.KillsRound > 0 &&
            !r.Survived);
        var multi = previous.Count(r =>
            r.KillsRound >= 2);

        var routes = previous
            .Where(r =>
                !string.IsNullOrWhiteSpace(
                    r.PositionPlan))
            .GroupBy(
                r => r.PositionPlan,
                StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Route = g.Key,
                Count = g.Count(),
                Wins = g.Count(r => r.Won == true),
                Survived = g.Count(r => r.Survived),
                Kills = g.Sum(r => r.KillsRound),
                ZeroDeaths = g.Count(r =>
                    r.DeathsRound > 0 &&
                    r.KillsRound == 0)
            })
            .Where(x => x.Count >= 2)
            .ToList();

        var bestRoute = routes
            .OrderByDescending(x =>
                2.0 * x.Wins / x.Count +
                0.8 * x.Survived / x.Count +
                0.4 * x.Kills / x.Count -
                0.5 * x.ZeroDeaths / x.Count)
            .FirstOrDefault();

        string keep;
        if (bestRoute != null &&
            bestRoute.Wins >= 1)
        {
            keep =
                $"discipline from {Short(bestRoute.Route)}";
        }
        else if (multi >= Math.Max(2, previous.Count / 5))
        {
            keep = "tradeable impact tempo";
        }
        else if (survival >= 50)
        {
            keep = "survival + reposition";
        }
        else
        {
            keep = "one clear role each round";
        }

        string change;
        string focus;

        if (zeroDeaths >= Math.Max(3, previous.Count / 3))
        {
            change = "less isolated first contact";
            focus = "make the first death tradeable";
        }
        else if (impactDeaths >= Math.Max(3, previous.Count / 3))
        {
            change = "stop after first impact";
            focus = "kill → reposition → second duel later";
        }
        else if (survival < 38)
        {
            change = "lower early risk";
            focus = "survive the opening phase";
        }
        else
        {
            change = "reset opening timings";
            focus = "new side, no autopilot repeats";
        }

        var opening = currentSide == "CT"
            ? "spawn-fit info/anchor • fall before second dry peek"
            : "spawn-fit route • utility/trade before commit";

        var confidence =
            previous.Count >= 10
                ? "HIGH"
                : previous.Count >= 7
                    ? "MEDIUM"
                    : "LOW";

        return new HalftimeReportV6(
            true,
            previousSide,
            currentSide,
            keep,
            change,
            opening,
            focus,
            confidence,
            previous.Count);
    }

    private static HalftimeReportV6 Empty(
        string currentSide)
        => new(
            false,
            "",
            currentSide,
            "",
            "",
            "",
            "",
            "LOW",
            0);

    private static string NormalizeSide(
        string? side)
    {
        var s = (side ?? "")
            .Trim()
            .ToUpperInvariant();

        return s is "CT" or "T"
            ? s
            : "";
    }

    private static string Short(string route)
    {
        route = (route ?? "").Trim();
        if (route.Length <= 34)
            return route;

        return route[..31] + "…";
    }
}
