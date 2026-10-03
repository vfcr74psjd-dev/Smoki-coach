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
    private AutoDemoInboxService? _demoInbox;
    private GameOverlayForm? _gameOverlay;
    private Cs2ChatIntentWatcher? _chatIntentWatcher;
    private readonly Dictionary<int, string> _roundIntents = new();
    private readonly Label _phoneUrl = new();
    private readonly Button _updateButton = new();
    private readonly Label _aiText = new();
    private readonly CoachPlanView _planView = new();
    private readonly Label _aiStatus = new();
    private readonly Label _liveContext = new();
    private readonly Label _profileBadge = new();
    private readonly Label _gsiHealth = new();
    private readonly Label _aiHealth = new();
    private readonly Label _faceitHealth = new();
    private readonly FlowLayoutPanel _roundTimeline = new();
    private readonly List<Button> _presetButtons = new();
    private TableLayoutPanel? _liveLeftLayout;
    private bool _liveLayoutExpanded;
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
    private string _trackedRoundWinTeam = "";
    private string _trackedRoundPrimaryWeapon = "";
    private int _trackedRoundLastHealth = 0;
    private DateTime _trackedRoundLiveStartedUtc = DateTime.MinValue;
    private bool _trackedBombPlanted;
    private string _trackedBombSite = "";
    private double? _trackedBombPlantSeconds;
    private DateTime _sessionStartedUtc = DateTime.UtcNow;
    private readonly System.Windows.Forms.Timer _uiPulseTimer = new();
    private DateTime _lastGsiUtc = DateTime.MinValue;
    private bool _pulseBright;
    private string _matchReviewShownKey = "";

    private GameSnapshot _current = new();
    private GameSnapshot? _previous;
    private readonly List<RoundRecord> _rounds = new();

    public MainForm()
    {
        Text = "Sm0ki Solo Coach";
        Width = 1460;
        Height = 900;
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(11, 13, 15);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();

        _gameOverlay = new GameOverlayForm();
        _gameOverlay.LayoutSaved += OnOverlayLayoutSaved;
        _gameOverlay.ApplySavedLayout(
            _prefs.OverlayCustomPlacement,
            _prefs.OverlayX,
            _prefs.OverlayY,
            _prefs.OverlayScale);

        _server.SnapshotReceived += OnSnapshot;
        _server.Start();
        Text = $"Sm0ki Solo Coach • {_profile.Nickname} • GSI {_server.BoundPort}";

        _phoneServer = new PhoneDashboardServer(
            () => _current,
            () => _prefs.Mode,
            () => _prefs.Role,
            () => _prefs.Focus,
            () => _profile.Nickname,
            () => _prefs.AutoAi,
            () => _latestAiAdvice,
            ApplyPhoneSettings
        );
        _phoneServer.Start();

        _demoInbox = new AutoDemoInboxService(
            () => FaceitSettingsStore.LoadNickname() ?? _profile.Nickname);
        _demoInbox.Start();

        UpdateAiStatus();

        _uiPulseTimer.Interval = 750;
        _uiPulseTimer.Tick += (_,__) => UpdateUiHeartbeat();
        _uiPulseTimer.Start();

        var path = GsiInstaller.TryInstall();
        if (path == null)
            _status.Text = "OFFLINE • GSI NOT INSTALLED";

        var chatLogPath = GsiInstaller.TryEnableChatLog();
        if (!string.IsNullOrWhiteSpace(chatLogPath))
        {
            _chatIntentWatcher = new Cs2ChatIntentWatcher(
                chatLogPath,
                () => FaceitSettingsStore.LoadNickname() ?? _profile.Nickname);
            _chatIntentWatcher.IntentDetected += OnChatIntentDetected;
            _chatIntentWatcher.Start();
        }

        Shown += async (_,__) =>
        {
            _gameOverlay?.ApplyMode(_prefs.OverlayMode);
            UpdateGameOverlay();

            await CheckForUpdatesSilentAsync();
            _ = AiCoachService.WarmUpAsync();
            _ = LoadFaceitSnapshotAsync();
        };

        FormClosing += (_,__) =>
        {
            ArchiveCurrentSession(_current.Map, _current);
            _aiCts?.Cancel();
            _aiCts?.Dispose();
            _uiPulseTimer.Stop();
            _phoneServer?.Dispose();
            _demoInbox?.Dispose();
            if (_chatIntentWatcher != null)
                _chatIntentWatcher.IntentDetected -= OnChatIntentDetected;
            _chatIntentWatcher?.Dispose();
            if (_gameOverlay != null)
                _gameOverlay.LayoutSaved -= OnOverlayLayoutSaved;
            _gameOverlay?.Close();
            _gameOverlay?.Dispose();
            _server.Dispose();
        };
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();
        _presetButtons.Clear();
        _stats.Clear();

        BackColor = Color.FromArgb(9, 10, 12);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(9, 10, 12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // COMPACT NAVIGATION
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = new Padding(12, 14, 12, 12),
            BackColor = Color.FromArgb(13, 14, 16)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var brand = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        brand.Controls.Add(new Panel
        {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = Color.FromArgb(255, 156, 44)
        });
        brand.Controls.Add(new Label
        {
            Text = "SM0KI",
            Left = 12,
            Top = 3,
            Width = 150,
            Height = 30,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White
        });
        brand.Controls.Add(new Label
        {
            Text = "SOLO COACH",
            Left = 14,
            Top = 36,
            Width = 145,
            Height = 20,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 156, 44)
        });
        sidebar.Controls.Add(brand, 0, 0);

        var profileCard = MakeCard();
        profileCard.Margin = new Padding(0, 0, 0, 8);
        profileCard.Padding = new Padding(12, 7, 12, 7);
        profileCard.Cursor = Cursors.Hand;
        profileCard.Click += (_,__) => EditProfile();

        _profileBadge.Text = string.IsNullOrWhiteSpace(_profile.Nickname)
            ? "PLAYER"
            : _profile.Nickname;
        _profileBadge.Dock = DockStyle.Fill;
        _profileBadge.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _profileBadge.ForeColor = Color.White;
        _profileBadge.TextAlign = ContentAlignment.MiddleLeft;
        profileCard.Controls.Add(_profileBadge);
        sidebar.Controls.Add(profileCard, 0, 1);

        sidebar.Controls.Add(new Label
        {
            Text = "WORKSPACE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(98, 104, 114),
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

        nav.Controls.Add(MakeNavButton("LIVE", true, (_,__) => { }));
        nav.Controls.Add(MakeNavButton("HEATMAP", false, (_,__) =>
        {
            if (_demoInbox == null) return;
            var nickname = FaceitSettingsStore.LoadNickname() ?? _profile.Nickname;
            using var dialog = new HeatMapForm(nickname, _demoInbox);
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
        nav.Controls.Add(MakeNavButton("TOOLS", false, (_,__) => ShowToolsHub()));
        sidebar.Controls.Add(nav, 0, 3);

        var health = MakeCard();
        health.Margin = new Padding(0, 8, 0, 8);
        health.Padding = new Padding(12, 9, 12, 9);
        var healthLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        healthLayout.Controls.Add(new Label
        {
            Text = "SYSTEM",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(96, 102, 112)
        }, 0, 0);

        _gsiHealth.Text = "● GSI WAITING";
        _gsiHealth.Dock = DockStyle.Fill;
        _gsiHealth.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _gsiHealth.ForeColor = Color.FromArgb(235, 112, 100);
        _gsiHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_gsiHealth, 0, 1);

        _aiHealth.Text = "● AI CHECK";
        _aiHealth.Dock = DockStyle.Fill;
        _aiHealth.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _aiHealth.ForeColor = Color.FromArgb(145, 151, 159);
        _aiHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_aiHealth, 0, 2);

        _faceitHealth.Text = FaceitSettingsStore.HasKey
            ? "● FACEIT CHECK"
            : "● FACEIT OFF";
        _faceitHealth.Dock = DockStyle.Fill;
        _faceitHealth.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _faceitHealth.ForeColor = Color.FromArgb(145, 151, 159);
        _faceitHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_faceitHealth, 0, 3);

        health.Controls.Add(healthLayout);
        sidebar.Controls.Add(health, 0, 4);

        sidebar.Controls.Add(new Label
        {
            Text = $"v{AppUpdater.CurrentVersion}  •  LOCAL",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f),
            ForeColor = Color.FromArgb(74, 80, 90),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        }, 0, 5);

        root.Controls.Add(sidebar, 0, 0);

        // MAIN WORKSPACE
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(9, 10, 12)
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(22, 12, 22, 8),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(12, 13, 15)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));

        var liveTitle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        liveTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        liveTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
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
        _liveContext.Font = new Font("Segoe UI", 8.8f);
        _liveContext.ForeColor = Color.FromArgb(130, 137, 147);
        _liveContext.TextAlign = ContentAlignment.TopLeft;
        liveTitle.Controls.Add(_liveContext, 0, 1);
        top.Controls.Add(liveTitle, 0, 0);

        _status.Text = "WAITING";
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(8, 12, 8, 12);
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _status.ForeColor = Color.FromArgb(235, 112, 100);
        _status.BackColor = Color.FromArgb(42, 27, 26);
        top.Controls.Add(_status, 1, 0);

        _updateButton.Text = "UPDATE";
        StyleButton(_updateButton, true);
        _updateButton.Dock = DockStyle.Fill;
        _updateButton.Margin = new Padding(8, 12, 0, 12);
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        top.Controls.Add(_updateButton, 2, 0);
        workspace.Controls.Add(top, 0, 0);

        // SINGLE-LINE LIVE METRICS
        var statStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 1,
            Padding = new Padding(18, 8, 18, 8),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(9, 10, 12)
        };
        for (int i = 0; i < 8; i++)
            statStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));

        string[] statKeys = { "map", "side", "round", "score", "kills", "deaths", "kd", "money" };
        string[] statTitles = { "MAP", "SIDE", "ROUND", "SCORE", "KILLS", "DEATHS", "K / D", "MONEY" };

        for (int i = 0; i < statKeys.Length; i++)
        {
            var label = new Label();
            _stats[statKeys[i]] = label;
            var metric = MakeMiniMetric(statTitles[i], label);
            metric.Margin = new Padding(i == 0 ? 0 : 4, 0, i == 7 ? 0 : 4, 0);
            statStrip.Controls.Add(metric, i, 0);
        }
        workspace.Controls.Add(statStrip, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 4, 18, 12),
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        // HERO COACH COLUMN
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 7, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var planCard = MakeCard();
        planCard.Margin = new Padding(0, 0, 0, 7);
        planCard.Padding = new Padding(18, 14, 18, 14);

        var planLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

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
            Text = "NEXT ROUND",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(116, 122, 132)
        }, 0, 0);

        _aiStatus.Text = "FAST PLAN";
        _aiStatus.AutoSize = true;
        _aiStatus.Anchor = AnchorStyles.Right;
        _aiStatus.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        _aiStatus.ForeColor = Color.FromArgb(112, 214, 151);
        planHeader.Controls.Add(_aiStatus, 1, 0);
        planLayout.Controls.Add(planHeader, 0, 0);

        planLayout.Controls.Add(new Label
        {
            Text = "ONE CLEAR PLAN. PLAY IT.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 16.5f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _planView.Dock = DockStyle.Fill;
        _planView.Margin = Padding.Empty;
        _planView.Advice = _latestAiAdvice;
        planLayout.Controls.Add(_planView, 0, 2);

        var noteLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        noteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        noteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        noteLayout.Controls.Add(new Label
        {
            Text = "COACH",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 156, 44),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _tip.Dock = DockStyle.Fill;
        _tip.Font = new Font("Segoe UI", 8.5f);
        _tip.ForeColor = Color.FromArgb(145, 151, 159);
        _tip.TextAlign = ContentAlignment.MiddleLeft;
        _tip.AutoEllipsis = true;
        noteLayout.Controls.Add(_tip, 1, 0);
        planLayout.Controls.Add(noteLayout, 0, 3);

        planCard.Controls.Add(planLayout);
        left.Controls.Add(planCard, 0, 0);

        var timelineCard = MakeCard();
        timelineCard.Margin = new Padding(0, 0, 0, 7);
        timelineCard.Padding = new Padding(14, 10, 14, 10);
        var timelineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        timelineLayout.Controls.Add(new Label
        {
            Text = "ROUND FLOW",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(105, 112, 122)
        }, 0, 0);

        _roundTimeline.Dock = DockStyle.Fill;
        _roundTimeline.FlowDirection = FlowDirection.LeftToRight;
        _roundTimeline.WrapContents = false;
        _roundTimeline.AutoScroll = true;
        _roundTimeline.Margin = Padding.Empty;
        _roundTimeline.Padding = new Padding(0, 8, 0, 0);
        _roundTimeline.Controls.Add(
            MakeRoundPill(
                "READY\n—",
                Color.FromArgb(27, 29, 32),
                Color.FromArgb(135, 142, 152)));
        timelineLayout.Controls.Add(_roundTimeline, 0, 1);

        timelineCard.Controls.Add(timelineLayout);
        left.Controls.Add(timelineCard, 0, 1);

        var sessionCard = MakeCard();
        sessionCard.Margin = Padding.Empty;
        sessionCard.Padding = new Padding(14, 8, 14, 8);
        _sessionText.Text = SessionInsight();
        _sessionText.Dock = DockStyle.Fill;
        _sessionText.Font = new Font("Segoe UI", 8.5f);
        _sessionText.ForeColor = Color.FromArgb(139, 146, 156);
        _sessionText.TextAlign = ContentAlignment.MiddleLeft;
        _sessionText.AutoEllipsis = true;
        sessionCard.Controls.Add(_sessionText);
        left.Controls.Add(sessionCard, 0, 2);

        // SECONDARY RAIL
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(7, 0, 0, 0),
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 430)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 246));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var setupCard = MakeCard();
        setupCard.Margin = new Padding(0, 0, 0, 7);
        setupCard.Padding = new Padding(14, 12, 14, 10);

        var setupLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty
        };
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        setupLayout.Controls.Add(new Label
        {
            Text = "QUICK COACH",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 119, 129)
        }, 0, 0);

        var presetFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = new Padding(0, 3, 0, 2)
        };
        presetFlow.Controls.Add(MakePresetButton("KILLS", "More Kills"));
        presetFlow.Controls.Add(MakePresetButton("ENTRY", "Entry"));
        presetFlow.Controls.Add(MakePresetButton("SAFE", "Survival"));
        presetFlow.Controls.Add(MakePresetButton("UTIL", "Utility"));
        presetFlow.Controls.Add(MakePresetButton("CLUTCH", "Clutch"));
        setupLayout.Controls.Add(presetFlow, 0, 1);

        _mode.Items.Clear();
        _mode.Items.AddRange(new object[] { "Balanced", "Aggressive", "Safe" });
        _mode.SelectedItem = _prefs.Mode;
        if (_mode.SelectedIndex < 0) _mode.SelectedIndex = 0;
        ConfigureCombo(_mode);
        _mode.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("MODE", _mode), 0, 2);

        _role.Items.Clear();
        _role.Items.AddRange(new object[] { "Flex", "Entry", "Lurk", "Support", "Anchor" });
        _role.SelectedItem = _prefs.Role;
        if (_role.SelectedIndex < 0) _role.SelectedIndex = 0;
        ConfigureCombo(_role);
        _role.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("ROLE", _role), 0, 3);

        _focus.Items.Clear();
        _focus.Items.AddRange(new object[]
        {
            "More kills",
            "Survive & trade",
            "Entry impact",
            "Utility impact",
            "Clutch / late round"
        });
        _focus.SelectedItem = _prefs.Focus;
        if (_focus.SelectedIndex < 0) _focus.SelectedIndex = 0;
        ConfigureCombo(_focus);
        _focus.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("FOCUS", _focus), 0, 4);

        _autoAi.Text = " Auto AI refine";
        _autoAi.Checked = _prefs.AutoAi;
        _autoAi.Dock = DockStyle.Fill;
        _autoAi.ForeColor = Color.FromArgb(180, 186, 195);
        _autoAi.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _autoAi.CheckedChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(_autoAi, 0, 5);

        setupCard.Controls.Add(setupLayout);
        right.Controls.Add(setupCard, 0, 0);

        var faceitCard = MakeCard();
        faceitCard.Margin = new Padding(0, 0, 0, 7);
        faceitCard.Padding = new Padding(12, 10, 12, 10);

        var faceitLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        faceitLayout.Controls.Add(new Label
        {
            Text = "FACEIT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 119, 129)
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
        faceitMetrics.Controls.Add(MakeMiniMetric("LVL", _faceitLevel), 1, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("AVG K", _faceitAvgKills), 2, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("K/D", _faceitKd), 3, 0);
        faceitLayout.Controls.Add(faceitMetrics, 0, 1);

        _faceitTrend.Text = FaceitSettingsStore.HasKey
            ? "Loading FACEIT…"
            : "Connect FACEIT in Tools.";
        _faceitTrend.Dock = DockStyle.Fill;
        _faceitTrend.Font = new Font("Segoe UI", 7.8f);
        _faceitTrend.ForeColor = Color.FromArgb(132, 139, 149);
        _faceitTrend.TextAlign = ContentAlignment.MiddleLeft;
        _faceitTrend.AutoEllipsis = true;
        faceitLayout.Controls.Add(_faceitTrend, 0, 2);

        faceitCard.Controls.Add(faceitLayout);
        right.Controls.Add(faceitCard, 0, 1);

        var recentCard = MakeCard();
        recentCard.Margin = Padding.Empty;
        recentCard.Padding = new Padding(14, 10, 14, 10);

        var recentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        recentLayout.Controls.Add(new Label
        {
            Text = "RECENT IMPACT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 119, 129)
        }, 0, 0);

        _history.Dock = DockStyle.Fill;
        _history.BackColor = Color.FromArgb(18, 20, 22);
        _history.ForeColor = Color.FromArgb(202, 207, 214);
        _history.BorderStyle = BorderStyle.None;
        _history.IntegralHeight = false;
        _history.Font = new Font("Cascadia Mono", 8.5f);
        _history.Items.Add("Waiting for completed rounds.");
        recentLayout.Controls.Add(_history, 0, 1);

        recentCard.Controls.Add(recentLayout);
        right.Controls.Add(recentCard, 0, 2);

        content.Controls.Add(left, 0, 0);
        content.Controls.Add(right, 1, 0);
        workspace.Controls.Add(content, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20, 2, 20, 2),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));

        footer.Controls.Add(new Label
        {
            Text = "POSITION → EXPECT → DO  •  PAST-PATTERN PREDICTION ONLY",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 76, 86),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var refreshGsi = MakeButton("REFRESH GSI", 100, false);
        refreshGsi.Dock = DockStyle.Fill;
        refreshGsi.Margin = new Padding(2, 0, 0, 0);
        refreshGsi.Font = new Font("Segoe UI", 7.2f, FontStyle.Bold);
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

        _phoneUrl.Visible = false;

        workspace.Controls.Add(footer, 0, 3);
        root.Controls.Add(workspace, 1, 0);

        void ApplyResponsiveLayout()
        {
            var w = Math.Max(1, ClientSize.Width);
            var h = Math.Max(1, ClientSize.Height);

            // Keep the navigation compact and give the coach/right rail enough
            // actual pixels even on 125/150% Windows scaling.
            root.ColumnStyles[0].Width = w < 1320 ? 166 : 188;

            workspace.RowStyles[0].Height = h < 820 ? 68 : 76;
            workspace.RowStyles[1].Height = h < 820 ? 72 : 86;
            workspace.RowStyles[3].Height = h < 820 ? 26 : 30;

            if (w < 1320)
            {
                content.ColumnStyles[0].Width = 66;
                content.ColumnStyles[1].Width = 34;
            }
            else
            {
                content.ColumnStyles[0].Width = 70;
                content.ColumnStyles[1].Width = 30;
            }

            // Secondary content yields space first; the live plan always gets
            // the largest flexible region.
            left.RowStyles[1].Height = h < 820 ? 90 : 110;
            left.RowStyles[2].Height = h < 820 ? 50 : 64;

            right.RowStyles[0].Height = h < 820 ? 220 : 246;
            right.RowStyles[1].Height = h < 820 ? 112 : 138;

            // Header buttons must not squeeze the live context at higher DPI.
            top.ColumnStyles[1].Width = w < 1320 ? 108 : 122;
            top.ColumnStyles[2].Width = w < 1320 ? 96 : 108;

            _planView.Invalidate();
            PerformLayout();
        }

        Resize += (_,__) => ApplyResponsiveLayout();
        DpiChanged += (_,__) => BeginInvoke(ApplyResponsiveLayout);

        Controls.Add(root);
        ApplyResponsiveLayout();

        _buyTitle.Text = "WAITING";
        _buyText.Text = "";
        _aiText.Text = _latestAiAdvice;

        UpdatePresetSelection(DetectCurrentPreset());
        ResumeLayout(true);
    }

    private void UpdateUiHeartbeat()
    {
        _pulseBright = !_pulseBright;

        var age = _lastGsiUtc == DateTime.MinValue
            ? TimeSpan.MaxValue
            : DateTime.UtcNow - _lastGsiUtc;

        if (age <= TimeSpan.FromSeconds(12))
        {
            _gameOverlay?.SetGameActive(!string.IsNullOrWhiteSpace(_current.Map));
            _status.Text = "GSI LIVE";
            _status.ForeColor = Color.FromArgb(126, 240, 174);
            _status.BackColor = _pulseBright
                ? Color.FromArgb(23, 64, 44)
                : Color.FromArgb(18, 52, 36);

            _gsiHealth.Text = "● GSI LIVE";
            _gsiHealth.ForeColor = _pulseBright
                ? Color.FromArgb(142, 246, 184)
                : Color.FromArgb(104, 214, 151);
        }
        else if (_lastGsiUtc != DateTime.MinValue)
        {
            _gameOverlay?.SetGameActive(false);
            _status.Text = "GSI WAITING";
            _status.ForeColor = Color.FromArgb(244, 155, 121);
            _status.BackColor = Color.FromArgb(52, 35, 31);
            _gsiHealth.Text = "● GSI WAITING";
            _gsiHealth.ForeColor = Color.FromArgb(244, 155, 121);
            SetLiveLayoutExpanded(false);
        }

        if (_aiStatus.Text.Contains("REFINE", StringComparison.OrdinalIgnoreCase))
        {
            _aiStatus.ForeColor = _pulseBright
                ? Color.FromArgb(196, 188, 255)
                : Color.FromArgb(145, 133, 235);
        }
    }

    private void ShowToolsHub()
    {
        using var dialog = new Form
        {
            Text = "Sm0ki Solo Coach • Tools",
            Width = 620,
            Height = 570,
            MinimumSize = new Size(560, 520),
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = Color.FromArgb(9, 13, 19),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 20, 24, 22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label
        {
            Text = "TOOLS & CONNECTIONS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White
        }, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        Button ToolButton(string title, string subtitle, EventHandler click)
        {
            var b = new Button
            {
                Text = title + "\n" + subtitle,
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                Padding = new Padding(14, 8, 14, 8),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(17, 22, 30),
                ForeColor = Color.FromArgb(225, 230, 238),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(40, 48, 63);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(27, 33, 46);
            b.Click += click;
            return b;
        }

        grid.Controls.Add(ToolButton("REVIEW LAB", "Inspect one demo in detail", (_,__) =>
        {
            var nickname = FaceitSettingsStore.LoadNickname() ?? _profile.Nickname;
            using var review = new ReviewLabForm(nickname);
            review.ShowDialog(dialog);
        }), 0, 0);

        grid.Controls.Add(ToolButton("FACEIT", "API key, nickname & stats", async (_,__) =>
        {
            using var faceit = new FaceitForm(_profile.Nickname);
            faceit.ShowDialog(dialog);
            await LoadFaceitSnapshotAsync(true);
        }), 1, 0);

        grid.Controls.Add(ToolButton("FACEIT HISTORY", "Index matches & demo availability", (_,__) =>
        {
            if (_demoInbox == null) return;
            var nickname = FaceitSettingsStore.LoadNickname() ?? _profile.Nickname;
            using var history = new FaceitHistoryForm(nickname, _demoInbox);
            history.ShowDialog(dialog);
        }), 0, 1);

        grid.Controls.Add(ToolButton("PHONE COMPANION", "Private LAN dashboard", (_,__) =>
        {
            if (_phoneServer == null) return;
            using var qr = new PhoneQrForm(_phoneServer.GetLocalUrl());
            qr.ShowDialog(dialog);
        }), 1, 1);

        grid.Controls.Add(ToolButton("PLAYER PROFILE", "Local player identity", (_,__) =>
        {
            dialog.Hide();
            EditProfile();
            dialog.Show();
        }), 0, 2);

        grid.Controls.Add(ToolButton("LOCAL AI", "Install, prepare or test Ollama", (_,__) =>
        {
            using var ai = new AiSettingsForm();
            if (ai.ShowDialog(dialog) == DialogResult.OK)
            {
                UpdateAiStatus();
                if (_current.Round is int && _autoAi.Checked)
                    _ = RefreshAiCoachAsync(_current, true);
            }
        }), 1, 2);

        grid.Controls.Add(ToolButton("GAME OVERLAY", "Click-through CS2 HUD • F8 toggle", (_,__) =>
        {
            ShowOverlaySettings(dialog);
        }), 0, 3);

        grid.Controls.Add(ToolButton("EDIT OVERLAY", "Drag position • wheel changes size", (_,__) =>
        {
            dialog.Close();
            BeginOverlayLayoutEdit();
        }), 1, 3);

        root.Controls.Add(grid, 0, 1);

        var close = MakeButton("CLOSE", 90, false);
        close.Anchor = AnchorStyles.Right;
        close.Click += (_,__) => dialog.Close();
        root.Controls.Add(close, 0, 2);

        dialog.Controls.Add(root);
        dialog.ShowDialog(this);
    }

    private void BeginOverlayLayoutEdit()
    {
        if (_gameOverlay == null)
            return;

        if (_prefs.OverlayMode == "Off")
        {
            _prefs.OverlayMode = "Minimal";
            UserSettingsStore.Save(_prefs);
            _gameOverlay.ApplyMode(_prefs.OverlayMode);
        }

        UpdateGameOverlay();
        _gameOverlay.BeginLayoutEdit();
    }

    private void OnOverlayLayoutSaved(Point location, float scale)
    {
        _prefs.OverlayCustomPlacement = true;
        _prefs.OverlayX = location.X;
        _prefs.OverlayY = location.Y;
        _prefs.OverlayScale = scale;
        UserSettingsStore.Save(_prefs);
    }

    private void ResetOverlayLayout()
    {
        if (_gameOverlay == null)
            return;

        _prefs.OverlayCustomPlacement = false;
        _prefs.OverlayX = -1;
        _prefs.OverlayY = -1;
        _prefs.OverlayScale = 1.0f;
        UserSettingsStore.Save(_prefs);

        _gameOverlay.ResetLayout();
    }

    private void ShowOverlaySettings(Form owner)
    {
        if (_gameOverlay == null)
            return;

        using var settings = new OverlaySettingsForm(
            _prefs.OverlayMode,
            _gameOverlay.HotkeyRegistered,
            _gameOverlay.HotkeyDisplay);

        if (settings.ShowDialog(owner) != DialogResult.OK)
            return;

        _prefs.OverlayMode = settings.SelectedMode;
        UserSettingsStore.Save(_prefs);

        _gameOverlay.ApplyMode(_prefs.OverlayMode);
        _gameOverlay.ApplySavedLayout(
            _prefs.OverlayCustomPlacement,
            _prefs.OverlayX,
            _prefs.OverlayY,
            _prefs.OverlayScale);
        UpdateGameOverlay();
    }

    private void UpdateGameOverlay()
    {
        if (_gameOverlay == null)
            return;

        _planView.Advice = _latestAiAdvice;

        _gameOverlay.UpdateData(
            _current,
            _latestAiAdvice,
            _aiStatus.Text);
    }

    private Button MakeNavButton(string text, bool active, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = 160,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Margin = new Padding(0, 2, 0, 2),
            Font = new Font("Segoe UI", 8.3f, FontStyle.Bold),
            ForeColor = active ? Color.White : Color.FromArgb(145, 151, 159),
            BackColor = active ? Color.FromArgb(38, 31, 23) : Color.FromArgb(13, 14, 16),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = active ? 1 : 0;
        button.FlatAppearance.BorderColor = Color.FromArgb(255, 156, 44);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(24, 26, 29);
        button.Click += click;
        return button;
    }

    private Button MakePresetButton(string text, string preset)
    {
        var button = new Button
        {
            Text = text,
            Tag = preset,
            Width = 76,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 5, 4),
            Font = new Font("Segoe UI", 7.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(202, 210, 223),
            BackColor = Color.FromArgb(20, 22, 24),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(58, 52, 45);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 34, 29);
        button.Click += (_,__) => ApplyCoachPreset(preset);
        _presetButtons.Add(button);
        return button;
    }

    private string? DetectCurrentPreset()
    {
        var mode = _mode.SelectedItem?.ToString();
        var role = _role.SelectedItem?.ToString();
        var focus = _focus.SelectedItem?.ToString();

        if (mode == "Balanced" && role == "Flex" && focus == "More kills")
            return "More Kills";
        if (mode == "Aggressive" && role == "Entry" && focus == "Entry impact")
            return "Entry";
        if (mode == "Safe" && role == "Flex" && focus == "Survive & trade")
            return "Survival";
        if (mode == "Balanced" && role == "Support" && focus == "Utility impact")
            return "Utility";
        if (mode == "Safe" && role == "Flex" && focus == "Clutch / late round")
            return "Clutch";

        return null;
    }

    private void UpdatePresetSelection(string? activePreset = null)
    {
        activePreset ??= DetectCurrentPreset();

        foreach (var button in _presetButtons)
        {
            var active = string.Equals(
                button.Tag?.ToString(),
                activePreset,
                StringComparison.OrdinalIgnoreCase);

            button.BackColor = active
                ? Color.FromArgb(67, 42, 22)
                : Color.FromArgb(20, 22, 24);
            button.ForeColor = active
                ? Color.White
                : Color.FromArgb(202, 210, 223);
            button.FlatAppearance.BorderColor = active
                ? Color.FromArgb(255, 156, 44)
                : Color.FromArgb(58, 52, 45);
        }
    }

    private Control MakeMiniMetric(string title, Label value)
    {
        var metric = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 1, 5, 1),
            Padding = new Padding(6, 3, 6, 3),
            BackColor = Color.FromArgb(16, 18, 20)
        };
        metric.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        metric.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        metric.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("Segoe UI", 6.6f, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 106, 128),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.AutoSize = false;
        value.Margin = Padding.Empty;
        value.Padding = Padding.Empty;
        value.Font = new Font("Segoe UI", 10, FontStyle.Bold);
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
            Padding = new Padding(0, 2, 0, 2)
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
        UpdatePresetSelection(preset);
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
        combo.BackColor = Color.FromArgb(26, 28, 30);
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
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(255, 156, 44)
            : Color.FromArgb(43, 46, 50);
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        b.BackColor = primary
            ? Color.FromArgb(255, 156, 44)
            : Color.FromArgb(21, 23, 26);
        b.ForeColor = primary ? Color.Black : Color.White;
        b.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(255, 174, 72)
            : Color.FromArgb(29, 32, 36);
    }

    private Panel MakeCard()
    {
        return new ModernCardPanel
        {
            BackColor = Color.FromArgb(18, 20, 22),
            BorderColor = Color.FromArgb(42, 45, 49),
            Radius = 14,
            Margin = new Padding(4),
            Dock = DockStyle.Fill
        };
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
        if (!_aiCoach.IsConfigured)
        {
            _aiStatus.Text = "INSTANT PLAN";
            _aiStatus.ForeColor = Color.FromArgb(255, 190, 132);
            _aiHealth.Text = "● AI OPTIONAL";
            _aiHealth.ForeColor = Color.FromArgb(255, 190, 132);
            _latestAiAdvice = "Instant coach dela brez AI. Za AI refine odpri Tools → Local AI.";
            _aiText.Text = _latestAiAdvice;
            return;
        }

        _aiStatus.Text = "AI CHECK";
        _aiStatus.ForeColor = Color.FromArgb(255, 178, 91);
        _aiHealth.Text = "● AI CHECKING";
        _aiHealth.ForeColor = Color.FromArgb(255, 178, 91);
        _ = RefreshAiReadinessAsync();
    }

    private async Task RefreshAiReadinessAsync()
    {
        var ready = await AiCoachService.IsFastModelReadyAsync();
        if (IsDisposed) return;

        if (ready)
        {
            _aiStatus.Text = "FAST AI READY";
            _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            _aiHealth.Text = "● FAST AI READY";
            _aiHealth.ForeColor = Color.FromArgb(126, 240, 174);

            if (_latestAiAdvice == "AI coach čaka na nastavitev." ||
                _latestAiAdvice.StartsWith("Instant coach dela", StringComparison.Ordinal))
            {
                _latestAiAdvice =
                    "Coach je pripravljen. Nova runda dobi instant plan takoj; Local AI ga v ozadju samo izboljša.";
                _aiText.Text = _latestAiAdvice;
            }
        }
        else
        {
            _aiStatus.Text = "INSTANT PLAN";
            _aiStatus.ForeColor = Color.FromArgb(255, 190, 132);
            _aiHealth.Text = "● AI NEEDS PREP";
            _aiHealth.ForeColor = Color.FromArgb(255, 190, 132);
            _latestAiAdvice = "Instant coach je aktiven. Za AI refine odpri Tools → Local AI → Prepare Fast AI.";
            _aiText.Text = _latestAiAdvice;
        }
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
            _rounds.ToList(),
            GetRoundIntent(snapshot.Round)
        );

        _latestAiAdvice = instant;
        _aiText.Text = instant;
        _aiStatus.Text = _aiCoach.IsConfigured ? "FAST PLAN • AI REFINE" : "FAST PLAN";
        _aiStatus.ForeColor = _aiCoach.IsConfigured
            ? Color.FromArgb(255, 178, 91)
            : Color.FromArgb(126, 240, 174);
        UpdateGameOverlay();

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
                GetRoundIntent(snapshot.Round),
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
            UpdateGameOverlay();
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
        SaveSessionSummary(map, snapshot, _rounds);
    }

    private void SaveSessionSummary(
        string map,
        GameSnapshot snapshot,
        IReadOnlyList<RoundRecord> rounds)
    {
        if (rounds.Count < 4 || string.IsNullOrWhiteSpace(map))
            return;

        try
        {
            var review = MatchReviewAnalyzer.Analyze(rounds);

            SessionHistoryStore.Save(new SessionSummary
            {
                StartedUtc = _sessionStartedUtc,
                EndedUtc = DateTime.UtcNow,
                Nickname = _profile.Nickname,
                Map = map,
                Rounds = rounds.Count,
                Kills = rounds.Sum(r => r.KillsRound),
                Deaths = rounds.Sum(r => r.DeathsRound),
                ZeroKillRounds = rounds.Count(r => r.KillsRound == 0),
                MultiKillRounds = rounds.Count(r => r.KillsRound >= 2),
                SurvivalRounds = rounds.Count(r => r.Survived),
                CtScore = snapshot.CtScore ?? 0,
                TScore = snapshot.TScore ?? 0,
                BestArea = review.BestTitle + " • " + review.BestDetail,
                TroubleArea = review.TroubleTitle + " • " + review.TroubleDetail,
                NextFocus = review.FocusTitle + " • " + review.FocusDetail
            });
        }
        catch { }
    }

    private List<RoundRecord> BuildMatchReviewRounds()
    {
        var result = _rounds.ToList();

        if (_trackedRound is not int tracked ||
            result.Any(r => r.Round == tracked))
            return result;

        var snapshot = _current;
        var intent = string.IsNullOrWhiteSpace(GetRoundIntent(tracked))
            ? CoachEngine.AutoRoundIntent(snapshot, result)
            : GetRoundIntent(tracked);

        result.Add(new RoundRecord
        {
            Round = tracked,
            Side = snapshot.Team,
            KillsTotal = snapshot.Kills ?? 0,
            DeathsTotal = snapshot.Deaths ?? 0,
            MoneyEnd = snapshot.Money ?? 0,
            KillsRound = Math.Max(0, (snapshot.Kills ?? 0) - _roundStartKills),
            DeathsRound = Math.Max(0, (snapshot.Deaths ?? 0) - _roundStartDeaths),
            Won = string.IsNullOrWhiteSpace(_trackedRoundWinTeam)
                ? null
                : string.Equals(
                    _trackedRoundWinTeam,
                    snapshot.Team,
                    StringComparison.OrdinalIgnoreCase),
            Survived = _trackedRoundLastHealth > 0,
            WeaponEnd = _trackedRoundLastHealth > 0
                ? _trackedRoundPrimaryWeapon
                : "",
            Intent = intent,
            PositionPlan = CoachEngine.PositionPlan(
                snapshot,
                GetRoundIntent(tracked),
                result),
            BombPlanted = _trackedBombPlanted,
            BombSite = _trackedBombSite,
            BombPlantSeconds = _trackedBombPlantSeconds
        });

        return result;
    }

    private void TryShowMatchReview(GameSnapshot snapshot)
    {
        if (!string.Equals(
                snapshot.MapPhase,
                "gameover",
                StringComparison.OrdinalIgnoreCase))
            return;

        var reviewRounds = BuildMatchReviewRounds();
        if (reviewRounds.Count < 4)
            return;

        var key =
            $"{snapshot.Map}|{snapshot.CtScore}:{snapshot.TScore}|{snapshot.Round}";

        if (string.Equals(
                _matchReviewShownKey,
                key,
                StringComparison.Ordinal))
            return;

        _matchReviewShownKey = key;
        SaveSessionSummary(snapshot.Map, snapshot, reviewRounds);

        using var review = new MatchReviewForm(
            snapshot.Map,
            snapshot.CtScore,
            snapshot.TScore,
            reviewRounds);
        review.ShowDialog(this);
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
        UpdatePresetSelection(DetectCurrentPreset());

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
            _lastGsiUtc = DateTime.UtcNow;
            _previous = _current;
            _current = s;
            _gameOverlay?.SetGameActive(!string.IsNullOrWhiteSpace(s.Map));

            if (_trackedRound == s.Round)
            {
                if (!string.IsNullOrWhiteSpace(s.RoundWinTeam))
                    _trackedRoundWinTeam = s.RoundWinTeam;

                _trackedRoundLastHealth = s.Health ?? _trackedRoundLastHealth;

                if ((s.Health ?? 0) > 0 && !string.IsNullOrWhiteSpace(s.PrimaryWeapon))
                    _trackedRoundPrimaryWeapon = s.PrimaryWeapon;

                TrackRoundPatternSnapshot(s);
            }

            bool mapChanged =
                !string.IsNullOrWhiteSpace(_previous.Map) &&
                !string.IsNullOrWhiteSpace(s.Map) &&
                !string.Equals(_previous.Map, s.Map, StringComparison.OrdinalIgnoreCase);

            if (mapChanged)
            {
                ArchiveCurrentSession(_previous.Map, _previous);
                _sessionStartedUtc = DateTime.UtcNow;
                _rounds.Clear();
                _roundIntents.Clear();
                _matchReviewShownKey = "";
                _trackedRound = s.Round;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
                _trackedRoundWinTeam = s.RoundWinTeam;
                _trackedRoundPrimaryWeapon = (s.Health ?? 0) > 0 ? s.PrimaryWeapon : "";
                _trackedRoundLastHealth = s.Health ?? 0;
                ResetRoundPatternTracking(s);
            }
            else if (_trackedRound == null && s.Round is int initialRound)
            {
                _trackedRound = initialRound;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
                _trackedRoundWinTeam = s.RoundWinTeam;
                _trackedRoundPrimaryWeapon = (s.Health ?? 0) > 0 ? s.PrimaryWeapon : "";
                _trackedRoundLastHealth = s.Health ?? 0;
                ResetRoundPatternTracking(s);
            }

            if (!mapChanged && _previous.Round is int pr && s.Round is int cr && pr != cr)
            {
                if (cr < pr)
                {
                    ArchiveCurrentSession(_previous.Map, _previous);
                    _sessionStartedUtc = DateTime.UtcNow;
                    _rounds.Clear();
                    _roundIntents.Clear();
                    _matchReviewShownKey = "";
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
                        DeathsRound = Math.Max(0, (_previous.Deaths ?? 0) - _roundStartDeaths),
                        Won = string.IsNullOrWhiteSpace(_trackedRoundWinTeam)
                            ? null
                            : string.Equals(
                                _trackedRoundWinTeam,
                                _previous.Team,
                                StringComparison.OrdinalIgnoreCase),
                        Survived = _trackedRoundLastHealth > 0,
                        WeaponEnd = _trackedRoundLastHealth > 0
                            ? _trackedRoundPrimaryWeapon
                            : "",
                        Intent = string.IsNullOrWhiteSpace(GetRoundIntent(pr))
                            ? CoachEngine.AutoRoundIntent(_previous, _rounds)
                            : GetRoundIntent(pr),
                        PositionPlan = CoachEngine.PositionPlan(
                            _previous,
                            GetRoundIntent(pr),
                            _rounds),
                        BombPlanted = _trackedBombPlanted,
                        BombSite = _trackedBombSite,
                        BombPlantSeconds = _trackedBombPlantSeconds
                    });
                    while (_rounds.Count > 40) _rounds.RemoveAt(0);
                }

                _trackedRound = cr;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
                _trackedRoundWinTeam = s.RoundWinTeam;
                _trackedRoundPrimaryWeapon = (s.Health ?? 0) > 0 ? s.PrimaryWeapon : "";
                _trackedRoundLastHealth = s.Health ?? 0;
                ResetRoundPatternTracking(s);
            }

            bool requestAi =
                s.Round is int aiRound &&
                (_lastAiRound != aiRound ||
                 !string.Equals(_lastAiMap, s.Map, StringComparison.OrdinalIgnoreCase));

            RefreshUi();
            TryShowMatchReview(s);

            if (requestAi && _autoAi.Checked)
            {
                _lastAiRound = s.Round;
                _lastAiMap = s.Map;
                _ = RefreshAiCoachAsync(s);
            }
        });
    }

    private void ResetRoundPatternTracking(GameSnapshot snapshot)
    {
        _trackedRoundLiveStartedUtc =
            string.Equals(
                snapshot.RoundPhase,
                "live",
                StringComparison.OrdinalIgnoreCase)
                ? DateTime.UtcNow
                : DateTime.MinValue;

        _trackedBombPlanted = false;
        _trackedBombSite = "";
        _trackedBombPlantSeconds = null;

        TrackRoundPatternSnapshot(snapshot);
    }

    private void TrackRoundPatternSnapshot(GameSnapshot snapshot)
    {
        if (_trackedRoundLiveStartedUtc == DateTime.MinValue &&
            string.Equals(
                snapshot.RoundPhase,
                "live",
                StringComparison.OrdinalIgnoreCase))
        {
            _trackedRoundLiveStartedUtc = DateTime.UtcNow;
        }

        if (!string.Equals(
                snapshot.BombState,
                "planted",
                StringComparison.OrdinalIgnoreCase))
            return;

        _trackedBombPlanted = true;

        if (_trackedBombPlantSeconds == null &&
            _trackedRoundLiveStartedUtc != DateTime.MinValue)
        {
            _trackedBombPlantSeconds = Math.Clamp(
                (DateTime.UtcNow - _trackedRoundLiveStartedUtc).TotalSeconds,
                0,
                120);
        }

        if (string.IsNullOrWhiteSpace(_trackedBombSite) &&
            snapshot.HasBombPosition)
        {
            _trackedBombSite = RadarCatalog.BombSiteFromWorld(
                snapshot.Map,
                snapshot.BombPositionX!.Value,
                snapshot.BombPositionY!.Value,
                snapshot.BombPositionZ!.Value);
        }
    }

    private string GetRoundIntent(int? round)
    {
        if (round is not int r)
            return "";

        return _roundIntents.TryGetValue(r, out var intent)
            ? CoachEngine.NormalizeRoundIntent(intent)
            : "";
    }

    private void OnChatIntentDetected(string intent)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnChatIntentDetected(intent));
            return;
        }

        intent = CoachEngine.NormalizeRoundIntent(intent);
        if (string.IsNullOrWhiteSpace(intent) || _current.Round is not int currentRound)
            return;

        var targetRound = string.Equals(
            _current.RoundPhase,
            "over",
            StringComparison.OrdinalIgnoreCase)
            ? currentRound + 1
            : currentRound;

        _roundIntents[targetRound] = intent;

        foreach (var old in _roundIntents.Keys
                     .Where(x => x < currentRound - 2)
                     .ToList())
        {
            _roundIntents.Remove(old);
        }

        _aiStatus.Text = $"CHAT {intent} • POSITION";
        _aiStatus.ForeColor = Color.FromArgb(255, 156, 44);

        if (targetRound == currentRound && _autoAi.Checked)
            _ = RefreshAiCoachAsync(_current, true);

        RefreshUi();
    }

    private async Task LoadFaceitSnapshotAsync(bool force = false)
    {
        if (_faceitLoading) return;

        if (!FaceitSettingsStore.HasKey)
        {
            _faceitSnapshot = null;
            _faceitHealth.Text = "● FACEIT OFF";
            _faceitHealth.ForeColor = Color.FromArgb(95, 106, 124);
            RefreshFaceitCard("Connect FACEIT to add ELO + recent form.");
            return;
        }

        if (!force &&
            _faceitSnapshot != null &&
            DateTime.UtcNow - _faceitLoadedUtc < TimeSpan.FromMinutes(10))
        {
            _faceitHealth.Text = "● FACEIT CONNECTED";
            _faceitHealth.ForeColor = Color.FromArgb(126, 240, 174);
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
        _faceitHealth.Text = "● FACEIT SYNC";
        _faceitHealth.ForeColor = Color.FromArgb(255, 178, 91);
        RefreshFaceitCard($"Loading {nickname}…");

        try
        {
            _faceitSnapshot = await new FaceitService().LoadAsync(nickname);
            _faceitLoadedUtc = DateTime.UtcNow;
            _faceitHealth.Text = "● FACEIT CONNECTED";
            _faceitHealth.ForeColor = Color.FromArgb(126, 240, 174);
            RefreshFaceitCard();
        }
        catch (Exception ex)
        {
            _faceitSnapshot = null;
            _faceitHealth.Text = "● FACEIT ERROR";
            _faceitHealth.ForeColor = Color.FromArgb(255, 174, 143);
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

    private void SetLiveLayoutExpanded(bool expanded)
    {
        if (_liveLeftLayout == null || _liveLayoutExpanded == expanded)
            return;

        _liveLayoutExpanded = expanded;

        if (expanded)
        {
            _liveLeftLayout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
            _liveLeftLayout.RowStyles[2] = new RowStyle(SizeType.Absolute, 116);
            _liveLeftLayout.RowStyles[3] = new RowStyle(SizeType.Absolute, 0);
        }
        else
        {
            _liveLeftLayout.RowStyles[1] = new RowStyle(SizeType.Absolute, 238);
            _liveLeftLayout.RowStyles[2] = new RowStyle(SizeType.Absolute, 104);
            _liveLeftLayout.RowStyles[3] = new RowStyle(SizeType.Percent, 100);
        }

        _liveLeftLayout.PerformLayout();
    }

    private void RefreshUi()
    {
        SetLiveLayoutExpanded(true);

        if (_phoneServer != null)
            _phoneUrl.Text = "Phone: " + _phoneServer.GetLocalUrl();

        _status.Text = "GSI LIVE";
        _gsiHealth.Text = "● GSI LIVE";

        int k = _current.Kills ?? 0;
        int d = _current.Deaths ?? 0;
        double kd = d > 0 ? (double)k / d : k;

        var prettyMap = CoachEngine.PrettyMap(_current.Map);
        var roundText = _current.Round is int currentRound ? $"R{currentRound + 1}" : "—";
        var scoreText = $"{_current.CtScore?.ToString() ?? "—"}:{_current.TScore?.ToString() ?? "—"}";

        var liveIntent = GetRoundIntent(_current.Round);
        var effectiveIntent = string.IsNullOrWhiteSpace(liveIntent)
            ? CoachEngine.AutoRoundIntent(_current, _rounds)
            : liveIntent;
        var intentSource = string.IsNullOrWhiteSpace(liveIntent) ? "AUTO" : "CHAT";

        _liveContext.Text =
            $"{prettyMap.ToUpperInvariant()}  •  {(_current.Team ?? "—")}  •  {roundText}  •  SCORE {scoreText}  •  {CoachEngine.ClassifyRound(_current).ToUpperInvariant()}  •  {intentSource} {effectiveIntent}";

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
            var outcome = rr.Won == true ? "W" : rr.Won == false ? "L" : "—";
            var carry = rr.Survived && !string.IsNullOrWhiteSpace(rr.WeaponEnd)
                ? " • " + CoachEngine.PrettyWeapon(rr.WeaponEnd)
                : "";
            _history.Items.Add(
                $"R{rr.Round + 1:00}   {outcome}   {rr.Side,2}   {rr.KillsRound}K/{rr.DeathsRound}D   $" +
                rr.MoneyEnd + carry);
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
        UpdateGameOverlay();
    }
}
