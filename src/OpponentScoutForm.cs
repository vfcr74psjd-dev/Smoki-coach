using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class OpponentScoutForm : Form
{
    private readonly string _currentMap;
    private readonly string _ownNickname;
    private readonly TextBox _match = new();
    private readonly Button _scan = new();
    private readonly Label _status = new();
    private readonly RichTextBox _report = new();

    public OpponentScoutForm(
        string currentMap,
        string ownNickname)
    {
        _currentMap = currentMap ?? "";
        _ownNickname = ownNickname ?? "";

        Text = "Sm0ki Tactical OS • Opponent Scout";
        Width = 760;
        Height = 720;
        MinimumSize = new Size(680, 600);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(10, 11, 13);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();
        LoadCached();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(new Label
        {
            Text = "OPPONENT SCOUT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 156, 44),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var input = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        input.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        input.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        input.Controls.Add(new Label
        {
            Text = "FACEIT MATCH ROOM URL / MATCH ID",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(135, 143, 154)
        }, 0, 0);
        input.SetColumnSpan(input.Controls[^1], 2);

        _match.Dock = DockStyle.Fill;
        _match.Margin = new Padding(0, 4, 8, 2);
        _match.BackColor = Color.FromArgb(20, 22, 25);
        _match.ForeColor = Color.White;
        _match.BorderStyle = BorderStyle.FixedSingle;
        input.Controls.Add(_match, 0, 1);

        _scan.Text = "SCAN";
        _scan.Dock = DockStyle.Fill;
        _scan.Margin = new Padding(0, 4, 0, 2);
        _scan.FlatStyle = FlatStyle.Flat;
        _scan.BackColor = Color.FromArgb(55, 38, 22);
        _scan.ForeColor = Color.FromArgb(255, 175, 86);
        _scan.FlatAppearance.BorderColor = Color.FromArgb(120, 76, 35);
        _scan.Click += async (_,__) => await ScanAsync();
        input.Controls.Add(_scan, 1, 1);

        root.Controls.Add(input, 0, 1);

        _status.Text =
            $"Current map: {CoachEngine.PrettyMap(_currentMap)} • paste the FACEIT match room.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(145, 153, 165);
        _status.Font = new Font("Segoe UI", 8.5f);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_status, 0, 2);

        _report.Dock = DockStyle.Fill;
        _report.ReadOnly = true;
        _report.BorderStyle = BorderStyle.FixedSingle;
        _report.BackColor = Color.FromArgb(15, 17, 20);
        _report.ForeColor = Color.FromArgb(225, 229, 234);
        _report.Font = new Font("Consolas", 10);
        root.Controls.Add(_report, 0, 3);

        var close = new Button
        {
            Text = "CLOSE",
            Width = 110,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 26, 29),
            ForeColor = Color.White
        };
        close.Click += (_,__) => Close();

        var bottom = new Panel { Dock = DockStyle.Fill };
        bottom.Controls.Add(close);
        root.Controls.Add(bottom, 0, 4);

        Controls.Add(root);
    }

    private async Task ScanAsync()
    {
        var raw = _match.Text.Trim();
        if (raw.Length < 3)
        {
            MessageBox.Show(
                "Prilepi FACEIT match-room URL ali match ID.",
                "Opponent Scout",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _scan.Enabled = false;
        _report.Text = "";
        _status.Text = "Starting scout…";

        var progress = new Progress<string>(
            x => _status.Text = x);

        try
        {
            var result = await new OpponentScoutService()
                .ScanMatchAsync(
                    raw,
                    _ownNickname,
                    _currentMap,
                    progress);

            Render(result, null);
            _status.Text =
                $"Stats ready • {result.Opponents.Count} opponents • checking old demos…";

            try
            {
                var demoIntel =
                    await new OpponentDemoIntelService()
                        .BuildAsync(
                            result,
                            3,
                            progress);

                Render(result, demoIntel);
                _status.Text =
                    $"Scout ready • stats {result.Confidence} • demos {demoIntel.Confidence}";
            }
            catch (Exception demoEx)
            {
                Render(result, null);
                _status.Text =
                    $"Scout ready • stats only • demos unavailable: {demoEx.Message}";
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Scout failed";
            MessageBox.Show(
                ex.Message,
                "Opponent Scout",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _scan.Enabled = true;
        }
    }

    private void LoadCached()
    {
        var cached =
            OpponentScoutStore.LoadLatest(_currentMap);

        if (cached != null)
            Render(
                cached,
                OpponentDemoIntelStore.LoadLatest(
                    cached.Map));
    }

    private void Render(
        OpponentScoutReport report,
        OpponentDemoIntelReport? demoIntel)
    {
        var lines = new List<string>
        {
            $"MAP       {CoachEngine.PrettyMap(report.Map)}",
            $"TEAM      avg ELO {report.TeamAverageElo:0}",
            $"SCOUT     {report.CompactSummary}",
            $"PREMADE   {report.PremadeSignal}",
            "",
            "OPPONENTS"
        };

        foreach (var p in report.Opponents)
        {
            var mapWr =
                p.MapWinRate is double wr
                    ? $"{wr:0}%"
                    : "—";

            var kd =
                p.MapKd ?? p.RecentKd;

            lines.Add(
                $"{p.Nickname,-18} ELO {p.Elo,4}  " +
                $"map {p.MapMatches,2}x / WR {mapWr,4}  " +
                $"KD {(kd?.ToString("0.00") ?? "—"),4}  " +
                $"threat {p.ThreatScore,3:0}");
        }

        lines.Add("");
        lines.Add("DEMO PATTERNS");

        if (demoIntel == null)
        {
            lines.Add("No usable opponent demo sample yet.");
        }
        else
        {
            lines.Add(
                $"Sample     {demoIntel.DemosAnalyzed} demos / {demoIntel.OpeningSamples} opening contacts");
            lines.Add(
                $"T opening  {FormatZone(demoIntel.TopTOpeningZone, demoIntel.TTopZoneShare)}  " +
                $"K/D {demoIntel.TOpeningKills}/{demoIntel.TOpeningDeaths}");
            lines.Add(
                $"CT opening {FormatZone(demoIntel.TopCtOpeningZone, demoIntel.CtTopZoneShare)}  " +
                $"K/D {demoIntel.CtOpeningKills}/{demoIntel.CtOpeningDeaths}");
            lines.Add(
                $"Opener     {(string.IsNullOrWhiteSpace(demoIntel.OpeningThreat) ? "—" : demoIntel.OpeningThreat)}");
            lines.Add(
                $"Confidence {demoIntel.Confidence}");
        }

        lines.Add("");
        lines.Add("PRE-MATCH COUNTER BRIEF");

        if (demoIntel == null)
        {
            lines.Add(
                "CT plan  • play standard setup until current-match evidence appears.");
            lines.Add(
                "T plan   • use spawn + Personal Playbook; no historical demo bias.");
        }
        else
        {
            var ctBrief =
                demoIntel.TTopZoneShare >= 0.55 &&
                demoIntel.TopTOpeningZone is "A" or "B" or "MID"
                    ? $"CT plan  • be rotate-ready toward {demoIntel.TopTOpeningZone}; " +
                      "hold info first, do not overrotate before contact."
                    : "CT plan  • no strong historical T opening bias; keep standard setup.";

            var tBrief =
                demoIntel.CtTopZoneShare >= 0.45 &&
                demoIntel.TopCtOpeningZone is "A" or "B" or "MID"
                    ? demoIntel.CtOpeningKills > demoIntel.CtOpeningDeaths
                        ? $"T plan   • avoid dry first contact into {demoIntel.TopCtOpeningZone}; " +
                          "use utility/trade or hit a different opening lane."
                        : $"T plan   • {demoIntel.TopCtOpeningZone} is a repeated CT contact area, " +
                          "but they lose many openings there; punish only with trade support."
                    : "T plan   • no strong historical CT opening bias; let spawn + playbook lead.";

            lines.Add(ctBrief);
            lines.Add(tBrief);

            if (!string.IsNullOrWhiteSpace(report.BiggestThreat))
            {
                lines.Add(
                    $"WATCH     • {report.BiggestThreat} is the top statistical threat; " +
                    "do not turn this into a solo-hunt.");
            }
        }

        lines.Add("");
        lines.Add(
            "Counter-Strat uses this only as weighted pre-match evidence. Current-match evidence takes priority after a few rounds.");

        _report.Text = string.Join(
            Environment.NewLine,
            lines);
    }

    private static string FormatZone(
        string zone,
        double share)
        => string.IsNullOrWhiteSpace(zone)
            ? "—"
            : $"{zone} {share * 100:0}%";
}
