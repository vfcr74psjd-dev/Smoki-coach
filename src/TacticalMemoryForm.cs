using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class TacticalMemoryForm : Form
{
    private static readonly Color Bg = Color.FromArgb(9, 10, 12);
    private static readonly Color Orange = Color.FromArgb(255, 156, 44);
    private static readonly Color Muted = Color.FromArgb(135, 143, 154);
    private readonly string _currentMap;

    private readonly ComboBox _map = new();
    private readonly Label _week = new();
    private readonly Label _leak = new();
    private readonly ListView _routes = new();

    public TacticalMemoryForm(string currentMap)
    {
        _currentMap = currentMap ?? "";

        Text = "Sm0ki Tactical OS • Playbook";
        Width = 1080;
        Height = 760;
        MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();
        LoadMaps();
        RefreshView();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22, 18, 22, 20),
            BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

        header.Controls.Add(new Label
        {
            Text = "PERSONAL PLAYBOOK",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        header.Controls.Add(new Label
        {
            Text = "TACTICAL OS • MEMORY",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Orange,
            TextAlign = ContentAlignment.MiddleRight
        }, 1, 0);

        root.Controls.Add(header, 0, 0);

        var filter = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var mapBox = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        mapBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        mapBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        mapBox.Controls.Add(new Label
        {
            Text = "MAP FILTER",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Muted
        }, 0, 0);

        _map.Dock = DockStyle.Fill;
        _map.DropDownStyle = ComboBoxStyle.DropDownList;
        _map.BackColor = Color.FromArgb(18, 20, 23);
        _map.ForeColor = Color.White;
        _map.SelectedIndexChanged += (_,__) => RefreshView();
        mapBox.Controls.Add(_map, 0, 1);

        filter.Controls.Add(mapBox, 0, 0);
        filter.Controls.Add(new Label
        {
            Text = "Routes are learned from completed tracked rounds. More attempts = more trustworthy personal evidence.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleRight
        }, 1, 0);

        root.Controls.Add(filter, 0, 1);

        var report = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 8)
        };
        report.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        report.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _week.Dock = DockStyle.Fill;
        _week.Margin = new Padding(0, 0, 5, 0);
        _week.Padding = new Padding(14, 10, 14, 10);
        _week.BackColor = Color.FromArgb(17, 19, 22);
        _week.ForeColor = Color.FromArgb(222, 226, 231);
        _week.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _week.TextAlign = ContentAlignment.MiddleLeft;

        _leak.Dock = DockStyle.Fill;
        _leak.Margin = new Padding(5, 0, 0, 0);
        _leak.Padding = new Padding(14, 10, 14, 10);
        _leak.BackColor = Color.FromArgb(29, 22, 15);
        _leak.ForeColor = Color.FromArgb(255, 185, 104);
        _leak.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _leak.TextAlign = ContentAlignment.MiddleLeft;

        report.Controls.Add(_week, 0, 0);
        report.Controls.Add(_leak, 1, 0);
        root.Controls.Add(report, 0, 2);

        _routes.Dock = DockStyle.Fill;
        _routes.View = View.Details;
        _routes.FullRowSelect = true;
        _routes.GridLines = false;
        _routes.BorderStyle = BorderStyle.FixedSingle;
        _routes.BackColor = Color.FromArgb(13, 15, 18);
        _routes.ForeColor = Color.FromArgb(225, 229, 234);
        _routes.Font = new Font("Segoe UI", 9);
        _routes.Columns.Add("MAP", 105);
        _routes.Columns.Add("SIDE", 60);
        _routes.Columns.Add("ROUTE / START", 360);
        _routes.Columns.Add("ATT", 55);
        _routes.Columns.Add("WIN", 65);
        _routes.Columns.Add("SURV", 65);
        _routes.Columns.Add("K/R", 65);
        _routes.Columns.Add("SCORE", 70);
        root.Controls.Add(_routes, 0, 3);

        var close = new Button
        {
            Text = "CLOSE",
            Width = 110,
            Height = 34,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(28, 30, 34),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        close.FlatAppearance.BorderColor = Color.FromArgb(58, 62, 68);
        close.Click += (_,__) => Close();
        root.Controls.Add(close, 0, 4);

        Controls.Add(root);
    }

    private void LoadMaps()
    {
        var maps = AdaptivePlaybookStore.Load()
            .Select(x => x.Map)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        _map.Items.Clear();
        _map.Items.Add("ALL MAPS");

        foreach (var map in maps)
            _map.Items.Add(map);

        var currentIndex =
            maps.FindIndex(x =>
                x.Equals(
                    _currentMap,
                    StringComparison.OrdinalIgnoreCase));

        _map.SelectedIndex =
            currentIndex >= 0
                ? currentIndex + 1
                : 0;
    }

    private void RefreshView()
    {
        if (_map.SelectedIndex < 0)
            return;

        var selected =
            _map.SelectedIndex == 0
                ? ""
                : _map.SelectedItem?.ToString() ?? "";

        var rows =
            AdaptivePlaybookStore.Load(
                selected);

        _routes.BeginUpdate();
        _routes.Items.Clear();

        foreach (var r in rows
                     .OrderByDescending(x => x.Attempts >= 3)
                     .ThenByDescending(x => x.SuccessScore)
                     .ThenByDescending(x => x.Attempts))
        {
            var item = new ListViewItem(
                CoachEngine.PrettyMap(r.Map));
            item.SubItems.Add(r.Side);
            item.SubItems.Add(r.Route);
            item.SubItems.Add(r.Attempts.ToString());
            item.SubItems.Add(
                $"{r.WinRate * 100:0}%");
            item.SubItems.Add(
                $"{r.SurvivalRate * 100:0}%");
            item.SubItems.Add(
                r.Attempts > 0
                    ? ((double)r.Kills / r.Attempts)
                        .ToString("0.00")
                    : "0.00");
            item.SubItems.Add(
                $"{r.SuccessScore * 100:0}");
            _routes.Items.Add(item);
        }

        _routes.EndUpdate();

        var sessions =
            SessionHistoryStore.Load()
                .Where(x =>
                    x.EndedUtc >=
                    DateTime.UtcNow.AddDays(-7) &&
                    (string.IsNullOrWhiteSpace(selected) ||
                     x.Map.Equals(
                         selected,
                         StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.EndedUtc)
                .ToList();

        if (sessions.Count == 0)
        {
            _week.Text =
                "7-DAY COACH REPORT\nNo completed session sample yet.";
        }
        else
        {
            var rounds =
                Math.Max(
                    1,
                    sessions.Sum(x => x.Rounds));
            var kpr =
                (double)sessions.Sum(x => x.Kills) /
                rounds;
            var survival =
                100.0 *
                sessions.Sum(x => x.SurvivalRounds) /
                rounds;
            var dev =
                sessions
                    .Where(x => x.DevelopmentScore > 0)
                    .Select(x => x.DevelopmentScore)
                    .DefaultIfEmpty(0)
                    .Average();

            _week.Text =
                $"7-DAY COACH REPORT • {sessions.Count} matches\n" +
                $"{kpr:0.00} K/R • {survival:0}% survival • DEV {dev:0}";
        }

        var leak =
            MistakeLibraryStore.TopRecurring(
                selected,
                10);

        _leak.Text =
            leak.Matches > 0
                ? $"RECURRING LEAK • {leak.Label}\n" +
                  $"{leak.Matches}/{leak.RecentMatchesRead} matches • {leak.Trend} • {leak.CoachingFocus}"
                : "RECURRING LEAK\nNo strong repeated leak yet.";
    }
}
