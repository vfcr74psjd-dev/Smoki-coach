using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class DigitalTwinForm : Form
{
    private readonly string _map;
    private readonly string _side;
    private readonly Label _summary = new();
    private readonly Label _mission = new();
    private readonly ListView _routes = new();
    private readonly ListView _calibration = new();

    public DigitalTwinForm(string map, string side)
    {
        _map = map ?? "";
        _side = side ?? "";

        Text = "Sm0ki Coach 7.0 • Digital Twin";
        Width = 1180;
        Height = 800;
        MinimumSize = new Size(960, 660);
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
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label
        {
            Text = "DIGITAL TWIN  //  PLAYER MODEL",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 23, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var summaryCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(16, 10, 16, 10),
            BackColor = Color.FromArgb(18, 14, 11),
            GradientEndColor = Color.FromArgb(12, 15, 18),
            BorderColor = Color.FromArgb(71, 49, 29),
            AccentColor = Color.FromArgb(255, 156, 44),
            AccentWidth = 4,
            Radius = 17
        };

        var summaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _summary.Dock = DockStyle.Fill;
        _summary.Font = new Font("Segoe UI", 10.2f, FontStyle.Bold);
        _summary.ForeColor = Color.FromArgb(235, 239, 244);
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        summaryLayout.Controls.Add(_summary, 0, 0);

        _mission.Dock = DockStyle.Fill;
        _mission.Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        _mission.ForeColor = Color.FromArgb(255, 186, 103);
        _mission.TextAlign = ContentAlignment.MiddleLeft;
        summaryLayout.Controls.Add(_mission, 1, 0);

        summaryCard.Controls.Add(summaryLayout);
        root.Controls.Add(summaryCard, 0, 1);

        ConfigureList(
            _routes,
            "DIGITAL TWIN ROUTE MODEL",
            new[]
            {
                ("SIDE",70),
                ("INTENT",75),
                ("ROUTE",320),
                ("SPAWN",85),
                ("TYPE",105),
                ("ATT",55),
                ("WIN",60),
                ("SURV",60),
                ("K/R",60),
                ("OUTCOME",75)
            });
        root.Controls.Add(_routes, 0, 2);

        ConfigureList(
            _calibration,
            "COACH SELF-CALIBRATION",
            new[]
            {
                ("SIDE",70),
                ("ROUTE",360),
                ("SAMPLES",70),
                ("PRED",70),
                ("ACTUAL",70),
                ("ERROR",70),
                ("TRUST Δ",75),
                ("OVERFAIL",80)
            });
        root.Controls.Add(_calibration, 0, 3);

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
            BackColor = Color.FromArgb(22, 25, 29),
            ForeColor = Color.White
        };
        close.FlatAppearance.BorderColor = Color.FromArgb(53, 59, 67);
        close.Click += (_,__) => Close();

        var ledger = new Button
        {
            Text = "DECISION LEDGER",
            Width = 150,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(52, 35, 20),
            ForeColor = Color.FromArgb(255, 184, 98)
        };
        ledger.FlatAppearance.BorderColor = Color.FromArgb(112, 72, 32);
        ledger.Click += (_,__) =>
        {
            using var view = new DecisionLedgerForm(_map);
            view.ShowDialog(this);
        };

        footer.Controls.Add(close);
        footer.Controls.Add(ledger);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);
    }

    private static void ConfigureList(
        ListView list,
        string name,
        IEnumerable<(string Title,int Width)> columns)
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = false;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.BackColor = Color.FromArgb(12, 15, 18);
        list.ForeColor = Color.FromArgb(220, 225, 232);
        list.Font = new Font("Segoe UI", 8.8f);
        list.AccessibleName = name;

        foreach (var (title,width) in columns)
            list.Columns.Add(title, width);
    }

    private void RefreshData()
    {
        var twin = DigitalTwinStore.Analyze(_map, _side);
        var mission = TrainingMissionStore.Current(_map);

        _summary.Text =
            $"{CoachEngine.PrettyMap(_map).ToUpperInvariant()} • {(_side.Length == 0 ? "ALL" : _side)}\n" +
            $"{twin.Archetype} • {twin.Samples} learned rounds • {twin.Confidence} confidence\n" +
            $"BEST: {twin.BestRoute}   |   RISK: {twin.RiskRoute}   |   {twin.KillsPerRound:0.00} K/R • {twin.SurvivalRate * 100:0}% survival";

        _mission.Text =
            string.IsNullOrWhiteSpace(mission.Key)
                ? "TRAINING OS\nNo persistent mission yet."
                : $"TRAINING OS\n{mission.Label}\n{mission.SuccessRounds}/{mission.TrackedRounds} success • streak {mission.CurrentStreak}";

        _routes.Items.Clear();
        foreach (var x in DigitalTwinStore
                     .LoadRoutes(_map, _side)
                     .OrderByDescending(x => x.Attempts)
                     .ThenByDescending(x => x.OutcomeScore)
                     .Take(80))
        {
            var item = new ListViewItem(x.Side);
            item.SubItems.Add(x.Intent);
            item.SubItems.Add(x.Route);
            item.SubItems.Add(x.SpawnBias);
            item.SubItems.Add(x.RoundType);
            item.SubItems.Add(x.Attempts.ToString());
            item.SubItems.Add($"{x.WinRate * 100:0}%");
            item.SubItems.Add($"{x.SurvivalRate * 100:0}%");
            item.SubItems.Add(x.KillsPerRound.ToString("0.00"));
            item.SubItems.Add($"{x.OutcomeScore * 100:0}");
            _routes.Items.Add(item);
        }

        _calibration.Items.Clear();
        foreach (var x in DigitalTwinStore
                     .LoadCalibrations(_map)
                     .Where(x => x.Samples >= 2)
                     .Take(60))
        {
            var item = new ListViewItem(x.Side);
            item.SubItems.Add(x.Route);
            item.SubItems.Add(x.Samples.ToString());
            item.SubItems.Add($"{x.AveragePrediction * 100:0}%");
            item.SubItems.Add($"{x.AverageOutcome * 100:0}%");
            item.SubItems.Add($"{x.CalibrationError * 100:0}%");
            item.SubItems.Add($"{x.TrustAdjustment:+0;-0;0}");
            item.SubItems.Add(x.OverPredictedFailures.ToString());
            _calibration.Items.Add(item);
        }
    }
}
