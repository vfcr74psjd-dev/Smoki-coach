using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace Sm0kiSoloCoach;

public sealed class AutoDemoLearningService : IDisposable
{
    private readonly Func<string> _nicknameProvider;
    private readonly AutoDemoInboxService _inbox;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _loop;

    private static readonly HttpClient DownloadHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    public event Action<string>? StatusChanged;

    public AutoDemoLearningService(
        Func<string> nicknameProvider,
        AutoDemoInboxService inbox)
    {
        _nicknameProvider = nicknameProvider;
        _inbox = inbox;
    }

    public void Start()
    {
        if (_loop != null)
            return;

        _loop = Task.Run(
            () => LoopAsync(_cts.Token));
    }

    public async Task TickNowAsync(
        CancellationToken cancellationToken = default)
    {
        await ProcessOnceAsync(
            cancellationToken,
            announce: true);
    }

    private async Task LoopAsync(
        CancellationToken ct)
    {
        try
        {
            await ProcessOnceAsync(
                ct,
                announce: false);

            using var timer =
                new PeriodicTimer(
                    TimeSpan.FromMinutes(5));

            while (await timer.WaitForNextTickAsync(ct))
            {
                await ProcessOnceAsync(
                    ct,
                    announce: false);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task ProcessOnceAsync(
        CancellationToken ct,
        bool announce)
    {
        if (!await _gate.WaitAsync(0, ct))
            return;

        try
        {
            var nickname =
                (_nicknameProvider() ?? "").Trim();

            if (nickname.Length < 2 ||
                !FaceitSettingsStore.HasKey)
                return;

            if (announce)
                StatusChanged?.Invoke(
                    "AUTO DEMO • syncing recent FACEIT matches…");

            // Keep this deliberately small. The queue only needs the recent
            // matches and FaceitHistoryService already caches detail checks.
            var sync =
                await new FaceitHistoryService()
                    .SyncAsync(
                        nickname,
                        8,
                        null,
                        ct);

            var recent =
                FaceitHistoryStore
                    .LoadForPlayer(nickname)
                    .Where(x =>
                        x.FinishedUtc != default)
                    .OrderByDescending(x => x.FinishedUtc)
                    .Take(8)
                    .ToList();

            DemoLearningQueueStore
                .UpsertFromHistory(
                    nickname,
                    recent);

            foreach (var waiting in DemoLearningQueueStore
                         .Load()
                         .Where(x =>
                             x.State == "WAITING_DEMO" &&
                             !x.Learned)
                         .Take(8))
            {
                var historyMatch =
                    recent.FirstOrDefault(x =>
                        x.MatchId.Equals(
                            waiting.MatchId,
                            StringComparison.OrdinalIgnoreCase));

                if (historyMatch?.DemoReady == true)
                {
                    DemoLearningQueueStore.SetState(
                        waiting.MatchId,
                        "READY");
                }
                else
                {
                    DemoLearningQueueStore.MarkWaiting(
                        waiting.MatchId,
                        TimeSpan.FromMinutes(10));
                }
            }

            var next =
                DemoLearningQueueStore.NextReady();

            if (next == null)
            {
                if (announce)
                {
                    StatusChanged?.Invoke(
                        $"AUTO DEMO • recent {sync.HistoryCount} • waiting for demo availability");
                }

                return;
            }

            var match =
                FaceitHistoryStore
                    .LoadForPlayer(nickname)
                    .FirstOrDefault(x =>
                        x.MatchId.Equals(
                            next.MatchId,
                            StringComparison.OrdinalIgnoreCase));

            if (match == null ||
                !match.DemoReady ||
                match.DemoResources.Count == 0)
            {
                DemoLearningQueueStore.MarkWaiting(
                    next.MatchId,
                    TimeSpan.FromMinutes(10));
                return;
            }

            await ProcessMatchAsync(
                match,
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (announce)
                StatusChanged?.Invoke(
                    "AUTO DEMO • " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ProcessMatchAsync(
        FaceitHistoryMatch match,
        CancellationToken ct)
    {
        var queueItem =
            DemoLearningQueueStore.Load()
                .FirstOrDefault(x =>
                    x.MatchId.Equals(
                        match.MatchId,
                        StringComparison.OrdinalIgnoreCase));

        if (queueItem?.Learned == true)
            return;

        DemoLearningQueueStore.SetState(
            match.MatchId,
            "DOWNLOADING");

        StatusChanged?.Invoke(
            $"AUTO DEMO • downloading {CoachEngine.PrettyMap(match.Map)}…");

        string? downloaded = null;

        try
        {
            downloaded =
                await DownloadOneAsync(
                    match,
                    ct);

            DemoLearningQueueStore.SetState(
                match.MatchId,
                "ANALYZING",
                localPath: downloaded);

            StatusChanged?.Invoke(
                $"AUTO DEMO • analyzing {CoachEngine.PrettyMap(match.Map)}…");

            var nickname =
                (_nicknameProvider() ?? "").Trim();

            var recommendationSession =
                RecommendationTraceStore.FindBestSession(
                    nickname,
                    match.Map,
                    match.FinishedUtc);

            StatusChanged?.Invoke(
                $"AUTO DEMO • Ground Truth • aligning rounds…");

            var groundTruth =
                await DemoGroundTruthAnalyzer.AnalyzeSourceAsync(
                    downloaded,
                    match.MatchId,
                    match.FinishedUtc,
                    nickname,
                    FaceitSettingsStore.LoadSteamId64(),
                    recommendationSession,
                    ct);

            GroundTruthStore.Save(
                groundTruth);

            DigitalTwinStore.ApplyGroundTruth(
                groundTruth);

            DemoLearningQueueStore.SetGroundTruth(
                match.MatchId,
                groundTruth);

            // Heat-map import is useful, but it is not Ground Truth.
            // A no-death match must still be allowed to teach the Digital Twin.
            HeatMapMatchRecord? imported = null;
            string heatMapWarning = "";

            try
            {
                imported =
                    await _inbox.ImportFileAsync(
                        downloaded,
                        ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                heatMapWarning = ex.Message;
            }

            DemoLearningQueueStore.SetState(
                match.MatchId,
                "LEARNED",
                localPath: downloaded,
                importedDeaths:
                    imported?.Deaths.Count ?? 0);

            var learnedMap =
                !string.IsNullOrWhiteSpace(
                    groundTruth.Map)
                    ? groundTruth.Map
                    : match.Map;

            var warning =
                string.IsNullOrWhiteSpace(
                    heatMapWarning)
                    ? ""
                    : " • heatmap skipped";

            StatusChanged?.Invoke(
                $"AUTO DEMO • LEARNED ✓ {CoachEngine.PrettyMap(learnedMap)} • " +
                $"{groundTruth.GroundTruthEligibleRounds}/{groundTruth.MatchedRounds} verified • " +
                $"{groundTruth.ExecutionDivergedRounds} diverged" +
                warning);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var attempts =
                DemoLearningQueueStore.Load()
                    .FirstOrDefault(x =>
                        x.MatchId.Equals(
                            match.MatchId,
                            StringComparison.OrdinalIgnoreCase))
                    ?.Attempts ?? 1;

            var retry =
                TimeSpan.FromMinutes(
                    Math.Min(
                        60,
                        5 * Math.Max(1, attempts)));

            DemoLearningQueueStore.SetState(
                match.MatchId,
                "RETRY",
                error: ex.Message,
                localPath: downloaded,
                retryAfter: retry);

            StatusChanged?.Invoke(
                $"AUTO DEMO • retry in {retry.TotalMinutes:0}m • {ex.Message}");
        }
    }

    private static async Task<string> DownloadOneAsync(
        FaceitHistoryMatch match,
        CancellationToken ct)
    {
        Directory.CreateDirectory(
            AutoDemoInboxService.InboxFolder);

        Exception? last = null;

        foreach (var resource in match.DemoResources
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(
                    resource,
                    UriKind.Absolute,
                    out var uri))
                continue;

            var reusable =
                FindReusableDownload(
                    match.MatchId);

            if (reusable != null)
                return reusable;

            var ext =
                GuessExtension(uri);

            var partial =
                Path.Combine(
                    AutoDemoInboxService.InboxFolder,
                    match.MatchId +
                    ".download.partial");

            var destination = "";

            try
            {
                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        uri);

                using var response =
                    await DownloadHttp.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                if (!response.IsSuccessStatusCode &&
                    response.StatusCode is
                        HttpStatusCode.Unauthorized or
                        HttpStatusCode.Forbidden)
                {
                    var key =
                        FaceitSettingsStore.LoadKey();

                    using var retry =
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            uri);

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        retry.Headers.Authorization =
                            new AuthenticationHeaderValue(
                                "Bearer",
                                key);
                    }

                    using var retryResponse =
                        await DownloadHttp.SendAsync(
                            retry,
                            HttpCompletionOption.ResponseHeadersRead,
                            ct);

                    retryResponse.EnsureSuccessStatusCode();

                    await using var input =
                        await retryResponse.Content
                            .ReadAsStreamAsync(ct);
                    await using var output =
                        File.Create(partial);

                    await input.CopyToAsync(
                        output,
                        ct);
                }
                else
                {
                    response.EnsureSuccessStatusCode();

                    await using var input =
                        await response.Content
                            .ReadAsStreamAsync(ct);
                    await using var output =
                        File.Create(partial);

                    await input.CopyToAsync(
                        output,
                        ct);
                }

                if (!File.Exists(partial) ||
                    new FileInfo(partial).Length < 1024)
                {
                    throw new InvalidOperationException(
                        "Downloaded demo je prazen.");
                }

                ext =
                    DetectDownloadedExtension(
                        partial,
                        ext);

                destination =
                    Path.Combine(
                        AutoDemoInboxService.InboxFolder,
                        match.MatchId + ext);

                DeleteOtherDownloadVariants(
                    match.MatchId,
                    destination);

                File.Move(
                    partial,
                    destination,
                    true);

                return destination;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;

                try
                {
                    if (File.Exists(partial))
                        File.Delete(partial);
                }
                catch { }
            }
        }

        throw last ??
              new InvalidOperationException(
                  "FACEIT demo URL ni uporaben.");
    }

    private static string? FindReusableDownload(
        string matchId)
    {
        foreach (var ext in new[]
                 {
                     ".dem",
                     ".dem.gz",
                     ".zip"
                 })
        {
            var path =
                Path.Combine(
                    AutoDemoInboxService.InboxFolder,
                    matchId + ext);

            try
            {
                if (!File.Exists(path))
                    continue;

                if (new FileInfo(path).Length < 1024)
                {
                    File.Delete(path);
                    continue;
                }

                var detected =
                    DetectDownloadedExtension(
                        path,
                        ext);

                if (!detected.Equals(
                        ext,
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    continue;
                }

                return path;
            }
            catch
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch { }
            }
        }

        return null;
    }

    private static string DetectDownloadedExtension(
        string path,
        string fallback)
    {
        using var stream =
            File.OpenRead(path);

        Span<byte> header =
            stackalloc byte[4];

        var read =
            stream.Read(header);

        if (read >= 2 &&
            header[0] == 0x1f &&
            header[1] == 0x8b)
            return ".dem.gz";

        if (read >= 4 &&
            header[0] == 0x50 &&
            header[1] == 0x4b &&
            header[2] == 0x03 &&
            header[3] == 0x04)
            return ".zip";

        // FACEIT presigned URLs do not always expose a useful suffix.
        // Anything that is neither gzip nor zip is treated as a raw demo.
        return ".dem";
    }

    private static void DeleteOtherDownloadVariants(
        string matchId,
        string keepPath)
    {
        foreach (var ext in new[]
                 {
                     ".dem",
                     ".dem.gz",
                     ".zip"
                 })
        {
            var path =
                Path.Combine(
                    AutoDemoInboxService.InboxFolder,
                    matchId + ext);

            if (path.Equals(
                    keepPath,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }

    private static string GuessExtension(
        Uri uri)
    {
        var path =
            uri.AbsolutePath.ToLowerInvariant();

        if (path.EndsWith(".zip"))
            return ".zip";

        if (path.EndsWith(".dem"))
            return ".dem";

        return ".dem.gz";
    }

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _loop?.Wait(
                TimeSpan.FromSeconds(1));
        }
        catch { }

        _gate.Dispose();
        _cts.Dispose();
    }
}
