using System.IO.Compression;

namespace Sm0kiSoloCoach;

public sealed class AutoDemoInboxService : IDisposable
{
    private readonly Func<string> _nicknameProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, DateTime> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
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
                    if (file.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        tempDemo = await ExpandGzipAsync(file, ct);
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
               path.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase);
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
