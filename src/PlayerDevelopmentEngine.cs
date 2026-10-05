namespace Sm0kiSoloCoach;

public sealed record DevelopmentMetric(
    string Key,
    string Label,
    int Score,
    string Evidence,
    string NextGoal);

public sealed record PlayerDevelopmentReport(
    int OverallScore,
    string BiggestLeak,
    string MatchFocus,
    string CoachMessage,
    IReadOnlyList<DevelopmentMetric> Metrics);

/// <summary>
/// Evidence-based development layer for v4.2.0.
/// Scores only signals that are present in RoundRecord; it deliberately avoids
/// pretending that GSI can measure aim, utility damage or trades when it cannot.
/// </summary>
public static class PlayerDevelopmentEngine
{
    public static PlayerDevelopmentReport Analyze(IReadOnlyList<RoundRecord> rounds)
    {
        var sample = rounds.TakeLast(20).ToList();
        if (sample.Count < 3)
        {
            return new PlayerDevelopmentReport(
                50,
                "Collecting evidence",
                "Play normal CS2 and let the coach learn your patterns.",
                $"Need more rounds: {sample.Count}/3 minimum collected.",
                new[]
                {
                    new DevelopmentMetric("sample", "Evidence", 50,
                        $"{sample.Count} rounds recorded", "Reach at least 8 rounds for stronger coaching")
                });
        }

        var survivalRate = Rate(sample.Count(r => r.Survived), sample.Count);
        var winRate = Rate(sample.Count(r => r.Won == true), sample.Count(r => r.Won.HasValue));
        var killRounds = Rate(sample.Count(r => r.KillsRound > 0), sample.Count);
        var multiKillRounds = Rate(sample.Count(r => r.KillsRound >= 2), sample.Count);
        var zeroKillDeaths = Rate(sample.Count(r => r.DeathsRound > 0 && r.KillsRound == 0), sample.Count);

        var survivalScore = ClampScore(35 + survivalRate * 65);
        var impactScore = ClampScore(30 + killRounds * 50 + multiKillRounds * 20);
        var disciplineScore = ClampScore(100 - zeroKillDeaths * 75);
        var conversionScore = ClampScore(35 + winRate * 65);

        var metrics = new List<DevelopmentMetric>
        {
            new("survival", "Survival", survivalScore,
                $"Survived {sample.Count(r => r.Survived)}/{sample.Count} recent rounds",
                survivalRate < .45 ? "Target 45%+ survival without becoming passive" : "Keep survival while preserving impact"),
            new("impact", "Round Impact", impactScore,
                $"Kill in {sample.Count(r => r.KillsRound > 0)}/{sample.Count}; multi-kill in {sample.Count(r => r.KillsRound >= 2)}/{sample.Count}",
                killRounds < .55 ? "Create one tradeable impact opportunity per round" : "Convert first impact into round advantage"),
            new("discipline", "Duel Discipline", disciplineScore,
                $"Zero-kill deaths {sample.Count(r => r.DeathsRound > 0 && r.KillsRound == 0)}/{sample.Count}",
                zeroKillDeaths > .35 ? "Reduce low-value deaths; avoid isolated repeat peeks" : "Maintain controlled first-contact decisions"),
            new("conversion", "Round Conversion", conversionScore,
                $"Won {sample.Count(r => r.Won == true)}/{sample.Count(r => r.Won.HasValue)} resolved rounds",
                winRate < .50 ? "After gaining advantage, prioritize trade/survival over another peek" : "Keep converting advantages consistently")
        };

        var weakest = metrics.OrderBy(m => m.Score).First();
        var overall = (int)Math.Round(metrics.Average(m => m.Score));
        var focus = FocusFor(weakest.Key);

        return new PlayerDevelopmentReport(
            overall,
            $"{weakest.Label} {weakest.Score}/100",
            focus,
            $"Main development target: {weakest.Label}. {weakest.NextGoal}",
            metrics);
    }

    public static string CompactLiveFocus(IReadOnlyList<RoundRecord> rounds)
    {
        var report = Analyze(rounds);
        return $"FOCUS: {report.MatchFocus}\nDEV: {report.BiggestLeak}";
    }

    private static string FocusFor(string key) => key switch
    {
        "survival" => "After first contact: reposition and stay tradeable.",
        "impact" => "Seek one high-quality, utility-supported or tradeable duel; do not chain-peek.",
        "discipline" => "No isolated repeat peek. Make the next death cost the enemy something.",
        "conversion" => "When your team has advantage, stop donating duels and play the trade.",
        _ => "Play normally while the coach collects a larger sample."
    };

    private static double Rate(int value, int total)
        => total <= 0 ? .5 : Math.Clamp((double)value / total, 0, 1);

    private static int ClampScore(double value)
        => (int)Math.Round(Math.Clamp(value, 0, 100));
}
