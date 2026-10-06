using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class OpponentDemoIntelService
{
    private static readonly HttpClient ApiHttp = new()
    {
        BaseAddress = new Uri("https://open.faceit.com/data/v4/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly HttpClient DownloadHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private static readonly string CacheDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach",
            "OpponentDemos");

    public async Task<OpponentDemoIntelReport> BuildAsync(
        OpponentScoutReport scout,
        int maxDemos = 3,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (scout.Opponents.Count == 0)
            throw new InvalidOperationException(
                "Opponent Scout nima igralcev za demo analizo.");

        var key = FaceitSettingsStore.LoadKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "FACEIT API key še ni nastavljen.");

        maxDemos = Math.Clamp(maxDemos, 1, 5);
        Directory.CreateDirectory(CacheDir);

        var candidates = BuildCandidateMatchIds(scout);
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "Za nasprotnike ni recent match ID-jev.");

        var aggregated = new OpponentDemoIntelReport
        {
            Map = scout.Map,
            ScannedUtc = DateTime.UtcNow
        };

        var nicknames = scout.Opponents
            .Select(x => x.Nickname)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var analyzed = 0;

        foreach (var matchId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (analyzed >= maxDemos)
                break;

            progress?.Report(
                $"Opponent demos {analyzed}/{maxDemos} • checking {ShortId(matchId)}…");

            MatchDemoInfo info;
            try
            {
                info = await LoadDemoInfoAsync(
                    matchId,
                    key,
                    cancellationToken);
            }
            catch
            {
                continue;
            }

            if (!MapMatches(info.Map, scout.Map) ||
                info.DemoUrls.Count == 0)
                continue;

            var url = info.DemoUrls[0];
            string? tempExpanded = null;
            string? tempZipDir = null;

            try
            {
                var downloaded =
                    await DownloadAsync(
                        matchId,
                        url,
                        key,
                        progress,
                        cancellationToken);

                var demoPath = downloaded;

                if (demoPath.EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase))
                {
                    (demoPath, tempZipDir) =
                        await ExtractZipAsync(
                            demoPath,
                            cancellationToken);
                }

                if (demoPath.EndsWith(
                        ".gz",
                        StringComparison.OrdinalIgnoreCase))
                {
                    tempExpanded =
                        await ExpandGzipAsync(
                            demoPath,
                            cancellationToken);
                    demoPath = tempExpanded;
                }

                progress?.Report(
                    $"Analyzing opponent patterns • {analyzed + 1}/{maxDemos}…");

                var result =
                    await OpponentDemoPatternAnalyzer.AnalyzeAsync(
                        demoPath,
                        nicknames,
                        cancellationToken);

                Merge(aggregated, result);
                analyzed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // One unavailable/expired demo must not kill the entire scout.
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempExpanded))
                {
                    try { File.Delete(tempExpanded); } catch { }
                }

                if (!string.IsNullOrWhiteSpace(tempZipDir))
                {
                    try
                    {
                        Directory.Delete(tempZipDir, true);
                    }
                    catch { }
                }
            }
        }

        aggregated.DemosAnalyzed = analyzed;
        FinalizeReport(aggregated);

        if (analyzed == 0)
            throw new InvalidOperationException(
                "Za recent tekme na tej mapi ni bilo uporabnega FACEIT demo posnetka.");

        OpponentDemoIntelStore.Save(aggregated);
        progress?.Report(
            $"Opponent demo intel ready • {aggregated.OpeningSamples} opening samples");

        return aggregated;
    }

    private static List<string> BuildCandidateMatchIds(
        OpponentScoutReport scout)
    {
        var scores =
            new Dictionary<string,double>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var player in scout.Opponents)
        {
            for (var i = 0;
                 i < player.RecentMatchIds.Count;
                 i++)
            {
                var id = player.RecentMatchIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                // Shared matches rise sharply. Recency still matters when a
                // player does not share history with the rest of the roster.
                scores.TryGetValue(id, out var score);
                score += 100.0 + Math.Max(0, 20 - i);
                scores[id] = score;
            }
        }

        return scores
            .OrderByDescending(x => x.Value)
            .Select(x => x.Key)
            .Take(16)
            .ToList();
    }

    private sealed record MatchDemoInfo(
        string Map,
        List<string> DemoUrls);

    private static async Task<MatchDemoInfo> LoadDemoInfoAsync(
        string matchId,
        string key,
        CancellationToken ct)
    {
        using var request =
            NewApiRequest(
                "matches/" +
                Uri.EscapeDataString(matchId),
                key);

        using var response =
            await ApiHttp.SendAsync(
                request,
                ct);

        var raw =
            await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"FACEIT match details {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var demos = new List<string>();

        if (root.TryGetProperty(
                "demo_url",
                out var demoUrl))
        {
            if (demoUrl.ValueKind ==
                JsonValueKind.Array)
            {
                foreach (var item in
                         demoUrl.EnumerateArray())
                {
                    if (item.ValueKind ==
                            JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(
                            item.GetString()))
                        demos.Add(item.GetString()!);
                }
            }
            else if (demoUrl.ValueKind ==
                     JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(
                         demoUrl.GetString()))
            {
                demos.Add(demoUrl.GetString()!);
            }
        }

        if (root.TryGetProperty(
                "instances",
                out var instances) &&
            instances.ValueKind ==
                JsonValueKind.Array)
        {
            foreach (var instance in
                     instances.EnumerateArray())
            {
                if (!instance.TryGetProperty(
                        "demos",
                        out var ds) ||
                    ds.ValueKind !=
                        JsonValueKind.Array)
                    continue;

                foreach (var item in
                         ds.EnumerateArray())
                {
                    if (item.ValueKind ==
                            JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(
                            item.GetString()))
                        demos.Add(item.GetString()!);
                }
            }
        }

        return new MatchDemoInfo(
            ReadMap(root),
            demos
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static async Task<string> DownloadAsync(
        string matchId,
        string resource,
        string key,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(
                resource,
                UriKind.Absolute,
                out var uri))
            throw new InvalidOperationException(
                "Demo URL ni veljaven.");

        var ext = GuessExtension(uri);
        var destination =
            Path.Combine(
                CacheDir,
                matchId + ext);

        if (File.Exists(destination) &&
            new FileInfo(destination).Length >
                1024)
            return destination;

        progress?.Report(
            $"Downloading opponent demo • {ShortId(matchId)}…");

        var partial = destination + ".partial";

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

            if (response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                using var retry =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        uri);
                retry.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        key);

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
                new FileInfo(partial).Length <
                    1024)
                throw new InvalidOperationException(
                    "Downloaded opponent demo je prazen.");

            File.Move(
                partial,
                destination,
                true);

            return destination;
        }
        finally
        {
            try
            {
                if (File.Exists(partial))
                    File.Delete(partial);
            }
            catch { }
        }
    }

    private static void Merge(
        OpponentDemoIntelReport target,
        OpponentDemoPatternResult source)
    {
        foreach (var opening in source.Openings)
        {
            target.OpeningSamples++;

            var zones =
                opening.Side == "T"
                    ? target.TContactZones
                    : target.CtContactZones;

            zones.TryGetValue(
                opening.Zone,
                out var zoneCount);
            zones[opening.Zone] =
                zoneCount + 1;

            if (opening.Side == "T")
            {
                if (opening.Outcome == "KILL")
                    target.TOpeningKills++;
                else
                    target.TOpeningDeaths++;
            }
            else if (opening.Side == "CT")
            {
                if (opening.Outcome == "KILL")
                    target.CtOpeningKills++;
                else
                    target.CtOpeningDeaths++;
            }

            if (opening.Outcome == "KILL" &&
                !string.IsNullOrWhiteSpace(
                    opening.Player))
            {
                target.OpeningKillsByPlayer.TryGetValue(
                    opening.Player,
                    out var kills);
                target.OpeningKillsByPlayer[
                    opening.Player] =
                    kills + 1;
            }
        }
    }

    private static void FinalizeReport(
        OpponentDemoIntelReport report)
    {
        static string TopZone(
            Dictionary<string,int> zones)
            => zones
                .Where(x =>
                    !x.Key.Equals(
                        "OTHER",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .Select(x => x.Key)
                .FirstOrDefault() ?? "";

        report.TopTOpeningZone =
            TopZone(report.TContactZones);

        report.TopCtOpeningZone =
            TopZone(report.CtContactZones);

        report.OpeningThreat =
            report.OpeningKillsByPlayer
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .Select(x => x.Key)
                .FirstOrDefault() ?? "";

        report.Confidence =
            report.OpeningSamples >= 24 &&
            report.DemosAnalyzed >= 3
                ? "HIGH"
                : report.OpeningSamples >= 10 &&
                  report.DemosAnalyzed >= 2
                    ? "MEDIUM"
                    : "LOW";
    }

    private static HttpRequestMessage NewApiRequest(
        string url,
        string key)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                key);
        request.Headers.UserAgent.ParseAdd(
            "Sm0kiSoloCoach/" +
            AppUpdater.CurrentVersion);

        return request;
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

    private static async Task<string> ExpandGzipAsync(
        string path,
        CancellationToken ct)
    {
        var output =
            Path.Combine(
                CacheDir,
                "expanded-" +
                Guid.NewGuid().ToString("N") +
                ".dem");

        await using var source =
            File.OpenRead(path);
        await using var gzip =
            new GZipStream(
                source,
                CompressionMode.Decompress);
        await using var target =
            File.Create(output);

        await gzip.CopyToAsync(
            target,
            ct);

        return output;
    }

    private static async Task<(string DemoPath,string TempDir)>
        ExtractZipAsync(
            string path,
            CancellationToken ct)
    {
        var dir =
            Path.Combine(
                CacheDir,
                "zip-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);

        try
        {
            using var archive =
                ZipFile.OpenRead(path);

            var entry =
                archive.Entries
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.Name) &&
                        (x.FullName.EndsWith(
                             ".dem",
                             StringComparison.OrdinalIgnoreCase) ||
                         x.FullName.EndsWith(
                             ".dem.gz",
                             StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(x => x.Length)
                    .FirstOrDefault();

            if (entry == null)
                throw new InvalidOperationException(
                    "Opponent ZIP nima demo datoteke.");

            var output =
                Path.Combine(
                    dir,
                    Path.GetFileName(
                        entry.FullName));

            await using var input =
                entry.Open();
            await using var target =
                File.Create(output);

            await input.CopyToAsync(
                target,
                ct);

            return (output, dir);
        }
        catch
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch { }

            throw;
        }
    }

    private static string ReadMap(
        JsonElement root)
    {
        try
        {
            if (root.TryGetProperty(
                    "voting",
                    out var voting) &&
                voting.ValueKind ==
                    JsonValueKind.Object &&
                voting.TryGetProperty(
                    "map",
                    out var mapObj) &&
                mapObj.ValueKind ==
                    JsonValueKind.Object &&
                mapObj.TryGetProperty(
                    "pick",
                    out var pick))
            {
                if (pick.ValueKind ==
                    JsonValueKind.Array)
                {
                    foreach (var value in
                             pick.EnumerateArray())
                    {
                        if (value.ValueKind ==
                            JsonValueKind.String)
                            return NormalizeMap(
                                value.GetString() ?? "");
                    }
                }

                if (pick.ValueKind ==
                    JsonValueKind.String)
                    return NormalizeMap(
                        pick.GetString() ?? "");
            }
        }
        catch { }

        if (root.TryGetProperty(
                "map",
                out var direct))
            return NormalizeMap(
                direct.ToString());

        return "";
    }

    private static bool MapMatches(
        string left,
        string right)
        => NormalizeMap(left)
            .Equals(
                NormalizeMap(right),
                StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMap(
        string map)
    {
        map = (map ?? "").Trim();

        if (map.StartsWith(
                "de_",
                StringComparison.OrdinalIgnoreCase))
            return map.ToLowerInvariant();

        var simple =
            map.ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "");

        return simple switch
        {
            "mirage" => "de_mirage",
            "inferno" => "de_inferno",
            "nuke" => "de_nuke",
            "ancient" => "de_ancient",
            "anubis" => "de_anubis",
            "dust2" or "dustii" => "de_dust2",
            "overpass" => "de_overpass",
            "train" => "de_train",
            "vertigo" => "de_vertigo",
            _ => map
        };
    }

    private static string ShortId(
        string id)
        => id.Length <= 8
            ? id
            : id[..8];
}
