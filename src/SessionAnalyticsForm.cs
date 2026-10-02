using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class SessionAnalyticsForm : Form
{
    public SessionAnalyticsForm(
        string nickname,
        string currentMap,
        IReadOnlyList<RoundRecord> currentRounds)
    {
        Text="Sm0ki Solo Coach • Analytics";
        Width=820;
        Height=650;
        StartPosition=FormStartPosition.CenterParent;
        AutoScaleMode=AutoScaleMode.Dpi;
        BackColor=Color.FromArgb(10,13,19);
        ForeColor=Color.White;
        Font=new Font("Segoe UI",10);

        var root=new TableLayoutPanel
        {
            Dock=DockStyle.Fill,
            ColumnCount=1,
            RowCount=5,
            Padding=new Padding(24,20,24,22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));

        root.Controls.Add(new Label
        {
            Text="SESSION ANALYTICS",
            Dock=DockStyle.Fill,
            Font=new Font("Segoe UI",18,FontStyle.Bold)
        },0,0);

        var current=new Label
        {
            Dock=DockStyle.Fill,
            ForeColor=Color.FromArgb(190,199,213),
            Font=new Font("Segoe UI",10)
        };

        if(currentRounds.Count==0)
            current.Text="Current session: waiting for completed rounds.";
        else
        {
            var kills=currentRounds.Sum(r=>r.KillsRound);
            var deaths=currentRounds.Sum(r=>r.DeathsRound);
            var kr=(double)kills/currentRounds.Count;
            var survival=100.0*currentRounds.Count(r=>r.DeathsRound==0)/currentRounds.Count;
            current.Text=
                $"CURRENT • {CoachEngine.PrettyMap(currentMap)} • {currentRounds.Count} rounds • " +
                $"{kills}K/{deaths}D • {kr:0.00} K/R • {survival:0}% survival";
        }
        root.Controls.Add(current,0,1);

        var all=SessionHistoryStore.Load()
            .Where(x=>x.Nickname.Equals(nickname,StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x=>x.EndedUtc)
            .ToList();

        var summary=new Label
        {
            Dock=DockStyle.Fill,
            BackColor=Color.FromArgb(17,22,30),
            ForeColor=Color.FromArgb(220,225,234),
            Padding=new Padding(14),
            Font=new Font("Segoe UI",10)
        };

        if(all.Count==0)
            summary.Text="No archived sessions yet. A session is saved locally when a match/map ends.";
        else
        {
            int rounds=all.Sum(x=>x.Rounds);
            int kills=all.Sum(x=>x.Kills);
            int survivalRounds=all.Sum(x=>x.SurvivalRounds);
            int zero=all.Sum(x=>x.ZeroKillRounds);
            int multi=all.Sum(x=>x.MultiKillRounds);
            summary.Text=
                $"{all.Count} sessions • {rounds} tracked rounds • {(rounds>0?(double)kills/rounds:0):0.00} K/R • " +
                $"{(rounds>0?100.0*survivalRounds/rounds:0):0}% survival\n" +
                $"{zero} zero-kill rounds • {multi} multi-kill rounds • stored only on this PC";
        }
        root.Controls.Add(summary,0,2);

        var list=new ListView
        {
            Dock=DockStyle.Fill,
            View=View.Details,
            FullRowSelect=true,
            GridLines=false,
            BackColor=Color.FromArgb(14,19,27),
            ForeColor=Color.FromArgb(211,218,229),
            BorderStyle=BorderStyle.None
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

        foreach(var s in all.Take(50))
        {
            var item=new ListViewItem(s.EndedUtc.ToLocalTime().ToString("dd.MM HH:mm"));
            item.SubItems.Add(CoachEngine.PrettyMap(s.Map));
            item.SubItems.Add(s.Rounds.ToString());
            item.SubItems.Add($"{s.Kills}/{s.Deaths}");
            item.SubItems.Add(s.KillsPerRound.ToString("0.00"));
            item.SubItems.Add(s.SurvivalRate.ToString("0")+"%");
            item.SubItems.Add(s.ZeroKillRounds.ToString());
            item.SubItems.Add(s.MultiKillRounds.ToString());
            item.SubItems.Add($"{s.CtScore}:{s.TScore}");
            list.Items.Add(item);
        }
        root.Controls.Add(list,0,3);

        var close=new Button
        {
            Text="Close",
            Width=100,
            Height=34,
            Anchor=AnchorStyles.Right,
            FlatStyle=FlatStyle.Flat,
            BackColor=Color.FromArgb(27,33,44),
            ForeColor=Color.White
        };
        close.FlatAppearance.BorderSize=0;
        close.Click+=(_,__)=>Close();
        root.Controls.Add(close,0,4);

        Controls.Add(root);
    }
}
