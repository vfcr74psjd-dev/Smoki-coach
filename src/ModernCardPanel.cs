using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class ModernCardPanel : Panel
{
    public int Radius { get; set; } = 14;
    public Color BorderColor { get; set; } = Color.FromArgb(43, 46, 50);

    public ModernCardPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 20, 22);
        Margin = new Padding(5);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRegion();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (Width < 2 || Height < 2)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(BorderColor, 1);
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        e.Graphics.DrawPath(border, path);
    }

    private void UpdateRegion()
    {
        if (Width < 2 || Height < 2)
            return;

        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), Radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
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
