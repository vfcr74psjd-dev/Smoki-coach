using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class AiSettingsForm : Form
{
    private readonly TextBox _key = new();
    private readonly Label _status = new();

    public AiSettingsForm()
    {
        Text = "AI Settings";
        Width = 520;
        Height = 310;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(10, 13, 19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            Text = "AI ROUND COACH",
            Left = 24,
            Top = 22,
            Width = 440,
            Height = 28,
            Font = new Font("Segoe UI", 15, FontStyle.Bold)
        };

        var info = new Label
        {
            Text = "Vnesi svoj OpenAI API key. Ključ se shrani šifrirano samo za tvoj Windows uporabniški račun in se nikoli ne shrani v GitHub.",
            Left = 24,
            Top = 58,
            Width = 448,
            Height = 48,
            ForeColor = Color.FromArgb(175, 185, 200)
        };

        _key.Left = 24;
        _key.Top = 116;
        _key.Width = 448;
        _key.Height = 30;
        _key.UseSystemPasswordChar = true;
        _key.BackColor = Color.FromArgb(23, 29, 39);
        _key.ForeColor = Color.White;
        _key.BorderStyle = BorderStyle.FixedSingle;
        _key.PlaceholderText = AiSettingsStore.HasApiKey
            ? "API key je že nastavljen — vnesi novega samo za zamenjavo"
            : "sk-…";

        _status.Left = 24;
        _status.Top = 156;
        _status.Width = 448;
        _status.Height = 24;
        _status.Text = AiSettingsStore.HasApiKey ? "Status: AI key nastavljen" : "Status: AI key ni nastavljen";
        _status.ForeColor = AiSettingsStore.HasApiKey
            ? Color.FromArgb(126, 240, 174)
            : Color.FromArgb(255, 170, 140);

        var keys = MakeButton("OpenAI API Keys", 24, 196, 130);
        keys.Click += (_,__) =>
        {
            Process.Start(new ProcessStartInfo("https://platform.openai.com/api-keys") { UseShellExecute = true });
        };

        var remove = MakeButton("Remove key", 164, 196, 105);
        remove.Click += (_,__) =>
        {
            AiSettingsStore.ClearApiKey();
            DialogResult = DialogResult.OK;
            Close();
        };

        var save = MakeButton("Save", 367, 196, 105, true);
        save.Click += (_,__) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_key.Text))
                    AiSettingsStore.SaveApiKey(_key.Text);

                if (!AiSettingsStore.HasApiKey)
                {
                    MessageBox.Show("Najprej vnesi API key.", "AI Settings");
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "AI Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        Controls.AddRange(new Control[] { title, info, _key, _status, keys, remove, save });
    }

    private static Button MakeButton(string text, int left, int top, int width, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(104, 92, 255) : Color.FromArgb(27, 33, 44),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
