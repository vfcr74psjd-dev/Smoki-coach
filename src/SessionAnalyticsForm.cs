using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class SessionAnalyticsForm : Form
{
    public SessionAnalyticsForm(
        string nickname,
        string currentMap,
        IReadOnlyList<RoundRecord> currentRounds,
        FaceitSnapshot? faceit = null)
    {
        Text = "Sm0ki Solo Coach • Progress";
        Width = 900;
        Height = 720;
        MinimumSize = new Size(800, 620);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(10,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI",10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(24,20,24,22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));

        root.Controls.Add(new Label
        {
            Text = "PLAYER PROGRESS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",18,FontStyle.Bold)
        },0,0);

        var current = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(190,199,213),
            Font = new Font("Segoe UI",10)
        };

        if (currentRounds.Count == 0)
        {
            current.Text = "Current session: waiting for completed rounds.";
        }
        else
        {
            var kills = currentRounds.Sum(r=>r.KillsRound);
            var deaths = currentRounds.Sum(r=>r.DeathsRound);
            var kr = (double)kills/currentRounds.Count;
            var survival = 100.0*currentRounds.Count(r=>r.DeathsRound==0)/currentRounds.Count;
            current.Text =
                $"CURRENT • {CoachEngine.PrettyMap(currentMap)} • {currentRounds.Count} rounds • " +
                $"{kills}K/{deaths}D • {kr:0.00} K/R • {survival:0}% survival";
        }
        root.Controls.Add(current,0,1);

        var faceitCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0,4,0,8),
            Padding = new Padding(16,12,16,12),
            BackColor = Color.FromArgb(17,22,30)
        };
        faceitCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));
        faceitCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));

        var faceitPrimary = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",10.5f),
            ForeColor = Color.FromArgb(225,230,238),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var faceitSecondary = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",9),
            ForeColor = Color.FromArgb(155,167,186),
            TextAlign = ContentAlignment.MiddleLeft
        };

        if (faceit == null)
        {
            faceitPrimary.Text = "FACEIT • not connected / snapshot unavailable";
            faceitSecondary.Text = "Open FACEIT from the sidebar to connect or refresh.";
        }
        else
        {
            faceitPrimary.Text =
                $"FACEIT • {faceit.Nickname}\n" +
                $"Level {faceit.SkillLevel} • {faceit.Elo} ELO • {faceit.Region}";

            var trend = faceit.RecentKillsTrendLabel;
            faceitSecondary.Text =
                $"Recent {faceit.RecentMatchesRead} matches\n" +
                $"AVG kills {faceit.RecentAverageKills?.ToString("0.0") ?? "—"} • " +
                $"AVG K/D {faceit.RecentAverageKd?.ToString("0.00") ?? "—"}\n" +
                trend;
        }

        faceitCard.Controls.Add(faceitPrimary,0,0);
        faceitCard.Controls.Add(faceitSecondary,1,0);
        root.Controls.Add(faceitCard,0,2);

        var all = SessionHistoryStore.Load()
            .Where(x=>x.Nickname.Equals(nickname,StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x=>x.EndedUtc)
            .ToList();

        var summary = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(17,22,30),
            ForeColor = Color.FromArgb(220,225,234),
            Padding = new Padding(14),
            Font = new Font("Segoe UI",10)
        };

        if (all.Count == 0)
        {
            summary.Text = "No archived sessions yet. A session is saved locally when a match/map ends.";
        }
        else
        {
            int rounds = all.Sum(x=>x.Rounds);
            int kills = all.Sum(x=>x.Kills);
            int survivalRounds = all.Sum(x=>x.SurvivalRounds);
            int zero = all.Sum(x=>x.ZeroKillRounds);
            int multi = all.Sum(x=>x.MultiKillRounds);

            summary.Text =
                $"{all.Count} sessions • {rounds} tracked rounds • " +
                $"{(rounds>0?(double)kills/rounds:0):0.00} K/R • " +
                $"{(rounds>0?100.0*survivalRounds/rounds:0):0}% survival\n" +
                $"{zero} zero-kill rounds • {multi} multi-kill rounds • stored only on this PC";
        }
        root.Controls.Add(summary,0,3);

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = Color.FromArgb(14,19,27),
            ForeColor = Color.FromArgb(211,218,229),
            BorderStyle = BorderStyle.None
        };
        list.Columns.Add("Date",130);
        list.Columns.Add("Map",120);
        list.Columns.Add("Rounds",70);
        list.Columns.Add("K/D",80);
        list.Columns.Add("K/R",70);
        list.Columns.Add("Survival",80);
        list.Columns.Add("0K",50);
        list.Columns.Add("Multi",55);
        list.Columns.Add("Score",70);

        foreach (var session in all.Take(50))
        {
            var item = new ListViewItem(session.EndedUtc.ToLocalTime().ToString("dd.MM HH:mm"));
            item.SubItems.Add(CoachEngine.PrettyMap(session.Map));
            item.SubItems.Add(session.Rounds.ToString());
            item.SubItems.Add($"{session.Kills}/{session.Deaths}");
            item.SubItems.Add(session.KillsPerRound.ToString("0.00"));
            item.SubItems.Add(session.SurvivalRate.ToString("0")+"%");
            item.SubItems.Add(session.ZeroKillRounds.ToString());
            item.SubItems.Add(session.MultiKillRounds.ToString());
            item.SubItems.Add($"{session.CtScore}:{session.TScore}");
            list.Items.Add(item);
        }
        root.Controls.Add(list,0,4);

        var close = new Button
        {
            Text = "Close",
            Width = 100,
            Height = 34,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(27,33,44),
            ForeColor = Color.White
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_,__) => Close();
        root.Controls.Add(close,0,5);

        Controls.Add(root);
    }
}
