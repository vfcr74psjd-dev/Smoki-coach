using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class TrainingMissionState
{
    public string Key { get; set; } = "";
    public string Map { get; set; } = "";
    public string Label { get; set; } = "";
    public string Instruction { get; set; } = "";
    public int TargetRounds { get; set; } = 15;
    public int TrackedRounds { get; set; }
    public int SuccessRounds { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Complete =>
        TrackedRounds >= TargetRounds;

    public double SuccessRate =>
        TrackedRounds > 0
            ? (double)SuccessRounds / TrackedRounds
            : 0;

    public string Compact =>
        string.IsNullOrWhiteSpace(Key)
            ? "No mission yet"
            : $"{Label} • {SuccessRounds}/{TrackedRounds} success • streak {CurrentStreak}";
}

public static class TrainingMissionStore
{
    private static readonly object Gate = new();

    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach");

    private static readonly string FilePath =
        Path.Combine(
            Dir,
            "training-mission-v7.json");

    private static TrainingMissionState? _cache;

    public static TrainingMissionState Current(
        string map)
    {
        lock (Gate)
        {
            var mission = LoadUnsafe();

            if (mission.Complete ||
                string.IsNullOrWhiteSpace(mission.Key))
            {
                mission = BuildFromMemory(map);
                SaveUnsafe(mission);
            }

            return Clone(mission);
        }
    }

    public static void Update(
        string map,
        RoundRecord round)
    {
        lock (Gate)
        {
            var mission = LoadUnsafe();

            if (mission.Complete ||
                string.IsNullOrWhiteSpace(mission.Key))
            {
                mission = BuildFromMemory(map);
            }

            if (string.IsNullOrWhiteSpace(mission.Key))
            {
                SaveUnsafe(mission);
                return;
            }

            var qualifies = true;
            var success = mission.Key switch
            {
                "discipline" =>
                    !(round.DeathsRound > 0 &&
                      round.KillsRound == 0),

                "post_impact" =>
                    round.KillsRound == 0 ||
                    round.Survived,

                "impact" =>
                    round.KillsRound > 0,

                "conversion" =>
                    round.KillsRound == 0 ||
                    round.Won == true,

                _ => true
            };

            if (qualifies)
            {
                mission.TrackedRounds++;

                if (success)
                {
                    mission.SuccessRounds++;
                    mission.CurrentStreak++;
                    mission.BestStreak =
                        Math.Max(
                            mission.BestStreak,
                            mission.CurrentStreak);
                }
                else
                {
                    mission.CurrentStreak = 0;
                }
            }

            mission.UpdatedUtc = DateTime.UtcNow;
            SaveUnsafe(mission);
        }
    }

    public static void ResetForNewMission(
        string map)
    {
        lock (Gate)
        {
            var mission =
                BuildFromMemory(map);
            SaveUnsafe(mission);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _cache =
                new TrainingMissionState();

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }

    private static TrainingMissionState BuildFromMemory(
        string map)
    {
        var leak =
            MistakeLibraryStore.TopRecurring(
                map,
                10);

        if (leak.Matches < 2 ||
            string.IsNullOrWhiteSpace(leak.Key))
        {
            return new TrainingMissionState
            {
                Map = map
            };
        }

        var (label, instruction, target) =
            leak.Key switch
            {
                "discipline" => (
                    "15R • TRADEABLE DEATHS",
                    "No isolated zero-impact death. One duel, then reset.",
                    15),

                "post_impact" => (
                    "15R • SURVIVE IMPACT",
                    "After first kill: break contact and reposition.",
                    15),

                "impact" => (
                    "12R • CREATE IMPACT",
                    "Create one utility-supported or tradeable kill chance.",
                    12),

                "conversion" => (
                    "12R • PROTECT ADVANTAGE",
                    "After impact, stop hunting and play the round win.",
                    12),

                _ => (
                    "15R • DISCIPLINE",
                    leak.CoachingFocus,
                    15)
            };

        return new TrainingMissionState
        {
            Key = leak.Key,
            Map = map,
            Label = label,
            Instruction = instruction,
            TargetRounds = target,
            StartedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static TrainingMissionState Clone(
        TrainingMissionState x)
        => new()
        {
            Key = x.Key,
            Map = x.Map,
            Label = x.Label,
            Instruction = x.Instruction,
            TargetRounds = x.TargetRounds,
            TrackedRounds = x.TrackedRounds,
            SuccessRounds = x.SuccessRounds,
            CurrentStreak = x.CurrentStreak,
            BestStreak = x.BestStreak,
            StartedUtc = x.StartedUtc,
            UpdatedUtc = x.UpdatedUtc
        };

    private static TrainingMissionState LoadUnsafe()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (!File.Exists(FilePath))
                return _cache =
                    new TrainingMissionState();

            return _cache =
                JsonSerializer.Deserialize<TrainingMissionState>(
                    File.ReadAllText(FilePath))
                ?? new TrainingMissionState();
        }
        catch
        {
            return _cache =
                new TrainingMissionState();
        }
    }

    private static void SaveUnsafe(
        TrainingMissionState mission)
    {
        _cache = mission;

        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    mission,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch { }
    }
}
