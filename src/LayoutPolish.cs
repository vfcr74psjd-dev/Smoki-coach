using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

/// <summary>
/// Final DPI safety pass. The current dashboard owns its responsive layout
/// directly; this helper only prevents the window from being resized into a
/// size where WinForms would clip cards at high scaling.
/// </summary>
internal static class LayoutPolish
{
    public static void Apply(MainForm form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.MinimumSize = new Size(1180, 760);
        form.KeyPreview = true;

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F5)
                return;

            form.Invalidate(true);
            form.PerformLayout();
            e.Handled = true;
        };

        form.PerformLayout();
    }
}
