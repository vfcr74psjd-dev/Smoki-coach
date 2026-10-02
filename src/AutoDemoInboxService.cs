using System.IO.Compression;

namespace Sm0kiSoloCoach;

public sealed class AutoDemoInboxService : IDisposable
{
    private readonly Func<string> _nicknameProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, DateTime> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient DownloadHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };
    private Task? _loop;

    public static string InboxFolder { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach",
            "DemoInbox");

    public event Action<HeatMapMatchRecord>? DemoImported;
    public event Action<string>? StatusChanged;

    public AutoDemoInboxService(Func<string> nicknameProvider)
    {
        _nicknameProvider = nicknameProvider;
        Directory.CreateDirectory(InboxFolder);
    }

    public void Start()
    {
        if (_loop != null) return;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task<int> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        return await ScanInternalAsync(cancellationToken, true);
    }

    public async Task<HeatMapMatchRecord?> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Demo file ni bil najden.", path);

        if (!IsSupportedDemoFile(path))
            throw new InvalidOperationException(
                "Podprti formati so .dem, .dem.gz in .zip z demo datoteko.");

        var nickname = (_nicknameProvider() ?? "").Trim();
        if (nickname.Length < 2)
            throw new InvalidOperationException("FACEIT/player nickname ni nastavljen.");

        var fingerprint = HeatMapStore.FingerprintFile(path);
        var existing = HeatMapStore.Load()
            .FirstOrDefault(x =>
                x.DemoFingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            StatusChanged?.Invoke(
                $"Demo je že analiziran: {CoachEngine.PrettyMap(existing.Map)} • {existing.Deaths.Count} deaths");
            return existing;
        }

        string? tempExpanded = null;
        string? tempZipDir = null;

        try
        {
            StatusChanged?.Invoke($"Analiziram {Path.GetFileName(path)}…");

            var demoPath = path;

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                (demoPath, tempZipDir) = await ExtractDemoFromZipAsync(path, cancellationToken);
            }

            if (demoPath.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase) ||
                demoPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                tempExpanded = await ExpandGzipAsync(demoPath, cancellationToken);
                demoPath = tempExpanded;
            }

            var result = await DemoReviewService.AnalyzeAsync(
                demoPath,
                nickname,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(result.Map))
                throw new InvalidOperationException(
                    "Demo je bil prebran, vendar mapa ni bila prepoznana.");

            if (result.Deaths.Count == 0)
                throw new InvalidOperationException(
                    $"Demo je bil prebran ({CoachEngine.PrettyMap(result.Map)}), vendar zate ni bilo najdenih death eventov. " +
                    "Preveri FACEIT nickname/SteamID v appu.");

            var record = HeatMapStore.AddFromResult(path, nickname, result);
            if (record == null)
                throw new InvalidOperationException("Heatmap recorda ni bilo mogoče shraniti.");

            DemoImported?.Invoke(record);
            StatusChanged?.Invoke(
                $"IMPORT OK: {CoachEngine.PrettyMap(record.Map)} • {record.Deaths.Count} deaths");

            return record;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempExpanded))
            {
                try { File.Delete(tempExpanded); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(tempZipDir))
            {
                try { Directory.Delete(tempZipDir, true); } catch { }
            }
        }
    }


    public async Task<int> DownloadReadyFaceitDemosAsync(
        CancellationToken cancellationToken = default)
    {
        var nickname = (_nicknameProvider() ?? "").Trim();
        if (nickname.Length < 2)
            throw new InvalidOperationException("FACEIT/player nickname ni nastavljen.");

        var ready = FaceitHistoryStore.LoadForPlayer(nickname)
            .Where(x => x.DemoReady && x.DemoResources.Count > 0)
            .OrderByDescending(x => x.FinishedUtc)
            .ToList();

        if (ready.Count == 0)
        {
            StatusChanged?.Invoke("FACEIT History nima demo URL-jev za prenos.");
            return 0;
        }

        Directory.CreateDirectory(InboxFolder);
        int downloaded = 0;
        int failed = 0;
        int index = 0;

        foreach (var match in ready)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            foreach (var resource in match.DemoResources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri))
                {
                    failed++;
                    continue;
                }

                var ext = GuessDemoExtension(uri);
                var safeMatch = string.IsNullOrWhiteSpace(match.MatchId)
                    ? Guid.NewGuid().ToString("N")
                    : match.MatchId;
                var destination = Path.Combine(
                    InboxFolder,
                    safeMatch + ext);

                if (File.Exists(destination) && new FileInfo(destination).Length > 1024)
                    continue;

                var partial = destination + ".partial";

                try
                {
                    StatusChanged?.Invoke(
                        $"Downloading FACEIT demos… {index}/{ready.Count} • {CoachEngine.PrettyMap(match.Map)}");

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var response = await DownloadHttp.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode &&
                        ((int)response.StatusCode == 401 || (int)response.StatusCode == 403))
                    {
                        var key = FaceitSettingsStore.LoadKey();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            using var retry = new HttpRequestMessage(HttpMethod.Get, uri);
                            retry.Headers.Authorization =
                                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

                            using var retryResponse = await DownloadHttp.SendAsync(
                                retry,
                                HttpCompletionOption.ResponseHeadersRead,
                                cancellationToken);

                            retryResponse.EnsureSuccessStatusCode();
                            await using var retryInput =
                                await retryResponse.Content.ReadAsStreamAsync(cancellationToken);
                            await using var retryOutput = File.Create(partial);
                            await retryInput.CopyToAsync(retryOutput, cancellationToken);
                        }
                        else
                        {
                            response.EnsureSuccessStatusCode();
                        }
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        await using var input =
                            await response.Content.ReadAsStreamAsync(cancellationToken);
                        await using var output = File.Create(partial);
                        await input.CopyToAsync(output, cancellationToken);
                    }

                    if (!File.Exists(partial) || new FileInfo(partial).Length < 1024)
                        throw new InvalidOperationException("Downloaded demo je prazen.");

                    File.Move(partial, destination, true);
                    downloaded++;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    failed++;
                    try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                }
            }
        }

        StatusChanged?.Invoke(
            failed > 0
                ? $"FACEIT backfill: downloaded {downloaded}, failed {failed}. Importing local demos…"
                : $"FACEIT backfill: downloaded {downloaded}. Importing local demos…");

        await ScanInternalAsync(cancellationToken, false);
        return downloaded;
    }

    private static string GuessDemoExtension(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.EndsWith(".dem.gz")) return ".dem.gz";
        if (path.EndsWith(".zip")) return ".zip";
        if (path.EndsWith(".dem")) return ".dem";
        return ".dem.gz";
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await ScanInternalAsync(ct, false);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(ct))
                await ScanInternalAsync(ct, false);
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task<int> ScanInternalAsync(CancellationToken ct, bool announce)
    {
        if (!await _scanGate.WaitAsync(0, ct))
            return 0;

        try
        {
            var nickname = (_nicknameProvider() ?? "").Trim();
            if (nickname.Length < 2)
            {
                if (announce) StatusChanged?.Invoke("FACEIT/player nickname ni nastavljen.");
                return 0;
            }

            var files = FindCandidateFiles()
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            var knownFingerprints = HeatMapStore.Load()
                .Select(x => x.DemoFingerprint)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int imported = 0;
            int checkedFiles = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }

                if (!info.Exists || info.Length < 1024)
                    continue;

                // Avoid files that are still being downloaded/written.
                if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromSeconds(8))
                    continue;

                string fingerprint;
                try { fingerprint = HeatMapStore.FingerprintFile(file); }
                catch { continue; }

                if (knownFingerprints.Contains(fingerprint))
                    continue;

                checkedFiles++;
                if (announce && checkedFiles % 10 == 0)
                    StatusChanged?.Invoke($"Scanning local demos… {checkedFiles} new candidate(s)");

                if (_retryAfter.TryGetValue(fingerprint, out var retry) && retry > DateTime.UtcNow)
                    continue;

                string? tempDemo = null;
                try
                {
                    StatusChanged?.Invoke($"Analiziram {Path.GetFileName(file)}…");

                    var demoPath = file;
                    string? tempZipDir = null;

                    try
                    {
                        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            (demoPath, tempZipDir) = await ExtractDemoFromZipAsync(file, ct);
                        }

                        if (demoPath.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase) ||
                            demoPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                        {
                            tempDemo = await ExpandGzipAsync(demoPath, ct);
                            demoPath = tempDemo;
                        }

                        var result = await DemoReviewService.AnalyzeAsync(demoPath, nickname, ct);

                        if (result.Deaths.Count == 0)
                        {
                            _retryAfter[fingerprint] = DateTime.UtcNow.AddMinutes(30);
                            continue;
                        }

                        var record = HeatMapStore.AddFromResult(file, nickname, result);
                        if (record != null)
                        {
                            imported++;
                            knownFingerprints.Add(record.DemoFingerprint);
                            DemoImported?.Invoke(record);
                            StatusChanged?.Invoke(
                                $"Dodano: {CoachEngine.PrettyMap(record.Map)} • {record.Deaths.Count} deaths");
                        }
                    }
                    finally
                    {
                        if (!string.IsNullOrWhiteSpace(tempZipDir))
                        {
                            try { Directory.Delete(tempZipDir, true); } catch { }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    _retryAfter[fingerprint] = DateTime.UtcNow.AddMinutes(5);
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempDemo))
                    {
                        try { File.Delete(tempDemo); } catch { }
                    }
                }
            }

            if (announce)
            {
                StatusChanged?.Invoke(imported > 0
                    ? $"Auto Demo Inbox: dodanih {imported} demo analiz."
                    : "Auto Demo Inbox: ni novih demo datotek.");
            }

            return imported;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private static IEnumerable<string> FindCandidateFiles()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            InboxFolder
        };

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(user))
            folders.Add(Path.Combine(user, "Downloads"));

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedDemoFile)
                    .ToList();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                yield return file;
        }
    }

    private static bool IsSupportedDemoFile(string path)
    {
        return path.EndsWith(".dem", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string DemoPath, string TempDir)> ExtractDemoFromZipAsync(
        string path,
        CancellationToken ct)
    {
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach",
            "TempDemos",
            "zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Name) &&
                    (x.FullName.EndsWith(".dem", StringComparison.OrdinalIgnoreCase) ||
                     x.FullName.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Length)
                .FirstOrDefault();

            if (entry == null)
                throw new InvalidOperationException(
                    "ZIP ne vsebuje .dem ali .dem.gz datoteke.");

            var safeName = Path.GetFileName(entry.FullName);
            var output = Path.Combine(tempDir, safeName);

            await using var input = entry.Open();
            await using var target = File.Create(output);
            await input.CopyToAsync(target, ct);

            return (output, tempDir);
        }
        catch
        {
            try { Directory.Delete(tempDir, true); } catch { }
            throw;
        }
    }

    private static async Task<string> ExpandGzipAsync(string path, CancellationToken ct)
    {
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sm0kiSoloCoach",
            "TempDemos");
        Directory.CreateDirectory(tempDir);

        var temp = Path.Combine(
            tempDir,
            Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) +
            "-" + Guid.NewGuid().ToString("N") + ".dem");

        await using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await using var output = File.Create(temp);
        await gzip.CopyToAsync(output, ct);

        return temp;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
        _scanGate.Dispose();
    }
}
