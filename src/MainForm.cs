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
        var top = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(18,16,18,8) };
        var title = new Label { Text = "Sm0ki Solo Coach", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), Left = 18, Top = 16 };
        _status.Text = "OFFLINE";
        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _status.ForeColor = Color.OrangeRed;
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.Top = 22;
        _status.Left = 800;
        top.Controls.Add(title);
        top.Controls.Add(_status);
        Controls.Add(top);

        var controls = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(18,6,18,6) };
        controls.Controls.Add(new Label { Text = "Coach mode:", AutoSize = true, Left = 18, Top = 14 });

        _mode.Items.AddRange(new object[] { "Balanced", "Aggressive", "Safe" });
        _mode.SelectedIndex = 0;
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Left = 120;
        _mode.Top = 8;
        _mode.Width = 140;
        controls.Controls.Add(_mode);

        var install = new Button { Text = "Refresh GSI", Width = 120, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        install.Left = 795;
        install.Top = 7;
        install.Click += (_,__) =>
        {
            var p = GsiInstaller.TryInstall();
            MessageBox.Show(p != null ? $"Installed to:\n{p}\n\nRestart CS2 if already open." : "CS2 cfg folder was not found automatically.",
                "Sm0ki Solo Coach");
        };
        controls.Controls.Add(install);

        var phone = new Button { Text = "Phone Mode", Width = 110, Height = 32 };
        phone.Left = 585;
        phone.Top = 7;
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
        controls.Controls.Add(phone);

        _updateButton.Text = "Update";
        _updateButton.Width = 85;
        _updateButton.Height = 32;
        _updateButton.Left = 700;
        _updateButton.Top = 7;
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        controls.Controls.Add(_updateButton);

        _phoneUrl.AutoSize = true;
        _phoneUrl.Left = 275;
        _phoneUrl.Top = 14;
        _phoneUrl.ForeColor = Color.FromArgb(145,155,170);
        controls.Controls.Add(_phoneUrl);

        Controls.Add(controls);

        var statPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 96, ColumnCount = 8, Padding = new Padding(18,8,18,8) };
        for (int i=0;i<8;i++) statPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,12.5f));
        var items = new[] { ("map","MAP"),("side","SIDE"),("round","ROUND"),("score","SCORE"),("kills","KILLS"),("deaths","DEATHS"),("kd","K/D"),("money","MONEY") };
        for (int i=0;i<items.Length;i++)
        {
            var card = MakeCard();
            var small = new Label { Text = items[i].Item2, ForeColor = Color.FromArgb(145,155,170), AutoSize=true, Top=8, Left=8, Font=new Font("Segoe UI",8) };
            var val = new Label { Text = "—", AutoSize=true, Top=30, Left=8, Font=new Font("Segoe UI",13,FontStyle.Bold) };
            card.Controls.Add(small);
            card.Controls.Add(val);
            _stats[items[i].Item1] = val;
            statPanel.Controls.Add(card,i,0);
        }
        Controls.Add(statPanel);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18,8,18,18) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));

        var left = MakeCard();
        var right = MakeCard();

        var buyHdr = Header("NEXT BUY"); buyHdr.Top=16; buyHdr.Left=16;
        _buyTitle.Text="Waiting for CS2…"; _buyTitle.Left=16; _buyTitle.Top=48; _buyTitle.Width=390; _buyTitle.Font=new Font("Segoe UI",17,FontStyle.Bold);
        _buyText.Left=16; _buyText.Top=90; _buyText.Width=390; _buyText.Height=120; _buyText.ForeColor=Color.FromArgb(220,225,235);

        var coachHdr=Header("SOLO AVG-KILLS COACH"); coachHdr.Top=240; coachHdr.Left=16;
        _tip.Left=16; _tip.Top=275; _tip.Width=390; _tip.Height=180; _tip.ForeColor=Color.FromArgb(220,225,235);

        left.Controls.Add(buyHdr); left.Controls.Add(_buyTitle); left.Controls.Add(_buyText); left.Controls.Add(coachHdr); left.Controls.Add(_tip);

        var histHdr=Header("ROUND HISTORY"); histHdr.Top=16; histHdr.Left=16;
        _history.Left=16; _history.Top=48; _history.Width=410; _history.Height=400;
        _history.BackColor=Color.FromArgb(17,21,28); _history.ForeColor=Color.FromArgb(220,225,235); _history.BorderStyle=BorderStyle.None;
        _history.Font=new Font("Consolas",10);
        right.Controls.Add(histHdr); right.Controls.Add(_history);

        body.Controls.Add(left,0,0);
        body.Controls.Add(right,1,0);
        Controls.Add(body);
    }

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
        _status.ForeColor = Color.LightGreen;

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
