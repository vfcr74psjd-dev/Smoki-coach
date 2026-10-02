using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class PhoneDashboardServer : IDisposable
{
    private sealed class PhoneState
    {
        public string map { get; init; } = "—";
        public string side { get; init; } = "—";
        public string round { get; init; } = "—";
        public string score { get; init; } = "—";
        public int kills { get; init; }
        public int deaths { get; init; }
        public string kd { get; init; } = "0.00";
        public int money { get; init; }
        public string hp { get; init; } = "—";
        public string armor { get; init; } = "—";
        public string weapon { get; init; } = "—";
        public string roundType { get; init; } = "—";
        public string buyTitle { get; init; } = "";
        public string buyAdvice { get; init; } = "";
        public string aiTip { get; init; } = "";
        public string tip { get; init; } = "";
    }

    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<GameSnapshot> _snapshotProvider;
    private readonly Func<string> _modeProvider;
    private readonly Func<string> _roleProvider;
    private readonly Func<string> _focusProvider;
    private readonly Func<string> _nicknameProvider;
    private readonly Func<bool> _autoAiProvider;
    private readonly Func<string> _aiProvider;
    private readonly Action<string,string,string,bool> _settingsUpdater;
    private readonly string _accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
    private Task? _loopTask;

    public int Port { get; private set; } = 31990;

    public PhoneDashboardServer(
        Func<GameSnapshot> snapshotProvider,
        Func<string> modeProvider,
        Func<string> roleProvider,
        Func<string> focusProvider,
        Func<string> nicknameProvider,
        Func<bool> autoAiProvider,
        Func<string> aiProvider,
        Action<string,string,string,bool> settingsUpdater)
    {
        _snapshotProvider = snapshotProvider;
        _modeProvider = modeProvider;
        _roleProvider = roleProvider;
        _focusProvider = focusProvider;
        _nicknameProvider = nicknameProvider;
        _autoAiProvider = autoAiProvider;
        _aiProvider = aiProvider;
        _settingsUpdater = settingsUpdater;
    }

    public void Start()
    {
        Exception? last = null;
        for (int p = 31990; p <= 32000; p++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, p);
                _listener.Start();
                Port = p;
                _loopTask = Task.Run(ListenLoop);
                return;
            }
            catch (SocketException ex)
            {
                last = ex;
            }
        }
        throw new InvalidOperationException("Phone Mode ni našel prostega porta 31990-32000.", last);
    }

    public string GetLocalUrl()
    {
        var ip = GetLanIp();
        var host = ip == null ? "127.0.0.1" : ip;
        return $"http://{host}:{Port}/?token={_accessToken}";
    }

    private static string? GetLanIp()
    {
        try
        {
            foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    return a.ToString();
            }
        }
        catch { }
        return null;
    }

    private async Task ListenLoop()
    {
        if (_listener == null) return;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var c = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => Handle(c));
            }
            catch (OperationCanceledException) { break; }
            catch when (_cts.IsCancellationRequested) { break; }
            catch { }
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen:true);
                string? first = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(first)) return;

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }

                var parts = first.Split(' ');
                if (parts.Length < 2)
                {
                    await SendText(stream, 400, "Bad Request");
                    return;
                }

                var target = parts[1];
                var uri = new Uri("http://localhost" + target);
                var query = ParseQuery(uri.Query);

                if (!query.TryGetValue("token", out var token) ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(token),
                        Encoding.UTF8.GetBytes(_accessToken)))
                {
                    await SendText(stream, 403, "Phone Mode link ni veljaven. Ponovno poskeniraj QR kodo v aplikaciji.");
                    return;
                }

                if (uri.AbsolutePath.Equals("/settings", StringComparison.OrdinalIgnoreCase))
                {
                    var mode = GetAllowed(query, "mode", new[] { "Balanced", "Aggressive", "Safe" }, _modeProvider());
                    var role = GetAllowed(query, "role", new[] { "Flex", "Entry", "Lurk", "Support", "Anchor" }, _roleProvider());
                    var focus = GetAllowed(query, "focus",
                        new[] { "More kills", "Survive & trade", "Entry impact", "Utility impact", "Clutch / late round" },
                        _focusProvider());
                    var autoAi = query.TryGetValue("autoAi", out var auto) && auto == "1";

                    _settingsUpdater(mode, role, focus, autoAi);
                    await SendRedirect(stream, $"/?token={_accessToken}");
                    return;
                }

                if (uri.AbsolutePath.Equals("/api/state", StringComparison.OrdinalIgnoreCase))
                {
                    var live = BuildState();
                    await SendJson(stream, JsonSerializer.Serialize(live));
                    return;
                }

                if (!uri.AbsolutePath.Equals("/", StringComparison.Ordinal))
                {
                    await SendText(stream, 404, "Not Found");
                    return;
                }

                var state = BuildState();
                var s = _snapshotProvider();
                var modeNow = _modeProvider();
                var roleNow = _roleProvider();
                var focusNow = _focusProvider();
                var nickname = _nicknameProvider();
                var autoAiNow = _autoAiProvider();

                string map = state.map;
                string round = state.round;
                string score = state.score;
                int k = state.kills;
                int d = state.deaths;
                string kd = state.kd;
                string buyTitle = state.buyTitle;
                string buyAdvice = state.buyAdvice;
                string tip = state.tip;
                string aiTip = state.aiTip;

                string html = $@"<!doctype html>
<html lang='sl'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,viewport-fit=cover'>
<meta name='theme-color' content='#090d13'>
<meta name='apple-mobile-web-app-capable' content='yes'>
<title>Sm0ki Solo Coach</title>
<style>
*{{box-sizing:border-box}}
body{{margin:0;background:#090d13;color:#f4f6fa;font-family:Segoe UI,Arial,sans-serif}}
.wrap{{max-width:820px;margin:auto;padding:18px 14px 34px}}
.hero{{position:sticky;top:0;z-index:2;background:rgba(9,13,19,.94);backdrop-filter:blur(12px);padding:9px 0 12px}}
h1{{font-size:25px;margin:2px 0 4px;letter-spacing:-.02em}}
.sub{{font-size:10px;letter-spacing:.14em;color:#737f93;font-weight:800}}
.badge{{display:inline-block;padding:5px 9px;border-radius:999px;background:#183927;color:#8af0b8;font-weight:800;font-size:10px;vertical-align:middle;animation:pulse 1.8s ease-in-out infinite}}
.grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:9px;margin:14px 0 10px}}
.card,.section{{background:linear-gradient(180deg,#121923,#0f151e);border:1px solid #252e3d;border-radius:15px;box-shadow:0 8px 24px rgba(0,0,0,.18)}}
.card{{padding:13px}} .label{{font-size:9px;color:#758197;letter-spacing:.11em;font-weight:800}}
.value{{font-size:21px;font-weight:800;margin-top:6px;letter-spacing:-.02em;overflow:hidden;text-overflow:ellipsis}}
.section{{padding:17px;margin-top:10px}} .section h2{{font-size:10px;color:#7d899e;margin:0 0 9px;letter-spacing:.13em}}
.big{{font-size:21px;font-weight:800;margin-bottom:7px}} .text{{line-height:1.5;color:#cbd3df;white-space:pre-line}}
.ai{{border-left:3px solid #685cff}}
.controls{{display:grid;grid-template-columns:1fr 1fr;gap:10px}}
.field label{{display:block;font-size:9px;color:#758197;font-weight:800;letter-spacing:.1em;margin:0 0 5px}}
select{{width:100%;background:#171e29;color:#f5f7fb;border:1px solid #303a4c;border-radius:10px;padding:10px}}
.toggle{{display:flex;align-items:center;gap:8px;color:#cbd3df;font-size:13px;padding-top:8px}}
button{{width:100%;margin-top:12px;border:0;border-radius:11px;padding:11px 14px;background:#685cff;color:white;font-weight:800;font-size:14px}}
@keyframes pulse{{0%,100%{{box-shadow:0 0 0 rgba(126,240,174,0)}}50%{{box-shadow:0 0 16px rgba(126,240,174,.28)}}}}
.note{{font-size:11px;color:#7e8ba1;margin-top:10px;line-height:1.45}}
@media(max-width:600px){{
 .wrap{{padding:12px 10px 28px}}
 .grid{{grid-template-columns:repeat(2,1fr)}}
 .value{{font-size:20px}}
 .controls{{grid-template-columns:1fr}}
}}
</style>
</head>
<body><div class='wrap'>
<div class='hero'>
<h1>Sm0ki <span style='color:#8176ff'>Solo Coach</span> <span class='badge'>LIVE</span></h1>
<div class='sub'>PROFILE: {Html(nickname)} • PRIVATE LAN COMPANION • v{Html(AppUpdater.CurrentVersion)}</div>
</div>

<div class='grid'>
<div class='card'><div class='label'>MAP</div><div id='map' class='value'>{Html(map)}</div></div>
<div class='card'><div class='label'>SIDE</div><div id='side' class='value'>{Html(s.Team)}</div></div>
<div class='card'><div class='label'>ROUND</div><div id='round' class='value'>{Html(round)}</div></div>
<div class='card'><div class='label'>SCORE</div><div id='score' class='value'>{Html(score)}</div></div>
<div class='card'><div class='label'>KILLS</div><div id='kills' class='value'>{k}</div></div>
<div class='card'><div class='label'>DEATHS</div><div id='deaths' class='value'>{d}</div></div>
<div class='card'><div class='label'>K/D</div><div id='kd' class='value'>{kd}</div></div>
<div class='card'><div class='label'>MONEY</div><div id='money' class='value'>&#36;{s.Money ?? 0}</div></div>
<div class='card'><div class='label'>HP</div><div id='hp' class='value'>{s.Health?.ToString() ?? "—"}</div></div>
<div class='card'><div class='label'>ARMOR</div><div id='armor' class='value'>{s.Armor?.ToString() ?? "—"}</div></div>
<div class='card'><div class='label'>WEAPON</div><div id='weapon' class='value'>{Html(s.Weapon)}</div></div>
<div class='card'><div class='label'>ROUND TYPE</div><div id='roundType' class='value'>{Html(CoachEngine.ClassifyRound(s))}</div></div>
</div>

<div class='section'><h2>NEXT BUY</h2><div id='buyTitle' class='big'>{Html(buyTitle)}</div><div id='buyAdvice' class='text'>{Html(buyAdvice)}</div></div>
<div class='section ai'><h2>NEXT ROUND PLAN</h2><div id='aiTip' class='text'>{Html(aiTip)}</div></div>
<div class='section'><h2>COACH NOTE</h2><div id='coachTip' class='text'>{Html(tip)}</div></div>

<div class='section'>
<h2>COACH CONTROLS</h2>
<form method='get' action='/settings'>
<input type='hidden' name='token' value='{Html(_accessToken)}'>
<div class='controls'>
<div class='field'><label>MODE</label><select name='mode'>{Options(new[] { "Balanced","Aggressive","Safe" }, modeNow)}</select></div>
<div class='field'><label>ROLE</label><select name='role'>{Options(new[] { "Flex","Entry","Lurk","Support","Anchor" }, roleNow)}</select></div>
<div class='field'><label>FOCUS</label><select name='focus'>{Options(new[] { "More kills","Survive & trade","Entry impact","Utility impact","Clutch / late round" }, focusNow)}</select></div>
<div class='toggle'><input id='autoAi' type='checkbox' name='autoAi' value='1' {(autoAiNow ? "checked" : "")}><label for='autoAi'>Auto AI</label></div>
</div>
<button type='submit'>Apply to PC coach</button>
</form>
<div class='note'>Spremembe se shranijo tudi na PC-ju. Telefon in PC morata ostati na istem Wi‑Fi/LAN omrežju.</div>
</div>
</div>
<script>
const liveToken='{Html(_accessToken)}';
const setText=(id,v)=>{{const e=document.getElementById(id);if(e)e.textContent=(v??'—');}};
async function refreshLive(){{
  try{{
    const r=await fetch('/api/state?token='+encodeURIComponent(liveToken),{{cache:'no-store'}});
    if(!r.ok)return;
    const x=await r.json();
    setText('map',x.map); setText('side',x.side); setText('round',x.round); setText('score',x.score);
    setText('kills',x.kills); setText('deaths',x.deaths); setText('kd',x.kd); setText('money','

                await SendHtml(stream, html);
            }
            catch { }
        }
    }

    private PhoneState BuildState()
    {
        var s = _snapshotProvider();
        var mode = _modeProvider();
        var role = _roleProvider();
        var focus = _focusProvider();

        var map = CoachEngine.PrettyMap(s.Map);
        var round = s.Round is int r ? $"R{r + 1}" : "—";
        var score = $"{s.CtScore?.ToString() ?? "—"}:{s.TScore?.ToString() ?? "—"}";
        var kills = s.Kills ?? 0;
        var deaths = s.Deaths ?? 0;
        var kd = deaths > 0 ? ((double)kills / deaths).ToString("0.00") : kills.ToString("0.00");
        var (buyTitle, buyAdvice) = CoachEngine.BuyAdvice(s);
        var tip = CoachEngine.SoloTip(s, mode) + $" Role: {role}. Focus: {focus}.";

        return new PhoneState
        {
            map,
            side = string.IsNullOrWhiteSpace(s.Team) ? "—" : s.Team,
            round,
            score,
            kills,
            deaths,
            kd,
            money = s.Money ?? 0,
            hp = s.Health?.ToString() ?? "—",
            armor = s.Armor?.ToString() ?? "—",
            weapon = string.IsNullOrWhiteSpace(s.Weapon) ? "—" : s.Weapon,
            roundType = CoachEngine.ClassifyRound(s),
            buyTitle,
            buyAdvice,
            aiTip = _aiProvider(),
            tip
        };
    }

    private static Dictionary<string,string> ParseQuery(string query)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : "";
            result[key] = value;
        }
        return result;
    }

    private static string GetAllowed(
        IReadOnlyDictionary<string,string> query,
        string key,
        IReadOnlyCollection<string> allowed,
        string fallback)
    {
        return query.TryGetValue(key, out var value) && allowed.Contains(value) ? value : fallback;
    }

    private static string Options(IEnumerable<string> items, string current)
        => string.Join("", items.Select(x =>
            $"<option value='{Html(x)}'{(x == current ? " selected" : "")}>{Html(x)}</option>"));

    private static async Task SendJson(NetworkStream stream, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task SendHtml(NetworkStream stream, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            "Pragma: no-cache\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task SendText(NetworkStream stream, int code, string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var status = code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Bad Request";
        var header =
            $"HTTP/1.1 {code} {status}\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task SendRedirect(NetworkStream stream, string location)
    {
        var header =
            "HTTP/1.1 303 See Other\r\n" +
            $"Location: {location}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.FlushAsync();
    }

    private static string Html(string? s)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s) ? "—" : s);

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts.Dispose();
    }
}
+x.money);
    setText('hp',x.hp); setText('armor',x.armor); setText('weapon',x.weapon); setText('roundType',x.roundType);
    setText('buyTitle',x.buyTitle); setText('buyAdvice',x.buyAdvice); setText('aiTip',x.aiTip); setText('coachTip',x.tip);
  }}catch{{}}
}}
setInterval(refreshLive,1500);
</script>
</body></html>";

                await SendHtml(stream, html);
            }
            catch { }
        }
    }

    private static Dictionary<string,string> ParseQuery(string query)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : "";
            result[key] = value;
        }
        return result;
    }

    private static string GetAllowed(
        IReadOnlyDictionary<string,string> query,
        string key,
        IReadOnlyCollection<string> allowed,
        string fallback)
    {
        return query.TryGetValue(key, out var value) && allowed.Contains(value) ? value : fallback;
    }

    private static string Options(IEnumerable<string> items, string current)
        => string.Join("", items.Select(x =>
            $"<option value='{Html(x)}'{(x == current ? " selected" : "")}>{Html(x)}</option>"));

    private static async Task SendHtml(NetworkStream stream, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            "Pragma: no-cache\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task SendText(NetworkStream stream, int code, string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var status = code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Bad Request";
        var header =
            $"HTTP/1.1 {code} {status}\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task SendRedirect(NetworkStream stream, string location)
    {
        var header =
            "HTTP/1.1 303 See Other\r\n" +
            $"Location: {location}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.FlushAsync();
    }

    private static string Html(string? s)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s) ? "—" : s);

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts.Dispose();
    }
}
