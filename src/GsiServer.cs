using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class GsiServer : IDisposable
{
    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public event Action<GameSnapshot>? SnapshotReceived;

    public int BoundPort { get; private set; }

    public void Start()
    {
        Exception? last = null;

        for (int port = 31972; port <= 31982; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();

                _listener = listener;
                BoundPort = port;
                GsiInstaller.Port = port;

                _loopTask = Task.Run(ListenLoop);
                return;
            }
            catch (SocketException ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Ni bilo mogoče najti prostega lokalnega porta med 31972 in 31982.",
            last
        );
    }

    private async Task ListenLoop()
    {
        if (_listener == null) return;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClient(client));
            }
            catch (OperationCanceledException) { break; }
            catch when (_cts.IsCancellationRequested) { break; }
            catch { }
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen:true);

                string? requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2 || !parts[0].Equals("POST", StringComparison.OrdinalIgnoreCase) || parts[1] != "/gsi")
                {
                    await SendResponse(stream, 404, "{\"error\":\"not found\"}");
                    return;
                }

                int contentLength = 0;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    var idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    var name = line[..idx].Trim();
                    var value = line[(idx + 1)..].Trim();

                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(value, out contentLength);
                }

                if (contentLength <= 0 || contentLength > 2_000_000)
                {
                    await SendResponse(stream, 400, "{\"error\":\"bad content length\"}");
                    return;
                }

                char[] chars = new char[contentLength];
                int total = 0;

                while (total < contentLength)
                {
                    int n = await reader.ReadAsync(chars, total, contentLength-total);
                    if (n <= 0) break;
                    total += n;
                }

                string body = new string(chars, 0, total);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("auth", out var auth) ||
                    !auth.TryGetProperty("token", out var token) ||
                    token.GetString() != GsiInstaller.Token)
                {
                    await SendResponse(stream, 403, "{\"error\":\"bad token\"}");
                    return;
                }

                SnapshotReceived?.Invoke(Parse(root));
                await SendResponse(stream, 200, "{\"ok\":true}");
            }
            catch
            {
                try
                {
                    using var stream = client.GetStream();
                    await SendResponse(stream, 400, "{\"error\":\"bad request\"}");
                }
                catch { }
            }
        }
    }

    private static async Task SendResponse(NetworkStream stream, int statusCode, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        string statusText = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            _ => "Error"
        };

        var header =
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static int? IntProp(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object &&
           e.TryGetProperty(name, out var v) &&
           v.TryGetInt32(out var n) ? n : null;

    private static string StrProp(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object &&
           e.TryGetProperty(name, out var v)
           ? (v.GetString() ?? "")
           : "";

    private static bool TryParseVector3(string raw, out float x, out float y, out float z)
    {
        x = y = z = 0;
        var parts = (raw ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;

        return float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)
            && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
    }

    private static GameSnapshot Parse(JsonElement root)
    {
        var s = new GameSnapshot();

        if (root.TryGetProperty("player", out var p))
        {
            s.PlayerName = StrProp(p, "name");
            s.Team = StrProp(p, "team");

            var rawPosition = StrProp(p, "position");
            if (TryParseVector3(rawPosition, out var px, out var py, out var pz))
            {
                s.PositionX = px;
                s.PositionY = py;
                s.PositionZ = pz;
            }

            if (p.TryGetProperty("state", out var ps))
            {
                s.Health = IntProp(ps, "health");
                s.Armor = IntProp(ps, "armor");
                s.Money = IntProp(ps, "money");

                if (ps.TryGetProperty("helmet", out var h) &&
                    h.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    s.Helmet = h.GetBoolean();
            }

            if (p.TryGetProperty("match_stats", out var ms))
            {
                s.Kills = IntProp(ms, "kills");
                s.Deaths = IntProp(ms, "deaths");
                s.Assists = IntProp(ms, "assists");
            }

            if (p.TryGetProperty("weapons", out var weapons) &&
                weapons.ValueKind == JsonValueKind.Object)
            {
                foreach (var w in weapons.EnumerateObject())
                {
                    if (w.Value.ValueKind == JsonValueKind.Object &&
                        StrProp(w.Value, "state") == "active")
                    {
                        s.Weapon = StrProp(w.Value, "name").Replace("weapon_", "");
                        break;
                    }
                }
            }
        }

        if (root.TryGetProperty("map", out var map))
        {
            s.Map = StrProp(map, "name");
            s.Round = IntProp(map, "round");

            if (map.TryGetProperty("team_ct", out var ct))
                s.CtScore = IntProp(ct, "score");

            if (map.TryGetProperty("team_t", out var t))
                s.TScore = IntProp(t, "score");
        }

        if (root.TryGetProperty("round", out var rnd))
        {
            s.RoundPhase = StrProp(rnd, "phase");
            s.RoundWinTeam = StrProp(rnd, "win_team");
        }

        s.Timestamp = DateTime.UtcNow;
        return s;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts.Dispose();
    }
}
