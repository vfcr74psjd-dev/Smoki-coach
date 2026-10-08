using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sm0kiSoloCoach;

public sealed class PhoneDashboardServer : IDisposable
{
    private sealed class PhoneRoundState
    {
        public int round { get; init; }
        public string side { get; init; } = "—";
        public string result { get; init; } = "—";
        public int kills { get; init; }
        public int deaths { get; init; }
        public bool survived { get; init; }
        public string intent { get; init; } = "";
        public string site { get; init; } = "";
        public string position { get; init; } = "";
    }

    private sealed class PhoneState
    {
        public string version { get; init; } = "";
        public string nickname { get; init; } = "";
        public string connection { get; init; } = "WAITING";
        public int telemetryAgeSeconds { get; init; }
        public string mapPhase { get; init; } = "";
        public string roundPhase { get; init; } = "";
        public string map { get; init; } = "—";
        public string side { get; init; } = "—";
        public string round { get; init; } = "—";
        public string score { get; init; } = "—";
        public int kills { get; init; }
        public int deaths { get; init; }
        public int assists { get; init; }
        public string kd { get; init; } = "0.00";
        public int money { get; init; }
        public string hp { get; init; } = "—";
        public string armor { get; init; } = "—";
        public string weapon { get; init; } = "—";
        public string roundType { get; init; } = "—";
        public string buyTitle { get; init; } = "";
        public string buyAdvice { get; init; } = "";
        public string position { get; init; } = "—";
        public string routeLabel { get; init; } = "ROUTE";
        public string expect { get; init; } = "—";
        public string action { get; init; } = "—";
        public string adapt { get; init; } = "—";
        public string planWhy { get; init; } = "";
        public string planConfidence { get; init; } = "LOW";
        public int decisionScore { get; init; }
        public bool halftimeActive { get; init; }
        public string halftimeText { get; init; } = "";
        public int devScore { get; init; }
        public string devLeak { get; init; } = "Collecting evidence";
        public string devFocus { get; init; } = "Play normal CS2 while the coach learns.";
        public string brainMistake { get; init; } = "Collecting evidence";
        public string brainPriority { get; init; } = "KEEP PLAN";
        public string brainFocus { get; init; } = "Play normal CS2 while Smart Match Brain learns your pattern.";
        public string brainEvidence { get; init; } = "";
        public string brainConfidence { get; init; } = "LOW";
        public string brainStatus { get; init; } = "LEARNING";
        public string brainProgress { get; init; } = "";
        public string planKey { get; init; } = "";
        public string roundKey { get; init; } = "";
        public string aiTip { get; init; } = "";
        public string tip { get; init; } = "";
        public string mode { get; init; } = "Balanced";
        public string role { get; init; } = "Flex";
        public string focus { get; init; } = "More kills";
        public bool autoAi { get; init; }
        public int roundsTracked { get; init; }
        public int wins { get; init; }
        public int losses { get; init; }
        public int unresolved { get; init; }
        public int survivedRounds { get; init; }
        public int multiKillRounds { get; init; }
        public int zeroKillRounds { get; init; }
        public string survival { get; init; } = "0%";
        public string killsPerRound { get; init; } = "0.00";
        public PhoneRoundState[] recentRounds { get; init; } = Array.Empty<PhoneRoundState>();
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
    private readonly Func<IReadOnlyList<RoundRecord>> _roundsProvider;
    private readonly Func<int?, string> _intentProvider;
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
        Func<IReadOnlyList<RoundRecord>> roundsProvider,
        Func<int?, string> intentProvider,
        Action<string,string,string,bool> settingsUpdater)
    {
        _snapshotProvider = snapshotProvider;
        _modeProvider = modeProvider;
        _roleProvider = roleProvider;
        _focusProvider = focusProvider;
        _nicknameProvider = nicknameProvider;
        _autoAiProvider = autoAiProvider;
        _aiProvider = aiProvider;
        _roundsProvider = roundsProvider;
        _intentProvider = intentProvider;
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

                if (uri.AbsolutePath.Equals("/settings", StringComparison.OrdinalIgnoreCase) ||
                    uri.AbsolutePath.Equals("/api/settings", StringComparison.OrdinalIgnoreCase))
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

                    if (uri.AbsolutePath.Equals("/api/settings", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendJson(stream, "{\"ok\":true}");
                    }
                    else
                    {
                        await SendRedirect(stream, $"/?token={_accessToken}");
                    }
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
        var rounds = _roundsProvider();

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
        var development = PlayerDevelopmentEngine.Analyze(rounds);
        var brain = SmartMatchBrainEngine.Analyze(rounds);
        var intent = _intentProvider(snapshot.Round);

        // Tactical OS is authoritative for every glance-critical field.
        // Local AI can refine language elsewhere, but the phone never waits.
        var tactical = TacticalBrainV6.Generate(
            snapshot,
            mode,
            role,
            focus,
            rounds,
            intent);
        var halftime = HalftimeBrainV6.Analyze(
            snapshot.Team,
            rounds);

        var (fallbackBuyTitle, _) = CoachEngine.BuyAdvice(snapshot);
        var buy = tactical.Buy;
        var position = tactical.Route;
        var expect = tactical.Expect;
        var action = tactical.FirstMove;
        var adapt = tactical.Fallback;

        var weapon = !string.IsNullOrWhiteSpace(snapshot.PrimaryWeapon)
            ? CoachEngine.PrettyWeapon(snapshot.PrimaryWeapon)
            : !string.IsNullOrWhiteSpace(snapshot.Weapon)
                ? CoachEngine.PrettyWeapon(snapshot.Weapon)
                : "—";

        var roundKey =
            $"{snapshot.Map}|{snapshot.Team}|{snapshot.Round?.ToString() ?? "—"}";

        var tracked = rounds.Count;
        var wins = rounds.Count(x => x.Won == true);
        var losses = rounds.Count(x => x.Won == false);
        var unresolved = rounds.Count(x => x.Won == null);
        var survived = rounds.Count(x => x.Survived);
        var multi = rounds.Count(x => x.KillsRound >= 2);
        var zeroKill = rounds.Count(x => x.KillsRound == 0);
        var roundKills = rounds.Sum(x => x.KillsRound);
        var survival = tracked > 0
            ? $"{100.0 * survived / tracked:0}%"
            : "0%";
        var killsPerRound = tracked > 0
            ? ((double)roundKills / tracked).ToString("0.00")
            : "0.00";

        var recentRounds = rounds
            .TakeLast(12)
            .Reverse()
            .Select(x => new PhoneRoundState
            {
                round = x.Round + 1,
                side = string.IsNullOrWhiteSpace(x.Side) ? "—" : x.Side,
                result = x.Won == true ? "W" : x.Won == false ? "L" : "—",
                kills = x.KillsRound,
                deaths = x.DeathsRound,
                survived = x.Survived,
                intent = x.Intent,
                site = x.BombPlanted
                    ? (string.IsNullOrWhiteSpace(x.BombSite) ? "PLANT" : $"PLANT {x.BombSite}")
                    : "",
                position = x.PositionPlan
            })
            .ToArray();

        var telemetryUtc = snapshot.Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(snapshot.Timestamp, DateTimeKind.Utc)
            : snapshot.Timestamp.ToUniversalTime();

        var telemetryAge = Math.Max(
            0,
            (int)Math.Round((DateTime.UtcNow - telemetryUtc).TotalSeconds));

        var connection = telemetryAge <= 12 && snapshot.Round.HasValue
            ? "LIVE"
            : telemetryAge <= 12 && !string.IsNullOrWhiteSpace(snapshot.Map)
                ? "CONNECTED"
                : snapshot.Round.HasValue || !string.IsNullOrWhiteSpace(snapshot.Map)
                    ? "STALE"
                    : "WAITING";

        return new PhoneState
        {
            version = AppUpdater.CurrentVersion,
            nickname = _nicknameProvider(),
            connection = connection,
            telemetryAgeSeconds = telemetryAge,
            mapPhase = snapshot.MapPhase ?? "",
            roundPhase = snapshot.RoundPhase ?? "",
            map = map,
            side = string.IsNullOrWhiteSpace(snapshot.Team) ? "—" : snapshot.Team,
            round = round,
            score = score,
            kills = kills,
            deaths = deaths,
            assists = snapshot.Assists ?? 0,
            kd = kd,
            money = snapshot.Money ?? 0,
            hp = snapshot.Health?.ToString() ?? "—",
            armor = snapshot.Armor?.ToString() ?? "—",
            weapon = weapon,
            roundType = CoachEngine.ClassifyRound(snapshot),
            buyTitle = fallbackBuyTitle,
            buyAdvice = buy,
            position = position,
            routeLabel = tactical.SideMode == "CT" ? "START POSITION" : "ROUTE",
            expect = expect,
            action = action,
            adapt = adapt,
            planWhy = tactical.Why,
            planConfidence = tactical.Confidence,
            decisionScore = tactical.DecisionScore,
            halftimeActive = halftime.Active,
            halftimeText = halftime.Active
                ? $"{halftime.FromSide} → {halftime.ToSide} • {halftime.Change} • {halftime.Opening}"
                : "",
            devScore = development.OverallScore,
            devLeak = development.BiggestLeak,
            devFocus = development.MatchFocus,
            brainMistake = brain.Mistake,
            brainPriority = brain.Priority,
            brainFocus = tactical.Focus,
            brainEvidence = brain.Evidence,
            brainConfidence = brain.Confidence,
            brainStatus = brain.FocusStatus,
            brainProgress = brain.FocusProgress,
            roundKey = roundKey,
            planKey = roundKey + "|" + tactical.PlanKey + "|" + buy + "|" + expect + "|" + tactical.Confidence,
            aiTip = advice,
            tip =
                CoachEngine.SoloTip(snapshot, mode) +
                $" Role: {role}. Focus: {focus}.",
            mode = mode,
            role = role,
            focus = focus,
            autoAi = _autoAiProvider(),
            roundsTracked = tracked,
            wins = wins,
            losses = losses,
            unresolved = unresolved,
            survivedRounds = survived,
            multiKillRounds = multi,
            zeroKillRounds = zeroKill,
            survival = survival,
            killsPerRound = killsPerRound,
            recentRounds = recentRounds
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

        return $$"""
<!doctype html>
<html lang="sl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover,user-scalable=no">
<meta name="theme-color" content="#090a0c">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<title>Sm0ki Match Coach</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
:root{
  color-scheme:dark;
  --bg:#090a0c;--panel:#111315;--line:#292d31;
  --text:#f4f6f8;--muted:#7f8791;--orange:#ff9c2c;
  --green:#78e6a5;--red:#ef7b71;
}
html,body{
  margin:0;width:100%;min-height:100%;background:var(--bg);color:var(--text);
  font-family:Inter,Segoe UI,Arial,sans-serif
}
body{padding:env(safe-area-inset-top) 0 env(safe-area-inset-bottom)}
button{font:inherit}
.wakeState{
  margin:7px 0 10px;padding:8px 10px;border:1px solid var(--line);
  border-radius:10px;background:#0f1215;color:var(--muted);
  font-size:11px;font-weight:800;letter-spacing:.05em;text-align:center
}
.wakeState.ok{color:var(--green);border-color:#28523a;background:#0f1c16}
.wakeState.warn{color:#ffcc7a;border-color:#59431f;background:#1b160d}
.wakeState.bad{color:var(--red);border-color:#5b2c28;background:#1d1110}
.app{max-width:680px;margin:auto;padding:0 10px 22px}
.top{
  position:sticky;top:0;z-index:20;margin:0 -10px;
  padding:9px 11px 8px;background:rgba(9,10,12,.96);
  backdrop-filter:blur(14px);border-bottom:1px solid #1c1f22
}
.toprow{display:flex;align-items:center;justify-content:space-between;gap:8px}
.brand{font-size:16px;font-weight:950;letter-spacing:-.03em}
.brand span{color:var(--orange)}
.right{display:flex;align-items:center;gap:6px}
.status{
  display:flex;align-items:center;gap:5px;padding:5px 7px;border-radius:999px;
  border:1px solid #2d573f;background:#10251b;color:var(--green);
  font-size:8px;font-weight:950;letter-spacing:.08em
}
.status.waiting{color:#e1b675;background:#2b2115;border-color:#514126}
.status.stale{color:var(--red);background:#2b1717;border-color:#5a2928}
.dot{width:6px;height:6px;border-radius:50%;background:currentColor}
.icon{
  width:30px;height:30px;border-radius:9px;border:1px solid #2b2f33;
  background:#121416;color:#aeb5be;font-size:14px
}
.context{
  display:flex;gap:6px;align-items:center;margin-top:7px;
  font-size:11px;font-weight:850;color:#b7bec8
}
.context .map{min-width:0;max-width:55vw;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.pill{
  padding:4px 7px;border-radius:7px;background:#121417;border:1px solid #292c30;
  white-space:nowrap
}
.pill.round{color:var(--orange)}
.offline{
  display:none;margin-top:9px;padding:9px 11px;border-radius:10px;
  background:#301918;border:1px solid #5b2926;color:#ffc0b9;
  font-size:10px;font-weight:850
}
.offline.show{display:block}
.activate{
  width:100%;margin-top:9px;padding:9px 11px;border-radius:11px;
  border:1px solid #664720;background:#21170e;color:#ffc079;
  font-size:10px;font-weight:950
}
.activate.on{
  background:#133220;border-color:#2e6947;color:#8ef0b4
}
.halftime{
  display:none;margin-top:8px;padding:8px 10px;border-radius:10px;
  border:1px solid #584020;background:#21180f;color:#ffc079;
  font-size:10px;line-height:1.25;font-weight:900
}
.halftime.show{display:block}
.buy{
  margin-top:9px;border:1px solid #67451f;border-radius:14px;
  background:linear-gradient(145deg,#21180f,#141516);padding:11px 13px
}
.label{
  font-size:8px;font-weight:950;letter-spacing:.13em;color:#808892;text-transform:uppercase
}
.buy .label,.tacticTitle,.focusLabel{color:var(--orange)}
.buyText{
  margin-top:5px;font-size:14px;line-height:1.25;font-weight:900;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis
}
.tactic{
  margin-top:8px;border:1px solid #31353a;border-radius:16px;
  background:#111315;overflow:hidden
}
.tacticHead{
  display:flex;align-items:center;justify-content:space-between;
  padding:9px 13px;border-bottom:1px solid #22262a
}
.tacticTitle{font-size:9px;font-weight:950;letter-spacing:.14em}
.lock{font-size:8px;color:#727b86;font-weight:850}
.row{padding:11px 13px;border-bottom:1px solid #22262a}
.row:last-child{border-bottom:0}
.row.position{background:linear-gradient(180deg,#18130e,#111315)}
.row.do{background:#15181a}
.value{margin-top:5px;font-size:15px;line-height:1.24;font-weight:850}
.row.position .value{font-size:20px;line-height:1.13;font-weight:950;letter-spacing:-.02em}
.row.do .value{font-size:18px;line-height:1.18;font-weight:950}
.focus{
  margin-top:8px;border:1px solid #34312a;border-radius:12px;
  background:#15130f;padding:9px 11px
}
.focusLabel{font-size:8px;font-weight:950;letter-spacing:.12em}
.focusText{margin-top:4px;font-size:12px;line-height:1.25;font-weight:900}
.row.fallback .value{font-size:13px;color:#c8ced6}
.whyBtn{
  margin-top:7px;padding:5px 7px;border-radius:7px;border:1px solid #34383d;
  background:#101214;color:#848d98;font-size:8px;font-weight:900;letter-spacing:.06em
}
.whyBox{
  display:none;margin-top:6px;padding-top:6px;border-top:1px solid #272b2f;
  color:#8f98a3;font-size:9px;line-height:1.3;font-weight:750
}
.whyBox.open{display:block}
.foot{
  display:flex;justify-content:space-between;gap:8px;
  margin:8px 2px 0;color:#69717b;font-size:8px;font-weight:800
}
.flash{animation:flash .58s ease-out}
@keyframes flash{
  0%{box-shadow:0 0 0 2px rgba(255,156,44,.85),0 0 24px rgba(255,156,44,.35)}
  100%{box-shadow:none}
}
.sheet{
  position:fixed;inset:0;z-index:50;display:none;
  align-items:flex-end;justify-content:center;background:rgba(0,0,0,.58)
}
.sheet.open{display:flex}
.panel{
  width:min(680px,100%);border-radius:18px 18px 0 0;
  background:#111315;border:1px solid #2a2e32;border-bottom:0;
  padding:14px 14px calc(14px + env(safe-area-inset-bottom))
}
.panelHead{display:flex;justify-content:space-between;align-items:center;margin-bottom:8px}
.panelHead b{font-size:13px}.close{
  width:30px;height:30px;border:0;border-radius:9px;background:#1b1e21;color:#d4d9df
}
.setting{
  display:flex;justify-content:space-between;align-items:center;gap:12px;
  padding:10px 0;border-bottom:1px solid #222529
}
.setting:last-child{border-bottom:0}
.setting b{display:block;font-size:11px}
.setting small{display:block;color:#767e88;font-size:9px;margin-top:2px}
.toggle{
  min-width:56px;border:1px solid #34383d;background:#16191c;color:#969ea8;
  border-radius:999px;padding:6px 8px;font-size:8px;font-weight:950
}
.toggle.on{background:#173522;border-color:#2f6846;color:#8ef0b4}
.reconnect{
  width:100%;border:1px solid #34383d;background:#171a1d;color:#dce1e7;
  border-radius:10px;padding:10px;margin-top:10px;font-size:9px;font-weight:950
}
.note{margin-top:9px;color:#69717c;font-size:8px;line-height:1.4}
@media(max-width:390px){
  .app{padding-left:8px;padding-right:8px}
  .top{margin-left:-8px;margin-right:-8px}
  .row.position .value{font-size:19px}
  .row.do .value{font-size:17px}
  .value{font-size:14px}
}
</style>
</head>
<body>
<div class="app">
  <header class="top">
    <div class="toprow">
      <div class="brand">SM0KI <span>MATCH COACH</span></div>
      <div class="right">
        <div id="conn" class="status {{state.connection.ToLowerInvariant()}}">
          <span class="dot"></span><span id="connText">{{Html(state.connection)}}</span>
        </div>
        <button id="settingsBtn" class="icon" type="button">⚙</button>
      </div>
    </div>
    <div class="context">
      <span id="map" class="map">{{Html(state.map)}}</span>
      <span id="side" class="pill">{{Html(state.side)}}</span>
      <span id="round" class="pill round">{{Html(state.round)}}</span>
    </div>
  </header>

  <div id="offline" class="offline">PC/CS2 telemetry ni svež. Coach čaka na povezavo.</div>

  <button id="activate" class="activate" type="button">START MATCH MODE</button>
  <div id="wakeState" class="wakeState">SCREEN AWAKE • tap START MATCH MODE</div>

  <div id="halftime" class="halftime {{(state.halftimeActive ? "show" : "")}}">
    <span class="label">HALFTIME RESET</span>
    <div id="halftimeText">{{Html(state.halftimeText)}}</div>
  </div>

  <section class="buy">
    <div class="label">BUY</div>
    <div id="buyAdvice" class="buyText">{{Html(state.buyAdvice)}}</div>
  </section>

  <section id="tactic" class="tactic">
    <div class="tacticHead">
      <div>
        <div id="tacticTitle" class="tacticTitle">PRE-ROUND WIN PLAN</div>
        <div id="planScore" class="lock">BEST PLAN • {{state.decisionScore}}/100 • {{Html(state.planConfidence)}}</div>
      </div>
      <div id="planLock" class="lock">refreshes between rounds</div>
    </div>

    <div class="row position">
      <div id="routeLabel" class="label">{{Html(state.routeLabel)}}</div>
      <div id="position" class="value">{{Html(state.position)}}</div>
    </div>

    <div class="row">
      <div class="label">EXPECT</div>
      <div id="expect" class="value">{{Html(state.expect)}}</div>
    </div>

    <div class="row do">
      <div class="label">FIRST MOVE</div>
      <div id="action" class="value">{{Html(state.action)}}</div>
    </div>

    <div class="row fallback">
      <div class="label">IF BLOCKED / PRESSURED</div>
      <div id="fallback" class="value">{{Html(state.adapt)}}</div>
    </div>
  </section>

  <section class="focus">
    <div class="focusLabel">FOCUS</div>
    <div id="brainFocus" class="focusText">{{Html(state.brainFocus)}}</div>
    <button id="whyBtn" class="whyBtn" type="button">WHY • {{Html(state.planConfidence)}} • {{state.decisionScore}}</button>
    <div id="whyBox" class="whyBox">{{Html(state.planWhy)}}</div>
  </section>

  <div class="foot">
    <span id="latency">— ms</span>
    <span>v{{Html(state.version)}}</span>
  </div>
</div>

<div id="settingsSheet" class="sheet">
  <div class="panel">
    <div class="panelHead">
      <b>MATCH MODE SETTINGS</b>
      <button id="closeSettings" class="close" type="button">×</button>
    </div>

    <div class="setting">
      <div><b>Vibration</b><small>Short cue when a new round starts</small></div>
      <button id="vibrationToggle" class="toggle" type="button">ON</button>
    </div>

    <div class="setting">
      <div><b>Sound cue</b><small>Quiet beep on new round</small></div>
      <button id="soundToggle" class="toggle" type="button">OFF</button>
    </div>

    <div class="setting">
      <div><b>Keep screen awake</b><small>Native wake lock + LAN fallback while Match Mode is active</small></div>
      <button id="wakeToggle" class="toggle" type="button">ON</button>
    </div>

    <div class="setting">
      <div><b>Fullscreen</b><small>Hide browser UI where supported</small></div>
      <button id="fullscreenToggle" class="toggle" type="button">OFF</button>
    </div>

    <button id="reconnect" class="reconnect" type="button">REFRESH / RECONNECT</button>

    <div class="note">
      {{Html(state.nickname)}} • private LAN • phone shows only information useful during a match.
    </div>
  </div>
</div>

<script>
const liveToken={{JsonSerializer.Serialize(_accessToken)}};
let lastRoundKey={{JsonSerializer.Serialize(state.roundKey)}};
let lastPlanKey={{JsonSerializer.Serialize(state.planKey)}};
let lastSuccess=Date.now();
let matchMode=false;
let wakeLock=null;
let noSleepVideo=null;
let wakeStrategy='OFF';
let forcePlan=true;
const noSleepMp4="data:video/mp4;base64,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";

const prefs={
  vibration:localStorage.getItem('smoki_vibration')!=='0',
  sound:localStorage.getItem('smoki_sound')==='1',
  wake:localStorage.getItem('smoki_wake')!=='0',
  fullscreen:localStorage.getItem('smoki_fullscreen')==='1'
};

const $=id=>document.getElementById(id);
const setText=(id,v)=>{const e=$(id);if(e)e.textContent=(v??'—')};

function setToggle(id,on){
  const b=$(id);if(!b)return;
  b.classList.toggle('on',!!on);
  b.textContent=on?'ON':'OFF';
}

function isLivePhase(v){
  return String(v||'').toLowerCase()==='live';
}

function updateConnection(x){
  const conn=$('conn');
  if(!conn)return;
  conn.className='status '+String(x.connection||'waiting').toLowerCase();
  setText('connText',x.connection||'WAITING');
  $('offline')?.classList.toggle('show',x.connection==='STALE'||x.connection==='WAITING');
}

function applyPlan(x,flash){
  const live=isLivePhase(x.roundPhase);
  setText('tacticTitle',live?'ROUND PLAN':'PRE-ROUND WIN PLAN');
  setText(
    'planScore',
    'BEST PLAN • '+(x.decisionScore??0)+'/100 • '+(x.planConfidence||'LOW')
  );
  setText('buyAdvice',x.buyAdvice);
  setText('routeLabel',x.routeLabel);
  setText('position',x.position);
  setText('expect',x.expect);
  setText('action',x.action);
  setText('fallback',x.adapt);
  setText('brainFocus',x.brainFocus);
  setText('whyBtn','WHY • '+(x.planConfidence||'LOW')+' • '+(x.decisionScore??0));
  setText('whyBox',x.planWhy);
  setText('halftimeText',x.halftimeText);
  $('halftime')?.classList.toggle('show',!!x.halftimeActive);

  if(flash){
    const card=$('tactic');
    card?.classList.remove('flash');
    void card?.offsetWidth;
    card?.classList.add('flash');
  }
}

function beep(){
  if(!prefs.sound)return;
  try{
    const AC=window.AudioContext||window.webkitAudioContext;
    const ctx=new AC();
    const osc=ctx.createOscillator();
    const gain=ctx.createGain();
    osc.frequency.value=650;
    gain.gain.setValueAtTime(.04,ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(.001,ctx.currentTime+.11);
    osc.connect(gain);gain.connect(ctx.destination);
    osc.start();osc.stop(ctx.currentTime+.12);
  }catch{}
}

function setWakeState(text,kind){
  const e=$('wakeState');
  if(!e)return;
  e.textContent=text;
  e.className='wakeState '+(kind||'');
}

function ensureNoSleepVideo(){
  if(noSleepVideo)return noSleepVideo;

  const video=document.createElement('video');
  video.setAttribute('title','Sm0ki screen awake fallback');
  video.setAttribute('playsinline','');
  video.setAttribute('webkit-playsinline','');
  video.setAttribute('loop','');
  video.setAttribute('disablepictureinpicture','');
  video.preload='auto';
  video.src=noSleepMp4;
  video.style.position='fixed';
  video.style.width='1px';
  video.style.height='1px';
  video.style.opacity='0.001';
  video.style.pointerEvents='none';
  video.style.left='-10px';
  video.style.bottom='0';
  document.body.appendChild(video);
  noSleepVideo=video;
  return video;
}

async function activateFallbackWake(){
  if(!matchMode||!prefs.wake)return false;

  try{
    const video=ensureNoSleepVideo();
    await video.play();
    wakeStrategy='VIDEO';
    setWakeState('SCREEN AWAKE ✓ • LAN FALLBACK','ok');
    return true;
  }catch{
    wakeStrategy='FAILED';
    setWakeState('SCREEN AWAKE FAILED • tap MATCH MODE again','bad');
    return false;
  }
}

async function ensureWake(){
  if(!matchMode||!prefs.wake){
    setWakeState(
      matchMode?'SCREEN AWAKE • disabled in settings':'SCREEN AWAKE • tap START MATCH MODE',
      'warn');
    return false;
  }

  if(wakeLock&&!wakeLock.released){
    wakeStrategy='NATIVE';
    setWakeState('SCREEN AWAKE ✓ • NATIVE','ok');
    return true;
  }

  if(window.isSecureContext&&navigator.wakeLock){
    try{
      wakeLock=await navigator.wakeLock.request('screen');
      wakeStrategy='NATIVE';
      setWakeState('SCREEN AWAKE ✓ • NATIVE','ok');

      wakeLock.addEventListener('release',()=>{
        wakeLock=null;
        if(matchMode&&prefs.wake&&document.visibilityState==='visible'){
          activateFallbackWake();
        }
      });

      return true;
    }catch{}
  }

  return await activateFallbackWake();
}

async function disableWake(){
  try{await wakeLock?.release()}catch{}
  wakeLock=null;

  try{
    noSleepVideo?.pause();
    if(noSleepVideo)noSleepVideo.currentTime=0;
  }catch{}

  wakeStrategy='OFF';
  setWakeState(
    matchMode?'SCREEN AWAKE • disabled in settings':'SCREEN AWAKE • tap START MATCH MODE',
    'warn');
}

async function setMatchMode(on){
  matchMode=!!on;
  const b=$('activate');
  if(b){
    b.classList.toggle('on',matchMode);
    b.textContent=matchMode?'MATCH MODE ACTIVE':'START MATCH MODE';
  }

  if(matchMode){
    await ensureWake();
    if(prefs.fullscreen){
      try{await document.documentElement.requestFullscreen?.()}catch{}
    }
  }else{
    await disableWake();
  }
}

async function refreshLive(){
  const started=performance.now();

  try{
    const response=await fetch(
      '/api/state?token='+encodeURIComponent(liveToken)+'&_='+Date.now(),
      {cache:'no-store'});

    if(!response.ok)throw new Error('HTTP '+response.status);
    const x=await response.json();

    lastSuccess=Date.now();
    setText('latency',Math.round(performance.now()-started)+' ms');
    setText('map',x.map);
    setText('side',x.side);
    setText('round',x.round);
    updateConnection(x);

    if(!isLivePhase(x.roundPhase)){
      setText('tacticTitle','PRE-ROUND WIN PLAN');
      setText(
        'planScore',
        'BEST PLAN • '+(x.decisionScore??0)+'/100 • '+(x.planConfidence||'LOW')
      );
    }

    const roundChanged=!!lastRoundKey&&!!x.roundKey&&x.roundKey!==lastRoundKey;
    const planChanged=!!lastPlanKey&&!!x.planKey&&x.planKey!==lastPlanKey;
    const betweenRounds=!isLivePhase(x.roundPhase);

    // Never rewrite the tactic while the player is inside a live round.
    if(forcePlan||roundChanged||(betweenRounds&&planChanged)){
      applyPlan(x,roundChanged);
      forcePlan=false;
    }

    setText(
      'planLock',
      isLivePhase(x.roundPhase)
        ? '🔒 locked during live round'
        : '↻ refreshes between rounds'
    );

    if(roundChanged&&matchMode){
      if(prefs.vibration){
        try{navigator.vibrate?.([75,40,75])}catch{}
      }
      beep();
    }

    lastRoundKey=x.roundKey||lastRoundKey;
    lastPlanKey=x.planKey||lastPlanKey;
  }catch{
    if(Date.now()-lastSuccess>2500){
      const conn=$('conn');
      if(conn)conn.className='status stale';
      setText('connText','STALE');
      $('offline')?.classList.add('show');
      setText('latency','OFFLINE');
    }
  }
}

$('activate')?.addEventListener('click',()=>setMatchMode(!matchMode));
$('whyBtn')?.addEventListener('click',()=>$('whyBox')?.classList.toggle('open'));

$('settingsBtn')?.addEventListener('click',()=>$('settingsSheet')?.classList.add('open'));
$('closeSettings')?.addEventListener('click',()=>$('settingsSheet')?.classList.remove('open'));
$('settingsSheet')?.addEventListener('click',e=>{
  if(e.target===$('settingsSheet'))$('settingsSheet')?.classList.remove('open');
});

$('vibrationToggle')?.addEventListener('click',()=>{
  prefs.vibration=!prefs.vibration;
  localStorage.setItem('smoki_vibration',prefs.vibration?'1':'0');
  setToggle('vibrationToggle',prefs.vibration);
});
$('soundToggle')?.addEventListener('click',()=>{
  prefs.sound=!prefs.sound;
  localStorage.setItem('smoki_sound',prefs.sound?'1':'0');
  setToggle('soundToggle',prefs.sound);
  if(prefs.sound)beep();
});
$('wakeToggle')?.addEventListener('click',async()=>{
  prefs.wake=!prefs.wake;
  localStorage.setItem('smoki_wake',prefs.wake?'1':'0');
  setToggle('wakeToggle',prefs.wake);
  if(prefs.wake)await ensureWake();
  else await disableWake();
});
$('fullscreenToggle')?.addEventListener('click',()=>{
  prefs.fullscreen=!prefs.fullscreen;
  localStorage.setItem('smoki_fullscreen',prefs.fullscreen?'1':'0');
  setToggle('fullscreenToggle',prefs.fullscreen);
});

$('reconnect')?.addEventListener('click',()=>{
  forcePlan=true;
  refreshLive();
  $('settingsSheet')?.classList.remove('open');
});

document.addEventListener('visibilitychange',async()=>{
  if(document.visibilityState==='visible'){
    if(wakeLock?.released)wakeLock=null;
    if(matchMode&&prefs.wake)await ensureWake();
    refreshLive();
  }
});

setToggle('vibrationToggle',prefs.vibration);
setToggle('soundToggle',prefs.sound);
setToggle('wakeToggle',prefs.wake);
setToggle('fullscreenToggle',prefs.fullscreen);
setWakeState('SCREEN AWAKE • tap START MATCH MODE','warn');

refreshLive();
setInterval(refreshLive,800);
</script>
</body>
</html>
""";
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
