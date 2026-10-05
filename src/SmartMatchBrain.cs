namespace Sm0kiSoloCoach;

public sealed record SmartMatchBrainReport(
    string MistakeKey,
    string Mistake,
    string Priority,
    string OneFocus,
    string Evidence,
    string Confidence,
    string FocusStatus,
    string FocusProgress,
    int FocusDetectedAtRound,
    int SampleSize);

public static class SmartMatchBrainEngine
{
    private sealed record Candidate(
        string Key,
        string Mistake,
        string Priority,
        string Focus,
        string Evidence,
        string Confidence,
        double Strength);

    public static SmartMatchBrainReport Analyze(IReadOnlyList<RoundRecord> rounds)
    {
        var currentSample = rounds.TakeLast(8).ToList();

        if (currentSample.Count < 3)
        {
            return new SmartMatchBrainReport(
                "learning",
                "Collecting evidence",
                "KEEP PLAN",
                "Play normal CS2 while Smart Match Brain learns your pattern.",
                $"{currentSample.Count}/3 minimum rounds collected",
                "LOW",
                "LEARNING",
                "Waiting for enough rounds",
                0,
                currentSample.Count);
        }

        var current = Detect(currentSample);
        var (focus, detectedAt) = FindOneFocus(rounds);

        var (focusStatus, focusProgress) =
            EvaluateFocusProgress(rounds, focus, detectedAt);

        return new SmartMatchBrainReport(
            current.Key,
            current.Mistake,
            current.Priority,
            focus.Focus,
            current.Evidence,
            current.Confidence,
            focusStatus,
            focusProgress,
            detectedAt,
            currentSample.Count);
    }

    private static (Candidate Focus, int DetectedAt) FindOneFocus(
        IReadOnlyList<RoundRecord> rounds)
    {
        // One Focus Mode is intentionally sticky:
        // once a medium/high-confidence repeat mistake first appears,
        // that becomes the match focus instead of changing every round.
        for (int count = 5; count <= rounds.Count; count++)
        {
            var sample = rounds
                .Take(count)
                .TakeLast(8)
                .ToList();

            var candidate = Detect(sample);

            if (candidate.Key != "steady" &&
                candidate.Confidence is "MEDIUM" or "HIGH")
            {
                return (candidate, count);
            }
        }

        var latest = Detect(rounds.TakeLast(8).ToList());

        if (latest.Key == "steady")
        {
            return (
                new Candidate(
                    "steady",
                    "No strong repeat mistake",
                    "KEEP PLAN",
                    "Keep your current discipline. Do not force a new fix.",
                    latest.Evidence,
                    latest.Confidence,
                    latest.Strength),
                0);
        }

        return (latest, rounds.Count);
    }

    private static Candidate Detect(IReadOnlyList<RoundRecord> sample)
    {
        if (sample.Count == 0)
            return Steady(sample, "No completed rounds yet");

        var candidates = new List<Candidate>();

        // 1) Low-value death pattern: died without producing a kill.
        var zeroKillDeaths = sample.Count(r =>
            r.DeathsRound > 0 &&
            r.KillsRound == 0);

        AddCandidate(
            candidates,
            key: "discipline",
            occurrences: zeroKillDeaths,
            relevant: sample.Count,
            threshold: .35,
            minOccurrences: 2,
            mistake: "Too many low-value deaths",
            priority: "NEXT ROUND: no isolated repeat peek. Make the first death tradeable.",
            focus: "Make every death cost the enemy something.",
            evidence: $"Zero-kill deaths {zeroKillDeaths}/{sample.Count}");

        // 2) Post-impact survival: got a kill but still died in the same round.
        var impactRounds = sample.Count(r => r.KillsRound > 0);
        var impactDeaths = sample.Count(r =>
            r.KillsRound > 0 &&
            !r.Survived);

        AddCandidate(
            candidates,
            key: "post_impact",
            occurrences: impactDeaths,
            relevant: impactRounds,
            threshold: .55,
            minOccurrences: 2,
            mistake: "Impact is being given back",
            priority: "NEXT ROUND: after your first kill, stop and reposition before the next duel.",
            focus: "After first impact: reposition and stay tradeable.",
            evidence: $"Died after impact {impactDeaths}/{Math.Max(1, impactRounds)} impact rounds");

        // 3) Low impact pattern: too many rounds without a kill.
        var zeroKillRounds = sample.Count(r => r.KillsRound == 0);

        AddCandidate(
            candidates,
            key: "impact",
            occurrences: zeroKillRounds,
            relevant: sample.Count,
            threshold: .50,
            minOccurrences: 3,
            mistake: "Too many rounds without impact",
            priority: "NEXT ROUND: create one tradeable or utility-supported duel. Do not hunt.",
            focus: "Create one high-quality impact chance per round.",
            evidence: $"0K rounds {zeroKillRounds}/{sample.Count}");

        // 4) Conversion pattern: produced impact but the round was still lost.
        var resolvedImpact = sample.Count(r =>
            r.KillsRound > 0 &&
            r.Won.HasValue);

        var lostImpact = sample.Count(r =>
            r.KillsRound > 0 &&
            r.Won == false);

        AddCandidate(
            candidates,
            key: "conversion",
            occurrences: lostImpact,
            relevant: resolvedImpact,
            threshold: .50,
            minOccurrences: 2,
            mistake: "Impact is not converting into rounds",
            priority: "NEXT ROUND: after advantage, stop donating duels and play the trade.",
            focus: "Protect advantages instead of chasing another duel.",
            evidence: $"Impact-round losses {lostImpact}/{Math.Max(1, resolvedImpact)}");

        if (candidates.Count == 0)
        {
            var survived = sample.Count(r => r.Survived);
            var kills = sample.Sum(r => r.KillsRound);

            return Steady(
                sample,
                $"No repeat leak crossed the threshold • survival {survived}/{sample.Count} • kills {kills}");
        }

        return candidates
            .OrderByDescending(x => x.Strength)
            .ThenByDescending(x => ConfidenceRank(x.Confidence))
            .First();
    }

    private static Candidate Steady(
        IReadOnlyList<RoundRecord> sample,
        string evidence)
        => new(
            "steady",
            "No strong repeat mistake",
            "KEEP PLAN",
            "Keep your current discipline. Do not force a new fix.",
            evidence,
            sample.Count >= 6 ? "HIGH" : "MEDIUM",
            0);

    private static void AddCandidate(
        ICollection<Candidate> candidates,
        string key,
        int occurrences,
        int relevant,
        double threshold,
        int minOccurrences,
        string mistake,
        string priority,
        string focus,
        string evidence)
    {
        if (relevant <= 0 ||
            occurrences < minOccurrences)
            return;

        var rate = Math.Clamp((double)occurrences / relevant, 0, 1);

        if (rate < threshold)
            return;

        var confidence =
            relevant >= 6 &&
            occurrences >= 4 &&
            rate >= .60
                ? "HIGH"
                : relevant >= 4 &&
                  occurrences >= 2
                    ? "MEDIUM"
                    : "LOW";

        var strength =
            Math.Max(0, rate - threshold) * 100 +
            occurrences * 8 +
            ConfidenceRank(confidence) * 4;

        candidates.Add(new Candidate(
            key,
            mistake,
            priority,
            focus,
            evidence,
            confidence,
            strength));
    }

    private static (string Status, string Progress) EvaluateFocusProgress(
        IReadOnlyList<RoundRecord> rounds,
        Candidate focus,
        int detectedAt)
    {
        if (focus.Key == "steady")
            return ("STEADY", "No forced correction needed");

        if (detectedAt <= 0 ||
            rounds.Count <= detectedAt)
        {
            return ("LOCKED", "Focus locked • waiting for follow-up rounds");
        }

        var followUp = rounds.Skip(detectedAt).ToList();

        int relevant;
        int success;

        switch (focus.Key)
        {
            case "discipline":
                relevant = followUp.Count;
                success = followUp.Count(r =>
                    !(r.DeathsRound > 0 && r.KillsRound == 0));
                break;

            case "post_impact":
                var impact = followUp
                    .Where(r => r.KillsRound > 0)
                    .ToList();
                relevant = impact.Count;
                success = impact.Count(r => r.Survived);
                break;

            case "impact":
                relevant = followUp.Count;
                success = followUp.Count(r => r.KillsRound > 0);
                break;

            case "conversion":
                var converted = followUp
                    .Where(r => r.KillsRound > 0 && r.Won.HasValue)
                    .ToList();
                relevant = converted.Count;
                success = converted.Count(r => r.Won == true);
                break;

            default:
                return ("LOCKED", "Focus active");
        }

        if (relevant == 0)
            return ("LOCKED", "No relevant follow-up round yet");

        var rate = (double)success / relevant;
        var status =
            relevant >= 3 && rate >= .67
                ? "ON TRACK"
                : relevant >= 3 && rate < .45
                    ? "NEEDS WORK"
                    : "ACTIVE";

        return (
            status,
            $"{success}/{relevant} follow-up rounds on target");
    }

    private static int ConfidenceRank(string confidence)
        => confidence switch
        {
            "HIGH" => 3,
            "MEDIUM" => 2,
            _ => 1
        };
}
