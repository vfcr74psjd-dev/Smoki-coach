using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class DemoLearningQueueItem
{
    public string MatchId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Map { get; set; } = "";
    public DateTime FinishedUtc { get; set; }
    public string State { get; set; } = "WAITING_DEMO";
    public int Attempts { get; set; }
    public DateTime NextRetryUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string LastError { get; set; } = "";
    public string LocalDemoPath { get; set; } = "";
    public int ImportedDeaths { get; set; }

    public bool Learned =>
        State.Equals(
            "LEARNED",
            StringComparison.OrdinalIgnoreCase);
}

public static class DemoLearningQueueStore
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
            "demo-learning-queue-v7.json");

    private static List<DemoLearningQueueItem>? _cache;

    public static IReadOnlyList<DemoLearningQueueItem> Load()
    {
        lock (Gate)
            return LoadUnsafe()
                .OrderByDescending(x => x.FinishedUtc)
                .Select(Clone)
                .ToList();
    }

    public static void UpsertFromHistory(
        string nickname,
        IEnumerable<FaceitHistoryMatch> matches)
    {
        lock (Gate)
        {
            var all = LoadUnsafe();

            foreach (var match in matches)
            {
                if (string.IsNullOrWhiteSpace(match.MatchId))
                    continue;

                var item = all.FirstOrDefault(x =>
                    x.MatchId.Equals(
                        match.MatchId,
                        StringComparison.OrdinalIgnoreCase));

                if (item == null)
                {
                    item = new DemoLearningQueueItem
                    {
                        MatchId = match.MatchId,
                        Nickname = nickname,
                        Map = match.Map,
                        FinishedUtc = match.FinishedUtc,
                        State = match.DemoReady
                            ? "READY"
                            : "WAITING_DEMO",
                        NextRetryUtc = DateTime.UtcNow
                    };
                    all.Add(item);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(match.Map))
                        item.Map = match.Map;

                    if (match.FinishedUtc != default)
                        item.FinishedUtc = match.FinishedUtc;

                    if (!item.Learned &&
                        match.DemoReady &&
                        item.State == "WAITING_DEMO")
                    {
                        item.State = "READY";
                        item.NextRetryUtc = DateTime.UtcNow;
                    }

                    item.UpdatedUtc = DateTime.UtcNow;
                }
            }

            _cache = all
                .OrderByDescending(x => x.FinishedUtc)
                .Take(50)
                .ToList();

            SaveUnsafe();
        }
    }

    public static DemoLearningQueueItem? NextReady()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;

            return LoadUnsafe()
                .Where(x =>
                    !x.Learned &&
                    x.NextRetryUtc <= now &&
                    (x.State == "READY" ||
                     x.State == "RETRY"))
                .OrderByDescending(x => x.FinishedUtc)
                .Select(Clone)
                .FirstOrDefault();
        }
    }

    public static void SetState(
        string matchId,
        string state,
        string? error = null,
        string? localPath = null,
        int? importedDeaths = null,
        TimeSpan? retryAfter = null)
    {
        lock (Gate)
        {
            var item = LoadUnsafe()
                .FirstOrDefault(x =>
                    x.MatchId.Equals(
                        matchId,
                        StringComparison.OrdinalIgnoreCase));

            if (item == null)
                return;

            item.State = state;
            item.UpdatedUtc = DateTime.UtcNow;

            if (state is "DOWNLOADING" or "ANALYZING")
                item.Attempts++;

            if (error != null)
                item.LastError = error;

            if (localPath != null)
                item.LocalDemoPath = localPath;

            if (importedDeaths.HasValue)
                item.ImportedDeaths = importedDeaths.Value;

            item.NextRetryUtc =
                retryAfter.HasValue
                    ? DateTime.UtcNow + retryAfter.Value
                    : DateTime.UtcNow;

            SaveUnsafe();
        }
    }

    public static void MarkWaiting(
        string matchId,
        TimeSpan retryAfter)
        => SetState(
            matchId,
            "WAITING_DEMO",
            retryAfter: retryAfter);

    public static void Clear()
    {
        lock (Gate)
        {
            _cache = new();

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }

    private static List<DemoLearningQueueItem> LoadUnsafe()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (!File.Exists(FilePath))
                return _cache = new();

            return _cache =
                JsonSerializer.Deserialize<List<DemoLearningQueueItem>>(
                    File.ReadAllText(FilePath))
                ?? new();
        }
        catch
        {
            return _cache = new();
        }
    }

    private static void SaveUnsafe()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(
                    _cache ?? new(),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch { }
    }

    private static DemoLearningQueueItem Clone(
        DemoLearningQueueItem x)
        => new()
        {
            MatchId = x.MatchId,
            Nickname = x.Nickname,
            Map = x.Map,
            FinishedUtc = x.FinishedUtc,
            State = x.State,
            Attempts = x.Attempts,
            NextRetryUtc = x.NextRetryUtc,
            UpdatedUtc = x.UpdatedUtc,
            LastError = x.LastError,
            LocalDemoPath = x.LocalDemoPath,
            ImportedDeaths = x.ImportedDeaths
        };
}
