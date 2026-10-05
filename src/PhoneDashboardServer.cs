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
        public string expect { get; init; } = "—";
        public string action { get; init; } = "—";
        public string adapt { get; init; } = "—";
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
        var plan = ParseCoachAdvice(advice);
        var development = PlayerDevelopmentEngine.Analyze(rounds);
        var brain = SmartMatchBrainEngine.Analyze(rounds);
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
            expect = expect,
            action = action,
            adapt = adapt,
            devScore = development.OverallScore,
            devLeak = development.BiggestLeak,
            devFocus = development.MatchFocus,
            brainMistake = brain.Mistake,
            brainPriority = brain.Priority,
            brainFocus = brain.OneFocus,
            brainEvidence = brain.Evidence,
            brainConfidence = brain.Confidence,
            brainStatus = brain.FocusStatus,
            brainProgress = brain.FocusProgress,
            roundKey = roundKey,
            planKey = roundKey + "|" + position + "|" + expect + "|" + action + "|" + buy + "|" + adapt + "|" + development.BiggestLeak,
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
<title>Sm0ki Phone Coach Pro</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
:root{
  color-scheme:dark;
  --bg:#090a0c;--panel:#111315;--panel2:#151719;--line:#292d31;
  --muted:#858d98;--text:#f5f7f9;--orange:#ff9c2c;--green:#7ce6a6;
  --red:#ef7b71;--blue:#82b9ef;
}
html,body{margin:0;width:100%;min-height:100%;background:var(--bg);color:var(--text);font-family:Inter,Segoe UI,Arial,sans-serif}
body{padding:env(safe-area-inset-top) 0 env(safe-area-inset-bottom);overflow-x:hidden}
button,select{font:inherit}
.app{min-height:100dvh;max-width:760px;margin:auto;padding:0 10px 74px}
.topbar{
  position:sticky;top:0;z-index:20;
  margin:0 -10px;padding:9px 12px 8px;
  background:rgba(9,10,12,.96);backdrop-filter:blur(16px);
  border-bottom:1px solid #1b1e21
}
.brandrow{display:flex;align-items:center;justify-content:space-between;gap:10px}
.brand{font-size:17px;font-weight:950;letter-spacing:-.03em;white-space:nowrap}
.brand span{color:var(--orange)}
.status{
  display:flex;align-items:center;gap:6px;min-width:78px;justify-content:center;
  font-size:8px;font-weight:950;letter-spacing:.1em;padding:5px 8px;
  border:1px solid #34403a;border-radius:999px;color:var(--green);background:#102219
}
.status.waiting{color:#e1b675;background:#2b2115;border-color:#514126}
.status.stale{color:var(--red);background:#2b1717;border-color:#5a2928}
.dot{width:6px;height:6px;border-radius:50%;background:currentColor}
.matchline{
  display:grid;grid-template-columns:1fr auto auto auto;gap:8px;align-items:center;
  margin-top:7px;color:#b7bec8;font-size:11px;font-weight:850
}
.map{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.chip{background:#121417;border:1px solid #292c30;border-radius:8px;padding:5px 7px;white-space:nowrap}
.chip.score{color:white}.chip.round{color:var(--orange)}
.screen{display:none}.screen.active{display:block}
.gameHead{display:grid;grid-template-columns:1fr auto;gap:8px;align-items:stretch;margin-top:9px}
.buy{
  background:linear-gradient(135deg,#20180f,#151515);
  border:1px solid #62421f;border-radius:13px;padding:10px 12px;min-width:0
}
.label{font-size:8px;font-weight:950;letter-spacing:.13em;color:#7f8791;text-transform:uppercase}
.buy .label{color:var(--orange)}
.buyText{margin-top:4px;font-size:13px;font-weight:850;line-height:1.24;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.gameToggle{
  width:86px;border:1px solid #31363b;border-radius:13px;background:#15181a;color:#d7dce2;
  font-size:9px;font-weight:900;line-height:1.15
}
.gameToggle.on{color:#07150d;background:#79e5a4;border-color:#79e5a4}
.focusStrip{
  margin-top:8px;padding:9px 11px;border-radius:12px;border:1px solid #34312a;
  background:#15130f;display:flex;gap:9px;align-items:flex-start
}
.focusStrip .focusTag{font-size:8px;font-weight:950;letter-spacing:.12em;color:var(--orange);padding-top:2px;white-space:nowrap}
.focusStrip .focusText{font-size:12px;line-height:1.25;font-weight:900;color:#edf0f3}
.plan{display:grid;gap:8px;margin-top:8px}
.planCard{border-radius:15px;border:1px solid var(--line);padding:12px 14px;background:var(--panel)}
.planCard.position{border-color:#6d4a22;background:linear-gradient(180deg,#211911,#141516)}
.planCard.expect{border-left:3px solid var(--orange)}
.planCard.action{background:#16191b;border-color:#34383d}
.planCard .label{margin-bottom:6px}
.planCard.position .label,.planCard.action .label{color:var(--orange)}
.planText{font-size:16px;line-height:1.25;font-weight:850}
.planCard.position .planText{font-size:22px;line-height:1.12;font-weight:950;letter-spacing:-.025em}
.planCard.action .planText{font-size:19px;line-height:1.17;font-weight:950}
.lockline{
  display:flex;justify-content:space-between;gap:10px;align-items:center;
  margin:8px 2px 0;color:#69717b;font-size:9px;font-weight:800
}
.locked{color:#91a0ad}
.roundPulse{animation:roundPulse .62s ease-out}
@keyframes roundPulse{
  0%{box-shadow:0 0 0 2px rgba(255,156,44,.85),0 0 28px rgba(255,156,44,.35)}
  100%{box-shadow:none}
}
.sectionTitle{margin:15px 2px 8px;font-size:10px;font-weight:950;letter-spacing:.11em;color:#8d96a2}
.metrics{display:grid;grid-template-columns:repeat(3,1fr);gap:7px}
.metric{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:10px}
.metric .v{margin-top:4px;font-size:18px;font-weight:950}
.devHero{
  margin-top:9px;padding:14px;border-radius:15px;border:1px solid #62421f;
  background:linear-gradient(145deg,#21180f,#121416)
}
.devScore{font-size:31px;font-weight:950;color:var(--orange);letter-spacing:-.04em}
.devLeak{font-size:16px;font-weight:900;margin-top:5px}
.devFocus{font-size:12px;color:#c1c7cf;line-height:1.35;margin-top:5px}
.brainCard{
  margin-top:9px;padding:13px;border-radius:15px;border:1px solid #34312a;background:#121313
}
.brainTop{display:flex;justify-content:space-between;gap:8px;align-items:center}
.brainBadge{font-size:8px;font-weight:950;letter-spacing:.1em;color:#ffbf75;border:1px solid #5a442b;border-radius:999px;padding:4px 7px}
.brainMistake{font-size:15px;font-weight:900;margin-top:7px}
.brainPriority{font-size:12px;line-height:1.34;color:#d2d7dd;margin-top:5px}
.brainMeta{font-size:9px;line-height:1.35;color:#747c86;margin-top:7px}
.rounds{display:grid;gap:6px}
.roundRow{
  display:grid;grid-template-columns:42px 35px 42px 1fr;gap:8px;align-items:center;
  background:var(--panel);border:1px solid var(--line);border-radius:11px;padding:8px 10px
}
.rno{font-weight:950}.win{color:var(--green);font-weight:950}.loss{color:var(--red);font-weight:950}
.rmeta{font-size:10px;color:#9ca4af;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.settingsCard{margin-top:10px;background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:13px}
.settingLine{
  display:flex;justify-content:space-between;align-items:center;gap:12px;
  padding:10px 0;border-bottom:1px solid #222529
}
.settingLine:last-child{border-bottom:0}
.settingText b{display:block;font-size:12px}.settingText small{display:block;color:#7d858f;font-size:9px;margin-top:2px}
.switch{
  min-width:58px;border:1px solid #34383d;background:#16191c;color:#9aa2ad;
  border-radius:999px;padding:7px 9px;font-size:8px;font-weight:950
}
.switch.on{background:#173522;border-color:#2f6846;color:#8ef0b4}
select{
  max-width:155px;background:#0e1012;color:white;border:1px solid #353a40;
  border-radius:9px;padding:8px;font-size:10px;font-weight:800
}
.save{
  width:100%;border:1px solid #62421f;background:#271b10;color:#ffc071;
  border-radius:10px;padding:10px;margin-top:10px;font-size:10px;font-weight:950
}
.reconnect{
  width:100%;border:1px solid #30343a;background:#15181b;color:#dbe0e6;
  border-radius:10px;padding:10px;margin-top:8px;font-size:10px;font-weight:900
}
.note{font-size:9px;color:#69717c;line-height:1.4;margin-top:10px}
.nav{
  position:fixed;left:50%;bottom:0;transform:translateX(-50%);z-index:30;
  width:min(760px,100%);display:grid;grid-template-columns:repeat(3,1fr);
  padding:6px 10px calc(6px + env(safe-area-inset-bottom));
  background:rgba(9,10,12,.97);backdrop-filter:blur(16px);border-top:1px solid #202327
}
.nav button{border:0;background:transparent;color:#747c87;padding:8px 4px;font-size:9px;font-weight:950;letter-spacing:.08em}
.nav button.active{color:var(--orange)}
.offlineBanner{
  display:none;margin-top:8px;padding:9px 11px;border-radius:10px;
  color:#ffc0b9;background:#301918;border:1px solid #5b2926;font-size:10px;font-weight:850
}
.offlineBanner.show{display:block}
.summary{
  display:none;margin-top:9px;border:1px solid #365d48;background:#10231a;border-radius:13px;padding:11px 12px
}
.summary.show{display:block}
.summary b{color:var(--green)}
@media(max-width:390px){
  .app{padding-left:8px;padding-right:8px}
  .topbar{margin-left:-8px;margin-right:-8px;padding-left:10px;padding-right:10px}
  .planCard.position .planText{font-size:20px}
  .planCard.action .planText{font-size:18px}
  .planText{font-size:15px}
  .metric .v{font-size:16px}
}
</style>
</head>
<body>
<div class="app">
  <header class="topbar">
    <div class="brandrow">
      <div class="brand">SM0KI <span>PHONE COACH</span></div>
      <div id="conn" class="status {{state.connection.ToLowerInvariant()}}">
        <span class="dot"></span><span id="connText">{{Html(state.connection)}}</span>
      </div>
    </div>
    <div class="matchline">
      <div id="map" class="map">{{Html(state.map)}}</div>
      <div id="side" class="chip">{{Html(state.side)}}</div>
      <div id="round" class="chip round">{{Html(state.round)}}</div>
      <div id="score" class="chip score">{{Html(state.score)}}</div>
    </div>
  </header>

  <div id="offlineBanner" class="offlineBanner">PC/CS2 telemetry ni svež. Coach čaka na povezavo.</div>

  <section id="coachScreen" class="screen active">
    <div class="gameHead">
      <div class="buy">
        <div class="label">BUY</div>
        <div id="buyAdvice" class="buyText">{{Html(state.buyAdvice)}}</div>
      </div>
      <button id="gameMode" class="gameToggle" type="button">GAME MODE<br>OFF</button>
    </div>

    <div id="matchSummary" class="summary">
      <div class="label">MATCH COMPLETE</div>
      <b id="summaryScore">{{Html(state.score)}}</b>
      <span id="summaryText"></span>
    </div>

    <div class="focusStrip">
      <div class="focusTag">FOCUS</div>
      <div id="brainFocus" class="focusText">{{Html(state.brainFocus)}}</div>
    </div>

    <div class="plan">
      <article id="positionCard" class="planCard position">
        <div class="label">POSITION</div>
        <div id="position" class="planText">{{Html(state.position)}}</div>
      </article>

      <article class="planCard expect">
        <div class="label">EXPECT</div>
        <div id="expect" class="planText">{{Html(state.expect)}}</div>
      </article>

      <article class="planCard action">
        <div class="label">DO</div>
        <div id="action" class="planText">{{Html(state.action)}}</div>
      </article>
    </div>

    <div class="lockline">
      <span id="planLock" class="locked">Plan refreshes between rounds</span>
      <span id="latency">— ms</span>
    </div>
  </section>

  <section id="progressScreen" class="screen">
    <div class="sectionTitle">MATCH SNAPSHOT</div>
    <div class="metrics">
      <div class="metric"><div class="label">K/D</div><div id="kd" class="v">{{Html(state.kd)}}</div></div>
      <div class="metric"><div class="label">K/R</div><div id="kpr" class="v">{{Html(state.killsPerRound)}}</div></div>
      <div class="metric"><div class="label">SURVIVAL</div><div id="survival" class="v">{{Html(state.survival)}}</div></div>
      <div class="metric"><div class="label">KILLS</div><div id="kills" class="v">{{state.kills}}</div></div>
      <div class="metric"><div class="label">MULTI</div><div id="multi" class="v">{{state.multiKillRounds}}</div></div>
      <div class="metric"><div class="label">0K</div><div id="zeroKill" class="v">{{state.zeroKillRounds}}</div></div>
    </div>

    <div class="devHero">
      <div class="label">DEVELOPMENT SCORE</div>
      <div><span id="devScore" class="devScore">{{state.devScore}}</span><span style="color:#767e88"> / 100</span></div>
      <div id="devLeak" class="devLeak">{{Html(state.devLeak)}}</div>
      <div id="devFocus" class="devFocus">{{Html(state.devFocus)}}</div>
    </div>

    <div class="sectionTitle">SMART MATCH BRAIN</div>
    <div class="brainCard">
      <div class="brainTop">
        <div class="label">DETECT → PRIORITIZE → FOCUS</div>
        <div id="brainBadge" class="brainBadge">{{Html(state.brainConfidence)}}</div>
      </div>
      <div id="brainMistake" class="brainMistake">{{Html(state.brainMistake)}}</div>
      <div id="brainPriority" class="brainPriority">{{Html(state.brainPriority)}}</div>
      <div id="brainMeta" class="brainMeta">{{Html(state.brainEvidence)}} • {{Html(state.brainStatus)}} • {{Html(state.brainProgress)}}</div>
    </div>

    <div class="sectionTitle">RECENT ROUNDS</div>
    <div id="rounds" class="rounds"></div>
  </section>

  <section id="settingsScreen" class="screen">
    <div class="sectionTitle">GAME MODE</div>
    <div class="settingsCard">
      <div class="settingLine">
        <div class="settingText"><b>Vibration</b><small>Short alert when a new round starts</small></div>
        <button id="vibrationToggle" class="switch" type="button">ON</button>
      </div>
      <div class="settingLine">
        <div class="settingText"><b>Sound cue</b><small>Quiet beep on new round</small></div>
        <button id="soundToggle" class="switch" type="button">OFF</button>
      </div>
      <div class="settingLine">
        <div class="settingText"><b>Keep screen awake</b><small>Best when phone sits next to your monitor</small></div>
        <button id="wakeToggle" class="switch" type="button">ON</button>
      </div>
      <div class="settingLine">
        <div class="settingText"><b>Fullscreen</b><small>Remove browser chrome where supported</small></div>
        <button id="fullscreenToggle" class="switch" type="button">OFF</button>
      </div>
    </div>

    <div class="sectionTitle">COACH STYLE</div>
    <div class="settingsCard">
      <div class="settingLine">
        <div class="settingText"><b>Mode</b></div>
        <select id="modeSelect">
          <option>Balanced</option><option>Aggressive</option><option>Safe</option>
        </select>
      </div>
      <div class="settingLine">
        <div class="settingText"><b>Role</b></div>
        <select id="roleSelect">
          <option>Flex</option><option>Entry</option><option>Lurk</option><option>Support</option><option>Anchor</option>
        </select>
      </div>
      <div class="settingLine">
        <div class="settingText"><b>Focus</b></div>
        <select id="focusSelect">
          <option>More kills</option><option>Survive & trade</option><option>Entry impact</option><option>Utility impact</option><option>Clutch / late round</option>
        </select>
      </div>
      <div class="settingLine">
        <div class="settingText"><b>Auto AI refine</b><small>Only allowed to refresh the visible plan between rounds</small></div>
        <button id="aiToggle" class="switch" type="button">ON</button>
      </div>
      <button id="saveCoach" class="save" type="button">SAVE TO PC COACH</button>
      <button id="reconnect" class="reconnect" type="button">REFRESH / RECONNECT</button>
      <div class="note">
        {{Html(state.nickname)}} • v{{Html(state.version)}} • private LAN link.<br>
        Telefon in PC morata biti na istem Wi‑Fi/LAN omrežju.
      </div>
    </div>
  </section>
</div>

<nav class="nav">
  <button data-view="coach" class="active">COACH</button>
  <button data-view="progress">PROGRESS</button>
  <button data-view="settings">SETTINGS</button>
</nav>

<script>
const liveToken={{JsonSerializer.Serialize(_accessToken)}};
let lastRoundKey={{JsonSerializer.Serialize(state.roundKey)}};
let lastPlanKey={{JsonSerializer.Serialize(state.planKey)}};
let lastRoundPhase={{JsonSerializer.Serialize(state.roundPhase)}};
let lastSuccess=Date.now();
let gameMode=false;
let wakeLock=null;
let forceRefreshPlan=true;
let latestState=null;

const prefs={
  vibration:localStorage.getItem('smoki_vibration')!=='0',
  sound:localStorage.getItem('smoki_sound')==='1',
  wake:localStorage.getItem('smoki_wake')!=='0',
  fullscreen:localStorage.getItem('smoki_fullscreen')==='1'
};

const $=id=>document.getElementById(id);
const setText=(id,v)=>{const e=$(id);if(e)e.textContent=(v??'—')};

function setSwitch(id,on){
  const b=$(id);if(!b)return;
  b.classList.toggle('on',!!on);
  b.textContent=on?'ON':'OFF';
}

function setView(view){
  document.querySelectorAll('.screen').forEach(x=>x.classList.remove('active'));
  document.querySelectorAll('.nav button').forEach(x=>x.classList.remove('active'));
  $(view+'Screen')?.classList.add('active');
  document.querySelector('.nav button[data-view="'+view+'"]')?.classList.add('active');
}

document.querySelectorAll('.nav button').forEach(b=>{
  b.addEventListener('click',()=>setView(b.dataset.view));
});

function isLivePhase(phase){
  return String(phase||'').toLowerCase()==='live';
}

function connectionUi(x){
  const conn=$('conn');
  if(!conn)return;
  conn.className='status '+String(x.connection||'waiting').toLowerCase();
  setText('connText',x.connection||'WAITING');
  $('offlineBanner')?.classList.toggle('show',x.connection==='STALE'||x.connection==='WAITING');
}

function applyPlan(x,animate){
  setText('position',x.position);
  setText('expect',x.expect);
  setText('action',x.action);
  setText('buyAdvice',x.buyAdvice);

  if(animate){
    const card=$('positionCard');
    card?.classList.remove('roundPulse');
    void card?.offsetWidth;
    card?.classList.add('roundPulse');
  }
}

function renderRounds(rounds){
  const host=$('rounds');
  if(!host)return;
  if(!rounds||!rounds.length){
    host.innerHTML='<div class="note">No completed rounds yet.</div>';
    return;
  }
  host.innerHTML=rounds.map(r=>{
    const resultClass=r.result==='W'?'win':r.result==='L'?'loss':'';
    const extra=[r.intent,r.site,r.position].filter(Boolean).join(' • ');
    return '<div class="roundRow">'+
      '<div class="rno">R'+r.round+'</div>'+
      '<div class="'+resultClass+'">'+r.result+'</div>'+
      '<div>'+r.kills+'K</div>'+
      '<div class="rmeta">'+escapeHtml(extra||r.side)+'</div>'+
      '</div>';
  }).join('');
}

function escapeHtml(s){
  return String(s??'').replace(/[&<>"']/g,c=>({
    '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'
  }[c]));
}

function updateSecondary(x){
  setText('kd',x.kd);
  setText('kpr',x.killsPerRound);
  setText('survival',x.survival);
  setText('kills',x.kills);
  setText('multi',x.multiKillRounds);
  setText('zeroKill',x.zeroKillRounds);
  setText('devScore',x.devScore);
  setText('devLeak',x.devLeak);
  setText('devFocus',x.devFocus);
  setText('brainFocus',x.brainFocus);
  setText('brainMistake',x.brainMistake);
  setText('brainPriority',x.brainPriority);
  setText('brainBadge',x.brainConfidence);
  setText('brainMeta',(x.brainEvidence||'')+' • '+(x.brainStatus||'')+' • '+(x.brainProgress||''));
  renderRounds(x.recentRounds);

  $('modeSelect').value=x.mode||'Balanced';
  $('roleSelect').value=x.role||'Flex';
  $('focusSelect').value=x.focus||'More kills';
  $('aiToggle').dataset.on=x.autoAi?'1':'0';
  setSwitch('aiToggle',x.autoAi);

  const over=String(x.mapPhase||'').toLowerCase()==='gameover';
  $('matchSummary')?.classList.toggle('show',over);
  if(over){
    setText('summaryScore',x.score);
    setText('summaryText',' • '+x.kills+' kills • '+x.kd+' K/D • DEV '+x.devScore);
  }
}

function beep(){
  if(!prefs.sound)return;
  try{
    const AudioContext=window.AudioContext||window.webkitAudioContext;
    const ctx=new AudioContext();
    const osc=ctx.createOscillator();
    const gain=ctx.createGain();
    osc.frequency.value=660;
    gain.gain.setValueAtTime(.045,ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(.001,ctx.currentTime+.12);
    osc.connect(gain);gain.connect(ctx.destination);
    osc.start();osc.stop(ctx.currentTime+.13);
  }catch{}
}

async function ensureWake(){
  if(!gameMode||!prefs.wake||!navigator.wakeLock)return;
  try{
    if(!wakeLock) wakeLock=await navigator.wakeLock.request('screen');
  }catch{}
}

async function setGameMode(on){
  gameMode=!!on;
  const b=$('gameMode');
  b?.classList.toggle('on',gameMode);
  if(b)b.innerHTML=gameMode?'GAME MODE<br>ON':'GAME MODE<br>OFF';

  if(gameMode){
    await ensureWake();
    if(prefs.fullscreen){
      try{await document.documentElement.requestFullscreen?.()}catch{}
    }
  }else{
    try{await wakeLock?.release()}catch{}
    wakeLock=null;
  }
}

async function refreshLive(){
  const started=performance.now();
  try{
    const response=await fetch('/api/state?token='+encodeURIComponent(liveToken)+'&_='+Date.now(),{cache:'no-store'});
    if(!response.ok)throw new Error('HTTP '+response.status);
    const x=await response.json();
    latestState=x;
    lastSuccess=Date.now();

    setText('latency',Math.round(performance.now()-started)+' ms');
    setText('map',x.map);
    setText('side',x.side);
    setText('round',x.round);
    setText('score',x.score);
    connectionUi(x);
    updateSecondary(x);

    const roundChanged=!!lastRoundKey && !!x.roundKey && x.roundKey!==lastRoundKey;
    const planChanged=!!lastPlanKey && !!x.planKey && x.planKey!==lastPlanKey;
    const betweenRounds=!isLivePhase(x.roundPhase);

    // Glance-first rule:
    // visible coaching text may refresh on a new round or while CS2 is not live.
    // Once the round is live, the player sees a stable plan.
    const mayRefreshPlan=forceRefreshPlan||roundChanged||betweenRounds;
    if(mayRefreshPlan && (forceRefreshPlan||roundChanged||planChanged)){
      applyPlan(x,roundChanged);
      forceRefreshPlan=false;
    }

    setText(
      'planLock',
      isLivePhase(x.roundPhase)
        ? '🔒 Plan locked during live round'
        : '↻ Plan can refresh between rounds'
    );

    if(roundChanged&&gameMode){
      if(prefs.vibration){
        try{navigator.vibrate?.([80,45,80])}catch{}
      }
      beep();
    }

    lastRoundKey=x.roundKey||lastRoundKey;
    lastPlanKey=x.planKey||lastPlanKey;
    lastRoundPhase=x.roundPhase||lastRoundPhase;
  }catch{
    if(Date.now()-lastSuccess>2500){
      const x=latestState||{connection:'STALE'};
      x.connection='STALE';
      connectionUi(x);
      setText('latency','OFFLINE');
    }
  }
}

$('gameMode')?.addEventListener('click',()=>setGameMode(!gameMode));

$('vibrationToggle')?.addEventListener('click',()=>{
  prefs.vibration=!prefs.vibration;
  localStorage.setItem('smoki_vibration',prefs.vibration?'1':'0');
  setSwitch('vibrationToggle',prefs.vibration);
});
$('soundToggle')?.addEventListener('click',()=>{
  prefs.sound=!prefs.sound;
  localStorage.setItem('smoki_sound',prefs.sound?'1':'0');
  setSwitch('soundToggle',prefs.sound);
  if(prefs.sound)beep();
});
$('wakeToggle')?.addEventListener('click',async()=>{
  prefs.wake=!prefs.wake;
  localStorage.setItem('smoki_wake',prefs.wake?'1':'0');
  setSwitch('wakeToggle',prefs.wake);
  if(prefs.wake)await ensureWake();
  else{try{await wakeLock?.release()}catch{}wakeLock=null;}
});
$('fullscreenToggle')?.addEventListener('click',()=>{
  prefs.fullscreen=!prefs.fullscreen;
  localStorage.setItem('smoki_fullscreen',prefs.fullscreen?'1':'0');
  setSwitch('fullscreenToggle',prefs.fullscreen);
});

$('aiToggle')?.addEventListener('click',()=>{
  const on=$('aiToggle').dataset.on!=='1';
  $('aiToggle').dataset.on=on?'1':'0';
  setSwitch('aiToggle',on);
});

$('saveCoach')?.addEventListener('click',async()=>{
  const q=new URLSearchParams({
    token:liveToken,
    mode:$('modeSelect').value,
    role:$('roleSelect').value,
    focus:$('focusSelect').value,
    autoAi:$('aiToggle').dataset.on==='1'?'1':'0'
  });
  try{
    const r=await fetch('/api/settings?'+q.toString(),{cache:'no-store'});
    if(r.ok){
      $('saveCoach').textContent='SAVED';
      forceRefreshPlan=true;
      setTimeout(()=>$('saveCoach').textContent='SAVE TO PC COACH',1100);
    }
  }catch{}
});

$('reconnect')?.addEventListener('click',()=>{
  forceRefreshPlan=true;
  refreshLive();
  setView('coach');
});

document.addEventListener('visibilitychange',async()=>{
  if(document.visibilityState==='visible'){
    wakeLock=null;
    await ensureWake();
    refreshLive();
  }
});

setSwitch('vibrationToggle',prefs.vibration);
setSwitch('soundToggle',prefs.sound);
setSwitch('wakeToggle',prefs.wake);
setSwitch('fullscreenToggle',prefs.fullscreen);

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
