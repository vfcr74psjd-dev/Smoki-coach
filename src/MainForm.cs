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
        Height = 780;
        MinimumSize = new Size(1100, 680);
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
            Height = 78,
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
        _mode.SelectedItem = _prefs.Mode;
        if (_mode.SelectedIndex < 0) _mode.SelectedIndex = 0;
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Left = 24;
        _mode.Top = 28;
        _mode.Width = 150;
        _mode.FlatStyle = FlatStyle.Flat;
        _mode.BackColor = Color.FromArgb(24,29,39);
        _mode.ForeColor = Color.White;
        _mode.SelectedIndexChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(_mode);

        toolbar.Controls.Add(new Label
        {
            Text = "ROLE",
            AutoSize = true,
            Left = 184,
            Top = 9,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI", 8, FontStyle.Bold)
        });
        _role.Items.AddRange(new object[] { "Flex", "Entry", "Lurk", "Support", "Anchor" });
        _role.SelectedItem = _prefs.Role;
        if (_role.SelectedIndex < 0) _role.SelectedIndex = 0;
        _role.DropDownStyle = ComboBoxStyle.DropDownList;
        _role.Left = 184;
        _role.Top = 28;
        _role.Width = 120;
        _role.FlatStyle = FlatStyle.Flat;
        _role.BackColor = Color.FromArgb(24,29,39);
        _role.ForeColor = Color.White;
        _role.SelectedIndexChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(_role);

        toolbar.Controls.Add(new Label
        {
            Text = "FOCUS",
            AutoSize = true,
            Left = 318,
            Top = 9,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI", 8, FontStyle.Bold)
        });
        _focus.Items.AddRange(new object[] { "More kills", "Survive & trade", "Entry impact", "Utility impact", "Clutch / late round" });
        _focus.SelectedItem = _prefs.Focus;
        if (_focus.SelectedIndex < 0) _focus.SelectedIndex = 0;
        _focus.DropDownStyle = ComboBoxStyle.DropDownList;
        _focus.Left = 318;
        _focus.Top = 28;
        _focus.Width = 170;
        _focus.FlatStyle = FlatStyle.Flat;
        _focus.BackColor = Color.FromArgb(24,29,39);
        _focus.ForeColor = Color.White;
        _focus.SelectedIndexChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(_focus);

        _phoneUrl.AutoSize = true;
        _phoneUrl.Visible = false;
        _phoneUrl.Left = 0;
        _phoneUrl.Top = 0;
        _phoneUrl.ForeColor = Color.FromArgb(112,124,145);
        _phoneUrl.Font = new Font("Segoe UI", 8.5f);
        toolbar.Controls.Add(_phoneUrl);

        var phone = MakeButton("Phone Mode", 118, false);
        phone.Left = 296;
        phone.Top = 15;
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
        _updateButton.Left = 504;
        _updateButton.Top = 27;
        _updateButton.Click += async (_,__) => await CheckForUpdatesAsync();
        toolbar.Controls.Add(_updateButton);

        var install = MakeButton("Refresh GSI", 118, false);
        install.Left = 730;
        install.Top = 27;
        install.Click += (_,__) =>
        {
            var p = GsiInstaller.TryInstall();
            MessageBox.Show(p != null ? $"Installed to:\n{p}\n\nRestart CS2 if already open." : "CS2 cfg folder was not found automatically.",
                "Sm0ki Solo Coach");
        };
        toolbar.Controls.Add(install);

        var aiSettings = MakeButton("Local AI", 118, false);
        aiSettings.Left = 858;
        aiSettings.Top = 27;
        aiSettings.Click += (_,__) =>
        {
            using var dialog = new AiSettingsForm();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                UpdateAiStatus();
                if (_current.Round is int)
                    _ = RefreshAiCoachAsync(_current, true);
            }
        };
        toolbar.Controls.Add(aiSettings);

        _autoAi.Text = "Auto AI";
        _autoAi.Left = 986;
        _autoAi.Top = 32;
        _autoAi.Width = 92;
        _autoAi.Height = 28;
        _autoAi.Checked = _prefs.AutoAi;
        _autoAi.ForeColor = Color.FromArgb(205,211,221);
        _autoAi.BackColor = Color.Transparent;
        _autoAi.CheckedChanged += (_,__) => PreferencesChanged();
        toolbar.Controls.Add(_autoAi);

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

        _buyText.Left=20; _buyText.Top=98; _buyText.Width=440; _buyText.Height=82;
        _buyText.ForeColor=Color.FromArgb(190,198,211);
        _buyText.Font = new Font("Segoe UI", 10);

        var divider = new Panel { Left=20, Top=190, Width=440, Height=1, BackColor=Color.FromArgb(43,50,64) };

        var aiHdr=Header("AI ROUND COACH");
        aiHdr.Top=208; aiHdr.Left=20;

        _aiStatus.AutoSize = true;
        _aiStatus.Top = 208;
        _aiStatus.Left = 350;
        _aiStatus.Font = new Font("Segoe UI", 8, FontStyle.Bold);

        _aiText.Left=20; _aiText.Top=238; _aiText.Width=440; _aiText.Height=150;
        _aiText.ForeColor=Color.FromArgb(220,225,234);
        _aiText.Font = new Font("Segoe UI", 10);
        _aiText.Text = "AI coach čaka na nastavitev.";

        var divider2 = new Panel { Left=20, Top=400, Width=440, Height=1, BackColor=Color.FromArgb(43,50,64) };

        var coachHdr=Header("LOCAL FALLBACK COACH");
        coachHdr.Top=418; coachHdr.Left=20;
        _tip.Left=20; _tip.Top=446; _tip.Width=440; _tip.Height=70;
        _tip.ForeColor=Color.FromArgb(170,180,195);
        _tip.Font = new Font("Segoe UI", 9);

        left.Controls.Add(buyHdr);
        left.Controls.Add(_buyTitle);
        left.Controls.Add(_buyText);
        left.Controls.Add(divider);
        left.Controls.Add(aiHdr);
        left.Controls.Add(_aiStatus);
        left.Controls.Add(_aiText);
        left.Controls.Add(divider2);
        left.Controls.Add(coachHdr);
        left.Controls.Add(_tip);

        var histHdr=Header("ROUND HISTORY");
        histHdr.Top=22; histHdr.Left=20;

        var histSub = new Label
        {
            Text = "Session insight + recent round deltas",
            AutoSize = true,
            Left = 20,
            Top = 47,
            ForeColor = Color.FromArgb(96,108,128),
            Font = new Font("Segoe UI",8.5f)
        };
        right.Controls.Add(histHdr);
        right.Controls.Add(histSub);

        _sessionText.Left=20; _sessionText.Top=76; _sessionText.Width=405; _sessionText.Height=62;
        _sessionText.ForeColor=Color.FromArgb(170,180,195);
        _sessionText.Font=new Font("Segoe UI",9);

        _history.Left=20; _history.Top=146; _history.Width=405; _history.Height=310;
        _history.BackColor=Color.FromArgb(17,22,30);
        _history.ForeColor=Color.FromArgb(206,213,223);
        _history.BorderStyle=BorderStyle.None;
        _history.Font=new Font("Cascadia Mono",10);
        right.Controls.Add(_sessionText);
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
            return "Po nekaj zaključenih rundah bom tukaj prikazal kill output, 0-kill runde in multi-kill trend.";

        int previousKills = 0;
        int previousDeaths = 0;
        int totalRoundKills = 0;
        int zero = 0;
        int multi = 0;
        int deathRounds = 0;

        foreach (var r in sample)
        {
            int rk = Math.Max(0, r.KillsTotal - previousKills);
            int rd = Math.Max(0, r.DeathsTotal - previousDeaths);
            totalRoundKills += rk;
            if (rk == 0) zero++;
            if (rk >= 2) multi++;
            if (rd > 0) deathRounds++;
            previousKills = r.KillsTotal;
            previousDeaths = r.DeathsTotal;
        }

        double kr = sample.Count > 0 ? (double)totalRoundKills / sample.Count : 0;
        return $"Last {sample.Count} rounds • approx {kr:0.00} K/R • {zero} zero-kill • {multi} multi-kill • deaths in {deathRounds}/{sample.Count}.";
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
