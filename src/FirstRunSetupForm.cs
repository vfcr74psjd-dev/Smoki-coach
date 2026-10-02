using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class FirstRunSetupForm : Form
{
    private readonly TextBox _nickname = new();
    private readonly ComboBox _role = new();
    private readonly ComboBox _focus = new();
    private readonly CheckBox _autoAi = new();
    private readonly UserProfile _profile;
    private readonly CoachPreferences _prefs;

    public FirstRunSetupForm(UserProfile profile, CoachPreferences prefs, bool firstRun)
    {
        _profile = profile;
        _prefs = prefs;

        Text = firstRun ? "Sm0ki Solo Coach • First Setup" : "Sm0ki Solo Coach • Profile";
        Width = 600;
        Height = 530;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(10,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(28,22,28,22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        root.Controls.Add(new Label
        {
            Text = firstRun ? "WELCOME TO SM0KI SOLO COACH" : "PLAYER PROFILE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        },0,0);

        root.Controls.Add(new Label
        {
            Text = firstRun
                ? "Vsak računalnik ima svoj profil, nastavitve, Local AI coaching in Phone QR."
                : "Spremeni nickname in privzete coaching nastavitve za ta računalnik.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(165,176,194)
        },0,1);

        _nickname.Text = string.IsNullOrWhiteSpace(profile.Nickname) ? Environment.UserName : profile.Nickname;
        root.Controls.Add(MakeField("PLAYER / FACEIT NICKNAME", _nickname),0,2);

        _role.Items.AddRange(new object[] { "Flex", "Entry", "Lurk", "Support", "Anchor" });
        _role.DropDownStyle = ComboBoxStyle.DropDownList;
        _role.SelectedItem = prefs.Role;
        if (_role.SelectedIndex < 0) _role.SelectedIndex = 0;
        StyleCombo(_role);
        root.Controls.Add(MakeField("DEFAULT ROLE", _role),0,3);

        _focus.Items.AddRange(new object[] { "More kills", "Survive & trade", "Entry impact", "Utility impact", "Clutch / late round" });
        _focus.DropDownStyle = ComboBoxStyle.DropDownList;
        _focus.SelectedItem = prefs.Focus;
        if (_focus.SelectedIndex < 0) _focus.SelectedIndex = 0;
        StyleCombo(_focus);
        root.Controls.Add(MakeField("DEFAULT FOCUS", _focus),0,4);

        _autoAi.Text = "Auto AI coach ob začetku vsake runde";
        _autoAi.Checked = prefs.AutoAi;
        _autoAi.Dock = DockStyle.Top;
        _autoAi.ForeColor = Color.FromArgb(210,216,226);
        root.Controls.Add(_autoAi,0,5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var save = MakeButton(firstRun ? "Save & Start" : "Save", 126, true);
        save.Click += (_,__) => SaveAndClose();
        var cancel = MakeButton(firstRun ? "Exit" : "Cancel", 100, false);
        cancel.Click += (_,__) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons,0,6);

        Controls.Add(root);
    }

    private void SaveAndClose()
    {
        var nick = _nickname.Text.Trim();
        if (nick.Length < 2)
        {
            MessageBox.Show("Vpiši nickname z vsaj 2 znakoma.", "Profile");
            return;
        }

        _profile.Nickname = nick;
        _profile.SetupComplete = true;
        UserProfileStore.Save(_profile);

        _prefs.Role = _role.SelectedItem?.ToString() ?? "Flex";
        _prefs.Focus = _focus.SelectedItem?.ToString() ?? "More kills";
        _prefs.AutoAi = _autoAi.Checked;
        UserSettingsStore.Save(_prefs);

        GsiInstaller.TryInstall();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Control MakeField(string title, Control input)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(126,137,156),
            Font = new Font("Segoe UI",8,FontStyle.Bold)
        },0,0);
        input.Dock = DockStyle.Fill;
        panel.Controls.Add(input,0,1);
        return panel;
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Color.FromArgb(24,29,39);
        combo.ForeColor = Color.White;
    }

    private static Button MakeButton(string text, int width, bool primary)
    {
        var b = new Button
        {
            Text=text, Width=width, Height=36, FlatStyle=FlatStyle.Flat,
            BackColor=primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44),
            ForeColor=Color.White, Font=new Font("Segoe UI",9,FontStyle.Bold),
            Margin=new Padding(8,4,0,0)
        };
        b.FlatAppearance.BorderSize=0;
        return b;
    }
}
