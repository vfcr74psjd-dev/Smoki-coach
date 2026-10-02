using DemoFile;
using DemoFile.Game.Cs;

namespace Sm0kiSoloCoach;

public sealed class DemoDeathPoint
{
    public int Round { get; set; }
    public string Victim { get; set; } = "";
    public string Killer { get; set; } = "";
    public string Weapon { get; set; } = "";
    public string Side { get; set; } = "";
    public string PlaceName { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class DemoReviewResult
{
    public string Map { get; set; } = "";
    public List<DemoDeathPoint> Deaths { get; } = new();
    public List<string> Players { get; } = new();
}

public static class DemoReviewService
{
    public static async Task<DemoReviewResult> AnalyzeAsync(
        string demoPath,
        string playerNickname,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(demoPath))
            throw new FileNotFoundException("Demo file ni bil najden.", demoPath);

        var result = new DemoReviewResult();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var demo = new CsDemoParser();
        int round = 0;

        demo.PacketEvents.SvcServerInfo += e =>
        {
            if (!string.IsNullOrWhiteSpace(e.MapName))
                result.Map = e.MapName;
        };

        demo.Source1GameEvents.RoundStart += _ =>
        {
            // FACEIT demos can contain warmup RoundStart events. Keep match round
            // numbering clean by counting only actual gameplay phases.
            if (demo.GameRules.CSGamePhase != CSGamePhase.WarmupRound)
                round++;
        };

        demo.Source1GameEvents.PlayerDeath += e =>
        {
            if (demo.GameRules.CSGamePhase == CSGamePhase.WarmupRound)
                return;

            var victim = e.Player?.PlayerName ?? "";
            var killer = e.Attacker?.PlayerName ?? "";

            if (!string.IsNullOrWhiteSpace(victim)) names.Add(victim);
            if (!string.IsNullOrWhiteSpace(killer)) names.Add(killer);

            if (!victim.Equals(playerNickname, StringComparison.OrdinalIgnoreCase))
                return;

            var pawn = e.PlayerPawn;
            if (pawn == null) return;

            var side = e.Player?.CSTeamNum switch
            {
                CSTeamNumber.CounterTerrorist => "CT",
                CSTeamNumber.Terrorist => "T",
                _ => ""
            };

            var pos = pawn.Origin;
            result.Deaths.Add(new DemoDeathPoint
            {
                Round = Math.Max(1, round),
                Victim = victim,
                Killer = killer,
                Weapon = e.Weapon ?? "",
                Side = side,
                PlaceName = pawn.LastPlaceName ?? "",
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z
            });
        };

        using var fs = File.OpenRead(demoPath);
        var reader = DemoFileReader.Create(demo, fs);
        await reader.ReadAllAsync(cancellationToken);

        result.Players.AddRange(names.OrderBy(x => x));
        return result;
    }
}
