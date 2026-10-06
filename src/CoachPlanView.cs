using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class CoachPlanView : Control
{
    private static readonly Color Orange = Color.FromArgb(255, 156, 44);
    private static readonly Color OrangeSoft = Color.FromArgb(255, 188, 105);
    private static readonly Color PrimaryText = Color.FromArgb(244, 246, 249);
    private static readonly Color Muted = Color.FromArgb(143, 151, 162);
    private static readonly Color Border = Color.FromArgb(48, 53, 60);
    private static readonly Color Green = Color.FromArgb(126, 240, 174);

    private string _advice = "";

    public string Advice
    {
        get => _advice;
        set
        {
            _advice = value ?? "";
            Invalidate();
        }
    }

    public CoachPlanView()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        DoubleBuffered = true;
        BackColor = Color.FromArgb(12, 14, 17);
        MinimumSize = new Size(360, 260);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const float logicalHeight = 390f;
        var availableHeight = Math.Max(1f, ClientSize.Height - 2f);
        var scale = Math.Min(1f, availableHeight / logicalHeight);
        scale = Math.Max(0.60f, scale);
        g.ScaleTransform(scale, scale);

        var logicalWidth = Math.Max(
            340,
            (int)Math.Floor(ClientSize.Width / scale));

        var data = Parse(_advice);
        var pad = 10;
        var width = Math.Max(40, logicalWidth - pad * 2);
        var y = 6;

        DrawRouteHero(
            g,
            new Rectangle(pad, y, width, 116),
            data["POSITION"],
            data["CONFIDENCE"]);
        y += 128;

        DrawFirstMove(
            g,
            new Rectangle(pad, y, width, 82),
            data["DO"]);
        y += 94;

        const int gap = 8;
        var third = Math.Max(90, (width - gap * 2) / 3);

        DrawIntelCard(
            g,
            new Rectangle(pad, y, third, 76),
            "EXPECT",
            data["EXPECT"],
            OrangeSoft);

        DrawIntelCard(
            g,
            new Rectangle(pad + third + gap, y, third, 76),
            "BUY",
            data["BUY"],
            Text);

        DrawIntelCard(
            g,
            new Rectangle(
                pad + (third + gap) * 2,
                y,
                width - third * 2 - gap * 2,
                76),
            "IF BLOCKED",
            data["ADAPT"],
            Muted);
        y += 88;

        DrawFocusStrip(
            g,
            new Rectangle(pad, y, width, 60),
            data["FOCUS"],
            data["WHY"]);
    }

    private static void DrawRouteHero(
        Graphics g,
        Rectangle rect,
        string value,
        string confidence)
    {
        using var path = RoundedRect(rect, 18);
        using var bg = new LinearGradientBrush(
            rect,
            Color.FromArgb(48, 32, 18),
            Color.FromArgb(17, 20, 24),
            LinearGradientMode.Horizontal);

        g.FillPath(bg, path);

        using var border = new Pen(Color.FromArgb(132, 87, 39), 1.2f);
        g.DrawPath(border, path);

        using var accent = new SolidBrush(Orange);
        g.FillRectangle(
            accent,
            rect.Left,
            rect.Top + 17,
            5,
            rect.Height - 34);

        using var labelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var routeFont = new Font("Segoe UI", 21f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Orange);
        using var textBrush = new SolidBrush(PrimaryText);

        g.DrawString(
            "TACTICAL ROUTE / START",
            labelFont,
            labelBrush,
            rect.Left + 20,
            rect.Top + 15);

        var routeRect = new RectangleF(
            rect.Left + 20,
            rect.Top + 44,
            rect.Width - 40,
            rect.Height - 54);

        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(
            string.IsNullOrWhiteSpace(value)
                ? "WAITING FOR LIVE ROUND DATA"
                : value,
            routeFont,
            textBrush,
            routeRect,
            format);

        var conf = string.IsNullOrWhiteSpace(confidence)
            ? "FAST PLAN"
            : confidence.Trim().ToUpperInvariant();

        var pill = new Rectangle(
            rect.Right - 124,
            rect.Top + 13,
            104,
            24);

        using var pillPath = RoundedRect(pill, 12);
        using var pillFill = new SolidBrush(
            conf.Contains("HIGH", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(27, 62, 43)
                : conf.Contains("MEDIUM", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(63, 49, 27)
                    : Color.FromArgb(38, 41, 46));

        g.FillPath(pillFill, pillPath);

        using var pillFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var pillBrush = new SolidBrush(
            conf.Contains("HIGH", StringComparison.OrdinalIgnoreCase)
                ? Green
                : conf.Contains("MEDIUM", StringComparison.OrdinalIgnoreCase)
                    ? OrangeSoft
                    : Muted);

        using var pillFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(
            conf,
            pillFont,
            pillBrush,
            pill,
            pillFormat);
    }

    private static void DrawFirstMove(
        Graphics g,
        Rectangle rect,
        string value)
    {
        using var path = RoundedRect(rect, 16);
        using var fill = new SolidBrush(Color.FromArgb(22, 25, 29));
        using var outline = new Pen(Color.FromArgb(53, 59, 66));
        g.FillPath(fill, path);
        g.DrawPath(outline, path);

        using var labelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 14.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(128, 139, 151));
        using var valueBrush = new SolidBrush(PrimaryText);

        g.DrawString(
            "FIRST MOVE",
            labelFont,
            labelBrush,
            rect.Left + 16,
            rect.Top + 12);

        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(
            string.IsNullOrWhiteSpace(value) ? "—" : value,
            valueFont,
            valueBrush,
            new RectangleF(
                rect.Left + 16,
                rect.Top + 34,
                rect.Width - 32,
                rect.Height - 42),
            format);
    }

    private static void DrawIntelCard(
        Graphics g,
        Rectangle rect,
        string label,
        string value,
        Color valueColor)
    {
        using var path = RoundedRect(rect, 13);
        using var fill = new SolidBrush(Color.FromArgb(18, 21, 25));
        using var outline = new Pen(Border);
        g.FillPath(fill, path);
        g.DrawPath(outline, path);

        using var labelFont = new Font("Segoe UI", 7.2f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 9.4f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(103, 113, 125));
        using var valueBrush = new SolidBrush(valueColor);

        g.DrawString(
            label,
            labelFont,
            labelBrush,
            rect.Left + 12,
            rect.Top + 10);

        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(
            string.IsNullOrWhiteSpace(value) ? "—" : value,
            valueFont,
            valueBrush,
            new RectangleF(
                rect.Left + 12,
                rect.Top + 31,
                rect.Width - 24,
                rect.Height - 37),
            format);
    }

    private static void DrawFocusStrip(
        Graphics g,
        Rectangle rect,
        string focus,
        string why)
    {
        using var path = RoundedRect(rect, 13);
        using var fill = new SolidBrush(Color.FromArgb(14, 17, 20));
        using var outline = new Pen(Color.FromArgb(42, 47, 53));
        g.FillPath(fill, path);
        g.DrawPath(outline, path);

        using var labelFont = new Font("Segoe UI", 7.2f, FontStyle.Bold);
        using var focusFont = new Font("Segoe UI", 9.6f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Orange);
        using var focusBrush = new SolidBrush(PrimaryText);
        using var whyBrush = new SolidBrush(Muted);

        g.DrawString(
            "ONE FOCUS",
            labelFont,
            labelBrush,
            rect.Left + 12,
            rect.Top + 9);

        var focusValue = string.IsNullOrWhiteSpace(focus)
            ? "KEEP PLAN"
            : focus;

        g.DrawString(
            focusValue,
            focusFont,
            focusBrush,
            rect.Left + 82,
            rect.Top + 7);

        if (!string.IsNullOrWhiteSpace(why))
        {
            using var format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            g.DrawString(
                why,
                labelFont,
                whyBrush,
                new RectangleF(
                    rect.Left + 12,
                    rect.Top + 33,
                    rect.Width - 24,
                    18),
                format);
        }
    }

    private static Dictionary<string, string> Parse(string advice)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["BUY"] = "—",
            ["POSITION"] = "Waiting for live round data",
            ["EXPECT"] = "Learning enemy patterns",
            ["DO"] = "—",
            ["ADAPT"] = "—",
            ["FOCUS"] = "KEEP PLAN",
            ["WHY"] = "",
            ["CONFIDENCE"] = ""
        };

        foreach (var line in (advice ?? "")
                     .Replace("\r", "")
                     .Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            if (!result.ContainsKey(key))
                continue;

            result[key] =
                line[(colon + 1)..].Trim();
        }

        return result;
    }

    private static GraphicsPath RoundedRect(
        Rectangle rect,
        int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, radius * 2);

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
