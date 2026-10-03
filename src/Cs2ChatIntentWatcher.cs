using System.Text;

namespace Sm0kiSoloCoach;

public sealed class Cs2ChatIntentWatcher : IDisposable
{
    private readonly string _logPath;
    private readonly Func<string> _nicknameProvider;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private long _offset;

    public event Action<string>? IntentDetected;
    public event Action<string>? StatusChanged;

    public Cs2ChatIntentWatcher(
        string logPath,
        Func<string> nicknameProvider)
    {
        _logPath = logPath;
        _nicknameProvider = nicknameProvider;
    }

    public void Start()
    {
        if (_loop != null)
            return;

        try
        {
            if (File.Exists(_logPath))
                _offset = new FileInfo(_logPath).Length;
        }
        catch { }

        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    ReadNewLines();
                }
                catch
                {
                    // Best-effort only. Never disturb live coaching because of chat logging.
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ReadNewLines()
    {
        if (!File.Exists(_logPath))
            return;

        var info = new FileInfo(_logPath);
        if (info.Length < _offset)
            _offset = 0;

        if (info.Length == _offset)
            return;

        using var fs = new FileStream(
            _logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        fs.Seek(_offset, SeekOrigin.Begin);
        using var reader = new StreamReader(
            fs,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var intent = TryExtractOwnIntent(line);
            if (!string.IsNullOrWhiteSpace(intent))
            {
                StatusChanged?.Invoke($"CHAT INTENT: {intent}");
                IntentDetected?.Invoke(intent);
            }
        }

        _offset = fs.Position;
    }

    private string TryExtractOwnIntent(string line)
    {
        var nickname = (_nicknameProvider() ?? "").Trim();
        if (nickname.Length < 2 || string.IsNullOrWhiteSpace(line))
            return "";

        var nickIndex = line.IndexOf(
            nickname,
            StringComparison.OrdinalIgnoreCase);

        if (nickIndex < 0)
            return "";

        // CS2 log chat lines normally end with ": <message>".
        // Use the last colon so location/team prefixes do not matter.
        var colon = line.LastIndexOf(':');
        if (colon < nickIndex || colon >= line.Length - 1)
            return "";

        var message = line[(colon + 1)..]
            .Trim()
            .Trim('"', '\'', ' ', '\t', '\r', '\n');

        // The user normally types only A or B. Exact matching prevents
        // teammate calls, system text or normal chat from changing the plan.
        return message.Equals("A", StringComparison.OrdinalIgnoreCase)
            ? "A"
            : message.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? "B"
                : "";
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
    }
}
