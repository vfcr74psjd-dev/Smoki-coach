using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class TacticalReplayForm : Form
{
    private readonly string _preferredMatchId;
    private readonly ComboBox _match = new();
    private readonly ComboBox _round = new();
    private readonly Label _plan = new();
    private readonly Label _actual = new();
    private readonly Label _verdict = new();
    private readonly ReplayCanvas _canvas = new();

    private IReadOnlyList<GroundTruthMatchReport> _reports =
        Array.Empty<GroundTruthMatchReport>();

    public TacticalReplayForm(
        string? preferredMatchId = null)
    {
        _preferredMatchId =
            preferredMatchId ?? "";

        Text =
            "Sm0ki Coach 7.0 • Tactical Replay";
        Width = 1280;
        Height = 820;
        MinimumSize =
            new Size(1000, 680);
        StartPosition =
            FormStartPosition.CenterParent;
        BackColor =
            Color.FromArgb(7, 9, 11);
        ForeColor =
            Color.White;
        Font =
            new Font("Segoe UI", 10);

        BuildUi();
        LoadReports();

        FormClosed += (_,__) =>
            _canvas.DisposeRadar();
    }

    private void BuildUi()
    {
        var root =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding =
                    new Padding(
                        22,
                        18,
                        22,
                        20),
                BackColor =
                    BackColor
            };

        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                64));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                58));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                42));

        root.Controls.Add(new Label
        {
            Text =
                "TACTICAL REPLAY  //  ACTUAL PATH",
            Dock = DockStyle.Fill,
            Font =
                new Font(
                    "Segoe UI",
                    22,
                    FontStyle.Bold),
            ForeColor =
                Color.White,
            TextAlign =
                ContentAlignment.MiddleLeft
        }, 0, 0);

        var filters =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                Margin = Padding.Empty
            };

        filters.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                64));
        filters.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                68));
        filters.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                70));
        filters.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                32));

        filters.Controls.Add(
            MakeLabel("MATCH"),
            0,
            0);

        ConfigureCombo(_match);
        _match.SelectedIndexChanged +=
            async (_,__) =>
                await MatchChangedAsync();

        filters.Controls.Add(
            _match,
            1,
            0);

        filters.Controls.Add(
            MakeLabel("ROUND"),
            2,
            0);

        ConfigureCombo(_round);
        _round.SelectedIndexChanged +=
            async (_,__) =>
                await RoundChangedAsync();

        filters.Controls.Add(
            _round,
            3,
            0);

        root.Controls.Add(
            filters,
            0,
            1);

        var body =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };

        body.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                72));
        body.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                28));

        var mapCard =
            new ModernCardPanel
            {
                Dock = DockStyle.Fill,
                Margin =
                    new Padding(
                        0,
                        4,
                        8,
                        4),
                Padding =
                    new Padding(12),
                BackColor =
                    Color.FromArgb(
                        13,
                        16,
                        20),
                GradientEndColor =
                    Color.FromArgb(
                        9,
                        11,
                        14),
                BorderColor =
                    Color.FromArgb(
                        39,
                        45,
                        52),
                Radius = 18
            };

        _canvas.Dock =
            DockStyle.Fill;

        mapCard.Controls.Add(
            _canvas);

        body.Controls.Add(
            mapCard,
            0,
            0);

        var intel =
            new ModernCardPanel
            {
                Dock = DockStyle.Fill,
                Margin =
                    new Padding(
                        8,
                        4,
                        0,
                        4),
                Padding =
                    new Padding(
                        16,
                        14,
                        16,
                        14),
                BackColor =
                    Color.FromArgb(
                        16,
                        18,
                        22),
                BorderColor =
                    Color.FromArgb(
                        43,
                        49,
                        56),
                AccentColor =
                    Color.FromArgb(
                        255,
                        156,
                        44),
                AccentWidth = 3,
                Radius = 18
            };

        var intelLayout =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Margin = Padding.Empty
            };

        intelLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                30));
        intelLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                120));
        intelLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                26));
        intelLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                150));
        intelLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                26));
        intelLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));

        intelLayout.Controls.Add(new Label
        {
            Text = "COACH PLAN",
            Dock = DockStyle.Fill,
            Font =
                new Font(
                    "Segoe UI",
                    8,
                    FontStyle.Bold),
            ForeColor =
                Color.FromArgb(
                    255,
                    156,
                    44)
        }, 0, 0);

        _plan.Dock = DockStyle.Fill;
        _plan.ForeColor =
            Color.FromArgb(
                235,
                239,
                244);
        _plan.Font =
            new Font(
                "Segoe UI",
                10,
                FontStyle.Bold);
        _plan.TextAlign =
            ContentAlignment.TopLeft;
        intelLayout.Controls.Add(
            _plan,
            0,
            1);

        intelLayout.Controls.Add(new Label
        {
            Text = "ACTUAL ROUTE",
            Dock = DockStyle.Fill,
            Font =
                new Font(
                    "Segoe UI",
                    8,
                    FontStyle.Bold),
            ForeColor =
                Color.FromArgb(
                    115,
                    126,
                    140)
        }, 0, 2);

        _actual.Dock =
            DockStyle.Fill;
        _actual.ForeColor =
            Color.FromArgb(
                205,
                212,
                221);
        _actual.Font =
            new Font(
                "Segoe UI",
                9,
                FontStyle.Bold);
        _actual.TextAlign =
            ContentAlignment.TopLeft;
        intelLayout.Controls.Add(
            _actual,
            0,
            3);

        intelLayout.Controls.Add(new Label
        {
            Text = "GROUND TRUTH",
            Dock = DockStyle.Fill,
            Font =
                new Font(
                    "Segoe UI",
                    8,
                    FontStyle.Bold),
            ForeColor =
                Color.FromArgb(
                    115,
                    126,
                    140)
        }, 0, 4);

        _verdict.Dock =
            DockStyle.Fill;
        _verdict.ForeColor =
            Color.FromArgb(
                255,
                184,
                98);
        _verdict.Font =
            new Font(
                "Segoe UI",
                10,
                FontStyle.Bold);
        _verdict.TextAlign =
            ContentAlignment.TopLeft;
        intelLayout.Controls.Add(
            _verdict,
            0,
            5);

        intel.Controls.Add(
            intelLayout);

        body.Controls.Add(
            intel,
            1,
            0);

        root.Controls.Add(
            body,
            0,
            2);

        var close =
            new Button
            {
                Text = "CLOSE",
                Width = 110,
                Height = 34,
                Anchor =
                    AnchorStyles.Right,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        22,
                        25,
                        29),
                ForeColor =
                    Color.White
            };

        close.FlatAppearance.BorderColor =
            Color.FromArgb(
                53,
                59,
                67);

        close.Click +=
            (_,__) => Close();

        root.Controls.Add(
            close,
            0,
            3);

        Controls.Add(root);
    }

    private void LoadReports()
    {
        _reports =
            GroundTruthStore.Load()
                .Where(x =>
                    x.Rounds.Any(r =>
                        r.Path.Count > 0))
                .ToList();

        _match.Items.Clear();

        foreach (var report in _reports)
        {
            _match.Items.Add(
                $"{report.MatchFinishedUtc.ToLocalTime():dd.MM HH:mm} • " +
                $"{CoachEngine.PrettyMap(report.Map)} • " +
                $"{report.MatchedRounds}R");
        }

        if (_reports.Count == 0)
        {
            _plan.Text =
                "No Ground Truth path yet.";
            _actual.Text =
                "Automatic demo learning will populate Tactical Replay.";
            return;
        }

        var preferred =
            _reports
                .Select((x,i) =>
                    new { x, i })
                .FirstOrDefault(x =>
                    x.x.MatchId.Equals(
                        _preferredMatchId,
                        StringComparison.OrdinalIgnoreCase))
                ?.i ?? 0;

        _match.SelectedIndex =
            preferred;
    }

    private async Task MatchChangedAsync()
    {
        var report =
            SelectedReport();

        if (report == null)
            return;

        _round.Items.Clear();

        foreach (var round in report.Rounds
                     .Where(x =>
                         x.Path.Count > 0)
                     .OrderBy(x =>
                         x.DemoRound))
        {
            _round.Items.Add(
                $"R{round.DemoRound} • {round.Side} • {round.Verdict}");
        }

        if (_round.Items.Count > 0)
            _round.SelectedIndex = 0;

        await _canvas.LoadRadarAsync(
            report.Map,
            CancellationToken.None);
    }

    private async Task RoundChangedAsync()
    {
        var report =
            SelectedReport();

        if (report == null)
            return;

        var rounds =
            report.Rounds
                .Where(x =>
                    x.Path.Count > 0)
                .OrderBy(x =>
                    x.DemoRound)
                .ToList();

        var index =
            _round.SelectedIndex;

        if (index < 0 ||
            index >= rounds.Count)
            return;

        var round =
            rounds[index];

        _plan.Text =
            $"R{round.DemoRound} • {round.Side}\n\n" +
            $"ROUTE\n{round.RecommendedRoute}\n\n" +
            $"FIRST\n{round.FirstMove}";

        _actual.Text =
            round.ActualRoute +
            "\n\n" +
            $"First contact: " +
            $"{(string.IsNullOrWhiteSpace(round.FirstContactPlace) ? "—" : round.FirstContactPlace)}\n" +
            $"Death: " +
            $"{(string.IsNullOrWhiteSpace(round.DeathPlace) ? "—" : round.DeathPlace)}";

        _verdict.Text =
            $"{round.RouteEvidence} • {round.Confidence}\n" +
            $"{round.Verdict}\n\n" +
            $"{round.Kills}K/{round.Deaths}D • " +
            $"{(round.Won == true ? "ROUND WON" : round.Won == false ? "ROUND LOST" : "RESULT —")} • " +
            $"outcome {round.OutcomeScore * 100:0}/100";

        _canvas.SetRound(
            report.Map,
            round);

        await Task.CompletedTask;
    }

    private GroundTruthMatchReport? SelectedReport()
    {
        var index =
            _match.SelectedIndex;

        return index >= 0 &&
               index < _reports.Count
            ? _reports[index]
            : null;
    }

    private static Label MakeLabel(
        string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor =
                Color.FromArgb(
                    104,
                    115,
                    129),
            Font =
                new Font(
                    "Segoe UI",
                    7.5f,
                    FontStyle.Bold),
            TextAlign =
                ContentAlignment.MiddleLeft
        };

    private static void ConfigureCombo(
        ComboBox box)
    {
        box.Dock = DockStyle.Fill;
        box.DropDownStyle =
            ComboBoxStyle.DropDownList;
        box.FlatStyle =
            FlatStyle.Flat;
        box.BackColor =
            Color.FromArgb(
                20,
                23,
                28);
        box.ForeColor =
            Color.White;
        box.Font =
            new Font(
                "Segoe UI",
                8.6f,
                FontStyle.Bold);
        box.Margin =
            new Padding(
                0,
                8,
                10,
                8);
    }

    private sealed class ReplayCanvas : Panel
    {
        private Image? _radar;
        private string _radarUrl = "";
        private string _map = "";
        private GroundTruthRoundVerdict? _round;
        private RadarDefinition? _definition;

        public ReplayCanvas()
        {
            DoubleBuffered = true;
            BackColor =
                Color.FromArgb(
                    8,
                    11,
                    16);
        }

        public void SetRound(
            string map,
            GroundTruthRoundVerdict round)
        {
            _map = map;
            _round = round;
            _definition =
                RadarCatalog.Get(map);
            Invalidate();
        }

        public async Task LoadRadarAsync(
            string map,
            CancellationToken ct)
        {
            _definition =
                RadarCatalog.Get(map);

            if (_definition == null)
            {
                DisposeRadar();
                Invalidate();
                return;
            }

            var path =
                _round?.Path ??
                new List<GroundTruthPathSample>();

            var lowerCount =
                _definition.LowerBelowZ.HasValue
                    ? path.Count(x =>
                        x.Z <
                        _definition.LowerBelowZ.Value)
                    : 0;

            var useLower =
                path.Count > 0 &&
                lowerCount >
                path.Count / 2 &&
                !string.IsNullOrWhiteSpace(
                    _definition.LowerRadarUrl);

            var url =
                useLower
                    ? _definition.LowerRadarUrl!
                    : _definition.RadarUrl;

            if (_radar != null &&
                _radarUrl.Equals(
                    url,
                    StringComparison.OrdinalIgnoreCase))
            {
                Invalidate();
                return;
            }

            var bytes =
                await RadarImageCache.GetAsync(
                    url,
                    ct);

            using var ms =
                new MemoryStream(bytes);
            using var temp =
                Image.FromStream(ms);

            var next =
                new Bitmap(temp);

            _radar?.Dispose();
            _radar = next;
            _radarUrl = url;
            _map = map;
            Invalidate();
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality =
                CompositingQuality.HighQuality;

            var rect =
                FitSquare(
                    ClientRectangle);

            if (_radar != null)
                e.Graphics.DrawImage(
                    _radar,
                    rect);
            else
            {
                using var bg =
                    new SolidBrush(
                        Color.FromArgb(
                            17,
                            22,
                            30));
                e.Graphics.FillRectangle(
                    bg,
                    rect);
            }

            if (_definition == null ||
                _round == null ||
                _round.Path.Count == 0)
            {
                DrawCentered(
                    e.Graphics,
                    rect,
                    "WAITING FOR GROUND TRUTH PATH");
                return;
            }

            var points =
                _round.Path
                    .Select(x =>
                    {
                        var n =
                            RadarCatalog.WorldToRadar(
                                _definition,
                                x.X,
                                x.Y);

                        return new PointF(
                            rect.Left +
                            n.X *
                            rect.Width,
                            rect.Top +
                            n.Y *
                            rect.Height);
                    })
                    .ToArray();

            if (points.Length >= 2)
            {
                using var shadow =
                    new Pen(
                        Color.FromArgb(
                            150,
                            8,
                            10,
                            12),
                        7)
                    {
                        LineJoin =
                            LineJoin.Round
                    };

                e.Graphics.DrawLines(
                    shadow,
                    points);

                using var line =
                    new Pen(
                        Color.FromArgb(
                            255,
                            173,
                            72),
                        3)
                    {
                        LineJoin =
                            LineJoin.Round
                    };

                e.Graphics.DrawLines(
                    line,
                    points);
            }

            DrawPoint(
                e.Graphics,
                points[0],
                Color.FromArgb(
                    106,
                    235,
                    164),
                7);

            if (_round.OpeningDuel)
            {
                var firstIndex =
                    Math.Min(
                        points.Length - 1,
                        Math.Max(
                            0,
                            points.Length / 3));

                DrawPoint(
                    e.Graphics,
                    points[firstIndex],
                    Color.FromArgb(
                        255,
                        191,
                        86),
                    7);
            }

            if (_round.Deaths > 0)
            {
                DrawPoint(
                    e.Graphics,
                    points[^1],
                    Color.FromArgb(
                        255,
                        92,
                        82),
                    8);
            }

            using var font =
                new Font(
                    "Segoe UI",
                    8.5f,
                    FontStyle.Bold);

            using var fill =
                new SolidBrush(
                    Color.FromArgb(
                        205,
                        8,
                        11,
                        16));

            var label =
                $"R{_round.DemoRound} • {_round.RouteEvidence} • {_round.Verdict}";

            var size =
                e.Graphics.MeasureString(
                    label,
                    font);

            var box =
                new RectangleF(
                    rect.Left + 12,
                    rect.Top + 12,
                    size.Width + 18,
                    26);

            e.Graphics.FillRectangle(
                fill,
                box);

            e.Graphics.DrawString(
                label,
                font,
                Brushes.White,
                box.Left + 9,
                box.Top + 4);
        }

        private static void DrawPoint(
            Graphics g,
            PointF point,
            Color color,
            float radius)
        {
            using var glowPath =
                new GraphicsPath();

            glowPath.AddEllipse(
                point.X - radius * 2,
                point.Y - radius * 2,
                radius * 4,
                radius * 4);

            using var glow =
                new PathGradientBrush(
                    glowPath)
                {
                    CenterColor =
                        Color.FromArgb(
                            120,
                            color),
                    SurroundColors =
                        new[]
                        {
                            Color.FromArgb(
                                0,
                                color)
                        }
                };

            g.FillPath(
                glow,
                glowPath);

            using var core =
                new SolidBrush(color);

            g.FillEllipse(
                core,
                point.X - radius / 2,
                point.Y - radius / 2,
                radius,
                radius);
        }

        private static Rectangle FitSquare(
            Rectangle r)
        {
            var size =
                Math.Max(
                    10,
                    Math.Min(
                        r.Width,
                        r.Height) -
                    8);

            return new Rectangle(
                r.Left +
                (r.Width - size) / 2,
                r.Top +
                (r.Height - size) / 2,
                size,
                size);
        }

        private static void DrawCentered(
            Graphics g,
            Rectangle rect,
            string text)
        {
            using var font =
                new Font(
                    "Segoe UI",
                    11,
                    FontStyle.Bold);

            using var brush =
                new SolidBrush(
                    Color.FromArgb(
                        150,
                        162,
                        181));

            using var format =
                new StringFormat
                {
                    Alignment =
                        StringAlignment.Center,
                    LineAlignment =
                        StringAlignment.Center
                };

            g.DrawString(
                text,
                font,
                brush,
                new RectangleF(
                    rect.Left + 20,
                    rect.Top + 20,
                    rect.Width - 40,
                    rect.Height - 40),
                format);
        }

        public void DisposeRadar()
        {
            _radar?.Dispose();
            _radar = null;
            _radarUrl = "";
        }
    }
}
