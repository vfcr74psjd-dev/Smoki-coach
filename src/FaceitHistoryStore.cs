using System.Text;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class FaceitHistoryMatch
{
    public string MatchId { get; set; } = "";
    public string PlayerNickname { get; set; } = "";
    public string GameId { get; set; } = "cs2";
    public DateTime FinishedUtc { get; set; }
    public string Map { get; set; } = "";
    public string FaceitUrl { get; set; } = "";
    public string Status { get; set; } = "";
    public bool DemoReady { get; set; }
    public bool DetailsChecked { get; set; }
    public DateTime DetailsCheckedUtc { get; set; }
    public List<string> DemoResources { get; set; } = new();
    public DateTime SyncedUtc { get; set; } = DateTime.UtcNow;
}

public static class FaceitHistoryStore
{
    private static readonly object Sync = new();
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sm0kiSoloCoach");
    private static readonly string FilePath = Path.Combine(Dir, "faceit-history.json");

    public static List<FaceitHistoryMatch> Load()
    {
        lock (Sync)
            return LoadUnlocked();
    }

    public static IReadOnlyList<FaceitHistoryMatch> LoadForPlayer(string nickname)
    {
        nickname = (nickname ?? "").Trim();
        return Load()
            .Where(x => x.PlayerNickname.Equals(nickname, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.FinishedUtc)
            .ToList();
    }

    public static void UpsertRange(string nickname, IEnumerable<FaceitHistoryMatch> matches)
    {
        nickname = (nickname ?? "").Trim();

        lock (Sync)
        {
            var all = LoadUnlocked();
            var byId = all
                .Where(x => !string.IsNullOrWhiteSpace(x.MatchId))
                .ToDictionary(x => x.MatchId, StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in matches)
            {
                if (string.IsNullOrWhiteSpace(incoming.MatchId))
                    continue;

                incoming.PlayerNickname = nickname;
                incoming.SyncedUtc = DateTime.UtcNow;

                if (byId.TryGetValue(incoming.MatchId, out var existing))
                {
                    existing.PlayerNickname = incoming.PlayerNickname;
                    existing.GameId = incoming.GameId;
                    if (incoming.FinishedUtc != default) existing.FinishedUtc = incoming.FinishedUtc;
                    if (!string.IsNullOrWhiteSpace(incoming.Map)) existing.Map = incoming.Map;
                    if (!string.IsNullOrWhiteSpace(incoming.FaceitUrl)) existing.FaceitUrl = incoming.FaceitUrl;
                    if (!string.IsNullOrWhiteSpace(incoming.Status)) existing.Status = incoming.Status;
                    existing.DemoReady = incoming.DemoReady || existing.DemoReady;
                    existing.DetailsChecked = incoming.DetailsChecked || existing.DetailsChecked;
                    if (incoming.DetailsCheckedUtc != default)
                        existing.DetailsCheckedUtc = incoming.DetailsCheckedUtc;
                    if (incoming.DemoResources.Count > 0)
                        existing.DemoResources = incoming.DemoResources.Distinct().ToList();
                    existing.SyncedUtc = incoming.SyncedUtc;
                }
                else
                {
                    all.Add(incoming);
                    byId[incoming.MatchId] = incoming;
                }
            }

            all = all.OrderByDescending(x => x.FinishedUtc).Take(1500).ToList();
            SaveUnlocked(all);
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch { }
        }
    }

    private static List<FaceitHistoryMatch> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<FaceitHistoryMatch>();

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<FaceitHistoryMatch>>(json)
                   ?? new List<FaceitHistoryMatch>();
        }
        catch
        {
            return new List<FaceitHistoryMatch>();
        }
    }

    private static void SaveUnlocked(List<FaceitHistoryMatch> items)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json, Encoding.UTF8);
    }
}
