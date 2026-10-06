using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class GroundTruthLabForm : Form
{
    private readonly ComboBox _matches = new();
    private readonly Label _finding = new();
    private readonly Label _summary = new();
    private readonly ListView _rounds = new();

    private IReadOnlyList<GroundTruthMatchReport> _reports =
        Array.Empty<GroundTruthMatchReport>();

    public GroundTruthLabForm()
    {
        Text = "Sm0ki Coach 7.0 • Ground Truth Lab";
        Width = 1320;
        Height = 820;
        MinimumSize = new Size(1040, 680);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(7, 9, 11);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();
        LoadReports();
    }

    private void BuildUi()
    {
        var orange =
            Color.FromArgb(255, 156, 44);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = BackColor
        };

        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label
        {
            Text = "GROUND TRUTH LAB  //  PLAN vs REALITY",
            Dock = DockStyle.Fill,
            Font = new Font(
                "Segoe UI",
                22,
                FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var picker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        picker.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                480));
        picker.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        _matches.Dock = DockStyle.Fill;
        _matches.DropDownStyle =
            ComboBoxStyle.DropDownList;
        _matches.FlatStyle =
            FlatStyle.Flat;
        _matches.BackColor =
            Color.FromArgb(20, 23, 28);
        _matches.ForeColor =
            Color.White;
        _matches.Font =
            new Font(
                "Segoe UI",
                9,
                FontStyle.Bold);
        _matches.SelectedIndexChanged +=
            (_,__) => RenderSelected();

        picker.Controls.Add(
            _matches,
            0,
            0);

        picker.Controls.Add(new Label
        {
            Text =
                "SUPPORTED = demo evidence matches coach route • CONTRADICTED = actual path clearly diverged",
            Dock = DockStyle.Fill,
            ForeColor =
                Color.FromArgb(116, 126, 139),
            Font =
                new Font(
                    "Segoe UI",
                    8,
                    FontStyle.Bold),
            TextAlign =
                ContentAlignment.MiddleRight
        }, 1, 0);

        root.Controls.Add(
            picker,
            0,
            1);

        var insight = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin =
                new Padding(0, 0, 0, 8),
            Padding =
                new Padding(16, 10, 16, 10),
            BackColor =
                Color.FromArgb(18, 14, 11),
            GradientEndColor =
                Color.FromArgb(12, 15, 18),
            BorderColor =
                Color.FromArgb(72, 50, 30),
            AccentColor = orange,
            AccentWidth = 4,
            Radius = 17
        };

        var insightLayout =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };

        insightLayout.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                64));
        insightLayout.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                36));

        _finding.Dock = DockStyle.Fill;
        _finding.ForeColor =
            Color.FromArgb(237, 240, 245);
        _finding.Font =
            new Font(
                "Segoe UI",
                9.5f,
                FontStyle.Bold);
        _finding.TextAlign =
            ContentAlignment.MiddleLeft;
        insightLayout.Controls.Add(
            _finding,
            0,
            0);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor =
            Color.FromArgb(255, 184, 98);
        _summary.Font =
            new Font(
                "Segoe UI",
                8.7f,
                FontStyle.Bold);
        _summary.TextAlign =
            ContentAlignment.MiddleRight;
        insightLayout.Controls.Add(
            _summary,
            1,
            0);

        insight.Controls.Add(
            insightLayout);
        root.Controls.Add(
            insight,
            0,
            2);

        _rounds.Dock = DockStyle.Fill;
        _rounds.View = View.Details;
        _rounds.FullRowSelect = true;
        _rounds.GridLines = false;
        _rounds.BorderStyle =
            BorderStyle.FixedSingle;
        _rounds.BackColor =
            Color.FromArgb(12, 15, 18);
        _rounds.ForeColor =
            Color.FromArgb(220, 225, 232);
        _rounds.Font =
            new Font(
                "Segoe UI",
                8.5f);

        _rounds.Columns.Add("R", 42);
        _rounds.Columns.Add("SIDE", 55);
        _rounds.Columns.Add("COACH ROUTE", 260);
        _rounds.Columns.Add("ACTUAL ROUTE", 330);
        _rounds.Columns.Add("EVIDENCE", 105);
        _rounds.Columns.Add("VERDICT", 155);
        _rounds.Columns.Add("K/D", 65);
        _rounds.Columns.Add("OPENING", 90);
        _rounds.Columns.Add("OUTCOME", 75);
        _rounds.Columns.Add("CONF", 70);

        root.Controls.Add(
            _rounds,
            0,
            3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var close = new Button
        {
            Text = "CLOSE",
            Width = 110,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor =
                Color.FromArgb(22, 25, 29),
            ForeColor = Color.White
        };
        close.FlatAppearance.BorderColor =
            Color.FromArgb(53, 59, 67);
        close.Click +=
            (_,__) => Close();

        var replay = new Button
        {
            Text = "TACTICAL REPLAY",
            Width = 150,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor =
                Color.FromArgb(52, 35, 20),
            ForeColor =
                Color.FromArgb(255, 184, 98)
        };
        replay.FlatAppearance.BorderColor =
            Color.FromArgb(112, 72, 32);
        replay.Click += (_,__) =>
        {
            var report = SelectedReport();
            using var form = new TacticalReplayForm(
                report?.MatchId);
            form.ShowDialog(this);
        };

        footer.Controls.Add(close);
        footer.Controls.Add(replay);

        root.Controls.Add(
            footer,
            0,
            4);

        Controls.Add(root);
    }

    private void LoadReports()
    {
        _reports =
            GroundTruthStore.Load();

        _matches.Items.Clear();

        foreach (var report in _reports)
        {
            var when =
                report.MatchFinishedUtc ==
                default
                    ? report.AnalyzedUtc
                    : report.MatchFinishedUtc;

            _matches.Items.Add(
                $"{when.ToLocalTime():dd.MM HH:mm} • " +
                $"{CoachEngine.PrettyMap(report.Map)} • " +
                $"{report.MatchedRounds} rounds • " +
                ShortId(report.MatchId));
        }

        if (_matches.Items.Count > 0)
            _matches.SelectedIndex = 0;
        else
        {
            _finding.Text =
                "No learned demo yet. Ground Truth appears automatically after FACEIT demo analysis.";
            _summary.Text =
                "WAITING FOR AUTO DEMO";
        }
    }

    private void RenderSelected()
    {
        var index =
            _matches.SelectedIndex;

        if (index < 0 ||
            index >= _reports.Count)
            return;

        var report =
            _reports[index];

        _finding.Text =
            report.BiggestFinding;

        _summary.Text =
            $"{CoachEngine.PrettyMap(report.Map).ToUpperInvariant()} • " +
            $"{report.Compact} • ALIGN {report.RoundAlignmentOffset:+0;-0;0}";

        _rounds.BeginUpdate();
        _rounds.Items.Clear();

        foreach (var r in report.Rounds
                     .OrderBy(x =>
                         x.DemoRound))
        {
            var item =
                new ListViewItem(
                    r.DemoRound.ToString());

            item.SubItems.Add(
                r.Side);

            item.SubItems.Add(
                r.RecommendedRoute);

            item.SubItems.Add(
                r.ActualRoute);

            item.SubItems.Add(
                r.RouteEvidence);

            item.SubItems.Add(
                PrettyVerdict(
                    r.Verdict));

            item.SubItems.Add(
                $"{r.Kills}/{r.Deaths}");

            item.SubItems.Add(
                r.OpeningDuel
                    ? r.OpeningResult
                    : "—");

            item.SubItems.Add(
                $"{r.OutcomeScore * 100:0}");

            item.SubItems.Add(
                r.Confidence);

            _rounds.Items.Add(item);
        }

        _rounds.EndUpdate();
    }

    private GroundTruthMatchReport? SelectedReport()
    {
        var index =
            _matches.SelectedIndex;

        return index >= 0 &&
               index < _reports.Count
            ? _reports[index]
            : null;
    }

    private static string PrettyVerdict(
        string verdict)
        => verdict switch
        {
            "PLAN_WORKED" =>
                "PLAN WORKED",
            "PLAN_MIXED" =>
                "MIXED",
            "PLAN_REVIEW" =>
                "PLAN REVIEW",
            "PLAN_UNDERPERFORMING" =>
                "PLAN UNDERPERFORMING",
            "EXECUTION_DIVERGED" =>
                "EXECUTION DIVERGED",
            _ =>
                "NOT ENOUGH EVIDENCE"
        };

    private static string ShortId(
        string id)
        => string.IsNullOrWhiteSpace(id)
            ? "—"
            : id.Length <= 8
                ? id
                : id[..8];
}
