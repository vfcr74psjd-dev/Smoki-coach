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
                var request = await ReadRequestAsync(stream, _cts.Token);

                if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                    request.Path != "/gsi")
                {
                    await SendResponse(stream, 404, "{\"error\":\"not found\"}");
                    return;
                }

                if (request.Body.Length == 0 || request.Body.Length > 2_000_000)
                {
                    await SendResponse(stream, 400, "{\"error\":\"bad content length\"}");
                    return;
                }

                using var doc = JsonDocument.Parse(request.Body);
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
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
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

    private sealed record HttpRequestData(string Method, string Path, byte[] Body);

    private static async Task<HttpRequestData> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        const int maxHeaderBytes = 32 * 1024;
        var received = new List<byte>(8192);
        var buffer = new byte[4096];
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
                throw new IOException("HTTP request ended before headers were complete.");

            received.AddRange(buffer.AsSpan(0, read).ToArray());

            if (received.Count > maxHeaderBytes + 2_000_000)
                throw new InvalidDataException("HTTP request is too large.");

            headerEnd = FindHeaderEnd(received);
            if (headerEnd < 0 && received.Count > maxHeaderBytes)
                throw new InvalidDataException("HTTP headers are too large.");
        }

        var headerBytes = received.Take(headerEnd).ToArray();
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);

        var requestLine = lines.FirstOrDefault() ?? "";
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2)
            throw new InvalidDataException("Invalid HTTP request line.");

        int contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;

            var name = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(value, out contentLength);
        }

        if (contentLength <= 0 || contentLength > 2_000_000)
            throw new InvalidDataException("Invalid Content-Length.");

        var bodyStart = headerEnd + 4;
        var alreadyBuffered = Math.Max(0, received.Count - bodyStart);
        var body = new byte[contentLength];

        if (alreadyBuffered > 0)
        {
            var copy = Math.Min(contentLength, alreadyBuffered);
            received.CopyTo(bodyStart, body, 0, copy);
            alreadyBuffered = copy;
        }
        else
        {
            alreadyBuffered = 0;
        }

        int total = alreadyBuffered;
        while (total < contentLength)
        {
            var read = await stream.ReadAsync(
                body.AsMemory(total, contentLength - total),
                cancellationToken);

            if (read <= 0)
                throw new IOException("HTTP request body ended early.");

            total += read;
        }

        return new HttpRequestData(requestParts[0], requestParts[1], body);
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 0; i <= bytes.Count - 4; i++)
        {
            if (bytes[i] == (byte)'\r' &&
                bytes[i + 1] == (byte)'\n' &&
                bytes[i + 2] == (byte)'\r' &&
                bytes[i + 3] == (byte)'\n')
                return i;
        }

        return -1;
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

    private static int PrimaryWeaponPriority(string weapon)
    {
        var w = (weapon ?? "").ToLowerInvariant();

        if (w is "awp" or "scar20" or "g3sg1") return 100;
        if (w is "ak47" or "m4a1" or "m4a1_silencer" or "aug" or "sg556") return 95;
        if (w is "famas" or "galilar") return 90;
        if (w is "ssg08") return 85;
        if (w is "mp9" or "mac10" or "mp7" or "mp5sd" or "ump45" or "p90" or "bizon") return 70;
        if (w is "nova" or "xm1014" or "mag7" or "sawedoff") return 60;
        if (w is "m249" or "negev") return 55;
        return 0;
    }

    private static GameSnapshot Parse(JsonElement root)
    {
        var s = new GameSnapshot();

        if (root.TryGetProperty("provider", out var provider))
            s.LocalSteamId = StrProp(provider, "steamid");

        if (root.TryGetProperty("player", out var p))
        {
            s.PlayerName = StrProp(p, "name");
            s.PlayerSteamId = StrProp(p, "steamid");
            s.IsSpectating =
                !string.IsNullOrWhiteSpace(s.LocalSteamId) &&
                !string.IsNullOrWhiteSpace(s.PlayerSteamId) &&
                !string.Equals(
                    s.LocalSteamId,
                    s.PlayerSteamId,
                    StringComparison.Ordinal);
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
                var owned = new List<string>();

                foreach (var w in weapons.EnumerateObject())
                {
                    if (w.Value.ValueKind != JsonValueKind.Object)
                        continue;

                    var name = StrProp(w.Value, "name")
                        .Replace("weapon_", "", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    owned.Add(name);

                    if (StrProp(w.Value, "state") == "active")
                        s.Weapon = name;
                }

                s.PrimaryWeapon = owned
                    .OrderByDescending(PrimaryWeaponPriority)
                    .FirstOrDefault(x => PrimaryWeaponPriority(x) > 0) ?? "";
            }
        }

        if (root.TryGetProperty("map", out var map))
        {
            s.Map = StrProp(map, "name");
            s.MapPhase = StrProp(map, "phase");
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

        if (root.TryGetProperty("bomb", out var bomb) &&
            bomb.ValueKind == JsonValueKind.Object)
        {
            s.BombState = StrProp(bomb, "state");

            var rawBombPosition = StrProp(bomb, "position");
            if (TryParseVector3(
                    rawBombPosition,
                    out var bx,
                    out var by,
                    out var bz))
            {
                s.BombPositionX = bx;
                s.BombPositionY = by;
                s.BombPositionZ = bz;
            }
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
