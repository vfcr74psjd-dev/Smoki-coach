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
    private AutoDemoLearningService? _autoDemoLearning;
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
    private CancellationTokenSource? _aiPrefetchCts;
    private string _latestAiAdvice = "AI coach čaka na nastavitev.";
    private int? _lastAiRound;
    private string _lastAiMap = "";
    private int? _aiPrefetchRound;
    private string _aiPrefetchMap = "";
    private string _aiPrefetchSide = "";
    private string _aiPrefetchIntent = "";
    private string _aiPrefetchedAdvice = "";
    private int? _trackedRound;
    private int? _ownKills;
    private int? _ownDeaths;
    private int? _ownAssists;
    private int? _lastOwnRound;
    private int? _lastOwnHealth;
    private string _ownStatsMap = "";
    private int? _spawnRound;
    private string _spawnMap = "";
    private float? _roundSpawnX;
    private float? _roundSpawnY;
    private float? _roundSpawnZ;
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
    private readonly Dictionary<int, TacticalPlanV6> _v6Plans = new();

    public MainForm()
    {
        Text = "Sm0ki Solo Coach";
        Width = 1580;
        Height = 960;
        MinimumSize = new Size(1240, 780);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(11, 13, 15);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        BuildUi();

        // External in-game overlay is intentionally disabled for stability.
        // Keep the setting pinned to Off so older profiles cannot recreate it.
        if (!string.Equals(_prefs.OverlayMode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            _prefs.OverlayMode = "Off";
            UserSettingsStore.Save(_prefs);
        }

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
            () => _rounds.ToArray(),
            GetRoundIntent,
            ApplyPhoneSettings
        );
        _phoneServer.Start();

        _demoInbox = new AutoDemoInboxService(
            () => FaceitSettingsStore.LoadNickname() ?? _profile.Nickname);
        _demoInbox.Start();

        _autoDemoLearning = new AutoDemoLearningService(
            () => FaceitSettingsStore.LoadNickname() ?? _profile.Nickname,
            _demoInbox);
        _autoDemoLearning.StatusChanged += status =>
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke((Action)(() =>
                {
                    _faceitTrend.Text = status;
                    _sessionText.Text = SessionInsight();
                }));
            }
            catch { }
        };
        _autoDemoLearning.Start();

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
            UpdateGameOverlay();

            await CheckForUpdatesSilentAsync();
            _ = AiCoachService.WarmUpAsync();
            _ = LoadFaceitSnapshotAsync();
            if (_autoDemoLearning != null)
                _ = _autoDemoLearning.TickNowAsync();
        };

        FormClosing += (_,__) =>
        {
            ArchiveCurrentSession(_current.Map, _current);
            _aiCts?.Cancel();
            _aiCts?.Dispose();
            _aiPrefetchCts?.Cancel();
            _aiPrefetchCts?.Dispose();
            _uiPulseTimer.Stop();
            _phoneServer?.Dispose();
            _autoDemoLearning?.Dispose();
            _demoInbox?.Dispose();
            if (_chatIntentWatcher != null)
                _chatIntentWatcher.IntentDetected -= OnChatIntentDetected;
            _chatIntentWatcher?.Dispose();
            _server.Dispose();
        };
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();
        _presetButtons.Clear();
        _stats.Clear();

        var bg = Color.FromArgb(7, 9, 11);
        var shell = Color.FromArgb(10, 12, 15);
        var rail = Color.FromArgb(9, 11, 14);
        var orange = Color.FromArgb(255, 156, 44);
        var muted = Color.FromArgb(119, 129, 142);
        var text = Color.FromArgb(241, 244, 248);

        BackColor = bg;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 222));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // TACTICAL OS NAV RAIL
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = new Padding(16, 18, 16, 14),
            BackColor = rail
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var brand = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(18, 10, 12, 8),
            BackColor = Color.FromArgb(20, 15, 11),
            GradientEndColor = Color.FromArgb(11, 13, 16),
            BorderColor = Color.FromArgb(74, 51, 29),
            AccentColor = orange,
            AccentWidth = 4,
            Radius = 18,
            DrawTopGlow = true
        };

        brand.Controls.Add(new Label
        {
            Text = "SM0KI",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 25, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 0, 0)
        });
        brand.Controls.Add(new Label
        {
            Text = "TACTICAL OS  //  LIVE AI",
            Dock = DockStyle.Bottom,
            Height = 24,
            Font = new Font("Segoe UI", 7.2f, FontStyle.Bold),
            ForeColor = orange,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 2)
        });
        sidebar.Controls.Add(brand, 0, 0);

        var profileCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(15, 6, 12, 6),
            BackColor = Color.FromArgb(15, 18, 22),
            BorderColor = Color.FromArgb(39, 44, 51),
            Radius = 13,
            Cursor = Cursors.Hand
        };
        profileCard.Click += (_,__) => EditProfile();

        _profileBadge.Text = string.IsNullOrWhiteSpace(_profile.Nickname)
            ? "PLAYER"
            : _profile.Nickname;
        _profileBadge.Dock = DockStyle.Fill;
        _profileBadge.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _profileBadge.ForeColor = text;
        _profileBadge.TextAlign = ContentAlignment.MiddleLeft;
        _profileBadge.Padding = new Padding(4, 0, 0, 0);
        profileCard.Controls.Add(_profileBadge);
        sidebar.Controls.Add(profileCard, 0, 1);

        sidebar.Controls.Add(new Label
        {
            Text = "COMMAND MODULES",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(78, 88, 101),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 0, 0)
        }, 0, 2);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Color.Transparent
        };

        nav.Controls.Add(MakeNavButton("●  LIVE COMMAND", true, (_,__) => { }));
        nav.Controls.Add(MakeNavButton("▦  PROGRESS", false, async (_,__) =>
        {
            await LoadFaceitSnapshotAsync();
            using var dialog = new SessionAnalyticsForm(
                _profile.Nickname,
                _current.Map,
                _rounds.ToList(),
                _faceitSnapshot);
            dialog.ShowDialog(this);
        }));
        nav.Controls.Add(MakeNavButton("⌁  OPPONENT SCOUT", false, (_,__) =>
        {
            using var scout = new OpponentScoutForm(
                _current.Map,
                FaceitSettingsStore.LoadNickname() ?? _profile.Nickname);
            scout.ShowDialog(this);

            if (_current.Round is int)
                _ = RefreshAiCoachAsync(_current, true);
        }));
        nav.Controls.Add(MakeNavButton("◇  MATCH LAB", false, (_,__) =>
        {
            using var lab = new ReviewLabForm(
                FaceitSettingsStore.LoadNickname() ?? _profile.Nickname);
            lab.ShowDialog(this);
        }));
        nav.Controls.Add(MakeNavButton("▤  PLAYBOOK", false, (_,__) =>
        {
            using var playbook = new TacticalMemoryForm(_current.Map);
            playbook.ShowDialog(this);
        }));
        nav.Controls.Add(MakeNavButton("◉  DIGITAL TWIN", false, (_,__) =>
        {
            using var twin = new DigitalTwinForm(
                _current.Map,
                _current.Team);
            twin.ShowDialog(this);
        }));
        nav.Controls.Add(MakeNavButton("⚙  TOOLS", false, (_,__) => ShowToolsHub()));
        sidebar.Controls.Add(nav, 0, 3);

        var health = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8),
            Padding = new Padding(14, 11, 14, 9),
            BackColor = Color.FromArgb(13, 16, 20),
            GradientEndColor = Color.FromArgb(10, 12, 15),
            BorderColor = Color.FromArgb(36, 42, 49),
            Radius = 15
        };

        var healthLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        healthLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        healthLayout.Controls.Add(new Label
        {
            Text = "SYSTEM LINK",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(84, 95, 108)
        }, 0, 0);

        _gsiHealth.Text = "●  GSI WAITING";
        _gsiHealth.Dock = DockStyle.Fill;
        _gsiHealth.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
        _gsiHealth.ForeColor = Color.FromArgb(235, 112, 100);
        _gsiHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_gsiHealth, 0, 1);

        _aiHealth.Text = "●  AI CHECK";
        _aiHealth.Dock = DockStyle.Fill;
        _aiHealth.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
        _aiHealth.ForeColor = Color.FromArgb(145, 151, 159);
        _aiHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_aiHealth, 0, 2);

        _faceitHealth.Text = FaceitSettingsStore.HasKey
            ? "●  FACEIT CHECK"
            : "●  FACEIT OFF";
        _faceitHealth.Dock = DockStyle.Fill;
        _faceitHealth.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
        _faceitHealth.ForeColor = Color.FromArgb(145, 151, 159);
        _faceitHealth.TextAlign = ContentAlignment.MiddleLeft;
        healthLayout.Controls.Add(_faceitHealth, 0, 3);

        health.Controls.Add(healthLayout);
        sidebar.Controls.Add(health, 0, 4);

        sidebar.Controls.Add(new Label
        {
            Text = $"TACTICAL OS  v{AppUpdater.CurrentVersion}",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(61, 70, 82),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 0, 0)
        }, 0, 5);

        root.Controls.Add(sidebar, 0, 0);

        // COMMAND CENTER WORKSPACE
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = bg
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(26, 14, 24, 10),
            Margin = Padding.Empty,
            BackColor = shell
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));

        var titleBlock = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        titleBlock.Controls.Add(new Label
        {
            Text = "SM0KI // TACTICAL OS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = orange,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);

        titleBlock.Controls.Add(new Label
        {
            Text = "LIVE COMMAND CENTER",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _liveContext.Text = "Waiting for CS2 telemetry…";
        _liveContext.Dock = DockStyle.Fill;
        _liveContext.Font = new Font("Segoe UI", 8.4f, FontStyle.Bold);
        _liveContext.ForeColor = muted;
        _liveContext.TextAlign = ContentAlignment.TopLeft;
        titleBlock.Controls.Add(_liveContext, 0, 2);
        top.Controls.Add(titleBlock, 0, 0);

        _status.Text = "WAITING";
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(8, 21, 8, 21);
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
        _status.ForeColor = Color.FromArgb(235, 112, 100);
        _status.BackColor = Color.FromArgb(43, 27, 27);
        top.Controls.Add(_status, 1, 0);

        _updateButton.Text = "UPDATE";
        StyleButton(_updateButton, true);
        _updateButton.Dock = DockStyle.Fill;
        _updateButton.Margin = new Padding(8, 21, 0, 21);
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        top.Controls.Add(_updateButton, 2, 0);
        workspace.Controls.Add(top, 0, 0);

        // MATCH CONTEXT RIBBON
        var statStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 1,
            Padding = new Padding(22, 9, 22, 9),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(8, 10, 12)
        };

        for (int i = 0; i < 8; i++)
            statStrip.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 12.5f));

        string[] statKeys =
        {
            "map", "side", "round", "score",
            "kills", "deaths", "kd", "money"
        };
        string[] statTitles =
        {
            "BATTLEFIELD", "SIDE", "ROUND", "SCORE",
            "KILLS", "DEATHS", "K / D", "ECONOMY"
        };

        for (int i = 0; i < statKeys.Length; i++)
        {
            var value = new Label();
            _stats[statKeys[i]] = value;

            var metric = MakeMiniMetric(
                statTitles[i],
                value);

            metric.Margin = new Padding(
                i == 0 ? 0 : 4,
                0,
                i == 7 ? 0 : 4,
                0);

            statStrip.Controls.Add(
                metric,
                i,
                0);
        }
        workspace.Controls.Add(statStrip, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(22, 7, 22, 12),
            Margin = Padding.Empty,
            BackColor = bg
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));

        // PRIMARY LIVE TACTICAL ZONE
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0, 0, 8, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _liveLeftLayout = left;
        _liveLayoutExpanded = false;

        var mission = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(18, 7, 16, 7),
            BackColor = Color.FromArgb(18, 14, 11),
            GradientEndColor = Color.FromArgb(12, 15, 18),
            BorderColor = Color.FromArgb(68, 48, 30),
            AccentColor = orange,
            AccentWidth = 4,
            Radius = 15
        };

        var missionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        missionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        missionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        missionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));

        missionLayout.Controls.Add(new Label
        {
            Text = "CURRENT DIRECTIVE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.4f, FontStyle.Bold),
            ForeColor = orange,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _tip.Dock = DockStyle.Fill;
        _tip.Font = new Font("Segoe UI", 10.2f, FontStyle.Bold);
        _tip.ForeColor = Color.FromArgb(219, 223, 229);
        _tip.TextAlign = ContentAlignment.MiddleLeft;
        _tip.AutoEllipsis = true;
        missionLayout.Controls.Add(_tip, 1, 0);

        _aiStatus.Text = "FAST PLAN";
        _aiStatus.Dock = DockStyle.Fill;
        _aiStatus.Font = new Font("Segoe UI", 7.6f, FontStyle.Bold);
        _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
        _aiStatus.TextAlign = ContentAlignment.MiddleRight;
        missionLayout.Controls.Add(_aiStatus, 2, 0);

        mission.Controls.Add(missionLayout);
        left.Controls.Add(mission, 0, 0);

        var planCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(16, 14, 16, 14),
            BackColor = Color.FromArgb(15, 18, 22),
            GradientEndColor = Color.FromArgb(10, 12, 15),
            BorderColor = Color.FromArgb(59, 48, 37),
            AccentColor = orange,
            AccentWidth = 4,
            Radius = 21,
            DrawTopGlow = true
        };

        var planLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
            Text = "LIVE TACTICAL DECISION",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        planHeader.Controls.Add(new Label
        {
            Text = "SPAWN • SIDE • MEMORY • ENEMY INTEL",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 7.1f, FontStyle.Bold),
            ForeColor = Color.FromArgb(102, 113, 126),
            TextAlign = ContentAlignment.MiddleRight
        }, 1, 0);

        planLayout.Controls.Add(planHeader, 0, 0);

        _planView.Dock = DockStyle.Fill;
        _planView.Margin = Padding.Empty;
        _planView.Advice = _latestAiAdvice;
        planLayout.Controls.Add(_planView, 0, 1);

        planCard.Controls.Add(planLayout);
        left.Controls.Add(planCard, 0, 1);

        var timelineCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(15, 10, 15, 8),
            BackColor = Color.FromArgb(13, 16, 19),
            BorderColor = Color.FromArgb(38, 44, 51),
            Radius = 15
        };

        var timelineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        timelineLayout.Controls.Add(new Label
        {
            Text = "ROUND SIGNAL  //  RECENT IMPACT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.3f, FontStyle.Bold),
            ForeColor = Color.FromArgb(91, 102, 115)
        }, 0, 0);

        _roundTimeline.Dock = DockStyle.Fill;
        _roundTimeline.FlowDirection = FlowDirection.LeftToRight;
        _roundTimeline.WrapContents = false;
        _roundTimeline.AutoScroll = true;
        _roundTimeline.Margin = Padding.Empty;
        _roundTimeline.Padding = new Padding(0, 7, 0, 0);
        _roundTimeline.BackColor = Color.Transparent;
        _roundTimeline.Controls.Add(
            MakeRoundPill(
                "READY\n—",
                Color.FromArgb(24, 28, 33),
                Color.FromArgb(139, 149, 160)));

        timelineLayout.Controls.Add(_roundTimeline, 0, 1);
        timelineCard.Controls.Add(timelineLayout);
        left.Controls.Add(timelineCard, 0, 2);

        var sessionCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(16, 11, 16, 11),
            BackColor = Color.FromArgb(12, 15, 18),
            GradientEndColor = Color.FromArgb(10, 12, 15),
            BorderColor = Color.FromArgb(35, 41, 48),
            Radius = 15
        };

        var sessionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        sessionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        sessionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sessionLayout.Controls.Add(new Label
        {
            Text = "TACTICAL MEMORY",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.3f, FontStyle.Bold),
            ForeColor = Color.FromArgb(88, 98, 111)
        }, 0, 0);

        _sessionText.Text = SessionInsight();
        _sessionText.Dock = DockStyle.Fill;
        _sessionText.Font = new Font("Segoe UI", 9.1f, FontStyle.Bold);
        _sessionText.ForeColor = Color.FromArgb(159, 169, 181);
        _sessionText.TextAlign = ContentAlignment.TopLeft;
        _sessionText.AutoEllipsis = true;
        sessionLayout.Controls.Add(_sessionText, 0, 1);
        sessionCard.Controls.Add(sessionLayout);
        left.Controls.Add(sessionCard, 0, 3);

        // INTELLIGENCE RAIL
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(8, 0, 0, 0),
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 520)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var coachCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(15, 13, 15, 11),
            BackColor = Color.FromArgb(15, 18, 22),
            GradientEndColor = Color.FromArgb(11, 13, 16),
            BorderColor = Color.FromArgb(44, 49, 56),
            AccentColor = orange,
            AccentWidth = 3,
            Radius = 17
        };

        var setupLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Margin = Padding.Empty
        };
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        setupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        setupLayout.Controls.Add(new Label
        {
            Text = "COACH PROFILE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = text
        }, 0, 0);

        setupLayout.Controls.Add(new Label
        {
            Text = "QUICK PRESETS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 102, 114)
        }, 0, 1);

        var presetFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 2),
            BackColor = Color.Transparent
        };
        presetFlow.Controls.Add(MakePresetButton("KILLS", "More Kills"));
        presetFlow.Controls.Add(MakePresetButton("ENTRY", "Entry"));
        presetFlow.Controls.Add(MakePresetButton("SAFE", "Survival"));
        presetFlow.Controls.Add(MakePresetButton("UTIL", "Utility"));
        presetFlow.Controls.Add(MakePresetButton("CLUTCH", "Clutch"));
        setupLayout.Controls.Add(presetFlow, 0, 2);

        _mode.Items.Clear();
        _mode.Items.AddRange(new object[] { "Balanced", "Aggressive", "Safe" });
        _mode.SelectedItem = _prefs.Mode;
        if (_mode.SelectedIndex < 0) _mode.SelectedIndex = 0;
        ConfigureCombo(_mode);
        _mode.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("MODE", _mode), 0, 3);

        _role.Items.Clear();
        _role.Items.AddRange(new object[] { "Flex", "Entry", "Lurk", "Support", "Anchor" });
        _role.SelectedItem = _prefs.Role;
        if (_role.SelectedIndex < 0) _role.SelectedIndex = 0;
        ConfigureCombo(_role);
        _role.SelectedIndexChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(MakeCompactSelector("ROLE", _role), 0, 4);

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
        setupLayout.Controls.Add(MakeCompactSelector("FOCUS", _focus), 0, 5);

        _autoAi.Text = "  AI REFINE ENABLED";
        _autoAi.Checked = _prefs.AutoAi;
        _autoAi.Dock = DockStyle.Fill;
        _autoAi.ForeColor = Color.FromArgb(173, 182, 194);
        _autoAi.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _autoAi.CheckedChanged += (_,__) => PreferencesChanged();
        setupLayout.Controls.Add(_autoAi, 0, 6);

        coachCard.Controls.Add(setupLayout);
        right.Controls.Add(coachCard, 0, 0);

        var faceitCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Color.FromArgb(14, 17, 21),
            BorderColor = Color.FromArgb(38, 44, 51),
            Radius = 17
        };

        var faceitLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        faceitLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        faceitLayout.Controls.Add(new Label
        {
            Text = "FACEIT INTEL",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = text
        }, 0, 0);

        faceitLayout.Controls.Add(new Label
        {
            Text = "LIVE PLAYER FORM",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(88, 99, 112)
        }, 0, 1);

        var faceitMetrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty
        };
        for (int i = 0; i < 4; i++)
            faceitMetrics.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25));

        faceitMetrics.Controls.Add(MakeMiniMetric("ELO", _faceitElo), 0, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("LVL", _faceitLevel), 1, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("AVG K", _faceitAvgKills), 2, 0);
        faceitMetrics.Controls.Add(MakeMiniMetric("K/D", _faceitKd), 3, 0);
        faceitLayout.Controls.Add(faceitMetrics, 0, 2);

        _faceitTrend.Text = FaceitSettingsStore.HasKey
            ? "Loading FACEIT intelligence…"
            : "Connect FACEIT in Tools.";
        _faceitTrend.Dock = DockStyle.Fill;
        _faceitTrend.Font = new Font("Segoe UI", 7.9f, FontStyle.Bold);
        _faceitTrend.ForeColor = Color.FromArgb(132, 139, 149);
        _faceitTrend.TextAlign = ContentAlignment.MiddleLeft;
        _faceitTrend.AutoEllipsis = true;
        faceitLayout.Controls.Add(_faceitTrend, 0, 3);

        faceitCard.Controls.Add(faceitLayout);
        right.Controls.Add(faceitCard, 0, 1);

        var recentCard = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Color.FromArgb(13, 16, 19),
            GradientEndColor = Color.FromArgb(10, 12, 15),
            BorderColor = Color.FromArgb(36, 42, 49),
            Radius = 17
        };

        var recentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        recentLayout.Controls.Add(new Label
        {
            Text = "ROUND MEMORY",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = text
        }, 0, 0);
        recentLayout.Controls.Add(new Label
        {
            Text = "LAST TRACKED IMPACT",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            ForeColor = Color.FromArgb(88, 99, 112)
        }, 0, 1);

        _history.Dock = DockStyle.Fill;
        _history.BackColor = Color.FromArgb(12, 15, 18);
        _history.ForeColor = Color.FromArgb(198, 205, 214);
        _history.BorderStyle = BorderStyle.None;
        _history.IntegralHeight = false;
        _history.Font = new Font("Cascadia Mono", 8.5f);
        _history.Items.Add("Waiting for completed rounds.");
        recentLayout.Controls.Add(_history, 0, 2);

        recentCard.Controls.Add(recentLayout);
        right.Controls.Add(recentCard, 0, 2);

        content.Controls.Add(left, 0, 0);
        content.Controls.Add(right, 1, 0);
        workspace.Controls.Add(content, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(24, 1, 22, 1),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(8, 10, 12)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));

        footer.Controls.Add(new Label
        {
            Text = "TACTICAL OS  //  DETERMINISTIC FIRST • AI REFINE SECOND • LIVE ROUND PLAN LOCK",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 6.8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 70, 82),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var refreshGsi = MakeButton("REFRESH GSI", 110, false);
        refreshGsi.Dock = DockStyle.Fill;
        refreshGsi.Margin = new Padding(2, 3, 0, 3);
        refreshGsi.MinimumSize = new Size(110, 28);
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
        _buyTitle.Visible = false;
        _buyText.Visible = false;
        _aiText.Visible = false;

        workspace.Controls.Add(footer, 0, 3);
        root.Controls.Add(workspace, 1, 0);

        void ApplyResponsiveLayout()
        {
            var w = Math.Max(1, ClientSize.Width);
            var h = Math.Max(1, ClientSize.Height);

            root.ColumnStyles[0].Width =
                w < 1360 ? 196 : 222;

            workspace.RowStyles[0].Height =
                h < 840 ? 100 : 112;
            workspace.RowStyles[1].Height =
                h < 840 ? 78 : 86;
            workspace.RowStyles[3].Height = 38;

            if (w < 1360)
            {
                content.ColumnStyles[0].Width = 70;
                content.ColumnStyles[1].Width = 30;
            }
            else
            {
                content.ColumnStyles[0].Width = 74;
                content.ColumnStyles[1].Width = 26;
            }

            left.RowStyles[0].Height =
                h < 840 ? 64 : 72;

            if (!_liveLayoutExpanded)
            {
                left.RowStyles[1].Height =
                    h < 840 ? 220 : 238;
                left.RowStyles[2].Height =
                    h < 840 ? 94 : 104;
            }

            right.RowStyles[0].Height =
                h < 840 ? 250 : 270;
            right.RowStyles[1].Height =
                h < 840 ? 170 : 184;

            top.ColumnStyles[1].Width =
                w < 1360 ? 118 : 132;
            top.ColumnStyles[2].Width =
                w < 1360 ? 104 : 118;

            _planView.Invalidate();
            PerformLayout();
        }

        Resize += (_,__) =>
            ApplyResponsiveLayout();

        DpiChanged += (_,__) =>
            BeginInvoke((Action)ApplyResponsiveLayout);

        Controls.Add(root);
        ApplyResponsiveLayout();

        _buyTitle.Text = "WAITING";
        _buyText.Text = "";
        _aiText.Text = _latestAiAdvice;

        UpdatePresetSelection(
            DetectCurrentPreset());

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

        grid.Controls.Add(ToolButton("PHONE LIVE COACH", "Round-by-round AI coach on your phone", (_,__) =>
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

        grid.Controls.Add(ToolButton("OVERLAY DISABLED", "Disabled for CS2 stability", (_,__) =>
        {
            MessageBox.Show(
                "In-game overlay je trenutno izklopljen zaradi stabilnosti CS2.\n\n" +
                "Live coach ostane v glavnem appu in na phone companionu.",
                "Sm0ki Solo Coach",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }), 0, 3);

        var resetButton = ToolButton("RESET TEST DATA", "Clear local stats • keep keys & settings", (_,__) =>
        {
            ResetTestData(dialog);
        });
        resetButton.ForeColor = Color.FromArgb(255, 188, 112);
        resetButton.FlatAppearance.BorderColor = Color.FromArgb(112, 72, 39);
        resetButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 34, 24);
        grid.Controls.Add(resetButton, 1, 3);

        root.Controls.Add(grid, 0, 1);

        var close = MakeButton("CLOSE", 90, false);
        close.Anchor = AnchorStyles.Right;
        close.Click += (_,__) => dialog.Close();
        root.Controls.Add(close, 0, 2);

        dialog.Controls.Add(root);
        dialog.ShowDialog(this);
    }

    private void ResetTestData(IWin32Window owner)
    {
        var confirm = MessageBox.Show(
            "To bo pobrisalo samo lokalne TESTNE podatke:\n\n" +
            "• Progress / session statistiko\n" +
            "• lokalni FACEIT history cache\n" +
            "• demo / heatmap history\n" +
            "• Personal Playbook + Opponent Scout intel\n" +
            "• cross-match Mistake Library\n" +
            "• Digital Twin + coach self-learning + Training Mission\n" +
            "• persistent Auto Demo learning queue\n" +
            "• recommendation traces + Demo Ground Truth\n" +
            "• trenutno lokalno rundno zgodovino\n\n" +
            "NE bo pobrisalo profila, FACEIT/OpenAI ključev ali nastavitev.\n" +
            "Tvoj dejanski FACEIT račun se s tem ne spremeni.\n\n" +
            "Nadaljujem?",
            "Reset test data",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
            return;

        SessionHistoryStore.Clear();
        FaceitHistoryStore.Clear();
        HeatMapStore.Clear();
        AdaptivePlaybookStore.Clear();
        OpponentScoutStore.Clear();
        OpponentDemoIntelStore.Clear();
        MistakeLibraryStore.Clear();
        DigitalTwinStore.Clear();
        TrainingMissionStore.Clear();
        DemoLearningQueueStore.Clear();
        RecommendationTraceStore.Clear();
        GroundTruthStore.Clear();

        _rounds.Clear();
        _roundIntents.Clear();
        _v6Plans.Clear();
        _history.Items.Clear();
        _history.Items.Add("Test data reset • waiting for completed rounds.");

        _trackedRound = null;
        _roundStartKills = 0;
        _roundStartDeaths = 0;
        _trackedRoundWinTeam = "";
        _trackedRoundPrimaryWeapon = "";
        _trackedRoundLastHealth = 0;
        _trackedRoundLiveStartedUtc = DateTime.MinValue;
        _trackedBombPlanted = false;
        _trackedBombSite = "";
        _trackedBombPlantSeconds = null;
        _sessionStartedUtc = DateTime.UtcNow;
        _matchReviewShownKey = "";
        _lastAiRound = null;
        _lastAiMap = "";

        _latestAiAdvice = "AI coach čaka na nove runde po resetu.";
        _aiText.Text = _latestAiAdvice;
        _planView.Advice = _latestAiAdvice;
        _planView.Invalidate();

        MessageBox.Show(
            "Testni podatki so pobrisani.\n\n" +
            "Profil, API ključi in nastavitve so ostali nespremenjeni.\n" +
            "FACEIT history se lahko pri naslednjem syncu ponovno napolni iz FACEIT-a.",
            "Sm0ki Solo Coach",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void UpdateGameOverlay()
    {
        // Keep the modern live coach panel synced. External game overlay is
        // intentionally disabled so this method never creates or touches a
        // second topmost window while CS2 is running.
        _planView.Advice = _latestAiAdvice;
    }

    private Button MakeNavButton(string text, bool active, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = 188,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Margin = new Padding(0, 3, 0, 3),
            Font = new Font("Segoe UI", 8.4f, FontStyle.Bold),
            ForeColor = active
                ? Color.White
                : Color.FromArgb(132, 143, 156),
            BackColor = active
                ? Color.FromArgb(38, 28, 19)
                : Color.FromArgb(9, 11, 14),
            Cursor = Cursors.Hand,
            TabStop = false
        };

        button.FlatAppearance.BorderSize = active ? 1 : 0;
        button.FlatAppearance.BorderColor =
            Color.FromArgb(132, 82, 35);
        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(24, 27, 32);
        button.FlatAppearance.MouseDownBackColor =
            Color.FromArgb(42, 31, 20);
        button.Click += click;
        return button;
    }

    private Button MakePresetButton(string text, string preset)
    {
        var button = new Button
        {
            Text = text,
            Tag = preset,
            Width = 72,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 5, 5),
            Font = new Font("Segoe UI", 7.1f, FontStyle.Bold),
            ForeColor = Color.FromArgb(183, 193, 205),
            BackColor = Color.FromArgb(18, 21, 25),
            Cursor = Cursors.Hand,
            TabStop = false
        };

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor =
            Color.FromArgb(48, 54, 62);
        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(34, 29, 23);
        button.Click += (_,__) =>
            ApplyCoachPreset(preset);

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
        var card = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(10, 6, 10, 6),
            BackColor = Color.FromArgb(14, 17, 21),
            GradientEndColor = Color.FromArgb(11, 13, 16),
            BorderColor = Color.FromArgb(37, 43, 50),
            Radius = 13
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("Segoe UI", 6.8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(84, 96, 111),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.AutoSize = false;
        value.Margin = Padding.Empty;
        value.Padding = Padding.Empty;
        value.Font = new Font("Segoe UI", 11.2f, FontStyle.Bold);
        value.ForeColor = Color.FromArgb(239, 243, 248);
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.AutoEllipsis = true;
        layout.Controls.Add(value, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private Control MakeCompactSelector(string title, ComboBox combo)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2),
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 72));
        row.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        row.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(91, 103, 117),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        combo.Dock = DockStyle.Fill;
        combo.Margin = new Padding(0, 1, 0, 1);
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

    private Label MakeRoundPill(
        string text,
        Color backColor,
        Color foreColor)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = 62,
            Height = 34,
            Margin = new Padding(0, 0, 7, 0),
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI", 7.4f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle
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
        combo.BackColor = Color.FromArgb(20, 23, 28);
        combo.ForeColor = Color.FromArgb(232, 236, 241);
        combo.Font = new Font("Segoe UI", 8.6f, FontStyle.Bold);
        combo.IntegralHeight = true;
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
        b.Height = 34;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(255, 156, 44)
            : Color.FromArgb(45, 51, 59);
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI", 8.3f, FontStyle.Bold);
        b.BackColor = primary
            ? Color.FromArgb(255, 156, 44)
            : Color.FromArgb(17, 20, 24);
        b.ForeColor = primary
            ? Color.FromArgb(16, 12, 8)
            : Color.FromArgb(226, 231, 237);
        b.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(255, 177, 79)
            : Color.FromArgb(28, 32, 38);
        b.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(225, 131, 30)
            : Color.FromArgb(34, 39, 45);
    }

    private Panel MakeCard()
    {
        return new ModernCardPanel
        {
            BackColor = Color.FromArgb(14, 17, 21),
            GradientEndColor = Color.FromArgb(10, 12, 15),
            BorderColor = Color.FromArgb(38, 44, 51),
            Radius = 17,
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

        var mode = _mode.SelectedItem?.ToString() ?? "Balanced";
        var role = _role.SelectedItem?.ToString() ?? "Flex";
        var focus = _focus.SelectedItem?.ToString() ?? "More kills";
        var intent = GetRoundIntent(snapshot.Round);

        // Tactical OS v6 makes the deterministic decision first. Local AI
        // may refine wording, but route selection is never blocked by the model.
        var tactical = TacticalBrainV6.Generate(
            snapshot,
            mode,
            role,
            focus,
            _rounds.ToList(),
            intent);

        if (snapshot.Round is int tacticalRound)
            _v6Plans[tacticalRound] = tactical;

        var instant = tactical.RenderLegacyCompatible();

        var prefetched = TryUsePrefetchedAi(snapshot, intent, instant, out var immediate);
        _latestAiAdvice = immediate;
        _aiText.Text = immediate;
        _aiStatus.Text = prefetched
            ? "AI PREFETCHED • READY"
            : _aiCoach.IsConfigured
                ? "FAST PLAN • AI REFINE"
                : "FAST PLAN";
        _aiStatus.ForeColor = prefetched || !_aiCoach.IsConfigured
            ? Color.FromArgb(126, 240, 174)
            : Color.FromArgb(255, 178, 91);
        UpdateGameOverlay();

        if (!_autoAi.Checked || !_aiCoach.IsConfigured)
            return;

        // If the between-round prefetch matched the current map/round/side/intent,
        // the useful AI refinement is already on screen. Do not burn more freeze-time
        // regenerating the same advice.
        if (prefetched && !force && !snapshot.HasSpawnPosition)
            return;

        // Once freeze-time exposes the real spawn, allow one fast refinement
        // with spawn context. The prefetched plan is already visible, so the
        // phone never waits on this call.

        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        _aiCts.CancelAfter(TimeSpan.FromSeconds(7));

        var requestedMap = snapshot.Map;
        var requestedRound = snapshot.Round;
        var requestToken = _aiCts.Token;

        try
        {
            var brain = SmartMatchBrainEngine.Analyze(_rounds);

            var advice = await _aiCoach.GenerateRoundAdviceAsync(
                snapshot,
                _rounds.ToList(),
                $"{mode} | Role={role} | Focus={focus} | SmartFocus={brain.OneFocus} | BrainPriority={brain.Priority}",
                _profile.Nickname,
                intent,
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
            if (_current.Map == requestedMap && _current.Round == requestedRound)
            {
                _aiStatus.Text = "FAST PLAN";
                _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            }
        }
        catch
        {
            if (_current.Map == requestedMap && _current.Round == requestedRound)
            {
                _aiStatus.Text = "FAST PLAN";
                _aiStatus.ForeColor = Color.FromArgb(126, 240, 174);
            }
        }
    }

    private async Task PrefetchNextRoundAiAsync(GameSnapshot snapshot)
    {
        if (!_autoAi.Checked ||
            !_aiCoach.IsConfigured ||
            snapshot.Round is not int currentRound ||
            string.IsNullOrWhiteSpace(snapshot.Map))
            return;

        var targetRound = currentRound + 1;
        var targetIntent = GetRoundIntent(targetRound);
        var targetMap = snapshot.Map;
        var targetSide = snapshot.Team ?? "";

        if (_aiPrefetchRound == targetRound &&
            string.Equals(_aiPrefetchMap, targetMap, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_aiPrefetchSide, targetSide, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_aiPrefetchIntent, targetIntent, StringComparison.OrdinalIgnoreCase))
            return;

        _aiPrefetchCts?.Cancel();
        _aiPrefetchCts?.Dispose();
        _aiPrefetchCts = new CancellationTokenSource();
        _aiPrefetchCts.CancelAfter(TimeSpan.FromSeconds(7));

        _aiPrefetchRound = targetRound;
        _aiPrefetchMap = targetMap;
        _aiPrefetchSide = targetSide;
        _aiPrefetchIntent = targetIntent;
        _aiPrefetchedAdvice = "";

        var rounds = BuildMatchReviewRounds();
        var brain = SmartMatchBrainEngine.Analyze(rounds);
        var mode = _mode.SelectedItem?.ToString() ?? "Balanced";
        var role = _role.SelectedItem?.ToString() ?? "Flex";
        var focus = _focus.SelectedItem?.ToString() ?? "More kills";
        var token = _aiPrefetchCts.Token;

        // Inventory/economy from round-over can be stale or spectated.
        // The prefetch is only used for DO + ADAPT; BUY/POSITION/EXPECT
        // are rebuilt from fresh next-round state.
        var predicted = new GameSnapshot
        {
            PlayerName = _profile.Nickname,
            LocalSteamId = snapshot.LocalSteamId,
            PlayerSteamId = snapshot.LocalSteamId,
            IsSpectating = false,
            Team = targetSide,
            Map = targetMap,
            MapPhase = snapshot.MapPhase,
            Round = targetRound,
            RoundPhase = "freezetime",
            CtScore = snapshot.CtScore,
            TScore = snapshot.TScore,
            Health = 100,
            Armor = snapshot.Armor,
            Helmet = snapshot.Helmet,
            Money = snapshot.Money,
            Kills = snapshot.Kills,
            Deaths = snapshot.Deaths,
            Assists = snapshot.Assists,
            Weapon = "",
            PrimaryWeapon = "",
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var advice = await _aiCoach.GenerateRoundAdviceAsync(
                predicted,
                rounds,
                $"{mode} | Role={role} | Focus={focus} | SmartFocus={brain.OneFocus} | BrainPriority={brain.Priority} | Prefetch=next-round",
                _profile.Nickname,
                targetIntent,
                token);

            if (token.IsCancellationRequested ||
                _aiPrefetchRound != targetRound ||
                !string.Equals(_aiPrefetchMap, targetMap, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_aiPrefetchSide, targetSide, StringComparison.OrdinalIgnoreCase))
                return;

            _aiPrefetchedAdvice = advice;
        }
        catch
        {
            // Prefetch is optional. The deterministic FAST PLAN remains immediate.
        }
    }

    private bool TryUsePrefetchedAi(
        GameSnapshot snapshot,
        string intent,
        string instant,
        out string merged)
    {
        merged = instant;

        if (snapshot.Round is not int round ||
            _aiPrefetchRound != round ||
            string.IsNullOrWhiteSpace(_aiPrefetchedAdvice) ||
            !string.Equals(_aiPrefetchMap, snapshot.Map, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_aiPrefetchSide, snapshot.Team, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_aiPrefetchIntent, intent, StringComparison.OrdinalIgnoreCase))
            return false;

        merged = MergeRefinedActionLines(instant, _aiPrefetchedAdvice);

        _aiPrefetchedAdvice = "";
        _aiPrefetchRound = null;
        _aiPrefetchMap = "";
        _aiPrefetchSide = "";
        _aiPrefetchIntent = "";

        return true;
    }

    private static string MergeRefinedActionLines(string instant, string refined)
    {
        static string? ReadLine(string text, string key)
            => text
                .Replace("\r", "")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .FirstOrDefault(x =>
                    x.StartsWith(
                        key + ":",
                        StringComparison.OrdinalIgnoreCase));

        var refinedDo = ReadLine(refined, "DO");
        var refinedAdapt = ReadLine(refined, "ADAPT");

        var lines = instant
            .Replace("\r", "")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            if (refinedDo != null &&
                lines[i].StartsWith("DO:", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = refinedDo;
            }
            else if (refinedAdapt != null &&
                     lines[i].StartsWith("ADAPT:", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = refinedAdapt;
            }
        }

        return string.Join("\n", lines);
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
            var development = PlayerDevelopmentEngine.Analyze(rounds);
            var matchKey =
                $"{map}|{_sessionStartedUtc:O}|{rounds.Count}|" +
                $"{snapshot.CtScore?.ToString() ?? "—"}:{snapshot.TScore?.ToString() ?? "—"}";

            MistakeLibraryStore.RecordMatch(
                matchKey,
                map,
                rounds);

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
                NextFocus = review.FocusTitle + " • " + review.FocusDetail,
                DevelopmentScore = development.OverallScore,
                DevelopmentLeak = development.BiggestLeak,
                DevelopmentFocus = development.MatchFocus
            });

            RecommendationTraceStore.MarkEnded(
                _sessionStartedUtc,
                FaceitSettingsStore.LoadNickname() ?? _profile.Nickname,
                map,
                DateTime.UtcNow);
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
            PositionPlan =
                _v6Plans.TryGetValue(tracked, out var reviewPlan)
                    ? reviewPlan.Route
                    : CoachEngine.PositionPlan(
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
        var halftime =
            HalftimeBrainV6.Analyze(
                _current.Team,
                _rounds);

        if (halftime.Active)
        {
            return
                $"HALFTIME BRAIN • {halftime.FromSide} → {halftime.ToSide} • " +
                $"KEEP: {halftime.Keep} • CHANGE: {halftime.Change} • " +
                $"OPEN: {halftime.Opening} • {halftime.Confidence}";
        }

        var side = string.IsNullOrWhiteSpace(_current.Team)
            ? ""
            : _current.Team;

        var twin =
            DigitalTwinStore.Analyze(
                _current.Map,
                side);

        var mission =
            TrainingMissionStore.Current(
                _current.Map);

        var sample =
            _rounds.TakeLast(10).ToList();

        if (sample.Count == 0)
        {
            var initialMissionText =
                !string.IsNullOrWhiteSpace(mission.Key)
                    ? $" • MISSION: {mission.Label}"
                    : "";

            return
                $"DIGITAL TWIN • {twin.Compact}{initialMissionText} • play normal CS2; v7 learns both your outcomes and its own recommendation accuracy.";
        }

        int kills =
            sample.Sum(r => r.KillsRound);
        int zero =
            sample.Count(r => r.KillsRound == 0);
        int multi =
            sample.Count(r => r.KillsRound >= 2);
        int deathRounds =
            sample.Count(r => r.DeathsRound > 0);

        double kr =
            (double)kills /
            sample.Count;

        double survival =
            100.0 *
            (sample.Count - deathRounds) /
            sample.Count;

        var recurring =
            MistakeLibraryStore.TopRecurring(
                _current.Map,
                10);

        var memory =
            recurring.Matches >= 3
                ? $" • MEMORY {recurring.Label} {recurring.Matches}/{recurring.RecentMatchesRead}"
                : "";

        var twinText =
            twin.Samples > 0
                ? $" • TWIN {twin.Archetype} {twin.Confidence} • best {twin.BestRoute}"
                : " • TWIN LEARNING";

        var missionText =
            !string.IsNullOrWhiteSpace(mission.Key)
                ? $" • MISSION {mission.SuccessRounds}/{mission.TrackedRounds} • streak {mission.CurrentStreak}"
                : "";

        string trend =
            kr >= 1.0
                ? "Impact high."
                : kr >= 0.7
                    ? "Solid impact."
                    : "Reduce early deaths.";

        return
            $"LAST {sample.Count} • {kr:0.00} K/R • {zero} zero-kill • " +
            $"{multi} multi • {survival:0}% survival • {trend}" +
            twinText +
            missionText +
            memory;
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

                _updateButton.Text = "Installing…";
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                        Close();
                }));
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

            var incomingMap = s.Map ?? "";
            var ownStatsMapChanged =
                !string.IsNullOrWhiteSpace(_ownStatsMap) &&
                !string.IsNullOrWhiteSpace(incomingMap) &&
                !string.Equals(
                    _ownStatsMap,
                    incomingMap,
                    StringComparison.OrdinalIgnoreCase);

            if (ownStatsMapChanged)
            {
                _ownKills = null;
                _ownDeaths = null;
                _ownAssists = null;
                _lastOwnRound = null;
                _lastOwnHealth = null;
            }

            if (!string.IsNullOrWhiteSpace(incomingMap))
                _ownStatsMap = incomingMap;

            if (!s.IsSpectating)
            {
                if (s.Kills.HasValue) _ownKills = s.Kills;
                if (s.Deaths.HasValue) _ownDeaths = s.Deaths;
                if (s.Assists.HasValue) _ownAssists = s.Assists;

                _lastOwnRound = s.Round;
                _lastOwnHealth = s.Health;
            }
            else
            {
                // Some GSI transitions jump straight from our live player
                // state to a teammate's spectated state. If the last own
                // snapshot was alive in this same live round, count exactly
                // one death before freezing our personal match stats.
                if (_lastOwnRound == s.Round &&
                    (_lastOwnHealth ?? 0) > 0 &&
                    string.Equals(
                        s.RoundPhase,
                        "live",
                        StringComparison.OrdinalIgnoreCase))
                {
                    _ownDeaths = (_ownDeaths ?? 0) + 1;
                    _lastOwnHealth = 0;
                }

                // Never allow spectated teammate stats into our Progress data.
                s.Kills = _ownKills;
                s.Deaths = _ownDeaths;
                s.Assists = _ownAssists;
            }

            var spawnIdentityChanged =
                _spawnRound != s.Round ||
                !string.Equals(
                    _spawnMap,
                    s.Map,
                    StringComparison.OrdinalIgnoreCase);

            if (spawnIdentityChanged)
            {
                _spawnRound = s.Round;
                _spawnMap = s.Map ?? "";
                _roundSpawnX = null;
                _roundSpawnY = null;
                _roundSpawnZ = null;
            }

            // Capture only our own freeze-time position. Never allow a
            // spectated teammate to replace the spawn used by the coach.
            if (!_roundSpawnX.HasValue &&
                !s.IsSpectating &&
                s.HasPosition &&
                string.Equals(
                    s.RoundPhase,
                    "freezetime",
                    StringComparison.OrdinalIgnoreCase))
            {
                _roundSpawnX = s.PositionX;
                _roundSpawnY = s.PositionY;
                _roundSpawnZ = s.PositionZ;
            }

            if (_roundSpawnX.HasValue &&
                _roundSpawnY.HasValue &&
                _roundSpawnZ.HasValue)
            {
                s.SpawnPositionX = _roundSpawnX;
                s.SpawnPositionY = _roundSpawnY;
                s.SpawnPositionZ = _roundSpawnZ;
            }

            _previous = _current;
            _current = s;

            if (_trackedRound == s.Round)
            {
                if (!string.IsNullOrWhiteSpace(s.RoundWinTeam))
                    _trackedRoundWinTeam = s.RoundWinTeam;

                // Survival/carry data must also remain personal. Spectated
                // teammate HP and weapons are never allowed into RoundRecord.
                if (!s.IsSpectating)
                {
                    _trackedRoundLastHealth = s.Health ?? _trackedRoundLastHealth;

                    if ((s.Health ?? 0) > 0 &&
                        !string.IsNullOrWhiteSpace(s.PrimaryWeapon))
                    {
                        _trackedRoundPrimaryWeapon = s.PrimaryWeapon;
                    }
                }
                else if ((_lastOwnHealth ?? 0) <= 0)
                {
                    _trackedRoundLastHealth = 0;
                }

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
                _v6Plans.Clear();
                _matchReviewShownKey = "";
                _trackedRound = s.Round;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
                _trackedRoundWinTeam = s.RoundWinTeam;
                _trackedRoundPrimaryWeapon =
                    !s.IsSpectating && (s.Health ?? 0) > 0
                        ? s.PrimaryWeapon
                        : "";
                _trackedRoundLastHealth =
                    !s.IsSpectating
                        ? s.Health ?? 0
                        : _lastOwnHealth ?? 0;
                ResetRoundPatternTracking(s);
            }
            else if (_trackedRound == null && s.Round is int initialRound)
            {
                _trackedRound = initialRound;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
                _trackedRoundWinTeam = s.RoundWinTeam;
                _trackedRoundPrimaryWeapon =
                    !s.IsSpectating && (s.Health ?? 0) > 0
                        ? s.PrimaryWeapon
                        : "";
                _trackedRoundLastHealth =
                    !s.IsSpectating
                        ? s.Health ?? 0
                        : _lastOwnHealth ?? 0;
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
                    _v6Plans.Clear();
                    _matchReviewShownKey = "";
                }
                else
                {
                    var completedRoute =
                        _v6Plans.TryGetValue(pr, out var completedPlan)
                            ? completedPlan.Route
                            : CoachEngine.PositionPlan(
                                _previous,
                                GetRoundIntent(pr),
                                _rounds);

                    var completedRound = new RoundRecord
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
                        PositionPlan = completedRoute,
                        BombPlanted = _trackedBombPlanted,
                        BombSite = _trackedBombSite,
                        BombPlantSeconds = _trackedBombPlantSeconds
                    };

                    _rounds.Add(completedRound);
                    AdaptivePlaybookStore.Record(
                        _previous.Map,
                        completedRound,
                        completedRoute);

                    if (completedPlan != null)
                    {
                        DigitalTwinStore.RecordOutcome(
                            _previous.Map,
                            completedRound,
                            completedPlan,
                            completedPlan.SpawnBias,
                            completedPlan.RoundType);

                        RecommendationTraceStore.RecordRound(
                            _sessionStartedUtc,
                            FaceitSettingsStore.LoadNickname() ?? _profile.Nickname,
                            _previous.Map,
                            completedRound,
                            completedPlan);
                    }

                    TrainingMissionStore.Update(
                        _previous.Map,
                        completedRound);

                    foreach (var oldPlanRound in _v6Plans.Keys
                                 .Where(x => x <= pr - 2)
                                 .ToList())
                    {
                        _v6Plans.Remove(oldPlanRound);
                    }

                    while (_rounds.Count > 40) _rounds.RemoveAt(0);
                }

                _trackedRound = cr;
                _roundStartKills = s.Kills ?? 0;
                _roundStartDeaths = s.Deaths ?? 0;
                _trackedRoundWinTeam = s.RoundWinTeam;
                _trackedRoundPrimaryWeapon =
                    !s.IsSpectating && (s.Health ?? 0) > 0
                        ? s.PrimaryWeapon
                        : "";
                _trackedRoundLastHealth =
                    !s.IsSpectating
                        ? s.Health ?? 0
                        : _lastOwnHealth ?? 0;
                ResetRoundPatternTracking(s);
            }

            if (string.Equals(
                    s.RoundPhase,
                    "over",
                    StringComparison.OrdinalIgnoreCase) &&
                s.Round is int)
            {
                _ = PrefetchNextRoundAiAsync(s);
            }

            bool requestAi =
                s.Round is int aiRound &&
                (_lastAiRound != aiRound ||
                 !string.Equals(_lastAiMap, s.Map, StringComparison.OrdinalIgnoreCase));

            RefreshUi();
            TryShowMatchReview(s);

            if (requestAi)
            {
                _lastAiRound = s.Round;
                _lastAiMap = s.Map;

                // Always regenerate the deterministic round plan so POSITION,
                // EXPECT, BUY, DO and ADAPT advance every round. Auto AI only
                // controls the optional local-model refinement.
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
