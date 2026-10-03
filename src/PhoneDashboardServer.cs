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
        public string position { get; init; } = "—";
        public string expect { get; init; } = "—";
        public string action { get; init; } = "—";
        public string adapt { get; init; } = "—";
        public string planKey { get; init; } = "";
        public string roundKey { get; init; } = "";
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
    private readonly string _accessToken =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
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

        throw new InvalidOperationException(
            "Phone Mode ni našel prostega porta 31990-32000.",
            last);
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
            foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                    return address.ToString();
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
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => Handle(client));
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
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    false,
                    4096,
                    leaveOpen: true);

                var first = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(first))
                    return;

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }

                var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    await SendText(stream, 400, "Bad Request");
                    return;
                }

                var uri = new Uri("http://localhost" + parts[1]);
                var query = ParseQuery(uri.Query);

                if (!query.TryGetValue("token", out var token) ||
                    !SafeTokenEquals(token, _accessToken))
                {
                    await SendText(
                        stream,
                        403,
                        "Phone Mode link ni veljaven. Ponovno poskeniraj QR kodo v aplikaciji.");
                    return;
                }

                if (uri.AbsolutePath.Equals("/settings", StringComparison.OrdinalIgnoreCase))
                {
                    var mode = GetAllowed(
                        query,
                        "mode",
                        new[] { "Balanced", "Aggressive", "Safe" },
                        _modeProvider());

                    var role = GetAllowed(
                        query,
                        "role",
                        new[] { "Flex", "Entry", "Lurk", "Support", "Anchor" },
                        _roleProvider());

                    var focus = GetAllowed(
                        query,
                        "focus",
                        new[]
                        {
                            "More kills",
                            "Survive & trade",
                            "Entry impact",
                            "Utility impact",
                            "Clutch / late round"
                        },
                        _focusProvider());

                    var autoAi =
                        query.TryGetValue("autoAi", out var auto) &&
                        auto == "1";

                    _settingsUpdater(mode, role, focus, autoAi);
                    await SendRedirect(stream, $"/?token={_accessToken}");
                    return;
                }

                if (uri.AbsolutePath.Equals("/api/state", StringComparison.OrdinalIgnoreCase))
                {
                    var json = JsonSerializer.Serialize(BuildState());
                    await SendJson(stream, json);
                    return;
                }

                if (!uri.AbsolutePath.Equals("/", StringComparison.Ordinal))
                {
                    await SendText(stream, 404, "Not Found");
                    return;
                }

                await SendHtml(stream, BuildHtml());
            }
            catch { }
        }
    }

    private PhoneState BuildState()
    {
        var snapshot = _snapshotProvider();
        var mode = _modeProvider();
        var role = _roleProvider();
        var focus = _focusProvider();

        var map = CoachEngine.PrettyMap(snapshot.Map);
        var round = snapshot.Round is int r ? $"R{r + 1}" : "—";
        var score =
            $"{snapshot.CtScore?.ToString() ?? "—"}:{snapshot.TScore?.ToString() ?? "—"}";
        var kills = snapshot.Kills ?? 0;
        var deaths = snapshot.Deaths ?? 0;
        var kd = deaths > 0
            ? ((double)kills / deaths).ToString("0.00")
            : kills.ToString("0.00");

        var advice = _aiProvider() ?? "";
        var plan = ParseCoachAdvice(advice);
        var (fallbackBuyTitle, fallbackBuyAdvice) =
            CoachEngine.BuyAdvice(snapshot);

        var buy = ValueOrFallback(
            plan,
            "BUY",
            fallbackBuyAdvice);

        var position = ValueOrFallback(
            plan,
            "POSITION",
            "Waiting for live round data");

        var expect = ValueOrFallback(
            plan,
            "EXPECT",
            "Learning enemy patterns");

        var action = ValueOrFallback(
            plan,
            "DO",
            "Waiting for next round plan");

        var adapt = ValueOrFallback(
            plan,
            "ADAPT",
            "—");

        var weapon = !string.IsNullOrWhiteSpace(snapshot.PrimaryWeapon)
            ? CoachEngine.PrettyWeapon(snapshot.PrimaryWeapon)
            : !string.IsNullOrWhiteSpace(snapshot.Weapon)
                ? CoachEngine.PrettyWeapon(snapshot.Weapon)
                : "—";

        var roundKey =
            $"{snapshot.Map}|{snapshot.Team}|{snapshot.Round?.ToString() ?? "—"}";

        return new PhoneState
        {
            map = map,
            side = string.IsNullOrWhiteSpace(snapshot.Team) ? "—" : snapshot.Team,
            round = round,
            score = score,
            kills = kills,
            deaths = deaths,
            kd = kd,
            money = snapshot.Money ?? 0,
            hp = snapshot.Health?.ToString() ?? "—",
            armor = snapshot.Armor?.ToString() ?? "—",
            weapon = weapon,
            roundType = CoachEngine.ClassifyRound(snapshot),
            buyTitle = fallbackBuyTitle,
            buyAdvice = buy,
            position = position,
            expect = expect,
            action = action,
            adapt = adapt,
            roundKey = roundKey,
            planKey = roundKey + "|" + position + "|" + expect + "|" + action + "|" + buy + "|" + adapt,
            aiTip = advice,
            tip =
                CoachEngine.SoloTip(snapshot, mode) +
                $" Role: {role}. Focus: {focus}."
        };
    }

    private static Dictionary<string,string> ParseCoachAdvice(string? advice)
    {
        var result = new Dictionary<string,string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in (advice ?? "")
                     .Replace("\r", "")
                     .Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (key is "BUY" or "POSITION" or "EXPECT" or "DO" or "ADAPT")
                result[key] = value;
        }

        return result;
    }

    private static string ValueOrFallback(
        IReadOnlyDictionary<string,string> plan,
        string key,
        string fallback)
    {
        return plan.TryGetValue(key, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private string BuildHtml()
    {
        var state = BuildState();
        var mode = _modeProvider();
        var role = _roleProvider();
        var focus = _focusProvider();
        var nickname = _nicknameProvider();
        var autoAi = _autoAiProvider();

        return $@"<!doctype html>
<html lang='sl'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,viewport-fit=cover,user-scalable=no'>
<meta name='theme-color' content='#090a0c'>
<meta name='apple-mobile-web-app-capable' content='yes'>
<meta name='apple-mobile-web-app-status-bar-style' content='black-translucent'>
<title>Sm0ki Live Coach</title>
<style>
*{{box-sizing:border-box;-webkit-tap-highlight-color:transparent}}
:root{{color-scheme:dark}}
html,body{{margin:0;min-height:100%;background:#090a0c;color:#f4f6f8;font-family:Inter,Segoe UI,Arial,sans-serif}}
body{{padding:env(safe-area-inset-top) 0 env(safe-area-inset-bottom)}}
.wrap{{max-width:720px;margin:auto;padding:10px 10px 28px}}
.top{{position:sticky;top:0;z-index:10;background:rgba(9,10,12,.96);backdrop-filter:blur(16px);padding:7px 2px 9px;border-bottom:1px solid #1b1d20}}
.brand{{display:flex;align-items:center;justify-content:space-between;gap:10px}}
.logo{{font-size:19px;font-weight:900;letter-spacing:-.03em}}
.orange{{color:#ff9c2c}}
.live{{font-size:9px;font-weight:900;letter-spacing:.11em;color:#78e6a5;background:#102a1e;border:1px solid #20583c;border-radius:999px;padding:5px 8px}}
.context{{display:flex;gap:7px;align-items:center;margin-top:7px;font-size:12px;font-weight:800;color:#b5bbc4;white-space:nowrap;overflow:hidden}}
.context span{{overflow:hidden;text-overflow:ellipsis}}
.dot{{color:#4d535c}}
.coach{{margin-top:10px}}
.heroCard,.card,.mini,.details{{background:#121416;border:1px solid #2a2d31;border-radius:14px}}
.heroCard{{padding:14px 15px 15px;border-color:#725024;background:linear-gradient(180deg,#211a13,#151515)}}
.kicker{{font-size:9px;font-weight:900;letter-spacing:.12em;color:#ff9c2c;margin-bottom:7px}}
.position{{font-size:24px;line-height:1.14;font-weight:900;letter-spacing:-.03em}}
.card{{padding:13px 14px;margin-top:8px}}
.card.expect{{border-left:3px solid #ff9c2c}}
.card.do{{background:#15181b}}
.valueBig{{font-size:17px;line-height:1.28;font-weight:800}}
.value{{font-size:14px;line-height:1.38;color:#d3d7dd}}
.bottomGrid{{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px}}
.mini{{padding:11px 12px;min-height:78px}}
.mini .value{{font-size:13px}}
.statRow{{display:grid;grid-template-columns:repeat(4,1fr);gap:7px;margin-top:10px}}
.stat{{background:#0f1113;border:1px solid #24272b;border-radius:11px;padding:9px 8px}}
.stat .kicker{{margin-bottom:4px;color:#747c87;font-size:8px}}
.stat .num{{font-size:15px;font-weight:900;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}}
.start{{width:100%;border:0;border-radius:12px;padding:12px 14px;margin-top:10px;background:#ff9c2c;color:#080808;font-weight:900;font-size:13px}}
.start.on{{background:#163c2a;color:#86f1b1;border:1px solid #286244}}
.details{{margin-top:10px;padding:0;overflow:hidden}}
details summary{{padding:13px 14px;cursor:pointer;font-size:11px;font-weight:900;letter-spacing:.08em;color:#9ba3ae;list-style:none}}
details summary::-webkit-details-marker{{display:none}}
.detailBody{{padding:0 14px 14px}}
.moreStats{{display:grid;grid-template-columns:repeat(4,1fr);gap:7px}}
.controls{{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:12px}}
.field label{{display:block;font-size:8px;font-weight:900;letter-spacing:.1em;color:#7b838e;margin-bottom:4px}}
select{{width:100%;background:#0e1012;color:#f5f6f7;border:1px solid #30343a;border-radius:9px;padding:9px}}
.toggle{{display:flex;align-items:center;gap:7px;padding-top:9px;font-size:12px;color:#c9ced5}}
.apply{{width:100%;border:0;border-radius:10px;padding:10px;margin-top:10px;background:#24282d;color:#fff;font-weight:800}}
.note{{font-size:10px;color:#6f7782;line-height:1.4;margin-top:9px}}
.flash{{animation:roundFlash .55s ease-out}}
@keyframes roundFlash{{0%{{box-shadow:0 0 0 2px rgba(255,156,44,.9),0 0 28px rgba(255,156,44,.45)}}100%{{box-shadow:none}}}}
@media(max-width:420px){{
 .wrap{{padding-left:8px;padding-right:8px}}
 .position{{font-size:22px}}
 .valueBig{{font-size:16px}}
 .statRow{{grid-template-columns:repeat(4,1fr);gap:5px}}
 .stat{{padding:8px 6px}}
 .stat .num{{font-size:14px}}
 .bottomGrid{{grid-template-columns:1fr}}
 .moreStats{{grid-template-columns:repeat(2,1fr)}}
 .controls{{grid-template-columns:1fr}}
}}
</style>
</head>
<body>
<div class='wrap'>
<header class='top'>
  <div class='brand'>
    <div class='logo'>SM0KI <span class='orange'>LIVE COACH</span></div>
    <div class='live'>● LIVE</div>
  </div>
  <div class='context'>
    <span id='map'>{Html(state.map)}</span><span class='dot'>•</span>
    <span id='side'>{Html(state.side)}</span><span class='dot'>•</span>
    <span id='round'>{Html(state.round)}</span><span class='dot'>•</span>
    <span id='score'>{Html(state.score)}</span>
  </div>
</header>

<main id='coach' class='coach'>
  <section class='heroCard' id='positionCard'>
    <div class='kicker'>POSITION</div>
    <div id='position' class='position'>{Html(state.position)}</div>
  </section>

  <section class='card expect'>
    <div class='kicker'>EXPECT</div>
    <div id='expect' class='value'>{Html(state.expect)}</div>
  </section>

  <section class='card do'>
    <div class='kicker'>DO</div>
    <div id='action' class='valueBig'>{Html(state.action)}</div>
  </section>

  <div class='bottomGrid'>
    <section class='mini'>
      <div class='kicker'>BUY</div>
      <div id='buyAdvice' class='value'>{Html(state.buyAdvice)}</div>
    </section>
    <section class='mini'>
      <div class='kicker'>ADAPT</div>
      <div id='adapt' class='value'>{Html(state.adapt)}</div>
    </section>
  </div>

  <div class='statRow'>
    <div class='stat'><div class='kicker'>MONEY</div><div id='money' class='num'>&#36;{state.money}</div></div>
    <div class='stat'><div class='kicker'>GUN</div><div id='weapon' class='num'>{Html(state.weapon)}</div></div>
    <div class='stat'><div class='kicker'>K/D</div><div id='kd' class='num'>{Html(state.kd)}</div></div>
    <div class='stat'><div class='kicker'>TYPE</div><div id='roundType' class='num'>{Html(state.roundType)}</div></div>
  </div>

  <button id='startCoach' class='start' type='button'>START LIVE COACH</button>

  <div class='details'>
    <details>
      <summary>MORE INFO & COACH SETTINGS</summary>
      <div class='detailBody'>
        <div class='moreStats'>
          <div class='stat'><div class='kicker'>KILLS</div><div id='kills' class='num'>{state.kills}</div></div>
          <div class='stat'><div class='kicker'>DEATHS</div><div id='deaths' class='num'>{state.deaths}</div></div>
          <div class='stat'><div class='kicker'>HP</div><div id='hp' class='num'>{Html(state.hp)}</div></div>
          <div class='stat'><div class='kicker'>ARMOR</div><div id='armor' class='num'>{Html(state.armor)}</div></div>
        </div>

        <form method='get' action='/settings'>
          <input type='hidden' name='token' value='{Html(_accessToken)}'>
          <div class='controls'>
            <div class='field'>
              <label>MODE</label>
              <select name='mode'>{Options(new[] { "Balanced","Aggressive","Safe" }, mode)}</select>
            </div>
            <div class='field'>
              <label>ROLE</label>
              <select name='role'>{Options(new[] { "Flex","Entry","Lurk","Support","Anchor" }, role)}</select>
            </div>
            <div class='field'>
              <label>FOCUS</label>
              <select name='focus'>{Options(new[] { "More kills","Survive & trade","Entry impact","Utility impact","Clutch / late round" }, focus)}</select>
            </div>
            <div class='toggle'>
              <input id='autoAi' type='checkbox' name='autoAi' value='1' {(autoAi ? "checked" : "")}>
              <label for='autoAi'>Auto AI refine</label>
            </div>
          </div>
          <button class='apply' type='submit'>APPLY TO PC COACH</button>
        </form>

        <div class='note'>
          {Html(nickname)} • private LAN link • v{Html(AppUpdater.CurrentVersion)}.
          Telefon in PC morata biti na istem Wi‑Fi/LAN omrežju.
        </div>
      </div>
    </details>
  </div>
</main>
</div>

<script>
const liveToken='{Html(_accessToken)}';
let lastRoundKey='{Html(state.roundKey)}';
let lastPlanKey='{Html(state.planKey)}';
let alertsEnabled=false;
let wakeLock=null;

const setText=(id,value)=>{{
  const el=document.getElementById(id);
  if(el) el.textContent=(value ?? '—');
}};

function flashPlan(){{
  const card=document.getElementById('positionCard');
  if(!card) return;
  card.classList.remove('flash');
  void card.offsetWidth;
  card.classList.add('flash');
}}

async function enableLiveCoach(){{
  alertsEnabled=true;
  const button=document.getElementById('startCoach');
  button.textContent='LIVE COACH ACTIVE';
  button.classList.add('on');

  try{{
    if(navigator.wakeLock && !wakeLock)
      wakeLock=await navigator.wakeLock.request('screen');
  }}catch{{}}

  try{{
    if(navigator.vibrate) navigator.vibrate(60);
  }}catch{{}}

  try{{
    if(document.documentElement.requestFullscreen)
      await document.documentElement.requestFullscreen();
  }}catch{{}}
}}

document.getElementById('startCoach')?.addEventListener('click',enableLiveCoach);

document.addEventListener('visibilitychange',async()=>{{
  if(document.visibilityState==='visible' && alertsEnabled){{
    try{{
      if(navigator.wakeLock && !wakeLock)
        wakeLock=await navigator.wakeLock.request('screen');
    }}catch{{}}
  }}
}});

async function refreshLive(){{
  try{{
    const response=await fetch(
      '/api/state?token='+encodeURIComponent(liveToken)+'&_=' + Date.now(),
      {{cache:'no-store'}});
    if(!response.ok) return;

    const x=await response.json();

    const roundChanged=lastRoundKey && x.roundKey && x.roundKey!==lastRoundKey;
    const planChanged=lastPlanKey && x.planKey && x.planKey!==lastPlanKey;

    setText('map',x.map);
    setText('side',x.side);
    setText('round',x.round);
    setText('score',x.score);
    setText('position',x.position);
    setText('expect',x.expect);
    setText('action',x.action);
    setText('buyAdvice',x.buyAdvice);
    setText('adapt',x.adapt);
    setText('money','$'+x.money);
    setText('weapon',x.weapon);
    setText('kd',x.kd);
    setText('roundType',x.roundType);
    setText('kills',x.kills);
    setText('deaths',x.deaths);
    setText('hp',x.hp);
    setText('armor',x.armor);

    if(roundChanged || planChanged)
      flashPlan();

    if(roundChanged && alertsEnabled){{
      try{{
        if(navigator.vibrate) navigator.vibrate([90,50,90]);
      }}catch{{}}
    }}

    lastRoundKey=x.roundKey || lastRoundKey;
    lastPlanKey=x.planKey || lastPlanKey;
  }}catch{{}}
}}

refreshLive();
setInterval(refreshLive,700);
</script>
</body>
</html>";
    }

    private static bool SafeTokenEquals(string supplied, string expected)
    {
        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length &&
               CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static Dictionary<string,string> ParseQuery(string query)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            var value =
                parts.Length > 1
                    ? Uri.UnescapeDataString(parts[1].Replace("+", " "))
                    : "";

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
        return query.TryGetValue(key, out var value) &&
               allowed.Contains(value)
            ? value
            : fallback;
    }

    private static string Options(IEnumerable<string> items, string current)
        => string.Join(
            "",
            items.Select(x =>
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

    private static async Task SendText(NetworkStream stream, int code, string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var status =
            code == 403 ? "Forbidden" :
            code == 404 ? "Not Found" :
            "Bad Request";

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

    private static string Html(string? value)
        => WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(value) ? "—" : value);

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts.Dispose();
    }
}
