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
        Width = 1200;
        Height = 900;
        MinimumSize = new Size(1040, 760);
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
            Padding = new Padding(24, 14, 24, 18),
            BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
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

    private static readonly Dictionary<string, string> MapArtworkUrls =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de_ancient"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_ancient.jpg",
            ["de_anubis"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_anubis.jpg",
            ["de_dust2"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_dust2.jpg",
            ["de_inferno"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_inferno.jpg",
            ["de_mirage"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_mirage.jpg",
            ["de_nuke"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_nuke.jpg",
            ["de_overpass"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_overpass.jpg",
            ["de_train"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_train.jpg",
            ["de_vertigo"] = "https://raw.githubusercontent.com/kus/cs2-modded-server/assets/images/de_vertigo.jpg"
        };

    private Control BuildHistory(IReadOnlyList<SessionSummary> all)
    {
        var card = MakeCard();
        card.Margin = new Padding(0, 6, 0, 6);
        card.Padding = new Padding(12, 8, 12, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Panel
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        header.Controls.Add(new Label
        {
            Text = "◷",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Symbol", 15, FontStyle.Bold),
            ForeColor = Orange,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);

        var headerText = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        headerText.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        headerText.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        headerText.Controls.Add(new Label
        {
            Text = "RECENT MATCHES",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        headerText.Controls.Add(new Label
        {
            Text = all.Count > 2
                ? $"Your latest analyzed matches • {Math.Min(all.Count, 50)} loaded • scroll for older matches"
                : "Your latest analyzed matches",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.6f),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        header.Controls.Add(headerText, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Panel
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));

        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Panel,
            TabStop = true
        };

        var content = new Panel
        {
            BackColor = Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var scrollbar = new SlimScrollBar
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 2, 1, 2),
            BackColor = Panel
        };

        viewport.Controls.Add(content);
        body.Controls.Add(viewport, 0, 0);
        body.Controls.Add(scrollbar, 1, 0);
        layout.Controls.Add(body, 0, 1);

        var recent = all.Take(50).ToList();
        foreach (var session in recent)
        {
            var match = BuildMatchCard(session);
            match.Dock = DockStyle.None;
            match.Height = 104;
            content.Controls.Add(match);
        }

        if (recent.Count == 0)
        {
            var empty = new RoundedPanel
            {
                BackColor = Panel2,
                BorderColor = Color.FromArgb(42, 45, 49),
                Radius = 13
            };
            empty.Controls.Add(new Label
            {
                Text = "No archived sessions yet. Finish a match and it will appear here.",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9),
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleCenter
            });
            content.Controls.Add(empty);
        }

        void ApplyOffset()
        {
            content.Top = -scrollbar.Value;
        }

        void LayoutHistory()
        {
            var width = Math.Max(320, viewport.ClientSize.Width - 2);
            var y = 2;

            foreach (Control control in content.Controls)
            {
                var rowHeight = recent.Count == 0 ? Math.Max(74, viewport.ClientSize.Height - 4) : 104;
                control.SetBounds(0, y, width, rowHeight);
                y += rowHeight + (recent.Count == 0 ? 0 : 8);
            }

            content.SetBounds(
                0,
                -scrollbar.Value,
                width,
                Math.Max(viewport.ClientSize.Height, y));

            scrollbar.SetMetrics(content.Height, viewport.ClientSize.Height);
            ApplyOffset();
        }

        void ScrollByWheel(MouseEventArgs e)
        {
            var steps = Math.Max(1, Math.Abs(e.Delta) / 120);
            var direction = e.Delta > 0 ? -1 : 1;
            scrollbar.ScrollBy(direction * 70 * steps);
        }

        void HookWheel(Control control)
        {
            control.MouseEnter += (_,__) =>
            {
                if (!viewport.IsDisposed && viewport.CanFocus)
                    viewport.Focus();
            };
            control.MouseWheel += (_, e) => ScrollByWheel(e);

            foreach (Control child in control.Controls)
                HookWheel(child);
        }

        HookWheel(viewport);
        HookWheel(content);
        foreach (Control child in content.Controls)
            HookWheel(child);

        viewport.MouseWheel += (_, e) => ScrollByWheel(e);
        scrollbar.ValueChanged += (_,__) => ApplyOffset();
        viewport.SizeChanged += (_,__) => LayoutHistory();
        content.ControlAdded += (_,__) => LayoutHistory();

        card.HandleCreated += (_,__) => LayoutHistory();
        card.SizeChanged += (_,__) => LayoutHistory();

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildMatchCard(SessionSummary session)
    {
        var row = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 17, 19),
            BorderColor = Color.FromArgb(44, 47, 51),
            HoverBorderColor = Color.FromArgb(131, 88, 41),
            Radius = 14,
            Cursor = Cursors.Hand,
            MinimumSize = new Size(0, 104)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(8, 5, 8, 5),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));

        var thumbHost = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(2),
            BackColor = Color.FromArgb(11, 13, 15),
            BorderColor = Color.FromArgb(53, 56, 60),
            Radius = 10
        };
        var picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(11, 13, 15),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        thumbHost.Controls.Add(picture);
        layout.Controls.Add(thumbHost, 0, 0);
        _ = LoadMapArtworkAsync(picture, session.Map);

        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(2, 0, 8, 0),
            BackColor = Color.Transparent
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 23));

        identity.Controls.Add(new Panel
        {
            Dock = DockStyle.Left,
            Width = 30,
            Height = 3,
            BackColor = Orange,
            Margin = new Padding(0, 1, 0, 0)
        }, 0, 0);
        identity.Controls.Add(new Label
        {
            Text = CoachEngine.PrettyMap(session.Map),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 1);
        identity.Controls.Add(new Label
        {
            Text = "Competitive",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.7f),
            ForeColor = Color.FromArgb(169, 177, 188),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 2);
        identity.Controls.Add(new Label
        {
            Text = session.EndedUtc.ToLocalTime().ToString("dd.MM • HH:mm"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(226, 229, 233),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);
        identity.Controls.Add(new Label
        {
            Text = RelativeAge(session.EndedUtc),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.1f),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 4);
        layout.Controls.Add(identity, 1, 0);

        var kdColor = session.Deaths == 0 || session.Kills >= session.Deaths ? Green : Red;
        var survivalColor = session.SurvivalRate >= 60 ? Green : Orange;
        var devColor = session.DevelopmentScore >= 70
            ? Green
            : session.DevelopmentScore > 0 ? Orange : Muted;

        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        for (int i = 0; i < 4; i++)
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        var statItems = new (string Title, string Value, Color Color)[]
        {
            ("K / D", $"{session.Kills}/{session.Deaths}", kdColor),
            ("K / R", session.KillsPerRound.ToString("0.00"), Color.White),
            ("SURVIVAL", session.SurvivalRate.ToString("0") + "%", survivalColor),
            ("SCORE", $"{session.CtScore}:{session.TScore}", Orange),
            ("ROUNDS", session.Rounds.ToString(), Color.White),
            ("MULTI", session.MultiKillRounds.ToString(), Color.FromArgb(105, 177, 238)),
            ("0K", session.ZeroKillRounds.ToString(), session.ZeroKillRounds > 0 ? Red : Green),
            ("DEV", session.DevelopmentScore > 0 ? session.DevelopmentScore.ToString() : "—", devColor)
        };

        for (int i = 0; i < statItems.Length; i++)
        {
            var item = statItems[i];
            stats.Controls.Add(
                MakeStatChip(item.Title, item.Value, item.Color),
                i % 4,
                i / 4);
        }
        layout.Controls.Add(stats, 2, 0);

        layout.Controls.Add(new Label
        {
            Text = "›",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 20),
            ForeColor = Color.FromArgb(205, 210, 216),
            TextAlign = ContentAlignment.MiddleCenter
        }, 3, 0);

        row.Controls.Add(layout);
        return row;
    }

    private Control MakeStatChip(string title, string value, Color valueColor)
    {
        var chip = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 2, 3, 2),
            BackColor = Color.FromArgb(20, 22, 25),
            BorderColor = Color.FromArgb(50, 53, 58),
            Radius = 8
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 0),
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 6.2f),
            ForeColor = Color.FromArgb(132, 140, 151),
            TextAlign = ContentAlignment.BottomCenter
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
            ForeColor = valueColor,
            TextAlign = ContentAlignment.TopCenter
        }, 0, 1);

        chip.Controls.Add(layout);
        return chip;
    }

    private async Task LoadMapArtworkAsync(PictureBox picture, string map)
    {
        try
        {
            var mapKey = NormalizeMapKey(map);
            if (!MapArtworkUrls.TryGetValue(mapKey, out var url))
            {
                SetPictureImage(picture, BuildMapFallback(map));
                return;
            }

            var bytes = await RadarImageCache.GetAsync(url);
            using var ms = new MemoryStream(bytes);
            using var source = Image.FromStream(ms);
            var image = new Bitmap(source);
            SetPictureImage(picture, image);
        }
        catch
        {
            SetPictureImage(picture, BuildMapFallback(map));
        }
    }

    private static void SetPictureImage(PictureBox picture, Image image)
    {
        if (picture.IsDisposed)
        {
            image.Dispose();
            return;
        }

        if (picture.InvokeRequired)
        {
            try
            {
                picture.BeginInvoke(new Action(() => SetPictureImage(picture, image)));
            }
            catch
            {
                image.Dispose();
            }
            return;
        }

        var old = picture.Image;
        picture.Image = image;
        old?.Dispose();
    }

    private static string NormalizeMapKey(string map)
    {
        var key = (map ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) return "";
        if (!key.StartsWith("de_", StringComparison.OrdinalIgnoreCase))
            key = "de_" + key.Replace(" ", "");
        return key;
    }

    private static Image BuildMapFallback(string map)
    {
        var bmp = new Bitmap(360, 160);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new LinearGradientBrush(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            Color.FromArgb(51, 39, 26),
            Color.FromArgb(13, 16, 19),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(bg, 0, 0, bmp.Width, bmp.Height);

        using var accent = new SolidBrush(Color.FromArgb(88, 255, 156, 44));
        g.FillEllipse(accent, bmp.Width - 118, -35, 160, 160);

        using var subtitleFont = new Font("Segoe UI", 8, FontStyle.Bold);
        using var titleFont = new Font("Segoe UI", 20, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.White);
        using var mutedBrush = new SolidBrush(Color.FromArgb(188, 196, 205));
        g.DrawString("CS2 MAP", subtitleFont, mutedBrush, new PointF(16, 38));
        g.DrawString(CoachEngine.PrettyMap(map), titleFont, titleBrush, new PointF(14, 62));
        return bmp;
    }

    private static string RelativeAge(DateTime endedUtc)
    {
        var age = DateTime.UtcNow - endedUtc.ToUniversalTime();
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;

        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalMinutes < 60) return $"~ {(int)age.TotalMinutes} min ago";
        if (age.TotalHours < 24) return $"~ {(int)age.TotalHours} h ago";
        return $"~ {(int)age.TotalDays} d ago";
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

    private sealed class SlimScrollBar : Control
    {
        private int _maximum;
        private int _value;
        private int _viewportSize;
        private int _contentSize;
        private bool _hovered;
        private bool _dragging;
        private int _dragOffset;

        public event EventHandler? ValueChanged;

        public int Value
        {
            get => _value;
            private set
            {
                var next = Math.Clamp(value, 0, _maximum);
                if (next == _value) return;
                _value = next;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public SlimScrollBar()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public void SetMetrics(int contentSize, int viewportSize)
        {
            _contentSize = Math.Max(0, contentSize);
            _viewportSize = Math.Max(0, viewportSize);
            _maximum = Math.Max(0, _contentSize - _viewportSize);

            if (_value > _maximum)
                _value = _maximum;

            Visible = _maximum > 0;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ScrollBy(int delta)
        {
            Value += delta;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging) return;
            _hovered = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var steps = Math.Max(1, Math.Abs(e.Delta) / 120);
            ScrollBy((e.Delta > 0 ? -1 : 1) * 70 * steps);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _maximum <= 0) return;

            var thumb = ThumbRect();
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragOffset = e.Y - thumb.Top;
                Capture = true;
                Invalidate();
                return;
            }

            JumpTo(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _maximum <= 0) return;

            var thumb = ThumbRect();
            var trackTop = 4;
            var trackHeight = Math.Max(1, Height - 8);
            var usable = Math.Max(1, trackHeight - thumb.Height);
            var y = Math.Clamp(e.Y - _dragOffset - trackTop, 0, usable);
            Value = (int)Math.Round((double)y / usable * _maximum);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;

            _dragging = false;
            Capture = false;
            Invalidate();
        }

        private void JumpTo(int mouseY)
        {
            var thumb = ThumbRect();
            var trackTop = 4;
            var trackHeight = Math.Max(1, Height - 8);
            var usable = Math.Max(1, trackHeight - thumb.Height);
            var y = Math.Clamp(mouseY - trackTop - thumb.Height / 2, 0, usable);
            Value = (int)Math.Round((double)y / usable * _maximum);
        }

        private Rectangle ThumbRect()
        {
            var trackTop = 4;
            var trackHeight = Math.Max(1, Height - 8);

            if (_maximum <= 0 || _contentSize <= 0)
                return new Rectangle(2, trackTop, Math.Max(4, Width - 4), trackHeight);

            var ratio = Math.Clamp((double)_viewportSize / _contentSize, 0.08, 1.0);
            var thumbHeight = Math.Clamp((int)Math.Round(trackHeight * ratio), 30, trackHeight);
            var usable = Math.Max(0, trackHeight - thumbHeight);
            var y = trackTop + (_maximum == 0
                ? 0
                : (int)Math.Round((double)_value / _maximum * usable));

            return new Rectangle(2, y, Math.Max(4, Width - 4), thumbHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_maximum <= 0) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new Rectangle(
                Math.Max(1, Width / 2 - 2),
                4,
                4,
                Math.Max(1, Height - 8));

            using var trackPath = RoundRect(track, 2);
            using var trackBrush = new SolidBrush(Color.FromArgb(40, 43, 47));
            e.Graphics.FillPath(trackBrush, trackPath);

            var thumb = ThumbRect();
            var thumbColor = _dragging
                ? Color.FromArgb(158, 164, 171)
                : _hovered
                    ? Color.FromArgb(126, 132, 140)
                    : Color.FromArgb(88, 94, 102);

            using var thumbPath = RoundRect(thumb, Math.Max(2, thumb.Width / 2));
            using var thumbBrush = new SolidBrush(thumbColor);
            e.Graphics.FillPath(thumbBrush, thumbPath);
        }

        private static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (rect.Width <= 1 || rect.Height <= 1)
            {
                path.AddRectangle(rect);
                return path;
            }

            var r = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            var d = r * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 12;
        public Color BorderColor { get; set; } = Color.FromArgb(48, 51, 55);
        public Color HoverBorderColor { get; set; } = Color.Empty;
        private bool _hovered;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            MouseEnter += (_,__) => { _hovered = true; Invalidate(); };
            MouseLeave += (_,__) => { _hovered = false; Invalidate(); };
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width <= 0 || Height <= 0) return;
            using var path = RoundRect(new Rectangle(0, 0, Width, Height), Radius);
            Region?.Dispose();
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var path = RoundRect(rect, Radius);
            using var fill = new SolidBrush(BackColor);
            var border = _hovered && HoverBorderColor != Color.Empty
                ? HoverBorderColor
                : BorderColor;
            using var pen = new Pen(border, _hovered ? 1.5f : 1f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(pen, path);
            base.OnPaint(e);
        }

        private static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var r = Math.Max(2, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            var d = r * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
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
