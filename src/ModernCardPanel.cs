using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class ModernCardPanel : Panel
{
    public int Radius { get; set; } = 18;
    public Color BorderColor { get; set; } = Color.FromArgb(42, 46, 52);
    public Color GradientEndColor { get; set; } = Color.Empty;
    public Color AccentColor { get; set; } = Color.Empty;
    public int AccentWidth { get; set; }
    public bool DrawTopGlow { get; set; }

    public ModernCardPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(17, 19, 22);
        Margin = new Padding(5);

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRegion();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2)
        {
            base.OnPaintBackground(e);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width, Height);
        using var path = RoundedRect(bounds, Radius);

        if (GradientEndColor != Color.Empty)
        {
            using var gradient = new LinearGradientBrush(
                bounds,
                BackColor,
                GradientEndColor,
                LinearGradientMode.Vertical);
            e.Graphics.FillPath(gradient, path);
        }
        else
        {
            using var fill = new SolidBrush(BackColor);
            e.Graphics.FillPath(fill, path);
        }

        if (DrawTopGlow)
        {
            var glowRect = new Rectangle(
                Math.Max(0, Width / 8),
                -Height / 3,
                Math.Max(1, Width * 3 / 4),
                Math.Max(1, Height / 2));

            using var glowPath = new GraphicsPath();
            glowPath.AddEllipse(glowRect);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(24, 255, 156, 44),
                SurroundColors = new[] { Color.FromArgb(0, 255, 156, 44) }
            };
            e.Graphics.FillPath(glow, glowPath);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (Width < 2 || Height < 2)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var border = new Pen(BorderColor, 1);
        using var path = RoundedRect(
            new Rectangle(0, 0, Width - 1, Height - 1),
            Radius);
        e.Graphics.DrawPath(border, path);

        if (AccentWidth > 0 &&
            AccentColor != Color.Empty)
        {
            using var accent = new SolidBrush(AccentColor);
            var accentRect = new Rectangle(
                0,
                Math.Max(8, Radius / 2),
                Math.Min(AccentWidth, Width),
                Math.Max(0, Height - Math.Max(16, Radius)));
            e.Graphics.FillRectangle(accent, accentRect);
        }
    }

    private void UpdateRegion()
    {
        if (Width < 2 || Height < 2)
            return;

        using var path = RoundedRect(
            new Rectangle(0, 0, Width, Height),
            Radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
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
