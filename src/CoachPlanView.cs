using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class CoachPlanView : Control
{
    private static readonly Color Orange = Color.FromArgb(255, 156, 44);
    private static readonly Color Text = Color.FromArgb(238, 240, 243);
    private static readonly Color Muted = Color.FromArgb(141, 148, 157);
    private static readonly Color SoftPanel = Color.FromArgb(23, 25, 28);
    private static readonly Color SoftBorder = Color.FromArgb(47, 50, 54);

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
        BackColor = Color.FromArgb(18, 20, 22);
        MinimumSize = new Size(420, 260);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var data = Parse(_advice);
        var pad = 8;
        var width = Math.Max(10, ClientSize.Width - pad * 2);
        var y = 4;

        DrawPrimary(g, new Rectangle(pad, y, width, 72),
            "POSITION",
            data["POSITION"],
            true);
        y += 80;

        DrawPrimary(g, new Rectangle(pad, y, width, 64),
            "DO",
            data["DO"],
            false);
        y += 72;

        DrawSlim(g, new Rectangle(pad, y, width, 42),
            "EXPECT",
            data["EXPECT"],
            Orange);
        y += 48;

        var half = (width - 8) / 2;
        DrawSlim(g, new Rectangle(pad, y, half, 52),
            "BUY",
            data["BUY"],
            Color.FromArgb(210, 214, 220));
        DrawSlim(g, new Rectangle(pad + half + 8, y, half, 52),
            "ADAPT",
            data["ADAPT"],
            Muted);
    }

    private static void DrawPrimary(
        Graphics g,
        Rectangle rect,
        string label,
        string value,
        bool emphasize)
    {
        using var bg = new SolidBrush(emphasize
            ? Color.FromArgb(34, 29, 23)
            : SoftPanel);
        using var border = new Pen(emphasize
            ? Color.FromArgb(112, 77, 39)
            : SoftBorder);
        using var path = RoundedRect(rect, 12);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        using var labelFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var valueFont = new Font(
            "Segoe UI",
            emphasize ? 15f : 12.5f,
            FontStyle.Bold);
        using var orange = new SolidBrush(Orange);
        using var text = new SolidBrush(Text);

        g.DrawString(label, labelFont, orange, rect.Left + 14, rect.Top + 10);

        var valueRect = new RectangleF(
            rect.Left + 14,
            rect.Top + 28,
            rect.Width - 28,
            rect.Height - 32);

        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(
            string.IsNullOrWhiteSpace(value) ? "—" : value,
            valueFont,
            text,
            valueRect,
            format);
    }

    private static void DrawSlim(
        Graphics g,
        Rectangle rect,
        string label,
        string value,
        Color valueColor)
    {
        using var bg = new SolidBrush(Color.FromArgb(19, 21, 23));
        using var border = new Pen(Color.FromArgb(40, 43, 47));
        using var path = RoundedRect(rect, 10);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        using var labelFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var labelBrush = new SolidBrush(Color.FromArgb(115, 121, 130));
        using var valueBrush = new SolidBrush(valueColor);

        g.DrawString(label, labelFont, labelBrush, rect.Left + 12, rect.Top + 7);

        var valueRect = new RectangleF(
            rect.Left + 62,
            rect.Top + 7,
            rect.Width - 74,
            rect.Height - 12);

        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            LineAlignment = StringAlignment.Near
        };

        g.DrawString(
            string.IsNullOrWhiteSpace(value) ? "—" : value,
            valueFont,
            valueBrush,
            valueRect,
            format);
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
            ["ADAPT"] = "—"
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

            result[key] = line[(colon + 1)..].Trim();
        }

        return result;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
