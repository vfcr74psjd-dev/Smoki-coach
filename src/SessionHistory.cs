using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class SessionSummary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public string Nickname { get; set; } = "";
    public string Map { get; set; } = "";
    public int Rounds { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int ZeroKillRounds { get; set; }
    public int MultiKillRounds { get; set; }
    public int SurvivalRounds { get; set; }
    public int CtScore { get; set; }
    public int TScore { get; set; }
    public string BestArea { get; set; } = "";
    public string TroubleArea { get; set; } = "";
    public string NextFocus { get; set; } = "";
    public int DevelopmentScore { get; set; }
    public string DevelopmentLeak { get; set; } = "";
    public string DevelopmentFocus { get; set; } = "";

    public double KillsPerRound => Rounds > 0 ? (double)Kills / Rounds : 0;
    public double SurvivalRate => Rounds > 0 ? 100.0 * SurvivalRounds / Rounds : 0;
}

public static class SessionHistoryStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string FilePath = Path.Combine(Dir, "session-history.json");

    public static List<SessionSummary> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<SessionSummary>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(SessionSummary session)
    {
        if (session.Rounds < 4) return;

        var all=Load();

        var duplicate=all.Any(x =>
            x.Nickname.Equals(session.Nickname,StringComparison.OrdinalIgnoreCase) &&
            x.Map.Equals(session.Map,StringComparison.OrdinalIgnoreCase) &&
            Math.Abs((x.EndedUtc-session.EndedUtc).TotalMinutes) < 2 &&
            x.Rounds == session.Rounds &&
            x.Kills == session.Kills);

        if (duplicate) return;

        all.Add(session);
        all=all
            .OrderByDescending(x=>x.EndedUtc)
            .Take(100)
            .ToList();

        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(all,new JsonSerializerOptions{WriteIndented=true}));
    }
}
