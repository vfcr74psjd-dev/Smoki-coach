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
    private UserProfile _profile = UserProfileStore.Load();
    private readonly GsiServer _server = new();
    private PhoneDashboardServer? _phoneServer;
    private readonly Label _phoneUrl = new();
    private readonly Button _updateButton = new();
    private readonly Label _aiText = new();
    private readonly Label _aiStatus = new();
    private readonly Label _liveContext = new();
    private readonly Label _profileBadge = new();
    private readonly Label _gsiHealth = new();
    private readonly Label _aiHealth = new();
    private readonly FlowLayoutPanel _roundTimeline = new();
    private readonly Label _faceitElo = new();
    private readonly Label _faceitLevel = new();
    private readonly Label _faceitAvgKills = new();
    private readonly Label _faceitKd = new();
    private readonly Label _faceitTrend = new();
    private readonly AiCoachService _aiCoach = new();
    private FaceitSnapshot? _faceitSnapshot;
    private DateTime _faceitLoadedUtc = DateTime.MinValue;
    private bool _faceitLoading;
    private CancellationTokenSource? _aiCts;
    private string _latestAiAdvice = "AI coach čaka na nastavitev.";
    private int? _lastAiRound;
    private string _lastAiMap = "";
    private int? _trackedRound;
    private int _roundStartKills;
    private int _roundStartDeaths;
    private DateTime _sessionStartedUtc = DateTime.UtcNow;

    private GameSnapshot _current = new();
    private GameSnapshot? _previous;
    private readonly List<RoundRecord> _rounds = new();

    public MainForm()
    {
        Text = "Sm0ki Solo Coach";
        Width = 1380;
        Height = 860;
        MinimumSize = new Size(1160, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14,17,23);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();

        _server.SnapshotReceived += OnSnapshot;
        _server.Start();
        Text = $"Sm0ki Solo Coach • {_profile.Nickname} • GSI {_server.BoundPort}";

        _phoneServer = new PhoneDashboardServer(
            () => _current,
            () => _mode.SelectedItem?.ToString() ?? "Balanced",
            () => _role.SelectedItem?.ToString() ?? "Flex",
            () => _focus.SelectedItem?.ToString() ?? "More kills",
            () => _profile.Nickname,
            () => _autoAi.Checked,
            () => _latestAiAdvice,
            ApplyPhoneSettings
        );
        _phoneServer.Start();
        UpdateAiStatus();

        var path = GsiInstaller.TryInstall();
        if (path == null)
            _status.Text = "OFFLINE • GSI NOT INSTALLED";

        Shown += async (_,__) =>
        {
            await CheckForUpdatesSilentAsync();
            _ = AiCoachService.WarmUpAsync();
            _ = LoadFaceitSnapshotAsync();
        };

        FormClosing += (_,__) =>
        {
            ArchiveCurrentSession(_current.Map, _current);
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
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(8, 11, 17)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // LEFT NAVIGATION
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = new Padding(14, 16, 14, 14),
            BackColor = Color.FromArgb(12, 16, 23)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var brand = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        brand.Controls.Add(new Label
        {
            Text = "SM0KI",
            Left = 2,
            Top = 5,
            Width = 170,
            Height = 35,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.White
        });
        brand.Controls.Add(new Label
        {
            Text = "SOLO COACH  /  PRO",
            Left = 4,
            Top = 44,
            Width = 180,
            Height = 20,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(118, 108, 255)
        });
        sidebar.Controls.Add(brand, 0, 0);

        var profileCard = MakeCard();
        profileCard.Margin = new Padding(0, 2, 0, 10);
        profileCard.Padding = new Padding(12, 9, 12, 8);
        var profileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        profileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        profileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        _profileBadge.Text = string.IsNullOrWhiteSpace(_profile.Nickname) ? "PLAYER" : _profile.Nickname;
        _profileBadge.Dock = DockStyle.Fill;
        _profileBadge.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _profileBadge.ForeColor = Color.White;
        _profileBadge.TextAlign = ContentAlignment.BottomLeft;
        profileLayout.Controls.Add(_profileBadge, 0, 0);
        profileLayout.Controls.Add(new Label
        {
            Text = "LOCAL PLAYER PROFILE",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(108, 121, 143)
        }, 0, 1);
        profileCard.Controls.Add(profileLayout);
        sidebar.Controls.Add(profileCard, 0, 1);

        sidebar.Controls.Add(new Label
        {
            Text = "WORKSPACE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(91, 104, 126),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        }, 0, 2);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0)
        };

        nav.Controls.Add(MakeNavButton("LIVE COACH", true, (_,__) => { }));

        nav.Controls.Add(MakeNavButton("REVIEW LAB", false, (_,__) =>
        {
            using var dialog = new ReviewLabForm(_profile.Nickname);
            dialog.ShowDialog(this);
        }));

        nav.Controls.Add(MakeNavButton("PROGRESS", false, async (_,__) =>
        {
            await LoadFaceitSnapshotAsync();
            using var dialog = new SessionAnalyticsForm(
                _profile.Nickname,
                _current.Map,
                _rounds.ToList(),
                _faceitSnapshot);
            dialog.ShowDialog(this);
        }));

        nav.Controls.Add(MakeNavButton("FACEIT", false, async (_,__) =>
        {
            using var dialog = new FaceitForm(_profile.Nickname);
            dialog.ShowDialog(this);
            await LoadFaceitSnapshotAsync(true);
        }));

        nav.Controls.Add(MakeNavButton("PHONE COMPANION", false, (_,__) =>
        {
            if (_phoneServer == null) return;
            using var qr = new PhoneQrForm(_phoneServer.GetLocalUrl());
            qr.ShowDialog(this);
        }));

        nav.Controls.Add(MakeNavButton("PLAYER PROFILE", false, (_,__) => EditProfile()));
        sidebar.Controls.Add(nav, 0, 3);

        var health = MakeCard();
        health.Margin = new Padding(0, 8, 0, 8);
        health.Padding = new Padding(12, 8, 12, 8);
        var healthLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        healthLayout.Controls.Add(new Label
        {
            Text = "SYSTEM HEALTH",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(99, 112, 134)
        }, 0, 0);

        _gsiHealth.Text = "● GSI WAITING";
        _gsiHealth.Dock = DockStyle.Fill;
        _gsiHealth.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _gsiHealth.ForeColor = Color.FromArgb(244, 155, 121);
        _gsiHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_gsiHealth, 0, 1);

        _aiHealth.Text = "● FAST AI CHECK";
        _aiHealth.Dock = DockStyle.Fill;
        _aiHealth.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _aiHealth.ForeColor = Color.FromArgb(160, 170, 187);
        _aiHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_aiHealth, 0, 2);
        health.Controls.Add(healthLayout);
        sidebar.Controls.Add(health, 0, 4);

        sidebar.Controls.Add(new Label
        {
            Text = $"v{AppUpdater.CurrentVersion}  •  LOCAL / PRIVATE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.FromArgb(73, 84, 103),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        }, 0, 5);

        root.Controls.Add(sidebar, 0, 0);

        // MAIN LIVE COACH WORKSPACE
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(8, 11, 17)
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(24, 14, 24, 8),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(10, 14, 21)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));

        var liveTitle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        liveTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        liveTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        liveTitle.Controls.Add(new Label
        {
            Text = "LIVE COACH",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);

        _liveContext.Text = "Waiting for CS2 telemetry…";
        _liveContext.Dock = DockStyle.Fill;
        _liveContext.Font = new Font("Segoe UI", 9);
        _liveContext.ForeColor = Color.FromArgb(123, 137, 159);
        _liveContext.TextAlign = ContentAlignment.TopLeft;
        liveTitle.Controls.Add(_liveContext, 0, 1);
        top.Controls.Add(liveTitle, 0, 0);

        _status.Text = "WAITING";
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(8, 13, 8, 13);
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _status.ForeColor = Color.FromArgb(248, 174, 143);
        _status.BackColor = Color.FromArgb(54, 34, 31);
        top.Controls.Add(_status, 1, 0);

        _updateButton.Text = "UPDATE";
        StyleButton(_updateButton, false);
        _updateButton.Dock = DockStyle.Fill;
        _updateButton.Margin = new Padding(8, 13, 0, 13);
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        top.Controls.Add(_updateButton, 2, 0);
        workspace.Controls.Add(top, 0, 0);

        // QUICK PRESETS
        var presets = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24, 10, 24, 10),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(8, 11, 17)
        };
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        presets.Controls.Add(new Label
        {
            Text = "COACH PRESET",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(99, 113, 135),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var presetFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        presetFlow.Controls.Add(MakePresetButton("MORE KILLS", "More Kills"));
        presetFlow.Controls.Add(MakePresetButton("ENTRY", "Entry"));
        presetFlow.Controls.Add(MakePresetButton("SURVIVAL", "Survival"));
        presetFlow.Controls.Add(MakePresetButton("UTILITY", "Utility"));
        presetFlow.Controls.Add(MakePresetButton("CLUTCH", "Clutch"));
        presets.Controls.Add(presetFlow, 1, 0);
        workspace.Controls.Add(presets, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24, 4, 24, 14),
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        // LEFT / primary live information.
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 8, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));

        var statsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = Padding.Empty
        };
        for (int i=0;i<4;i++) statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _stats.Clear();
        var statItems = new[]
        {
            ("map","MAP"), ("side","SIDE"), ("round","ROUND"), ("score","SCORE"),
            ("kills","KILLS"), ("deaths","DEATHS"), ("kd","K / D"), ("money","MONEY")
        };
        for (int i=0;i<statItems.Length;i++)
        {
            var card = MakeStatCard(statItems[i].Item2, out var value);
            card.Margin = new Padding(i % 4 == 0 ? 0 : 5, 4, i % 4 == 3 ? 0 : 5, 4);
            _stats[statItems[i].Item1] = value;
            statsGrid.Controls.Add(card, i % 4, i / 4);
        }
        left.Controls.Add(statsGrid, 0, 0);

        var planCard = MakeCard();
        planCard.Margin = new Padding(0, 7, 0, 7);
        planCard.Padding = new Padding(22, 16, 22, 18);

        var planLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var planHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        planHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        planHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        planHeader.Controls.Add(new Label
        {
            Text = "NEXT ROUND PLAN",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(119, 130, 151),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _aiStatus.Text = "FAST PLAN";
        _aiStatus.AutoSize = true;
        _aiStatus.Anchor = AnchorStyles.Right;
        _aiStatus.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
        planHeader.Controls.Add(_aiStatus, 1, 0);
        planLayout.Controls.Add(planHeader, 0, 0);

        planLayout.Controls.Add(new Label
        {
            Text = "WIN THE NEXT ROUND",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _aiText.Text = "CS2 še ni poslal trenutne runde. Ko se match začne, bo plan tukaj takoj.";
        _aiText.Dock = DockStyle.Fill;
        _aiText.Font = new Font("Segoe UI", 11.5f, FontStyle.Regular);
        _aiText.ForeColor = Color.FromArgb(220, 226, 235);
        _aiText.Padding = new Padding(0, 8, 0, 4);
        _aiText.AutoEllipsis = true;
        planLayout.Controls.Add(_aiText, 0, 2);

        var noteLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        noteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        noteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        noteLayout.Controls.Add(new Label
        {
            Text = "COACH NOTE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(118, 108, 255),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _tip.Dock = DockStyle.Fill;
        _tip.Font = new Font("Segoe UI", 8.5f);
        _tip.ForeColor = Color.FromArgb(145, 156, 174);
        _tip.AutoEllipsis = true;
        _tip.TextAlign = ContentAlignment.MiddleLeft;
        noteLayout.Controls.Add(_tip, 1, 0);
        planLayout.Controls.Add(noteLayout, 0, 3);

        planCard.Controls.Add(planLayout);
        left.Controls.Add(planCard, 0, 1);

        var timelineCard = MakeCard();
        timelineCard.Margin = new Padding(0, 7, 0, 0);
        timelineCard.Padding = new Padding(18, 12, 18, 12);
        var timelineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timelineLayout.Controls.Add(new Label
        {
            Text = "ROUND TIMELINE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 124, 145)
        }, 0, 0);

        _roundTimeline.Dock = DockStyle.Fill;
        _roundTimeline.FlowDirection = FlowDirection.LeftToRight;
        _roundTimeline.WrapContents = false;
        _roundTimeline.AutoScroll = true;
        _roundTimeline.Margin = Padding.Empty;
        _roundTimeline.Padding = new Padding(0, 4, 0, 0);
        timelineLayout.Controls.Add(_roundTimeline, 0, 1);

        _sessionText.Dock = DockStyle.Fill;
        _sessionText.Font = new Font("Segoe UI", 8.5f);
        _sessionText.ForeColor = Color.FromArgb(137, 149, 168);
        _sessionText.TextAlign = ContentAlignment.MiddleLeft;
        timelineLayout.Controls.Add(_sessionText, 0, 2);
        timelineCard.Controls.Add(timelineLayout);
        left.Controls.Add(timelineCard, 0, 2);

        // RIGHT / action stack.
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(8, 0, 0, 0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buyCard = MakeCard();
        buyCard.Margin = new Padding(0, 4, 0, 7);
        buyCard.Padding = new Padding(18, 14, 18, 14);
        var buyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        buyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        buyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        buyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        buyLayout.Controls.Add(new Label
        {
            Text = "BUY RECOMMENDATION",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(111, 123, 144)
        }, 0, 0);
        _buyTitle.Text = "WAITING";
        _buyTitle.Dock = DockStyle.Fill;
        _buyTitle.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _buyTitle.ForeColor = Color.White;
        _buyTitle.TextAlign = ContentAlignment.MiddleLeft;
        buyLayout.Controls.Add(_buyTitle, 0, 1);
        _buyText.Dock = DockStyle.Fill;
        _buyText.Font = new Font("Segoe UI", 8.5f);
        _buyText.ForeColor = Color.FromArgb(166, 177, 194);
        _buyText.AutoEllipsis = true;
        buyLayout.Controls.Add(_buyText, 0, 2);
        buyCard.Controls.Add(buyLayout);
        right.Controls.Add(buyCard, 0, 0);

        var faceitCard = MakeCard();
        faceitCard.Margin = new Padding(0, 7, 0, 7);
        faceitCard.Padding = new Padding(14, 10, 14, 10);
        var faceitLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        faceitLayout.Controls.Add(new Label
        {
            Text = "FACEIT PERFORMANCE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(111, 123, 144)
        }, 0, 0);

        var faceitMetrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty
        };
        for (int i = 0; i < 4; i++)
            faceitMetrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        faceitMetrics.Controls.Add(MakeMiniMetric("ELO", _faceitElo), 0, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("LEVEL", _faceitLevel), 1, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("AVG K", _faceitAvgKills), 2, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("K / D", _faceitKd), 3, 0);
        faceitLayout.Controls.Add(faceitMetrics, 0, 1);

        _faceitTrend.Text = FaceitSettingsStore.HasKey
            ? "Loading FACEIT snapshot…"
            : "Connect FACEIT to add ELO + recent form.";
        _faceitTrend.Dock = DockStyle.Fill;
        _faceitTrend.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _faceitTrend.ForeColor = Color.FromArgb(139, 151, 170);
        _faceitTrend.TextAlign = ContentAlignment.MiddleLeft;
        _faceitTrend.AutoEllipsis = true;
        faceitLayout.Controls.Add(_faceitTrend, 0, 2);

        faceitCard.Controls.Add(faceitLayout);
        right.Controls.Add(faceitCard, 0, 1);

        var setupCard = MakeCard();
        setupCard.Margin = new Padding(0, 7, 0, 7);
        setupCard.Padding = new Padding(18, 12, 18, 12);
        var setupLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty
        };
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        setupLayout.Controls.Add(new Label
        {
            Text = "COACH SETUP",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(111, 123, 144)
        }, 0, 0);

        _mode.Items.Clear();
        _mode.Items.AddRange(new object[] { "Balanced", "Aggressive", "Safe" });
        _mode.SelectedItem = _prefs.Mode;
        if (_mode.SelectedIndex < 0) _mode.SelectedIndex = 0;
        ConfigureCombo(_mode);
        _mode.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("MODE", _mode), 0, 1);

        _role.Items.Clear();
        _role.Items.AddRange(new object[] { "Flex", "Entry", "Lurk", "Support", "Anchor" });
        _role.SelectedItem = _prefs.Role;
        if (_role.SelectedIndex < 0) _role.SelectedIndex = 0;
        ConfigureCombo(_role);
        _role.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("ROLE", _role), 0, 2);

        _focus.Items.Clear();
        _focus.Items.AddRange(new object[]
        {
            "More kills", "Survive & trade", "Entry impact",
            "Utility impact", "Clutch / late round"
        });
        _focus.SelectedItem = _prefs.Focus;
        if (_focus.SelectedIndex < 0) _focus.SelectedIndex = 0;
        ConfigureCombo(_focus);
        _focus.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("FOCUS", _focus), 0, 3);

        var setupFooter = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        setupFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        setupFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));

        _autoAi.Text = " Auto AI refine";
        _autoAi.Checked = _prefs.AutoAi;
        _autoAi.Dock = DockStyle.Fill;
        _autoAi.ForeColor = Color.FromArgb(191, 201, 215);
        _autoAi.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _autoAi.CheckedChanged += (_,__) => PreferencesChanged();
        setupFooter.Controls.Add(_autoAi, 0, 0);

        var localAi = MakeButton("LOCAL AI", 96, false);
        localAi.Dock = DockStyle.Fill;
        localAi.Margin = new Padding(5, 4, 0, 4);
        localAi.Click += (_,__) =>
        {
            using var dialog = new AiSettingsForm();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                UpdateAiStatus();
                if (_current.Round is int && _autoAi.Checked)
                    _ = RefreshAiCoachAsync(_current, true);
            }
        };
        setupFooter.Controls.Add(localAi, 1, 0);
        setupLayout.Controls.Add(setupFooter, 0, 4);
        setupCard.Controls.Add(setupLayout);
        right.Controls.Add(setupCard, 0, 2);

        var recentCard = MakeCard();
        recentCard.Margin = new Padding(0, 7, 0, 0);
        recentCard.Padding = new Padding(18, 12, 18, 12);
        var recentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recentLayout.Controls.Add(new Label
        {
            Text = "RECENT ROUND IMPACT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(111, 123, 144)
        }, 0, 0);

        _history.Dock = DockStyle.Fill;
        _history.BackColor = Color.FromArgb(12, 16, 23);
        _history.ForeColor = Color.FromArgb(204, 212, 224);
        _history.BorderStyle = BorderStyle.None;
        _history.IntegralHeight = false;
        _history.Font = new Font("Cascadia Mono", 9);
        recentLayout.Controls.Add(_history, 0, 1);
        recentCard.Controls.Add(recentLayout);
        right.Controls.Add(recentCard, 0, 3);

        content.Controls.Add(left, 0, 0);
        content.Controls.Add(right, 1, 0);
        workspace.Controls.Add(content, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(24, 3, 24, 3),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(8, 11, 17)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        _phoneUrl.Visible = false;
        footer.Controls.Add(new Label
        {
            Text = "INSTANT PLAN → OPTIONAL AI REFINE • NO HIDDEN ENEMY DATA",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(67, 78, 96),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var refreshGsi = MakeButton("REFRESH GSI", 112, false);
        refreshGsi.Dock = DockStyle.Fill;
        refreshGsi.Height = 24;
        refreshGsi.Margin = new Padding(4, 0, 4, 0);
        refreshGsi.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        refreshGsi.Click += (_,__) =>
        {
            var p = GsiInstaller.TryInstall();
            MessageBox.Show(
                p != null
                    ? $"GSI config je osvežen:\n{p}\n\nČe je CS2 odprt, ga ponovno zaženi."
                    : "CS2 cfg mape nisem našel samodejno.",
                "Sm0ki Solo Coach");
        };
        footer.Controls.Add(refreshGsi, 1, 0);

        var phoneButton = MakeButton("PHONE QR", 112, false);
        phoneButton.Dock = DockStyle.Fill;
        phoneButton.Height = 24;
        phoneButton.Margin = new Padding(4, 0, 0, 0);
        phoneButton.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        phoneButton.Click += (_,__) =>
        {
            if (_phoneServer == null) return;
            using var qr = new PhoneQrForm(_phoneServer.GetLocalUrl());
            qr.ShowDialog(this);
        };
        footer.Controls.Add(phoneButton, 2, 0);

        workspace.Controls.Add(footer, 0, 3);
        root.Controls.Add(workspace, 1, 0);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private Button MakeNavButton(string text, bool active, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = 180,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Margin = new Padding(0, 2, 0, 2),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = active ? Color.White : Color.FromArgb(153, 164, 181),
            BackColor = active ? Color.FromArgb(35, 31, 67) : Color.FromArgb(12, 16, 23),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = active ? 1 : 0;
        button.FlatAppearance.BorderColor = Color.FromArgb(104, 92, 255);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 33, 44);
        button.Click += click;
        return button;
    }

    private Button MakePresetButton(string text, string preset)
    {
        var button = new Button
        {
            Text = text,
            Width = 116,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 8, 0),
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(202, 210, 223),
            BackColor = Color.FromArgb(20, 25, 35),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(43, 51, 66);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 36, 49);
        button.Click += (_,__) => ApplyCoachPreset(preset);
        return button;
    }

    private Control MakeMiniMetric(string title, Label value)
    {
        var metric = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 1, 5, 1),
            Padding = new Padding(5, 2, 5, 2),
            BackColor = Color.FromArgb(13, 18, 26)
        };
        metric.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        metric.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        metric.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 6.8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 106, 128),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        value.ForeColor = Color.White;
        value.TextAlign = ContentAlignment.MiddleLeft;
        metric.Controls.Add(value, 0, 1);

        return metric;
    }

    private Control MakeCompactSelector(string title, ComboBox combo)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(105, 118, 140),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        combo.Dock = DockStyle.Fill;
        row.Controls.Add(combo, 1, 0);
        return row;
    }

    private void ApplyCoachPreset(string preset)
    {
        switch (preset)
        {
            case "Entry":
                _mode.SelectedItem = "Aggressive";
                _role.SelectedItem = "Entry";
                _focus.SelectedItem = "Entry impact";
                break;
            case "Survival":
                _mode.SelectedItem = "Safe";
                _role.SelectedItem = "Flex";
                _focus.SelectedItem = "Survive & trade";
                break;
            case "Utility":
                _mode.SelectedItem = "Balanced";
                _role.SelectedItem = "Support";
                _focus.SelectedItem = "Utility impact";
                break;
            case "Clutch":
                _mode.SelectedItem = "Safe";
                _role.SelectedItem = "Flex";
                _focus.SelectedItem = "Clutch / late round";
                break;
            default:
                _mode.SelectedItem = "Balanced";
                _role.SelectedItem = "Flex";
                _focus.SelectedItem = "More kills";
                break;
        }

        PreferencesChanged();
    }

    private Label MakeRoundPill(string text, Color backColor, Color foreColor)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = 54,
            Height = 28,
            Margin = new Padding(0, 0, 6, 0),
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
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
        Dock = DockStyle.Top,
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
            _aiStatus.Text = "FAST AI READY";
            _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            _aiHealth.Text = "● FAST AI READY";
            _aiHealth.ForeColor = Color.FromArgb(126, 240, 174);
            if (_latestAiAdvice == "AI coach čaka na nastavitev.")
                _latestAiAdvice = "Local AI je na voljo. V Local AI meniju klikni Prepare Local AI, nato se plan ustvari ob začetku runde.";
        }
        else
        {
            _aiStatus.Text = "INSTANT PLAN";
            _aiStatus.ForeColor = Color.FromArgb(255, 190, 132);
            _aiHealth.Text = "● AI OPTIONAL";
            _aiHealth.ForeColor = Color.FromArgb(255, 190, 132);
            _latestAiAdvice = "Klikni Local AI in namesti brezplačni lokalni AI.";
        }

        _aiText.Text = _latestAiAdvice;
    }

    private async Task RefreshAiCoachAsync(GameSnapshot snapshot, bool force = false)
    {
        if (!force && snapshot.Round == null) return;

        // Every round gets a useful plan immediately. Local AI is only a refinement.
        var instant = CoachEngine.InstantRoundPlan(
            snapshot,
            _mode.SelectedItem?.ToString() ?? "Balanced",
            _role.SelectedItem?.ToString() ?? "Flex",
            _focus.SelectedItem?.ToString() ?? "More kills",
            _rounds.ToList()
        );

        _latestAiAdvice = instant;
        _aiText.Text = instant;
        _aiStatus.Text = _aiCoach.IsConfigured ? "FAST PLAN • AI REFINE" : "FAST PLAN";
        _aiStatus.ForeColor = _aiCoach.IsConfigured
            ? Color.FromArgb(176, 166, 255)
            : Color.FromArgb(126, 240, 174);

        if (!_aiCoach.IsConfigured)
            return;

        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        _aiCts.CancelAfter(TimeSpan.FromSeconds(7));

        var requestedMap = snapshot.Map;
        var requestedRound = snapshot.Round;
        var requestToken = _aiCts.Token;

        try
        {
            var advice = await _aiCoach.GenerateRoundAdviceAsync(
                snapshot,
                _rounds.ToList(),
                $"{_mode.SelectedItem?.ToString() ?? "Balanced"} | Role={_role.SelectedItem?.ToString() ?? "Flex"} | Focus={_focus.SelectedItem?.ToString() ?? "More kills"}",
                _profile.Nickname,
                requestToken
            );

            if (requestToken.IsCancellationRequested ||
                _current.Map != requestedMap ||
                _current.Round != requestedRound)
                return;

            _latestAiAdvice = advice;
            _aiText.Text = advice;
            _aiStatus.Text = "AI REFINED";
            _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
        }
        catch (OperationCanceledException)
        {
            // New round or 7-second budget reached: keep the instant plan.
            if (_current.Map == requestedMap && _current.Round == requestedRound)
            {
                _aiStatus.Text = "FAST PLAN";
                _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            }
        }
        catch
        {
            // Never replace a usable round plan with an AI error during a match.
            if (_current.Map == requestedMap && _current.Round == requestedRound)
            {
                _aiStatus.Text = "FAST PLAN";
                _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            }
        }
    }

    private void ArchiveCurrentSession(string map, GameSnapshot snapshot)
    {
        if (_rounds.Count < 4 || string.IsNullOrWhiteSpace(map))
            return;

        try
        {
            SessionHistoryStore.Save(new SessionSummary
            {
                StartedUtc = _sessionStartedUtc,
                EndedUtc = DateTime.UtcNow,
                Nickname = _profile.Nickname,
                Map = map,
                Rounds = _rounds.Count,
                Kills = _rounds.Sum(r => r.KillsRound),
                Deaths = _rounds.Sum(r => r.DeathsRound),
                ZeroKillRounds = _rounds.Count(r => r.KillsRound == 0),
                MultiKillRounds = _rounds.Count(r => r.KillsRound >= 2),
                SurvivalRounds = _rounds.Count(r => r.DeathsRound == 0),
                CtScore = snapshot.CtScore ?? 0,
                TScore = snapshot.TScore ?? 0
            });
        }
        catch { }
    }

    private void EditProfile()
    {
        using var dialog = new FirstRunSetupForm(_profile, _prefs, false);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _profile = UserProfileStore.Load();
        _profileBadge.Text = _profile.Nickname;
        Text = $"Sm0ki Solo Coach • {_profile.Nickname} • GSI {_server.BoundPort}";
        MessageBox.Show(
            $"Profil je shranjen za {_profile.Nickname}.\nCoach in Local AI bosta uporabljala ta profil.",
            "Profile updated",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    private void ApplyPhoneSettings(string mode, string role, string focus, bool autoAi)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyPhoneSettings(mode, role, focus, autoAi));
            return;
        }

        if (_mode.Items.Contains(mode)) _mode.SelectedItem = mode;
        if (_role.Items.Contains(role)) _role.SelectedItem = role;
        if (_focus.Items.Contains(focus)) _focus.SelectedItem = focus;
        _autoAi.Checked = autoAi;

        _prefs.Mode = _mode.SelectedItem?.ToString() ?? "Balanced";
        _prefs.Role = _role.SelectedItem?.ToString() ?? "Flex";
        _prefs.Focus = _focus.SelectedItem?.ToString() ?? "More kills";
        _prefs.AutoAi = _autoAi.Checked;
        UserSettingsStore.Save(_prefs);

        if (!string.IsNullOrWhiteSpace(_current.Map))
            RefreshUi();
    }

    private void PreferencesChanged()
    {
        _prefs.Mode = _mode.SelectedItem?.ToString() ?? "Balanced";
        _prefs.Role = _role.SelectedItem?.ToString() ?? "Flex";
        _prefs.Focus = _focus.SelectedItem?.ToString() ?? "More kills";
        _prefs.AutoAi = _autoAi.Checked;
        UserSettingsStore.Save(_prefs);

        if (_current.Round is int && _autoAi.Checked)
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
                ArchiveCurrentSession(_previous.Map, _previous);
                _sessionStartedUtc = DateTime.UtcNow;
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
                    ArchiveCurrentSession(_previous.Map, _previous);
                    _sessionStartedUtc = DateTime.UtcNow;
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
                    while (_rounds.Count > 40) _rounds.RemoveAt(0);
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

    private async Task LoadFaceitSnapshotAsync(bool force = false)
    {
        if (_faceitLoading) return;

        if (!FaceitSettingsStore.HasKey)
        {
            _faceitSnapshot = null;
            RefreshFaceitCard("Connect FACEIT to add ELO + recent form.");
            return;
        }

        if (!force &&
            _faceitSnapshot != null &&
            DateTime.UtcNow - _faceitLoadedUtc < TimeSpan.FromMinutes(10))
        {
            RefreshFaceitCard();
            return;
        }

        var nickname = FaceitSettingsStore.LoadNickname() ?? _profile.Nickname;
        if (string.IsNullOrWhiteSpace(nickname))
        {
            RefreshFaceitCard("Set your FACEIT nickname first.");
            return;
        }

        _faceitLoading = true;
        RefreshFaceitCard($"Loading {nickname}…");

        try
        {
            _faceitSnapshot = await new FaceitService().LoadAsync(nickname);
            _faceitLoadedUtc = DateTime.UtcNow;
            RefreshFaceitCard();
        }
        catch (Exception ex)
        {
            _faceitSnapshot = null;
            RefreshFaceitCard("FACEIT unavailable • open FACEIT to retry");
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _faceitLoading = false;
        }
    }

    private void RefreshFaceitCard(string? overrideStatus = null)
    {
        var f = _faceitSnapshot;

        _faceitElo.Text = f?.Elo > 0 ? f.Elo.ToString() : "—";
        _faceitLevel.Text = f?.SkillLevel > 0 ? f.SkillLevel.ToString() : "—";
        _faceitAvgKills.Text = f?.RecentAverageKills?.ToString("0.0") ?? "—";
        _faceitKd.Text = f?.RecentAverageKd?.ToString("0.00") ?? "—";

        if (!string.IsNullOrWhiteSpace(overrideStatus))
        {
            _faceitTrend.Text = overrideStatus;
            _faceitTrend.ForeColor = Color.FromArgb(139, 151, 170);
            return;
        }

        if (f == null)
        {
            _faceitTrend.Text = "FACEIT snapshot not loaded.";
            _faceitTrend.ForeColor = Color.FromArgb(139, 151, 170);
            return;
        }

        _faceitTrend.Text =
            $"{f.Nickname} • recent {f.RecentMatchesRead} • {f.RecentKillsTrendLabel}";

        _faceitTrend.ForeColor =
            f.RecentKillsTrendDelta is double delta && delta > 0.25
                ? Color.FromArgb(126, 240, 174)
                : f.RecentKillsTrendDelta is double down && down < -0.25
                    ? Color.FromArgb(255, 174, 143)
                    : Color.FromArgb(164, 174, 191);
    }

    private void RefreshUi()
    {
        if (_phoneServer != null)
            _phoneUrl.Text = "Phone: " + _phoneServer.GetLocalUrl();

        _status.Text = "GSI LIVE";
        _status.ForeColor = Color.FromArgb(126, 240, 174);
        _status.BackColor = Color.FromArgb(20, 56, 39);
        _gsiHealth.Text = "● GSI LIVE";
        _gsiHealth.ForeColor = Color.FromArgb(126, 240, 174);

        int k = _current.Kills ?? 0;
        int d = _current.Deaths ?? 0;
        double kd = d > 0 ? (double)k / d : k;

        var prettyMap = CoachEngine.PrettyMap(_current.Map);
        var roundText = _current.Round is int currentRound ? $"R{currentRound + 1}" : "—";
        var scoreText = $"{_current.CtScore?.ToString() ?? "—"}:{_current.TScore?.ToString() ?? "—"}";

        _liveContext.Text =
            $"{prettyMap.ToUpperInvariant()}  •  {(_current.Team ?? "—")}  •  {roundText}  •  SCORE {scoreText}  •  {CoachEngine.ClassifyRound(_current).ToUpperInvariant()}";

        _stats["map"].Text = prettyMap;
        _stats["side"].Text = string.IsNullOrWhiteSpace(_current.Team) ? "—" : _current.Team;
        _stats["round"].Text = roundText;
        _stats["score"].Text = scoreText;
        _stats["kills"].Text = k.ToString();
        _stats["deaths"].Text = d.ToString();
        _stats["kd"].Text = kd.ToString("0.00");
        _stats["money"].Text = "$" + (_current.Money ?? 0);

        var (title, advice) = CoachEngine.BuyAdvice(_current);
        _buyTitle.Text = title;
        _buyText.Text = advice;

        _tip.Text = CoachEngine.SoloTip(
            _current,
            _mode.SelectedItem?.ToString() ?? "Balanced") + " " + RoleFocusTip();

        _sessionText.Text = SessionInsight();

        _history.Items.Clear();
        foreach (var rr in _rounds.TakeLast(10))
        {
            _history.Items.Add(
                $"R{rr.Round + 1:00}   {rr.Side,2}   {rr.KillsRound}K/{rr.DeathsRound}D   $" + rr.MoneyEnd);
        }
        if (_history.Items.Count == 0)
            _history.Items.Add("No completed rounds tracked yet.");

        _roundTimeline.SuspendLayout();
        _roundTimeline.Controls.Clear();

        foreach (var rr in _rounds.TakeLast(14))
        {
            Color back;
            Color fore = Color.White;

            if (rr.KillsRound >= 2)
                back = Color.FromArgb(25, 87, 61);
            else if (rr.KillsRound == 0 && rr.DeathsRound > 0)
                back = Color.FromArgb(92, 39, 42);
            else if (rr.KillsRound > 0)
                back = Color.FromArgb(35, 60, 78);
            else
            {
                back = Color.FromArgb(35, 40, 52);
                fore = Color.FromArgb(174, 184, 199);
            }

            _roundTimeline.Controls.Add(
                MakeRoundPill($"R{rr.Round + 1}\n{rr.KillsRound}K", back, fore));
        }

        if (_current.Round is int liveRound)
        {
            _roundTimeline.Controls.Add(
                MakeRoundPill($"R{liveRound + 1}\nLIVE",
                    Color.FromArgb(71, 58, 145),
                    Color.White));
        }

        if (_roundTimeline.Controls.Count == 0)
            _roundTimeline.Controls.Add(
                MakeRoundPill("WAIT\n—",
                    Color.FromArgb(31, 36, 47),
                    Color.FromArgb(132, 144, 163)));

        _roundTimeline.ResumeLayout(true);
    }
}
