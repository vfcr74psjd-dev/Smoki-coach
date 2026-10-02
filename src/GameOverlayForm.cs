using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class GameOverlayForm : Form
{
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x534D;
    private const uint ModNoRepeat = 0x4000;

    private static readonly Color Bg = Color.FromArgb(12, 14, 16);
    private static readonly Color Panel = Color.FromArgb(20, 22, 24);
    private static readonly Color Border = Color.FromArgb(71, 61, 51);
    private static readonly Color Orange = Color.FromArgb(255, 156, 44);
    private static readonly Color Green = Color.FromArgb(112, 214, 151);
    private static readonly Color Muted = Color.FromArgb(150, 157, 166);

    private readonly System.Windows.Forms.Timer _animation = new();
    private GameSnapshot _snapshot = new();
    private string _advice = "BUY: waiting for CS2\nDO: waiting for round data\nADAPT: —";
    private string _aiStatus = "FAST PLAN";
    private string _mode = "Minimal";
    private bool _temporarilyHidden;
    private bool _gameActive;
    private DateTime _roundPulseStarted = DateTime.MinValue;
    private int? _lastRound;
    private string _lastMap = "";
    private bool _hotkeyRegistered;
    private Keys _hotkeyKey = Keys.None;

    public bool HotkeyRegistered => _hotkeyRegistered;
    public string HotkeyDisplay => _hotkeyRegistered ? _hotkeyKey.ToString() : "Unavailable";
    public string OverlayMode => _mode;

    public GameOverlayForm()
    {
        Text = "Sm0ki Coach Overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        ForeColor = Color.White;
        Opacity = 0.94;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 570;
        Height = 158;

        _animation.Interval = 30;
        _animation.Tick += (_,__) =>
        {
            if ((DateTime.UtcNow - _roundPulseStarted).TotalMilliseconds > 520)
                _animation.Stop();

            Invalidate();
        };

        PositionTopRight();
        UpdateRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        foreach (var candidate in new[] { Keys.F8, Keys.F9, Keys.F10, Keys.F11 })
        {
            if (!RegisterHotKey(
                    Handle,
                    HotkeyId,
                    ModNoRepeat,
                    (uint)candidate))
                continue;

            _hotkeyRegistered = true;
            _hotkeyKey = candidate;
            break;
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
            _hotkeyKey = Keys.None;
        }

        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleTemporaryVisibility();
            return;
        }

        base.WndProc(ref m);
    }

    public void ApplyMode(string? mode)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyMode(mode));
            return;
        }

        _mode = mode is "Full" or "Off" ? mode : "Minimal";
        Height = _mode == "Full" ? 218 : 158;
        UpdateRegion();
        PositionTopRight();

        if (_mode == "Off")
        {
            Hide();
            return;
        }

        _temporarilyHidden = false;
        RefreshVisibility();
        Invalidate();
    }

    public void SetGameActive(bool active)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetGameActive(active));
            return;
        }

        if (_gameActive == active)
            return;

        _gameActive = active;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        var shouldShow =
            _mode != "Off" &&
            !_temporarilyHidden &&
            _gameActive;

        if (!shouldShow)
        {
            Hide();
            return;
        }

        PositionTopRight();
        if (!Visible)
            Show();

        TopMost = true;
    }

    public void UpdateData(GameSnapshot snapshot, string advice, string aiStatus)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateData(snapshot, advice, aiStatus));
            return;
        }

        var roundChanged =
            snapshot.Round is int round &&
            (_lastRound != round ||
             !string.Equals(_lastMap, snapshot.Map, StringComparison.OrdinalIgnoreCase));

        _snapshot = snapshot;
        _advice = string.IsNullOrWhiteSpace(advice)
            ? "BUY: —\nDO: —\nADAPT: —"
            : advice.Trim();
        _aiStatus = string.IsNullOrWhiteSpace(aiStatus) ? "FAST PLAN" : aiStatus.Trim();

        if (snapshot.Round is int currentRound)
            _lastRound = currentRound;
        _lastMap = snapshot.Map ?? "";

        if (roundChanged)
        {
            _roundPulseStarted = DateTime.UtcNow;
            _animation.Start();
        }

        Invalidate();
    }

    public void ToggleTemporaryVisibility()
    {
        if (_mode == "Off")
            return;

        _temporarilyHidden = !_temporarilyHidden;
        RefreshVisibility();
        Invalidate();
    }

    public void PositionTopRight()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea
                     ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(
            Math.Max(screen.Left + 12, screen.Right - Width - 28),
            screen.Top + 34);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;

        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 13);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var panelBrush = new SolidBrush(Panel);
        using var borderPen = new Pen(Border, 1);
        using var shape = RoundedRect(rect, 13);
        g.FillPath(panelBrush, shape);
        g.DrawPath(borderPen, shape);

        var elapsed = (DateTime.UtcNow - _roundPulseStarted).TotalMilliseconds;
        var pulsing = elapsed >= 0 && elapsed <= 520;
        var pulse = pulsing
            ? 0.5 + 0.5 * Math.Sin(elapsed / 520.0 * Math.PI)
            : 0.0;

        var accentAlpha = pulsing ? 150 + (int)(105 * pulse) : 220;
        using var accentBrush = new SolidBrush(Color.FromArgb(accentAlpha, Orange));
        g.FillRectangle(accentBrush, 0, 0, 4, Height);

        DrawHeader(g);
        DrawAdvice(g, pulsing ? elapsed : 1000);

        if (_mode == "Full")
            DrawStats(g);
    }

    private void DrawHeader(Graphics g)
    {
        using var brandFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var brandBrush = new SolidBrush(Color.White);
        using var orangeBrush = new SolidBrush(Orange);
        using var mutedBrush = new SolidBrush(Muted);
        using var greenBrush = new SolidBrush(Green);

        g.DrawString("SM0KI COACH", brandFont, brandBrush, 18, 11);

        var map = CoachEngine.PrettyMap(_snapshot.Map).ToUpperInvariant();
        var round = _snapshot.Round is int r ? $"R{r + 1}" : "R—";
        var side = string.IsNullOrWhiteSpace(_snapshot.Team) ? "—" : _snapshot.Team;
        var context = $"{map}  •  {side}  •  {round}";
        g.DrawString(context, smallFont, orangeBrush, 122, 13);

        var statusSize = g.MeasureString(_aiStatus, smallFont);
        g.DrawString(
            _aiStatus,
            smallFont,
            _aiStatus.Contains("AI", StringComparison.OrdinalIgnoreCase) ? greenBrush : mutedBrush,
            Width - statusSize.Width - 18,
            13);

        using var separator = new Pen(Color.FromArgb(45, 48, 51));
        g.DrawLine(separator, 18, 34, Width - 18, 34);
    }

    private void DrawAdvice(Graphics g, double elapsedMs)
    {
        var lines = ParseAdvice(_advice);
        var reveal = elapsedMs < 110 ? 1 : elapsedMs < 220 ? 2 : 3;

        using var labelFont = new Font("Segoe UI", 8.3f, FontStyle.Bold);
        using var textFont = new Font("Segoe UI", 9.4f, FontStyle.Regular);
        using var orangeBrush = new SolidBrush(Orange);
        using var textBrush = new SolidBrush(Color.FromArgb(235, 237, 240));
        using var mutedBrush = new SolidBrush(Color.FromArgb(178, 184, 192));

        var y = 46f;
        var rowHeight = 31f;
        var labels = new[] { "BUY", "DO", "ADAPT" };

        for (var i = 0; i < 3; i++)
        {
            if (i >= reveal)
                break;

            var value = i < lines.Count ? lines[i] : "—";
            var colon = value.IndexOf(':');
            if (colon >= 0)
                value = value[(colon + 1)..].Trim();

            g.DrawString(labels[i], labelFont, i == 2 ? mutedBrush : orangeBrush, 18, y + 1);
            g.DrawString(value, textFont, i == 2 ? mutedBrush : textBrush, 76, y);
            y += rowHeight;
        }
    }

    private void DrawStats(Graphics g)
    {
        var top = 150;
        using var separator = new Pen(Color.FromArgb(45, 48, 51));
        g.DrawLine(separator, 18, top - 4, Width - 18, top - 4);

        var kills = _snapshot.Kills ?? 0;
        var deaths = _snapshot.Deaths ?? 0;
        var kd = deaths > 0 ? (double)kills / deaths : kills;
        var score = $"{_snapshot.CtScore?.ToString() ?? "—"}:{_snapshot.TScore?.ToString() ?? "—"}";
        var money = "$" + (_snapshot.Money ?? 0);

        var items = new[]
        {
            ("SCORE", score),
            ("K / D", kd.ToString("0.00")),
            ("KILLS", kills.ToString()),
            ("MONEY", money)
        };

        using var labelFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Muted);
        using var valueBrush = new SolidBrush(Color.White);

        var cellWidth = (Width - 36f) / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var x = 18 + i * cellWidth;
            g.DrawString(items[i].Item1, labelFont, labelBrush, x, top + 8);
            g.DrawString(items[i].Item2, valueFont, valueBrush, x, top + 27);
        }

        using var hintFont = new Font("Segoe UI", 7f);
        using var hintBrush = new SolidBrush(Color.FromArgb(112, 119, 128));
        var hotkeyHint = _hotkeyRegistered
            ? $"{_hotkeyKey}  HIDE / SHOW"
            : "HOTKEY UNAVAILABLE";
        var hintSize = g.MeasureString(hotkeyHint, hintFont);
        g.DrawString(hotkeyHint, hintFont, hintBrush, Width - hintSize.Width - 18, Height - 20);
    }

    private static List<string> ParseAdvice(string advice)
    {
        var lines = advice
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var ordered = new List<string>();
        foreach (var prefix in new[] { "BUY:", "DO:", "ADAPT:" })
        {
            var found = lines.FirstOrDefault(x =>
                x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(found))
                ordered.Add(found);
        }

        if (ordered.Count == 3)
            return ordered;

        foreach (var line in lines)
        {
            if (ordered.Count >= 3)
                break;
            if (!ordered.Contains(line))
                ordered.Add(line);
        }

        while (ordered.Count < 3)
            ordered.Add("—");

        return ordered;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id);
}
