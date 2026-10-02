using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class FaceitForm : Form
{
    private readonly string _nickname;
    private readonly TextBox _key = new();
    private readonly Label _status = new();
    private readonly Label _stats = new();
    private readonly Button _refresh = new();

    public FaceitForm(string nickname)
    {
        _nickname = nickname;

        Text = "Sm0ki Solo Coach • FACEIT";
        Width = 650;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(10,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI",10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(26,20,26,22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));

        root.Controls.Add(new Label
        {
            Text = "FACEIT PERFORMANCE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",18,FontStyle.Bold)
        },0,0);

        root.Controls.Add(new Label
        {
            Text = $"Profile: {_nickname} • uradni FACEIT Data API",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(155,167,186)
        },0,1);

        var keyPanel = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=2 };
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
        keyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,22));
        keyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        keyPanel.Controls.Add(new Label
        {
            Text="FACEIT CLIENT-SIDE API KEY",
            Dock=DockStyle.Fill,
            ForeColor=Color.FromArgb(126,137,156),
            Font=new Font("Segoe UI",8,FontStyle.Bold)
        },0,0);
        keyPanel.SetColumnSpan(keyPanel.Controls[0],2);

        _key.Dock=DockStyle.Fill;
        _key.UseSystemPasswordChar=true;
        _key.Text = FaceitSettingsStore.HasKey ? "••••••••••••••••" : "";
        _key.BackColor=Color.FromArgb(24,29,39);
        _key.ForeColor=Color.White;
        _key.BorderStyle=BorderStyle.FixedSingle;
        keyPanel.Controls.Add(_key,0,1);

        var save=Button("Save key",100,true);
        save.Dock=DockStyle.Fill;
        save.Margin=new Padding(10,0,0,0);
        save.Click += (_,__) => SaveKey();
        keyPanel.Controls.Add(save,1,1);
        root.Controls.Add(keyPanel,0,2);

        var links=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.LeftToRight };
        var docs=Button("API key help",112,false);
        docs.Click += (_,__) => Process.Start(new ProcessStartInfo(
            "https://docs.faceit.com/getting-started/authentication/api-keys/") { UseShellExecute=true });
        var remove=Button("Remove key",112,false);
        remove.Click += (_,__) =>
        {
            FaceitSettingsStore.ClearKey();
            _key.Clear();
            _status.Text="API key odstranjen.";
            _stats.Text="";
        };
        links.Controls.Add(docs);
        links.Controls.Add(remove);
        root.Controls.Add(links,0,3);

        _status.Text = FaceitSettingsStore.HasKey
            ? "API key je shranjen lokalno. Klikni Refresh FACEIT."
            : "Dodaj FACEIT API key. Tvoj FACEIT password ni potreben.";
        _status.Dock=DockStyle.Fill;
        _status.ForeColor=Color.FromArgb(150,164,184);
        root.Controls.Add(_status,0,4);

        _stats.Dock=DockStyle.Fill;
        _stats.Font=new Font("Segoe UI",11);
        _stats.ForeColor=Color.FromArgb(220,225,234);
        _stats.Padding=new Padding(16);
        _stats.BackColor=Color.FromArgb(17,22,30);
        root.Controls.Add(_stats,0,5);

        var bottom=new FlowLayoutPanel
        {
            Dock=DockStyle.Fill,
            FlowDirection=FlowDirection.RightToLeft,
            WrapContents=false
        };
        _refresh=Button("Refresh FACEIT",130,true);
        _refresh.Click += async (_,__) => await RefreshAsync();
        var close=Button("Close",90,false);
        close.Click += (_,__) => Close();
        bottom.Controls.Add(_refresh);
        bottom.Controls.Add(close);
        root.Controls.Add(bottom,0,6);

        Controls.Add(root);
    }

    private void SaveKey()
    {
        try
        {
            var entered=_key.Text.Trim();
            if (entered.Contains('•') && FaceitSettingsStore.HasKey)
            {
                _status.Text="Obstoječi API key je že shranjen.";
                return;
            }

            FaceitSettingsStore.SaveKey(entered);
            _key.Text="••••••••••••••••";
            _status.Text="API key varno shranjen za ta Windows profil.";
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"FACEIT",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            _refresh.Enabled=false;
            _refresh.Text="Loading…";
            _status.Text="Berem javne FACEIT podatke…";

            var s=await new FaceitService().LoadAsync(_nickname);

            _status.Text=$"{s.Nickname} • {s.GameId.ToUpperInvariant()} • connected";
            _stats.Text=
                $"ELO: {s.Elo}\n" +
                $"Level: {s.SkillLevel}\n" +
                $"Region: {(string.IsNullOrWhiteSpace(s.Region) ? "—" : s.Region)}\n\n" +
                $"Lifetime matches: {s.Matches}\n" +
                $"Lifetime win rate: {s.WinRate}\n" +
                $"Lifetime K/D: {s.LifetimeKd}\n\n" +
                $"Recent {s.RecentMatchesRead} matches\n" +
                $"AVG kills: {(s.RecentAverageKills?.ToString("0.0") ?? "—")}\n" +
                $"AVG deaths: {(s.RecentAverageDeaths?.ToString("0.0") ?? "—")}\n" +
                $"AVG K/D: {(s.RecentAverageKd?.ToString("0.00") ?? "—")}";
        }
        catch(Exception ex)
        {
            _status.Text="FACEIT connection error";
            _stats.Text=ex.Message;
        }
        finally
        {
            _refresh.Enabled=true;
            _refresh.Text="Refresh FACEIT";
        }
    }

    private static Button Button(string text,int width,bool primary)
    {
        var b=new Button
        {
            Text=text, Width=width, Height=34,
            FlatStyle=FlatStyle.Flat,
            BackColor=primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44),
            ForeColor=Color.White,
            Font=new Font("Segoe UI",9,FontStyle.Bold),
            Cursor=Cursors.Hand
        };
        b.FlatAppearance.BorderSize=0;
        return b;
    }
}
