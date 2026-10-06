using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class DecisionLedgerForm : Form
{
    private readonly string _map;
    private readonly ListView _rounds = new();
    private readonly Label _summary = new();

    public DecisionLedgerForm(string map)
    {
        _map = map ?? "";

        Text = "Sm0ki Coach 7.0 • Decision Ledger";
        Width = 1280;
        Height = 760;
        MinimumSize = new Size(1000, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(7, 9, 11);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();
        RefreshData();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label
        {
            Text = "DECISION LEDGER  //  WHY THIS PLAN WON",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var summaryCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(15, 9, 15, 9),
            BackColor = Color.FromArgb(18, 14, 11),
            GradientEndColor = Color.FromArgb(12, 15, 18),
            BorderColor = Color.FromArgb(70, 49, 30),
            AccentColor = Color.FromArgb(255, 156, 44),
            AccentWidth = 4,
            Radius = 16
        };

        _summary.Dock = DockStyle.Fill;
        _summary.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        _summary.ForeColor = Color.FromArgb(225, 231, 239);
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        summaryCard.Controls.Add(_summary);
        root.Controls.Add(summaryCard, 0, 1);

        _rounds.Dock = DockStyle.Fill;
        _rounds.View = View.Details;
        _rounds.FullRowSelect = true;
        _rounds.GridLines = false;
        _rounds.BorderStyle = BorderStyle.FixedSingle;
        _rounds.BackColor = Color.FromArgb(12, 15, 18);
        _rounds.ForeColor = Color.FromArgb(220, 225, 232);
        _rounds.Font = new Font("Segoe UI", 8.5f);

        _rounds.Columns.Add("TIME", 105);
        _rounds.Columns.Add("R", 42);
        _rounds.Columns.Add("SIDE", 55);
        _rounds.Columns.Add("WINNER", 260);
        _rounds.Columns.Add("SCORE", 65);
        _rounds.Columns.Add("PLANS", 60);
        _rounds.Columns.Add("CONF", 70);
        _rounds.Columns.Add("MARGIN", 70);
        _rounds.Columns.Add("RUNNER-UP", 250);
        _rounds.Columns.Add("#2", 55);
        _rounds.Columns.Add("EVIDENCE", 250);

        root.Controls.Add(_rounds, 0, 2);

        var close = new Button
        {
            Text = "CLOSE",
            Width = 110,
            Height = 34,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 25, 29),
            ForeColor = Color.White
        };
        close.FlatAppearance.BorderColor = Color.FromArgb(53, 59, 67);
        close.Click += (_,__) => Close();

        root.Controls.Add(close, 0, 3);
        Controls.Add(root);
    }

    private void RefreshData()
    {
        var sessions =
            RecommendationTraceStore.Load()
                .Where(x =>
                    string.IsNullOrWhiteSpace(_map) ||
                    x.Map.Equals(
                        _map,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.StartedUtc)
                .Take(20)
                .ToList();

        var rows =
            sessions
                .SelectMany(session =>
                    session.Rounds.Select(round =>
                        new
                        {
                            Session = session,
                            Round = round
                        }))
                .Where(x =>
                    x.Round.SimulationCandidateCount > 0)
                .OrderByDescending(x =>
                    x.Session.StartedUtc)
                .ThenByDescending(x =>
                    x.Round.Round)
                .Take(120)
                .ToList();

        var high =
            rows.Count(x =>
                x.Round.SimulationConfidence ==
                "HIGH");

        var closeCalls =
            rows.Count(x =>
                x.Round.SimulationMargin < 4);

        _summary.Text =
            rows.Count == 0
                ? "No Strategy Simulator decisions recorded yet."
                : $"{CoachEngine.PrettyMap(_map).ToUpperInvariant()} • {rows.Count} decisions • " +
                  $"{high} HIGH confidence • {closeCalls} close calls (<4 margin). " +
                  "Close calls are useful review targets after Ground Truth arrives.";

        _rounds.BeginUpdate();
        _rounds.Items.Clear();

        foreach (var x in rows)
        {
            var r = x.Round;
            var item = new ListViewItem(
                x.Session.StartedUtc.ToLocalTime()
                    .ToString("dd.MM HH:mm"));

            item.SubItems.Add(r.Round.ToString());
            item.SubItems.Add(r.Side);
            item.SubItems.Add(r.Route);
            item.SubItems.Add(r.DecisionScore.ToString());
            item.SubItems.Add(r.SimulationCandidateCount.ToString());
            item.SubItems.Add(r.SimulationConfidence);
            item.SubItems.Add(r.SimulationMargin.ToString("+0.0;-0.0;0.0"));
            item.SubItems.Add(
                string.IsNullOrWhiteSpace(r.RunnerUpRoute)
                    ? "—"
                    : r.RunnerUpRoute);
            item.SubItems.Add(
                r.RunnerUpScore > 0
                    ? r.RunnerUpScore.ToString("0")
                    : "—");
            item.SubItems.Add(
                string.IsNullOrWhiteSpace(r.SimulationWinnerEvidence)
                    ? "—"
                    : r.SimulationWinnerEvidence);

            _rounds.Items.Add(item);
        }

        _rounds.EndUpdate();
    }
}
