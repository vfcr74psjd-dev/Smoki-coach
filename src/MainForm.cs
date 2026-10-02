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
    private readonly ComboBox _role = new();
    private readonly ComboBox _focus = new();
    private readonly CheckBox _autoAi = new();
    private readonly Label _sessionText = new();
    private readonly CoachPreferences _prefs = UserSettingsStore.Load();
    private readonly GsiServer _server = new();
    private PhoneDashboardServer? _phoneServer;
    private readonly Label _phoneUrl = new();
    private readonly Button _updateButton = new();
    private readonly Label _aiText = new();
    private readonly Label _aiStatus = new();
    private readonly AiCoachService _aiCoach = new();
    private CancellationTokenSource? _aiCts;
    private string _latestAiAdvice = "AI coach čaka na nastavitev.";
    private int? _lastAiRound;
    private string _lastAiMap = "";
    private int? _trackedRound;
    private int _roundStartKills;
    private int _roundStartDeaths;

    private GameSnapshot _current = new();
    private GameSnapshot? _previous;
    private readonly List<RoundRecord> _rounds = new();

    public MainForm()
    {
        Text = "Sm0ki Solo Coach";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(1020, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14,17,23);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();

        _server.SnapshotReceived += OnSnapshot;
        _server.Start();
        Text = $"Sm0ki Solo Coach • GSI {_server.BoundPort}";

        _phoneServer = new PhoneDashboardServer(
            () => _current,
            () => _mode.SelectedItem?.ToString() ?? "Balanced",
            () => _role.SelectedItem?.ToString() ?? "Flex",
            () => _focus.SelectedItem?.ToString() ?? "More kills",
            () => _latestAiAdvice
        );
        _phoneServer.Start();
        UpdateAiStatus();

        var path = GsiInstaller.TryInstall();
        if (path == null)
            _status.Text = "OFFLINE • GSI NOT INSTALLED";

        Shown += async (_,__) => await CheckForUpdatesSilentAsync();

        FormClosing += (_,__) =>
        {
            _aiCts?.Cancel();
            _aiCts?.Dispose();
            _phoneServer?.Dispose();
            _server.Dispose();
        };
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.FromArgb(10,13,19),
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24, 12, 24, 10),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(14,18,26)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var accent = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 9, 8, 8),
            BackColor = Color.FromArgb(104,92,255)
        };
        brand.Controls.Add(accent, 0, 0);
        brand.SetRowSpan(accent, 2);

        var title = new Label
        {
            Text = "Sm0ki Solo Coach",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = Padding.Empty
        };
        brand.Controls.Add(title, 1, 0);

        var subtitle = new Label
        {
            Text = "LIVE CS2 • SOLO PERFORMANCE COMPANION",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(1, 2, 0, 0)
        };
        brand.Controls.Add(subtitle, 1, 1);
        header.Controls.Add(brand, 0, 0);

        var statusHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _status.Text = "OFFLINE";
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Width = 104;
        _status.Height = 32;
        _status.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _status.ForeColor = Color.FromArgb(255,150,130);
        _status.BackColor = Color.FromArgb(55,28,28);
        _status.Anchor = AnchorStyles.None;
        statusHost.Controls.Add(_status);
        statusHost.Resize += (_,__) =>
        {
            _status.Left = Math.Max(0, (statusHost.ClientSize.Width - _status.Width) / 2);
            _status.Top = Math.Max(0, (statusHost.ClientSize.Height - _status.Height) / 2);
        };
        header.Controls.Add(statusHost, 1, 0);
        root.Controls.Add(header, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = false,
            Padding = new Padding(18, 10, 18, 8),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(11,15,22)
        };

        _mode.Items.Clear();
        _mode.Items.AddRange(new object[] { "Balanced", "Aggressive", "Safe" });
        _mode.SelectedItem = _prefs.Mode;
        if (_mode.SelectedIndex < 0) _mode.SelectedIndex = 0;
        ConfigureCombo(_mode);
        _mode.SelectedIndexChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(MakeSelector("COACH MODE", _mode, 150));

        _role.Items.Clear();
        _role.Items.AddRange(new object[] { "Flex", "Entry", "Lurk", "Support", "Anchor" });
        _role.SelectedItem = _prefs.Role;
        if (_role.SelectedIndex < 0) _role.SelectedIndex = 0;
        ConfigureCombo(_role);
        _role.SelectedIndexChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(MakeSelector("ROLE", _role, 120));

        _focus.Items.Clear();
        _focus.Items.AddRange(new object[] { "More kills", "Survive & trade", "Entry impact", "Utility impact", "Clutch / late round" });
        _focus.SelectedItem = _prefs.Focus;
        if (_focus.SelectedIndex < 0) _focus.SelectedIndex = 0;
        ConfigureCombo(_focus);
        _focus.SelectedIndexChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(MakeSelector("FOCUS", _focus, 175));

        _updateButton.Text = "Update";
        StyleButton(_updateButton, true);
        _updateButton.Width = 100;
        _updateButton.Margin = new Padding(5, 22, 5, 0);
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        toolbar.Controls.Add(_updateButton);

        var refresh = MakeButton("Refresh GSI", 112, false);
        refresh.Margin = new Padding(5, 22, 5, 0);
        refresh.Click += (_,__) =>
        {
            var p = GsiInstaller.TryInstall();
            MessageBox.Show(
                p != null ? $"GSI config je osvežen:\n{p}\n\nČe je CS2 odprt, ga ponovno zaženi."
                          : "CS2 cfg mape nisem našel samodejno.",
                "Sm0ki Solo Coach"
            );
        };
        toolbar.Controls.Add(refresh);

        var phone = MakeButton("Phone QR", 108, false);
        phone.Margin = new Padding(5, 22, 5, 0);
        phone.Click += (_,__) =>
        {
            if (_phoneServer == null) return;
            using var qr = new PhoneQrForm(_phoneServer.GetLocalUrl());
            qr.ShowDialog(this);
        };
        toolbar.Controls.Add(phone);

        var aiSettings = MakeButton("Local AI", 102, false);
        aiSettings.Margin = new Padding(5, 22, 5, 0);
        aiSettings.Click += (_,__) =>
        {
            using var dialog = new AiSettingsForm();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                UpdateAiStatus();
                if (_current.Round is int && _autoAi.Checked)
                    _ = RefreshAiCoachAsync(_current, true);
            }
        };
        toolbar.Controls.Add(aiSettings);

        var autoAiHost = new Panel
        {
            Width = 96,
            Height = 50,
            Margin = new Padding(5, 16, 5, 0),
            BackColor = Color.Transparent
        };
        _autoAi.Text = "Auto AI";
        _autoAi.Dock = DockStyle.Fill;
        _autoAi.Checked = _prefs.AutoAi;
        _autoAi.ForeColor = Color.FromArgb(205,211,221);
        _autoAi.BackColor = Color.Transparent;
        _autoAi.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _autoAi.TextAlign = ContentAlignment.MiddleLeft;
        _autoAi.CheckedChanged += (_,__) => PreferencesChanged();
        autoAiHost.Controls.Add(_autoAi);
        toolbar.Controls.Add(autoAiHost);

        _phoneUrl.Visible = false;
        toolbar.Controls.Add(_phoneUrl);

        root.Controls.Add(toolbar, 0, 1);

        var statGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(18, 9, 18, 9),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(10,13,19)
        };
        for (int i=0;i<4;i++)
            statGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        statGrid.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        statGrid.RowStyles.Add(new RowStyle(SizeType.Percent,50));

        _stats.Clear();
        var items = new[]
        {
            ("map","MAP"),("side","SIDE"),("round","ROUND"),("score","SCORE"),
            ("kills","KILLS"),("deaths","DEATHS"),("kd","K / D"),("money","MONEY")
        };
        for (int i=0;i<items.Length;i++)
        {
            var card = MakeStatCard(items[i].Item2, out var value);
            _stats[items[i].Item1] = value;
            statGrid.Controls.Add(card, i % 4, i / 4);
        }
        root.Controls.Add(statGrid, 0, 2);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 6, 18, 12),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(10,13,19)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var leftCard = MakeCard();
        leftCard.Margin = new Padding(4,4,8,4);
        leftCard.Padding = new Padding(20);

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Margin = Padding.Empty
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        leftLayout.Controls.Add(Header("NEXT BUY"), 0, 0);
        _buyTitle.Text = "Waiting for CS2…";
        _buyTitle.Dock = DockStyle.Fill;
        _buyTitle.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        _buyTitle.ForeColor = Color.FromArgb(240,242,247);
        _buyTitle.TextAlign = ContentAlignment.MiddleLeft;
        leftLayout.Controls.Add(_buyTitle, 0, 1);

        _buyText.Dock = DockStyle.Fill;
        _buyText.ForeColor = Color.FromArgb(190,198,211);
        _buyText.Font = new Font("Segoe UI", 10);
        _buyText.Padding = new Padding(0, 4, 0, 0);
        leftLayout.Controls.Add(_buyText, 0, 2);
        leftLayout.Controls.Add(MakeDivider(), 0, 3);

        var aiHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        aiHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        aiHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        aiHeader.Controls.Add(Header("AI ROUND COACH"), 0, 0);
        _aiStatus.AutoSize = true;
        _aiStatus.Anchor = AnchorStyles.Right;
        _aiStatus.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        aiHeader.Controls.Add(_aiStatus, 1, 0);
        leftLayout.Controls.Add(aiHeader, 0, 4);

        _aiText.Dock = DockStyle.Fill;
        _aiText.AutoEllipsis = true;
        _aiText.ForeColor = Color.FromArgb(220,225,234);
        _aiText.Font = new Font("Segoe UI", 10);
        _aiText.Padding = new Padding(0, 5, 0, 0);
        _aiText.Text = "AI coach čaka na nastavitev.";
        leftLayout.Controls.Add(_aiText, 0, 5);

        leftLayout.Controls.Add(MakeDivider(), 0, 6);
        leftLayout.Controls.Add(Header("LOCAL FALLBACK COACH"), 0, 7);

        _tip.Dock = DockStyle.Fill;
        _tip.AutoEllipsis = true;
        _tip.ForeColor = Color.FromArgb(170,180,195);
        _tip.Font = new Font("Segoe UI", 9);
        _tip.Padding = new Padding(0, 4, 0, 0);
        leftLayout.Controls.Add(_tip, 0, 8);
        leftCard.Controls.Add(leftLayout);

        var rightCard = MakeCard();
        rightCard.Margin = new Padding(8,4,4,4);
        rightCard.Padding = new Padding(20);

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        rightLayout.Controls.Add(Header("ROUND HISTORY"), 0, 0);
        rightLayout.Controls.Add(new Label
        {
            Text = "SESSION INSIGHT + RECENT ROUND DELTAS",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(96,108,128),
            Font = new Font("Segoe UI", 8, FontStyle.Bold)
        }, 0, 1);

        _sessionText.Dock = DockStyle.Fill;
        _sessionText.ForeColor = Color.FromArgb(170,180,195);
        _sessionText.Font = new Font("Segoe UI", 9);
        _sessionText.Padding = new Padding(0, 5, 0, 5);
        rightLayout.Controls.Add(_sessionText, 0, 2);

        _history.Dock = DockStyle.Fill;
        _history.BackColor = Color.FromArgb(17,22,30);
        _history.ForeColor = Color.FromArgb(206,213,223);
        _history.BorderStyle = BorderStyle.None;
        _history.IntegralHeight = false;
        _history.Font = new Font("Cascadia Mono", 10);
        rightLayout.Controls.Add(_history, 0, 3);
        rightCard.Controls.Add(rightLayout);

        body.Controls.Add(leftCard, 0, 0);
        body.Controls.Add(rightCard, 1, 0);
        root.Controls.Add(body, 0, 3);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(22, 4, 22, 3),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(10,13,19)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label
        {
            Text = $"v{AppUpdater.CurrentVersion}  •  CS2 Game State Integration",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(77,88,106),
            Font = new Font("Segoe UI",8)
        }, 0, 0);
        footer.Controls.Add(new Label
        {
            Text = "LOCAL • PRIVATE • NO PAID API",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            ForeColor = Color.FromArgb(91,105,126),
            Font = new Font("Segoe UI",8,FontStyle.Bold)
        }, 1, 0);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private Panel MakeSelector(string title, ComboBox combo, int width)
    {
        var panel = new Panel
        {
            Width = width,
            Height = 62,
            Margin = new Padding(5, 0, 5, 0),
            BackColor = Color.Transparent
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI",8,FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        });
        combo.Dock = DockStyle.Bottom;
        combo.Height = 32;
        panel.Controls.Add(combo);
        return panel;
    }

    private static void ConfigureCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Color.FromArgb(24,29,39);
        combo.ForeColor = Color.White;
        combo.Font = new Font("Segoe UI", 9);
    }

    private Panel MakeStatCard(string caption, out Label value)
    {
        var card = MakeCard();
        card.Margin = new Padding(5);
        card.Padding = new Padding(12, 7, 12, 7);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(113,125,146),
            Font = new Font("Segoe UI",8,FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        },0,0);

        value = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",15,FontStyle.Bold),
            ForeColor = Color.FromArgb(241,244,249),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        layout.Controls.Add(value,0,1);
        card.Controls.Add(layout);
        return card;
    }

    private static Panel MakeDivider() => new()
    {
        Dock = DockStyle.Fill,
        Height = 1,
        Margin = new Padding(0, 6, 0, 6),
        BackColor = Color.FromArgb(43,50,64)
    };

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
        Dock=DockStyle.Fill,
        TextAlign=ContentAlignment.MiddleLeft,
        Font=new Font("Segoe UI",9,FontStyle.Bold)
    };

    private void UpdateAiStatus()
    {
        if (_aiCoach.IsConfigured)
        {
            _aiStatus.Text = "LOCAL AI";
            _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            if (_latestAiAdvice == "AI coach čaka na nastavitev.")
                _latestAiAdvice = "Local AI je na voljo. V Local AI meniju klikni Prepare Local AI, nato se plan ustvari ob začetku runde.";
        }
        else
        {
            _aiStatus.Text = "AI OFF";
            _aiStatus.ForeColor = Color.FromArgb(255, 170, 140);
            _latestAiAdvice = "Klikni Local AI in namesti brezplačni lokalni AI.";
        }

        _aiText.Text = _latestAiAdvice;
    }

    private async Task RefreshAiCoachAsync(GameSnapshot snapshot, bool force = false)
    {
        if (!_aiCoach.IsConfigured)
        {
            UpdateAiStatus();
            return;
        }

        if (!force && snapshot.Round == null) return;

        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();

        var requestedMap = snapshot.Map;
        var requestedRound = snapshot.Round;

        try
        {
            _aiStatus.Text = "AI THINKING";
            _aiStatus.ForeColor = Color.FromArgb(176, 166, 255);
            _latestAiAdvice = "Pripravljam plan za to rundo…";
            _aiText.Text = _latestAiAdvice;

            var advice = await _aiCoach.GenerateRoundAdviceAsync(
                snapshot,
                _rounds.ToList(),
                $"{_mode.SelectedItem?.ToString() ?? "Balanced"} | Role={_role.SelectedItem?.ToString() ?? "Flex"} | Focus={_focus.SelectedItem?.ToString() ?? "More kills"}",
                _aiCts.Token
            );

            if (_current.Map != requestedMap || _current.Round != requestedRound)
                return;

            _latestAiAdvice = advice;
            _aiText.Text = advice;
            _aiStatus.Text = "AI LIVE";
            _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _latestAiAdvice = ex.Message;
            _aiText.Text = _latestAiAdvice;
            _aiStatus.Text = "AI ERROR";
            _aiStatus.ForeColor = Color.FromArgb(255, 150, 130);
        }
    }

    private void PreferencesChanged()
    {
        _prefs.Mode = _mode.SelectedItem?.ToString() ?? "Balanced";
        _prefs.Role = _role.SelectedItem?.ToString() ?? "Flex";
        _prefs.Focus = _focus.SelectedItem?.ToString() ?? "More kills";
        _prefs.AutoAi = _autoAi.Checked;
        UserSettingsStore.Save(_prefs);

        if (_current.Round is int && _aiCoach.IsConfigured && _autoAi.Checked)
            _ = RefreshAiCoachAsync(_current, true);

        if (!string.IsNullOrWhiteSpace(_current.Map))
            RefreshUi();
    }

    private string RoleFocusTip()
    {
        var role = _role.SelectedItem?.ToString() ?? "Flex";
        var focus = _focus.SelectedItem?.ToString() ?? "More kills";

        var roleTip = role switch
        {
            "Entry" => "Entry: prvi kontakt vzemi samo, ko si tradeable; po entry killu ne sili drugega duela.",
            "Lurk" => "Lurk: drži rotacijo/info, vendar se pravočasno priključi ekipi.",
            "Support" => "Support: utility naj odpre duel, nato takoj sledi za trade.",
            "Anchor" => "Anchor: igraj pozicijo z možnostjo umika in kupi čas pred smrtjo.",
            _ => "Flex: izberi nalogo glede na spawn in teammate pozicije."
        };

        var focusTip = focus switch
        {
            "Survive & trade" => "Focus: survival po prvem kontaktu in trade distance.",
            "Entry impact" => "Focus: ustvari prostor zgodaj, vendar z utilityjem ali trade podporo.",
            "Utility impact" => "Focus: utility uporabi pred izpostavitvijo, ne po izgubljenem duelu.",
            "Clutch / late round" => "Focus: ohrani HP, info in utility za pozno rundo.",
            _ => "Focus: en kakovosten opening/trade kill, nato varna druga pozicija."
        };

        return roleTip + " " + focusTip;
    }

    private string SessionInsight()
    {
        var sample = _rounds.TakeLast(10).ToList();
        if (sample.Count == 0)
            return "Po nekaj zaključenih rundah bom tukaj prikazal K/R, 0-kill runde, multi-kille in survival trend.";

        int kills = sample.Sum(r => r.KillsRound);
        int zero = sample.Count(r => r.KillsRound == 0);
        int multi = sample.Count(r => r.KillsRound >= 2);
        int deathRounds = sample.Count(r => r.DeathsRound > 0);
        double kr = (double)kills / sample.Count;
        double survival = 100.0 * (sample.Count - deathRounds) / sample.Count;

        string trend =
            kr >= 1.0 ? "Impact je trenutno visok."
            : kr >= 0.7 ? "Solidno — išči še en varen trade na rundo."
            : "Zmanjšaj early deaths in igraj bližje trade razdalji.";

        return $"Last {sample.Count} rounds • {kr:0.00} K/R • {zero} zero-kill • {multi} multi-kill • {survival:0}% survival. {trend}";
    }

    private async Task CheckForUpdatesSilentAsync()
    {
        try
        {
            var manifest = await AppUpdater.CheckAsync();
            if (manifest != null && AppUpdater.IsNewer(manifest.Version))
                _updateButton.Text = "Update " + manifest.Version;
        }
        catch { }
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

    private void OnSnapshot(GameSnapshot s)
    {
        BeginInvoke(() =>
        {
            _previous = _current;
            _current = s;

            bool mapChanged =
                !string.IsNullOrWhiteSpace(_previous.Map) &&
                !string.IsNullOrWhiteSpace(s.Map) &&
                !string.Equals(_previous.Map, s.Map, StringComparison.OrdinalIgnoreCase);

            if (mapChanged)
            {
                _rounds.Clear();
                _trackedRound = s.Round;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
            }
            else if (_trackedRound == null && s.Round is int initialRound)
            {
                _trackedRound = initialRound;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
            }

            if (!mapChanged && _previous.Round is int pr && s.Round is int cr && pr != cr)
            {
                if (cr < pr)
                {
                    _rounds.Clear();
                }
                else
                {
                    _rounds.Add(new RoundRecord
                    {
                        Round = pr,
                        Side = _previous.Team,
                        KillsTotal = _previous.Kills ?? 0,
                        DeathsTotal = _previous.Deaths ?? 0,
                        MoneyEnd = _previous.Money ?? 0,
                        KillsRound = Math.Max(0, (_previous.Kills ?? 0) - _roundStartKills),
                        DeathsRound = Math.Max(0, (_previous.Deaths ?? 0) - _roundStartDeaths)
                    });
                    while (_rounds.Count > 12) _rounds.RemoveAt(0);
                }

                _trackedRound = cr;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
            }

            bool requestAi =
                s.Round is int aiRound &&
                (_lastAiRound != aiRound ||
                 !string.Equals(_lastAiMap, s.Map, StringComparison.OrdinalIgnoreCase));

            RefreshUi();

            if (requestAi && _autoAi.Checked)
            {
                _lastAiRound = s.Round;
                _lastAiMap = s.Map;
                _ = RefreshAiCoachAsync(s);
            }
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
        _tip.Text = CoachEngine.SoloTip(_current, _mode.SelectedItem?.ToString() ?? "Balanced")
            + " " + RoleFocusTip();
        _sessionText.Text = SessionInsight();

        _history.Items.Clear();
        foreach (var rr in _rounds.TakeLast(10))
        {
            _history.Items.Add($"R{rr.Round+1:00}   {rr.Side,2}   +{rr.KillsRound}K +{rr.DeathsRound}D   $" + rr.MoneyEnd);
        }
        if (_history.Items.Count==0) _history.Items.Add("No completed rounds tracked yet.");
    }
}
