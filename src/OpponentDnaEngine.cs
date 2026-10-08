namespace Sm0kiSoloCoach;

public sealed record OpponentDnaProfile(
    string Map,
    string Archetype,
    string TPressureZone,
    double TPressureShare,
    string CtControlZone,
    double CtControlShare,
    double TOpeningWinRate,
    double CtOpeningWinRate,
    string BiggestThreat,
    double TeamMapWinRate,
    double TeamRecentKd,
    int OpeningSamples,
    string Confidence)
{
    public string Compact
    {
        get
        {
            var parts = new List<string>
            {
                Archetype
            };

            if (TPressureZone is "A" or "B" or "MID" &&
                TPressureShare >= 0.45)
            {
                parts.Add(
                    $"T {TPressureZone} {TPressureShare * 100:0}%");
            }

            if (CtControlZone is "A" or "B" or "MID" &&
                CtControlShare >= 0.40)
            {
                parts.Add(
                    $"CT {CtControlZone} {CtControlShare * 100:0}%");
            }

            if (!string.IsNullOrWhiteSpace(BiggestThreat))
                parts.Add("watch " + BiggestThreat);

            parts.Add(Confidence);

            return string.Join(
                " • ",
                parts.Take(4));
        }
    }
}

public static class OpponentDnaEngine
{
    public static OpponentDnaProfile Analyze(
        string map)
        => Build(
            map,
            OpponentScoutStore.LoadLatest(map),
            OpponentDemoIntelStore.LoadLatest(map));

    public static OpponentDnaProfile Build(
        string map,
        OpponentScoutReport? scout,
        OpponentDemoIntelReport? demo)
    {
        var tZone =
            demo?.TopTOpeningZone ?? "";

        var ctZone =
            demo?.TopCtOpeningZone ?? "";

        var tShare =
            demo?.TTopZoneShare ?? 0;

        var ctShare =
            demo?.CtTopZoneShare ?? 0;

        var tOpenRate =
            Rate(
                demo?.TOpeningKills ?? 0,
                demo?.TOpeningDeaths ?? 0);

        var ctOpenRate =
            Rate(
                demo?.CtOpeningKills ?? 0,
                demo?.CtOpeningDeaths ?? 0);

        var mapWr =
            scout?.TeamMapWinRate ?? 0;

        var recentKd =
            scout?.TeamRecentKd ?? 0;

        var threat =
            ResolveThreat(
                scout,
                demo);

        var archetype =
            ResolveArchetype(
                tShare,
                ctShare,
                tOpenRate,
                ctOpenRate,
                mapWr,
                recentKd,
                demo?.OpeningSamples ?? 0);

        var evidence = 0;

        if (scout != null)
            evidence += 2;

        if (scout?.TeamMapWinRate != null)
            evidence++;

        if (scout?.TeamRecentKd != null)
            evidence++;

        var openings =
            demo?.OpeningSamples ?? 0;

        if (openings >= 6)
            evidence += 2;

        if (openings >= 12)
            evidence += 2;

        if (tShare >= 0.55 ||
            ctShare >= 0.50)
            evidence++;

        var confidence =
            evidence >= 7
                ? "HIGH"
                : evidence >= 4
                    ? "MEDIUM"
                    : "LOW";

        return new OpponentDnaProfile(
            map,
            archetype,
            tZone,
            tShare,
            ctZone,
            ctShare,
            tOpenRate,
            ctOpenRate,
            threat,
            mapWr,
            recentKd,
            openings,
            confidence);
    }

    public static double ScorePlan(
        OpponentDnaProfile dna,
        string side,
        string intent,
        string route)
    {
        var score = 50.0;

        if (side.Equals(
                "CT",
                StringComparison.OrdinalIgnoreCase))
        {
            if (dna.TPressureZone.Equals(
                    intent,
                    StringComparison.OrdinalIgnoreCase) &&
                dna.TPressureShare >= 0.45)
            {
                score +=
                    12 +
                    Math.Min(
                        10,
                        (dna.TPressureShare - 0.45) * 40);
            }

            if (dna.TOpeningWinRate >= 0.62 &&
                IsDirectRoute(route))
            {
                score -= 7;
            }
            else if (dna.TOpeningWinRate <= 0.42 &&
                     IsDirectRoute(route))
            {
                score += 5;
            }
        }
        else
        {
            var zone =
                RouteZone(route);

            if (zone.Length > 0 &&
                zone.Equals(
                    dna.CtControlZone,
                    StringComparison.OrdinalIgnoreCase) &&
                dna.CtControlShare >= 0.40)
            {
                score +=
                    dna.CtOpeningWinRate >= 0.58
                        ? -18
                        : dna.CtOpeningWinRate <= 0.42
                            ? 10
                            : -6;
            }

            if (dna.TeamMapWinRate >= 60 &&
                IsDirectRoute(route))
            {
                score -= 5;
            }

            if (dna.TeamRecentKd >= 1.15 &&
                IsDirectRoute(route))
            {
                score -= 4;
            }
        }

        return Math.Clamp(
            score,
            10,
            95);
    }

    private static double Rate(
        int wins,
        int losses)
    {
        var total = wins + losses;

        return total <= 0
            ? 0.5
            : (double)wins / total;
    }

    private static string ResolveThreat(
        OpponentScoutReport? scout,
        OpponentDemoIntelReport? demo)
    {
        var demoThreat =
            demo?.OpeningKillsByPlayer?
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .FirstOrDefault();

        if (demoThreat.HasValue &&
            !string.IsNullOrWhiteSpace(
                demoThreat.Value.Key) &&
            demoThreat.Value.Value >= 2)
        {
            return demoThreat.Value.Key;
        }

        return scout?.BiggestThreat ?? "";
    }

    private static string ResolveArchetype(
        double tShare,
        double ctShare,
        double tOpenRate,
        double ctOpenRate,
        double mapWr,
        double recentKd,
        int openings)
    {
        if (openings >= 8 &&
            tShare >= 0.62)
            return "T PRESSURE HEAVY";

        if (openings >= 8 &&
            ctShare >= 0.55 &&
            ctOpenRate >= 0.58)
            return "CT OPENING CONTROL";

        if (openings >= 8 &&
            tOpenRate >= 0.62)
            return "OPENING AGGRESSION";

        if (mapWr >= 60 ||
            recentKd >= 1.15)
            return "HIGH-FORM TEAM";

        if (openings >= 6)
            return "BALANCED PATTERN";

        return "LOW EVIDENCE";
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
