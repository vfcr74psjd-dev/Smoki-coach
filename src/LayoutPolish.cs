using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

/// <summary>
/// Post-build responsive layout pass for the main WinForms shell.  It deliberately
/// leaves all GSI/coaching behaviour untouched and only replaces the brittle fixed
/// dashboard sizing with DPI-aware table/flow sizing.
/// </summary>
internal static class LayoutPolish
{
    public static void Apply(MainForm form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.MinimumSize = new Size(1000, 700);

        var root = FindRootTable(form);
        if (root == null) return;

        // Header / toolbar / stats / body / footer.  AutoSize prevents wrapped toolbar
        // controls from painting over the stat cards at 125/150/175% Windows scaling.
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var toolbar = root.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (toolbar != null)
        {
            toolbar.AutoSize = true;
            toolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toolbar.WrapContents = true;
            toolbar.Padding = new Padding(18, 10, 18, 10);

            // Keep the phone entry point visually prominent instead of allowing it to
            // disappear in a crowded first row.
            var phone = toolbar.Controls.OfType<Button>()
                .FirstOrDefault(b => b.Text.Contains("Phone QR", StringComparison.OrdinalIgnoreCase));
            if (phone != null)
            {
                phone.BackColor = Color.FromArgb(46, 72, 104);
                phone.MinimumSize = new Size(112, 34);
                phone.TabIndex = 0;
            }

            foreach (Control child in toolbar.Controls)
                child.Margin = new Padding(child.Margin.Left, Math.Max(4, child.Margin.Top), child.Margin.Right, 4);
        }

        // Body collapses gracefully to one column when the window is narrow.  This is
        // layout-only: both coach cards remain present and GSI state is unchanged.
        var body = root.Controls.OfType<TableLayoutPanel>()
            .FirstOrDefault(t => t.ColumnCount == 2 && t.RowCount == 1 && t != root);
        if (body != null)
        {
            void Reflow()
            {
                bool compact = form.ClientSize.Width < 1080;
                body.SuspendLayout();
                if (compact)
                {
                    body.ColumnCount = 1;
                    body.RowCount = 2;
                    body.ColumnStyles.Clear();
                    body.RowStyles.Clear();
                    body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    body.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
                    body.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
                    if (body.Controls.Count >= 2)
                    {
                        body.SetCellPosition(body.Controls[0], new TableLayoutPanelCellPosition(0, 0));
                        body.SetCellPosition(body.Controls[1], new TableLayoutPanelCellPosition(0, 1));
                    }
                }
                else
                {
                    body.ColumnCount = 2;
                    body.RowCount = 1;
                    body.ColumnStyles.Clear();
                    body.RowStyles.Clear();
                    body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
                    body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
                    body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    if (body.Controls.Count >= 2)
                    {
                        body.SetCellPosition(body.Controls[0], new TableLayoutPanelCellPosition(0, 0));
                        body.SetCellPosition(body.Controls[1], new TableLayoutPanelCellPosition(1, 0));
                    }
                }
                body.ResumeLayout(true);
            }

            form.Resize += (_, _) => Reflow();
            Reflow();
        }

        // QOL: Escape closes secondary dialogs as before, while the main dashboard gets
        // a predictable refresh shortcut without any input automation into CS2.
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                form.Invalidate(true);
                form.PerformLayout();
                e.Handled = true;
            }
        };

        form.PerformLayout();
    }

    private static TableLayoutPanel? FindRootTable(Control parent)
        => parent.Controls.OfType<TableLayoutPanel>()
            .FirstOrDefault(t => t.Dock == DockStyle.Fill && t.ColumnCount == 1 && t.RowCount == 5);
}
