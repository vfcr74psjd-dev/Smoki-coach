using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class OverlaySettingsForm : Form
{
    public string SelectedMode { get; private set; }

    public OverlaySettingsForm(string currentMode, bool hotkeyRegistered, string hotkeyDisplay)
    {
        SelectedMode = currentMode is "Full" or "Off" ? currentMode : "Minimal";

        Text = "Sm0ki Solo Coach • Game Overlay";
        Width = 500;
        Height = 330;
        MinimumSize = new Size(460, 300);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(9, 11, 13);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22, 18, 22, 20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label
        {
            Text = "GAME OVERLAY",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "External click-through HUD. No game injection. The app automatically reserves a free overlay hotkey.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(153, 163, 179),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        var choices = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 8)
        };
        for (int i = 0; i < 3; i++)
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

        Button ModeButton(string mode, string subtitle)
        {
            var selected = SelectedMode == mode;
            var b = new Button
            {
                Text = mode.ToUpperInvariant() + "\n" + subtitle,
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                FlatStyle = FlatStyle.Flat,
                BackColor = selected
                    ? Color.FromArgb(67, 42, 22)
                    : Color.FromArgb(20, 22, 24),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Tag = mode
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = selected
                ? Color.FromArgb(255, 156, 44)
                : Color.FromArgb(58, 52, 45);
            b.Click += (_,__) =>
            {
                SelectedMode = mode;
                foreach (Control control in choices.Controls)
                {
                    if (control is not Button button) continue;
                    var active = string.Equals(
                        button.Tag?.ToString(),
                        SelectedMode,
                        StringComparison.OrdinalIgnoreCase);
                    button.BackColor = active
                        ? Color.FromArgb(67, 42, 22)
                        : Color.FromArgb(20, 22, 24);
                    button.FlatAppearance.BorderColor = active
                        ? Color.FromArgb(255, 156, 44)
                        : Color.FromArgb(58, 52, 45);
                }
            };
            return b;
        }

        choices.Controls.Add(ModeButton("Off", "Overlay disabled"), 0, 0);
        choices.Controls.Add(ModeButton("Minimal", "BUY / DO / ADAPT"), 1, 0);
        choices.Controls.Add(ModeButton("Full", "+ score / K-D / money"), 2, 0);
        root.Controls.Add(choices, 0, 2);

        root.Controls.Add(new Label
        {
            Text = hotkeyRegistered
                ? $"● {hotkeyDisplay.ToUpperInvariant()} HOTKEY READY • HIDE / SHOW"
                : "● HOTKEY UNAVAILABLE • F8–F11 are already in use",
            Dock = DockStyle.Fill,
            ForeColor = hotkeyRegistered
                ? Color.FromArgb(112, 214, 151)
                : Color.FromArgb(244, 155, 121),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        actions.Controls.Add(new Label
        {
            Text = "Best with CS2 Fullscreen Windowed / Borderless.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(112, 120, 132),
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var save = new Button
        {
            Text = "SAVE",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 156, 44),
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_,__) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(save, 1, 0);

        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
    }
}
