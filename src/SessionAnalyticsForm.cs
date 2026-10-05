using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class SessionAnalyticsForm : Form
{
    private static readonly Color Bg = Color.FromArgb(9, 11, 13);
    private static readonly Color Panel = Color.FromArgb(20, 22, 24);
    private static readonly Color Panel2 = Color.FromArgb(15, 17, 19);
    private static readonly Color Border = Color.FromArgb(57, 51, 44);
    private static readonly Color Muted = Color.FromArgb(145, 151, 159);
    private static readonly Color Orange = Color.FromArgb(255, 156, 44);
    private static readonly Color Green = Color.FromArgb(112, 214, 151);
    private static readonly Color Red = Color.FromArgb(232, 108, 96);

    public SessionAnalyticsForm(
        string nickname,
        string currentMap,
        IReadOnlyList<RoundRecord> currentRounds,
        FaceitSnapshot? faceit = null)
    {
        Text = "Sm0ki Solo Coach • Performance";
        Width = 1180;
        Height = 800;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        DoubleBuffered = true;

        var all = SessionHistoryStore.Load()
            .Where(x => x.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.EndedUtc)
            .ToList();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(24, 18, 24, 20),
            BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(BuildHeader(nickname, currentMap, currentRounds, faceit), 0, 0);
        root.Controls.Add(BuildKpis(currentRounds, all, faceit), 0, 1);
        root.Controls.Add(BuildCharts(currentRounds, all), 0, 2);
        root.Controls.Add(BuildImpactRow(all), 0, 3);
        root.Controls.Add(BuildHistory(all), 0, 4);

        var close = new Button
        {
            Text = "CLOSE",
            Width = 110,
            Height = 34,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(29, 31, 33),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        close.FlatAppearance.BorderSize = 1;
        close.FlatAppearance.BorderColor = Border;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 38, 31);
        close.Click += (_,__) => Close();
        root.Controls.Add(close, 0, 5);

        Controls.Add(root);
    }

    private Control BuildHeader(
        string nickname,
        string currentMap,
        IReadOnlyList<RoundRecord> currentRounds,
        FaceitSnapshot? faceit)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var title = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        title.Controls.Add(new Label
        {
            Text = "PERFORMANCE CENTER",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        title.Controls.Add(new Label
        {
            Text = currentRounds.Count > 0
                ? $"{nickname}  •  LIVE: {CoachEngine.PrettyMap(currentMap)}  •  {currentRounds.Count} tracked rounds"
                : $"{nickname}  •  waiting for live round data",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        header.Controls.Add(title, 0, 0);

        header.Controls.Add(new Label
        {
            Text = faceit == null
                ? "FACEIT  •  OFFLINE"
                : $"FACEIT  •  LVL {faceit.SkillLevel}  •  {faceit.Elo} ELO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = faceit == null ? Muted : Orange
        }, 1, 0);

        return header;
    }

    private Control BuildKpis(
        IReadOnlyList<RoundRecord> currentRounds,
        IReadOnlyList<SessionSummary> all,
        FaceitSnapshot? faceit)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty
        };
        for (int i = 0; i < 5; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        int liveKills = currentRounds.Sum(r => r.KillsRound);
        int liveDeaths = currentRounds.Sum(r => r.DeathsRound);
        double liveKr = currentRounds.Count > 0 ? (double)liveKills / currentRounds.Count : 0;
        double liveSurvival = currentRounds.Count > 0
            ? 100.0 * currentRounds.Count(r => r.DeathsRound == 0) / currentRounds.Count
            : 0;

        int histRounds = all.Sum(x => x.Rounds);
        int histKills = all.Sum(x => x.Kills);
        double histKr = histRounds > 0 ? (double)histKills / histRounds : 0;
        double histSurvival = histRounds > 0 ? 100.0 * all.Sum(x => x.SurvivalRounds) / histRounds : 0;

        var liveDevelopment = PlayerDevelopmentEngine.Analyze(currentRounds);
        var latestDevelopment = all.FirstOrDefault(x => x.DevelopmentScore > 0);
        var developmentScore = currentRounds.Count >= 3
            ? liveDevelopment.OverallScore
            : latestDevelopment?.DevelopmentScore ?? 0;
        var developmentSubtitle = currentRounds.Count >= 3
            ? liveDevelopment.BiggestLeak
            : latestDevelopment?.DevelopmentLeak ?? "needs v4.2 match data";

        grid.Controls.Add(Metric("KILLS", currentRounds.Count > 0 ? liveKills.ToString() : all.Sum(x => x.Kills).ToString(), "current / archived"), 0, 0);
        grid.Controls.Add(Metric("K / ROUND", (currentRounds.Count > 0 ? liveKr : histKr).ToString("0.00"), "fragging pace"), 1, 0);
        grid.Controls.Add(Metric("SURVIVAL", (currentRounds.Count > 0 ? liveSurvival : histSurvival).ToString("0") + "%", "rounds survived"), 2, 0);
        grid.Controls.Add(Metric("AVG K/D", faceit?.RecentAverageKd?.ToString("0.00") ?? (liveDeaths > 0 ? ((double)liveKills/liveDeaths).ToString("0.00") : "—"), "recent form"), 3, 0);
        grid.Controls.Add(Metric("DEV SCORE", developmentScore > 0 ? developmentScore + "/100" : "—", developmentSubtitle), 4, 0);
        return grid;
    }

    private Control BuildCharts(IReadOnlyList<RoundRecord> currentRounds, IReadOnlyList<SessionSummary> all)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        double[] kills;
        string[] killLabels;
        if (currentRounds.Count >= 3)
        {
            kills = currentRounds.TakeLast(16).Select(r => (double)r.KillsRound).ToArray();
            killLabels = currentRounds.TakeLast(16).Select(r => "R" + r.Round).ToArray();
        }
        else
        {
            kills = all.Take(12).Reverse().Select(x => (double)x.Kills).ToArray();
            killLabels = all.Take(12).Reverse().Select(x => CoachEngine.PrettyMap(x.Map)).ToArray();
        }

        var killsCard = ChartCard(
            "KILL OUTPUT",
            currentRounds.Count >= 3 ? "Kills per round • current session" : "Kills per match • recent archived sessions",
            kills,
            killLabels,
            Orange,
            false);
        killsCard.Margin = new Padding(0, 0, 6, 0);
        grid.Controls.Add(killsCard, 0, 0);

        var developmentForm = all
            .Where(x => x.DevelopmentScore > 0)
            .Take(12)
            .Reverse()
            .ToList();
        var developmentScores = developmentForm
            .Select(x => (double)x.DevelopmentScore)
            .ToArray();
        var developmentLabels = developmentForm
            .Select(x => CoachEngine.PrettyMap(x.Map))
            .ToArray();
        var developmentCard = ChartCard(
            "DEVELOPMENT TREND",
            "Player Development score across recent v4.2 sessions",
            developmentScores,
            developmentLabels,
            Green,
            false);
        developmentCard.Margin = new Padding(6, 0, 0, 0);
        grid.Controls.Add(developmentCard, 1, 0);

        return grid;
    }

    private Control BuildImpactRow(IReadOnlyList<SessionSummary> all)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));

        int rounds = all.Sum(x => x.Rounds);
        double multiRate = rounds > 0 ? 100.0 * all.Sum(x => x.MultiKillRounds) / rounds : 0;
        double zeroRate = rounds > 0 ? 100.0 * all.Sum(x => x.ZeroKillRounds) / rounds : 0;
        double survival = rounds > 0 ? 100.0 * all.Sum(x => x.SurvivalRounds) / rounds : 0;

        grid.Controls.Add(ImpactCard("MULTI-KILL IMPACT", multiRate, "2+ kill rounds", Green), 0, 0);
        grid.Controls.Add(ImpactCard("ZERO-KILL RATE", zeroRate, "rounds without a frag", Red), 1, 0);

        var utility = MakeCard();
        utility.Margin = new Padding(6, 4, 0, 4);
        utility.Padding = new Padding(14, 12, 14, 10);
        var utilLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        utilLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        utilLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        utilLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        utilLayout.Controls.Add(new Label
        {
            Text = "UTILITY IMPACT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Orange
        }, 0, 0);
        utilLayout.Controls.Add(new Label
        {
            Text = "DEMO DATA",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        utilLayout.Controls.Add(new Label
        {
            Text = "HE / flash / molotov impact will appear here once demo utility events are stored.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8),
            ForeColor = Muted,
            AutoEllipsis = true
        }, 0, 2);
        utility.Controls.Add(utilLayout);
        grid.Controls.Add(utility, 2, 0);

        return grid;
    }

    private Control BuildHistory(IReadOnlyList<SessionSummary> all)
    {
        var card = MakeCard();
        card.Margin = new Padding(0, 8, 0, 8);
        card.Padding = new Padding(12, 8, 12, 8);

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = Panel,
            ForeColor = Color.FromArgb(224, 227, 232),
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9)
        };
        list.Columns.Add("DATE", 120);
        list.Columns.Add("MAP", 125);
        list.Columns.Add("ROUNDS", 70);
        list.Columns.Add("K / D", 80);
        list.Columns.Add("K / R", 70);
        list.Columns.Add("SURVIVAL", 85);
        list.Columns.Add("MULTI", 65);
        list.Columns.Add("0K", 55);
        list.Columns.Add("DEV", 60);
        list.Columns.Add("SCORE", 70);

        foreach (var session in all.Take(50))
        {
            var item = new ListViewItem(session.EndedUtc.ToLocalTime().ToString("dd.MM HH:mm"));
            item.SubItems.Add(CoachEngine.PrettyMap(session.Map));
            item.SubItems.Add(session.Rounds.ToString());
            item.SubItems.Add($"{session.Kills}/{session.Deaths}");
            item.SubItems.Add(session.KillsPerRound.ToString("0.00"));
            item.SubItems.Add(session.SurvivalRate.ToString("0") + "%");
            item.SubItems.Add(session.MultiKillRounds.ToString());
            item.SubItems.Add(session.ZeroKillRounds.ToString());
            item.SubItems.Add(session.DevelopmentScore > 0 ? session.DevelopmentScore.ToString() : "—");
            item.SubItems.Add($"{session.CtScore}:{session.TScore}");
            list.Items.Add(item);
        }

        if (all.Count == 0)
        {
            list.Items.Add(new ListViewItem("No archived sessions yet."));
        }

        card.Controls.Add(list);
        return card;
    }

    private Control Metric(string title, string value, string subtitle)
    {
        var card = MakeCard();
        card.Margin = new Padding(4);
        card.Padding = new Padding(12, 8, 12, 7);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Orange
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Muted,
            AutoEllipsis = true
        }, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private Control ImpactCard(string title, double value, string subtitle, Color accent)
    {
        var card = MakeCard();
        card.Margin = new Padding(0, 4, 6, 4);
        card.Padding = new Padding(14, 12, 14, 10);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = accent
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = value.ToString("0") + "%",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        var meter = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp((int)Math.Round(value), 0, 100),
            Style = ProgressBarStyle.Continuous
        };
        layout.Controls.Add(meter, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8),
            ForeColor = Muted,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private Control ChartCard(
        string title,
        string subtitle,
        double[] values,
        string[] labels,
        Color accent,
        bool decimalValues)
    {
        var card = MakeCard();
        card.Padding = new Padding(14, 10, 14, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = accent
        }, 0, 0);

        var chart = new PerformanceChart
        {
            Dock = DockStyle.Fill,
            Values = values,
            Labels = labels,
            Accent = accent,
            DecimalValues = decimalValues,
            BackColor = Panel
        };
        layout.Controls.Add(chart, 0, 1);

        layout.Controls.Add(new Label
        {
            Text = values.Length == 0 ? "Not enough tracked data yet." : subtitle,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Muted,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Panel MakeCard()
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Panel,
            Margin = new Padding(4)
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, p.Width - 1), Math.Max(0, p.Height - 1));
        };
        return p;
    }

    private sealed class PerformanceChart : Control
    {
        public double[] Values { get; set; } = Array.Empty<double>();
        public string[] Labels { get; set; } = Array.Empty<string>();
        public Color Accent { get; set; } = Orange;
        public bool DecimalValues { get; set; }

        public PerformanceChart()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            if (rect.Width < 80 || rect.Height < 60) return;

            const int left = 28;
            const int right = 12;
            const int top = 10;
            const int bottom = 30;
            var plot = new Rectangle(left, top, rect.Width - left - right, rect.Height - top - bottom);

            using var gridPen = new Pen(Color.FromArgb(42, 45, 48), 1);
            for (int i = 0; i <= 3; i++)
            {
                int y = plot.Top + (int)(plot.Height * i / 3.0);
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            }

            if (Values.Length == 0)
            {
                using var emptyBrush = new SolidBrush(Muted);
                using var emptyFont = new Font("Segoe UI", 9);
                g.DrawString("Waiting for data", emptyFont, emptyBrush, plot.Left + 8, plot.Top + 16);
                return;
            }

            var max = Math.Max(1.0, Values.Max());
            var min = Math.Min(0.0, Values.Min());
            var range = Math.Max(0.001, max - min);
            int count = Values.Length;
            var points = new PointF[count];

            for (int i = 0; i < count; i++)
            {
                float x = count == 1
                    ? plot.Left + plot.Width / 2f
                    : plot.Left + (float)i / (count - 1) * plot.Width;
                float y = plot.Bottom - (float)((Values[i] - min) / range) * plot.Height;
                points[i] = new PointF(x, y);
            }

            if (points.Length >= 2)
            {
                using var linePen = new Pen(Accent, 2.4f);
                g.DrawLines(linePen, points);
            }

            using var dotBrush = new SolidBrush(Accent);
            using var textBrush = new SolidBrush(Color.FromArgb(205, 210, 216));
            using var labelBrush = new SolidBrush(Muted);
            using var valueFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var labelFont = new Font("Segoe UI", 7);

            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                g.FillEllipse(dotBrush, p.X - 3.5f, p.Y - 3.5f, 7, 7);

                if (points.Length <= 12 || i == points.Length - 1)
                {
                    var value = DecimalValues ? Values[i].ToString("0.00") : Values[i].ToString("0");
                    g.DrawString(value, valueFont, textBrush, p.X - 9, Math.Max(plot.Top, p.Y - 20));
                }
            }

            if (Labels.Length > 0)
            {
                int step = Math.Max(1, (int)Math.Ceiling(Labels.Length / 5.0));
                for (int i = 0; i < Labels.Length && i < points.Length; i += step)
                {
                    var text = Labels[i].Length > 10 ? Labels[i][..10] : Labels[i];
                    g.DrawString(text, labelFont, labelBrush, points[i].X - 15, plot.Bottom + 6);
                }
            }
        }
    }
}
