using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class MainForm : Form
{
    private readonly Label _status = new();
    private readonly Dictionary<string, Label> _stats = new();
    private readonly Label _buyTitle = new();
    private readonly Label _buyText = new();
    private readonly Label _tip = new();
    private readonly ListBox _history = new();
    private readonly ComboBox _mode = new();
    private readonly GsiServer _server = new();
    private PhoneDashboardServer? _phoneServer;
    private readonly Label _phoneUrl = new();
    private readonly Button _updateButton = new();

    private GameSnapshot _current = new();
    private GameSnapshot? _previous;
    private readonly List<RoundRecord> _rounds = new();

    public MainForm()
    {
        Text = "Sm0ki Solo Coach";
        Width = 980;
        Height = 680;
        MinimumSize = new Size(860, 600);
        BackColor = Color.FromArgb(14,17,23);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();

        _server.SnapshotReceived += OnSnapshot;
        _server.Start();
        Text = $"Sm0ki Solo Coach • GSI {_server.BoundPort}";

        _phoneServer = new PhoneDashboardServer(
            () => _current,
            () => _mode.SelectedItem?.ToString() ?? "Balanced"
        );
        _phoneServer.Start();

        var path = GsiInstaller.TryInstall();
        if (path == null)
            _status.Text = "OFFLINE • GSI NOT INSTALLED";

        FormClosing += (_,__) =>
        {
            _phoneServer?.Dispose();
            _server.Dispose();
        };
    }

    private void BuildUi()
    {
        SuspendLayout();

        var top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.FromArgb(14,18,26),
            Padding = new Padding(24,18,24,12)
        };

        var accent = new Panel
        {
            BackColor = Color.FromArgb(104, 92, 255),
            Width = 5,
            Height = 44,
            Left = 22,
            Top = 21
        };
        top.Controls.Add(accent);

        var title = new Label
        {
            Text = "Sm0ki Solo Coach",
            AutoSize = true,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            Left = 40,
            Top = 15
        };
        top.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "LIVE CS2 • SOLO PERFORMANCE COMPANION",
            AutoSize = true,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Left = 42,
            Top = 52
        };
        top.Controls.Add(subtitle);

        _status.Text = "OFFLINE";
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Width = 94;
        _status.Height = 30;
        _status.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _status.ForeColor = Color.FromArgb(255, 150, 130);
        _status.BackColor = Color.FromArgb(55, 28, 28);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.Top = 28;
        _status.Left = 940;
        top.Controls.Add(_status);
        Controls.Add(top);

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(11,15,22),
            Padding = new Padding(24,10,24,10)
        };

        toolbar.Controls.Add(new Label
        {
            Text = "COACH MODE",
            AutoSize = true,
            Left = 24,
            Top = 9,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI", 8, FontStyle.Bold)
        });

        _mode.Items.AddRange(new object[] { "Balanced", "Aggressive", "Safe" });
        _mode.SelectedIndex = 0;
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Left = 24;
        _mode.Top = 26;
        _mode.Width = 150;
        _mode.FlatStyle = FlatStyle.Flat;
        _mode.BackColor = Color.FromArgb(24,29,39);
        _mode.ForeColor = Color.White;
        toolbar.Controls.Add(_mode);

        _phoneUrl.AutoSize = true;
        _phoneUrl.Left = 194;
        _phoneUrl.Top = 30;
        _phoneUrl.ForeColor = Color.FromArgb(112,124,145);
        _phoneUrl.Font = new Font("Segoe UI", 8.5f);
        toolbar.Controls.Add(_phoneUrl);

        var phone = MakeButton("Phone Mode", 118, false);
        phone.Left = 690;
        phone.Top = 15;
        phone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        phone.Click += (_,__) =>
        {
            if (_phoneServer == null) return;
            var url = _phoneServer.GetLocalUrl();
            Clipboard.SetText(url);
            MessageBox.Show(
                $"Na telefonu, ki je na istem Wi-Fi, odpri:\n\n{url}\n\nNaslov sem ti kopiral tudi v clipboard.",
                "Phone Mode"
            );
        };
        toolbar.Controls.Add(phone);

        _updateButton.Text = "Update";
        StyleButton(_updateButton, true);
        _updateButton.Width = 92;
        _updateButton.Left = 818;
        _updateButton.Top = 15;
        _updateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        toolbar.Controls.Add(_updateButton);

        var install = MakeButton("Refresh GSI", 118, false);
        install.Left = 918;
        install.Top = 15;
        install.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        install.Click += (_,__) =>
        {
            var p = GsiInstaller.TryInstall();
            MessageBox.Show(p != null ? $"Installed to:\n{p}\n\nRestart CS2 if already open." : "CS2 cfg folder was not found automatically.",
                "Sm0ki Solo Coach");
        };
        toolbar.Controls.Add(install);

        Controls.Add(toolbar);

        var statPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 110,
            ColumnCount = 8,
            Padding = new Padding(20,10,20,10),
            BackColor = Color.FromArgb(10,13,19)
        };
        for (int i=0;i<8;i++) statPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,12.5f));

        var items = new[] { ("map","MAP"),("side","SIDE"),("round","ROUND"),("score","SCORE"),("kills","KILLS"),("deaths","DEATHS"),("kd","K / D"),("money","MONEY") };
        for (int i=0;i<items.Length;i++)
        {
            var card = MakeCard();
            card.Margin = new Padding(5);

            var small = new Label
            {
                Text = items[i].Item2,
                ForeColor = Color.FromArgb(113,125,146),
                AutoSize = true,
                Top = 13,
                Left = 13,
                Font = new Font("Segoe UI",8,FontStyle.Bold)
            };
            var val = new Label
            {
                Text = "—",
                AutoSize = true,
                Top = 36,
                Left = 13,
                Font = new Font("Segoe UI",15,FontStyle.Bold),
                ForeColor = Color.FromArgb(241,244,249)
            };
            card.Controls.Add(small);
            card.Controls.Add(val);
            _stats[items[i].Item1] = val;
            statPanel.Controls.Add(card,i,0);
        }
        Controls.Add(statPanel);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20,8,20,18),
            BackColor = Color.FromArgb(10,13,19)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));

        var left = MakeCard();
        left.Margin = new Padding(5,5,9,5);
        var right = MakeCard();
        right.Margin = new Padding(9,5,5,5);

        var buyHdr = Header("NEXT BUY");
        buyHdr.Top=22; buyHdr.Left=20;
        _buyTitle.Text="Waiting for CS2…";
        _buyTitle.Left=20; _buyTitle.Top=54; _buyTitle.Width=430; _buyTitle.Height=36;
        _buyTitle.Font=new Font("Segoe UI",18,FontStyle.Bold);
        _buyTitle.ForeColor = Color.FromArgb(240,242,247);

        _buyText.Left=20; _buyText.Top=98; _buyText.Width=440; _buyText.Height=115;
        _buyText.ForeColor=Color.FromArgb(190,198,211);
        _buyText.Font = new Font("Segoe UI", 10);

        var divider = new Panel { Left=20, Top=224, Width=440, Height=1, BackColor=Color.FromArgb(43,50,64) };

        var coachHdr=Header("SOLO AVG-KILLS COACH");
        coachHdr.Top=246; coachHdr.Left=20;
        _tip.Left=20; _tip.Top=281; _tip.Width=440; _tip.Height=180;
        _tip.ForeColor=Color.FromArgb(205,211,221);
        _tip.Font = new Font("Segoe UI", 10);

        left.Controls.Add(buyHdr);
        left.Controls.Add(_buyTitle);
        left.Controls.Add(_buyText);
        left.Controls.Add(divider);
        left.Controls.Add(coachHdr);
        left.Controls.Add(_tip);

        var histHdr=Header("ROUND HISTORY");
        histHdr.Top=22; histHdr.Left=20;

        var histSub = new Label
        {
            Text = "Recent round deltas",
            AutoSize = true,
            Left = 20,
            Top = 47,
            ForeColor = Color.FromArgb(96,108,128),
            Font = new Font("Segoe UI",8.5f)
        };
        right.Controls.Add(histHdr);
        right.Controls.Add(histSub);

        _history.Left=20; _history.Top=78; _history.Width=405; _history.Height=380;
        _history.BackColor=Color.FromArgb(17,22,30);
        _history.ForeColor=Color.FromArgb(206,213,223);
        _history.BorderStyle=BorderStyle.None;
        _history.Font=new Font("Cascadia Mono",10);
        right.Controls.Add(_history);

        body.Controls.Add(left,0,0);
        body.Controls.Add(right,1,0);
        Controls.Add(body);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            BackColor = Color.FromArgb(10,13,19)
        };
        footer.Controls.Add(new Label
        {
            Text = $"v{AppUpdater.CurrentVersion}  •  CS2 Game State Integration",
            AutoSize = true,
            ForeColor = Color.FromArgb(77,88,106),
            Left = 24,
            Top = 5,
            Font = new Font("Segoe UI",8)
        });
        Controls.Add(footer);

        ResumeLayout();
    }

    private Button MakeButton(string text, int width, bool primary)
    {
        var b = new Button { Text=text, Width=width };
        StyleButton(b, primary);
        return b;
    }

    private void StyleButton(Button b, bool primary)
    {
        b.Height = 32;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        b.BackColor = primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44);
        b.ForeColor = Color.White;
    }

    private Panel MakeCard()
    {
        var p = new Panel
        {
            BackColor = Color.FromArgb(17,22,30),
            Margin = new Padding(4),
            Dock = DockStyle.Fill
        };
        p.Paint += (_,e) =>
        {
            using var pen = new Pen(Color.FromArgb(39,47,61));
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0,p.Width-1), Math.Max(0,p.Height-1));
        };
        return p;
    }

    private Label Header(string t) => new()
    {
        Text=t,
        ForeColor=Color.FromArgb(126,137,156),
        AutoSize=true,
        Font=new Font("Segoe UI",9,FontStyle.Bold)
    };

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _updateButton.Enabled = false;
            _updateButton.Text = "Checking…";
            var m = await AppUpdater.CheckAsync();
            if (m == null)
            {
                MessageBox.Show("Update manifest ni dosegljiv.", "Sm0ki Solo Coach");
                return;
            }

            if (!AppUpdater.IsNewer(m.Version))
            {
                MessageBox.Show($"Imaš najnovejšo verzijo ({AppUpdater.CurrentVersion}).", "Sm0ki Solo Coach");
                return;
            }

            var result = MessageBox.Show(
                $"Nova verzija {m.Version} je na voljo.\n\n{m.Notes}\n\nPrenesem in namestim zdaj?",
                "Sm0ki Solo Coach Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (result == DialogResult.Yes)
            {
                _updateButton.Text = "Downloading…";
                await AppUpdater.DownloadAndInstallAsync(m);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Update ni uspel:\n\n" + ex.Message, "Sm0ki Solo Coach", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _updateButton.Enabled = true;
            _updateButton.Text = "Update";
        }
    }

    private Panel MakeCard() => new() { BackColor = Color.FromArgb(23,27,35), Margin = new Padding(4), Dock = DockStyle.Fill };
    private Label Header(string t) => new() { Text=t, ForeColor=Color.FromArgb(145,155,170), AutoSize=true, Font=new Font("Segoe UI",9,FontStyle.Bold) };

    private void OnSnapshot(GameSnapshot s)
    {
        BeginInvoke(() =>
        {
            _previous = _current;
            _current = s;

            if (_previous?.Round is int pr && s.Round is int cr && pr != cr)
            {
                _rounds.Add(new RoundRecord
                {
                    Round = pr,
                    Side = _previous.Team,
                    KillsTotal = _previous.Kills ?? 0,
                    DeathsTotal = _previous.Deaths ?? 0,
                    MoneyEnd = _previous.Money ?? 0
                });
                while (_rounds.Count > 12) _rounds.RemoveAt(0);
            }

            RefreshUi();
        });
    }

    private void RefreshUi()
    {
        if (_phoneServer != null)
            _phoneUrl.Text = "Phone: " + _phoneServer.GetLocalUrl();

        _status.Text = "LIVE";
        _status.ForeColor = Color.FromArgb(126, 240, 174);
        _status.BackColor = Color.FromArgb(20, 56, 39);

        int k = _current.Kills ?? 0;
        int d = _current.Deaths ?? 0;
        double kd = d > 0 ? (double)k/d : k;

        _stats["map"].Text = CoachEngine.PrettyMap(_current.Map);
        _stats["side"].Text = string.IsNullOrWhiteSpace(_current.Team) ? "—" : _current.Team;
        _stats["round"].Text = _current.Round is int r ? $"R{r+1}" : "—";
        _stats["score"].Text = $"{_current.CtScore?.ToString() ?? "—"}:{_current.TScore?.ToString() ?? "—"}";
        _stats["kills"].Text = k.ToString();
        _stats["deaths"].Text = d.ToString();
        _stats["kd"].Text = kd.ToString("0.00");
        _stats["money"].Text = "$" + (_current.Money ?? 0);

        var (title, advice) = CoachEngine.BuyAdvice(_current);
        _buyTitle.Text = title;
        _buyText.Text = advice;
        _tip.Text = CoachEngine.SoloTip(_current, _mode.SelectedItem?.ToString() ?? "Balanced");

        _history.Items.Clear();
        int pk=0, pd=0;
        foreach (var rr in _rounds.TakeLast(10))
        {
            int dk = Math.Max(0, rr.KillsTotal-pk);
            int dd = Math.Max(0, rr.DeathsTotal-pd);
            _history.Items.Add($"R{rr.Round+1:00}   {rr.Side,2}   +{dk}K +{dd}D   ${rr.MoneyEnd}");
            pk=rr.KillsTotal;
            pd=rr.DeathsTotal;
        }
        if (_history.Items.Count==0) _history.Items.Add("No completed rounds tracked yet.");
    }
}
