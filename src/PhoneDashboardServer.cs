using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Sm0kiSoloCoach;

public sealed class PhoneDashboardServer : IDisposable
{
    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<GameSnapshot> _snapshotProvider;
    private readonly Func<string> _modeProvider;
    private readonly Func<string> _aiProvider;
    private Task? _loopTask;

    public int Port { get; private set; } = 31990;

    public PhoneDashboardServer(Func<GameSnapshot> snapshotProvider, Func<string> modeProvider, Func<string> aiProvider)
    {
        _snapshotProvider = snapshotProvider;
        _modeProvider = modeProvider;
        _aiProvider = aiProvider;
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
        return ip == null ? $"http://127.0.0.1:{Port}" : $"http://{ip}:{Port}";
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

                var s = _snapshotProvider();
                var mode = _modeProvider();

                string map = CoachEngine.PrettyMap(s.Map);
                string round = s.Round is int r ? $"R{r+1}" : "—";
                string score = $"{s.CtScore?.ToString() ?? "—"}:{s.TScore?.ToString() ?? "—"}";
                int k = s.Kills ?? 0;
                int d = s.Deaths ?? 0;
                string kd = d > 0 ? ((double)k/d).ToString("0.00") : k.ToString("0.00");
                var (buyTitle, buyAdvice) = CoachEngine.BuyAdvice(s);
                string tip = CoachEngine.SoloTip(s, mode);
                string aiTip = _aiProvider();

                string html = $@"<!doctype html>
<html lang='sl'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,viewport-fit=cover'>
<meta http-equiv='refresh' content='2'>
<title>Sm0ki Solo Coach</title>
<style>
*{{box-sizing:border-box}} body{{margin:0;background:#090d13;color:#f4f6fa;font-family:Segoe UI,Arial,sans-serif}}
.wrap{{max-width:780px;margin:auto;padding:20px 16px 28px}} h1{{font-size:25px;margin:4px 0 4px;letter-spacing:-.02em}}
h1:after{{content:'LIVE PERFORMANCE DASHBOARD';display:block;font-size:10px;letter-spacing:.14em;color:#737f93;margin-top:7px}}
.badge{{display:inline-block;padding:5px 9px;border-radius:7px;background:#183927;color:#8af0b8;font-weight:800;font-size:11px;vertical-align:middle}}
.grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:20px 0 12px}}
.card{{background:linear-gradient(180deg,#121923,#0f151e);border:1px solid #252e3d;border-radius:15px;padding:14px;box-shadow:0 8px 24px rgba(0,0,0,.18)}}
.label{{font-size:10px;color:#758197;letter-spacing:.12em;font-weight:800}} .value{{font-size:23px;font-weight:800;margin-top:6px;letter-spacing:-.02em}}
.section{{background:#101720;border:1px solid #252e3d;border-radius:16px;padding:18px;margin-top:11px;box-shadow:0 8px 24px rgba(0,0,0,.14)}}
.section h2{{font-size:11px;color:#7d899e;margin:0 0 10px;letter-spacing:.13em}}
.big{{font-size:22px;font-weight:800;margin-bottom:8px;color:#fff}} .text{{line-height:1.55;color:#cbd3df}}
.section:first-of-type{{border-left:3px solid #685cff}}
@media(max-width:600px){{.wrap{{padding:14px 12px 24px}}.grid{{grid-template-columns:repeat(2,1fr)}}.value{{font-size:21px}}}}
</style>
</head>
<body><div class='wrap'>
<h1>Sm0ki Solo Coach <span class='badge'>PHONE MODE</span></h1>
<div class='grid'>
<div class='card'><div class='label'>MAP</div><div class='value'>{Html(map)}</div></div>
<div class='card'><div class='label'>SIDE</div><div class='value'>{Html(s.Team)}</div></div>
<div class='card'><div class='label'>ROUND</div><div class='value'>{Html(round)}</div></div>
<div class='card'><div class='label'>SCORE</div><div class='value'>{Html(score)}</div></div>
<div class='card'><div class='label'>KILLS</div><div class='value'>{k}</div></div>
<div class='card'><div class='label'>DEATHS</div><div class='value'>{d}</div></div>
<div class='card'><div class='label'>K/D</div><div class='value'>{kd}</div></div>
<div class='card'><div class='label'>MONEY</div><div class='value'>${s.Money ?? 0}</div></div>
</div>
<div class='section'><h2>NEXT BUY</h2><div class='big'>{Html(buyTitle)}</div><div class='text'>{Html(buyAdvice)}</div></div>
<div class='section'><h2>AI ROUND COACH</h2><div class='text' style='white-space:pre-line'>{Html(aiTip)}</div></div>
<div class='section'><h2>SOLO AVG-KILLS COACH</h2><div class='text'>{Html(tip)}</div></div>
<div class='section'><h2>PHONE MODE</h2><div class='text'>Ta stran se osveži na 2 sekundi. PC in telefon morata biti na istem Wi‑Fi/LAN omrežju.</div></div>
</div></body></html>";

                byte[] body = Encoding.UTF8.GetBytes(html);
                string header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                                $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            catch { }
        }
    }

    private static string Html(string? s)
        => System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s) ? "—" : s);

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts.Dispose();
    }
}
