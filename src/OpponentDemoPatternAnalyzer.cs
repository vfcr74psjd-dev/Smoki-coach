using DemoFile;
using DemoFile.Game.Cs;

namespace Sm0kiSoloCoach;

public sealed record OpponentOpeningEvent(
    int Round,
    string Side,
    string Outcome,
    string Zone,
    string Player,
    string PlaceName);

public sealed class OpponentDemoPatternResult
{
    public string Map { get; set; } = "";
    public List<OpponentOpeningEvent> Openings { get; } = new();
}

public static class OpponentDemoPatternAnalyzer
{
    public static async Task<OpponentDemoPatternResult> AnalyzeAsync(
        string demoPath,
        IReadOnlyCollection<string> opponentNicknames,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(demoPath))
            throw new FileNotFoundException(
                "Opponent demo ni bil najden.",
                demoPath);

        var targets = opponentNicknames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targets.Count == 0)
            throw new InvalidOperationException(
                "Opponent demo analyzer nima ciljnih igralcev.");

        var result = new OpponentDemoPatternResult();
        var demo = new CsDemoParser();
        var round = 0;
        var roundHasOpening = new HashSet<int>();

        bool IsTarget(CCSPlayerController? player)
            => player != null &&
               targets.Contains(player.PlayerName);

        static string SideOf(CCSPlayerController? player)
            => player?.CSTeamNum switch
            {
                CSTeamNumber.CounterTerrorist => "CT",
                CSTeamNumber.Terrorist => "T",
                _ => ""
            };

        demo.PacketEvents.SvcServerInfo += e =>
        {
            if (!string.IsNullOrWhiteSpace(e.MapName))
                result.Map = e.MapName;
        };

        demo.Source1GameEvents.RoundStart += _ =>
        {
            if (demo.GameRules.CSGamePhase != CSGamePhase.WarmupRound)
                round++;
        };

        demo.Source1GameEvents.PlayerDeath += e =>
        {
            if (demo.GameRules.CSGamePhase == CSGamePhase.WarmupRound ||
                round <= 0 ||
                roundHasOpening.Contains(round))
                return;

            var attackerTarget = IsTarget(e.Attacker);
            var victimTarget = IsTarget(e.Player);

            if (attackerTarget == victimTarget)
                return;

            // This is the first death in this round involving the opponent
            // roster. It is measurable opening/contact evidence, not a claim
            // about the entire execute.
            var targetPlayer =
                attackerTarget
                    ? e.Attacker
                    : e.Player;

            var side = SideOf(targetPlayer);
            if (string.IsNullOrWhiteSpace(side))
                return;

            var place =
                e.PlayerPawn?.LastPlaceName ?? "";

            var zone = NormalizeZone(place);
            var outcome = attackerTarget ? "KILL" : "DEATH";
            var playerName = targetPlayer?.PlayerName ?? "";

            roundHasOpening.Add(round);
            result.Openings.Add(
                new OpponentOpeningEvent(
                    round,
                    side,
                    outcome,
                    zone,
                    playerName,
                    place));
        };

        using var fs = File.OpenRead(demoPath);
        var reader = DemoFileReader.Create(demo, fs);
        await reader.ReadAllAsync(cancellationToken);

        return result;
    }

    public static string NormalizeZone(string? placeName)
    {
        var p = (placeName ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "");

        if (p.Length == 0)
            return "OTHER";

        if (p.Contains("mid") ||
            p.Contains("middle") ||
            p.Contains("connector") ||
            p.Contains("catwalk") ||
            p.Contains("short") ||
            p.Contains("water"))
            return "MID";

        if (p.Contains("bombsiteb") ||
            p.Contains("bsite") ||
            p.Contains("bapps") ||
            p.Contains("bapart") ||
            p.Contains("btunnel") ||
            p.Contains("uppertunnel") ||
            p.Contains("banana") ||
            p.Contains("bmain") ||
            p.Contains("monster") ||
            p.Contains("bramp") ||
            p.Contains("cave") ||
            p.Contains("ramp") && p.Contains("b"))
            return "B";

        if (p.Contains("bombsitea") ||
            p.Contains("asite") ||
            p.Contains("aramp") ||
            p.Contains("amain") ||
            p.Contains("palace") ||
            p.Contains("longa") ||
            p.Contains("along") ||
            p.Contains("ivy") ||
            p.Contains("hut") ||
            p.Contains("squeaky"))
            return "A";

        return "OTHER";
    }
}

public sealed class OpponentDemoIntelReport
{
    public string Map { get; set; } = "";
    public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
    public int DemosAnalyzed { get; set; }
    public int OpeningSamples { get; set; }

    public Dictionary<string,int> TContactZones { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,int> CtContactZones { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int TOpeningKills { get; set; }
    public int TOpeningDeaths { get; set; }
    public int CtOpeningKills { get; set; }
    public int CtOpeningDeaths { get; set; }

    public Dictionary<string,int> OpeningKillsByPlayer { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string TopTOpeningZone { get; set; } = "";
    public string TopCtOpeningZone { get; set; } = "";
    public string OpeningThreat { get; set; } = "";
    public string Confidence { get; set; } = "LOW";

    public double TTopZoneShare =>
        ZoneShare(TContactZones, TopTOpeningZone);

    public double CtTopZoneShare =>
        ZoneShare(CtContactZones, TopCtOpeningZone);

    private static double ZoneShare(
        IReadOnlyDictionary<string,int> zones,
        string zone)
    {
        var total = zones
            .Where(x => !x.Key.Equals(
                "OTHER",
                StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Value);

        if (total <= 0 ||
            string.IsNullOrWhiteSpace(zone) ||
            !zones.TryGetValue(zone, out var count))
            return 0;

        return (double)count / total;
    }

    public string CompactSummary
        => $"demo {DemosAnalyzed}x • openings {OpeningSamples} • " +
           $"T {TopTOpeningZoneOrDash()} • CT {TopCtOpeningZoneOrDash()} • " +
           $"watch {(string.IsNullOrWhiteSpace(OpeningThreat) ? "—" : OpeningThreat)} • {Confidence}";

    private string TopTOpeningZoneOrDash()
        => string.IsNullOrWhiteSpace(TopTOpeningZone)
            ? "—"
            : $"{TopTOpeningZone} {TTopZoneShare * 100:0}%";

    private string TopCtOpeningZoneOrDash()
        => string.IsNullOrWhiteSpace(TopCtOpeningZone)
            ? "—"
            : $"{TopCtOpeningZone} {CtTopZoneShare * 100:0}%";
}
