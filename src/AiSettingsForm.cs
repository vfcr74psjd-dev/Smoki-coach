using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class AiSettingsForm : Form
{
    private readonly Label _status = new();
    private readonly Button _prepare = new();
    private readonly Button _test = new();

    public AiSettingsForm()
    {
        Text = "Local AI Settings";
        Width = 560;
        Height = 350;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(10, 13, 19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            Text = "FREE LOCAL AI ROUND COACH",
            Left = 24,
            Top = 22,
            Width = 490,
            Height = 30,
            Font = new Font("Segoe UI", 15, FontStyle.Bold)
        };

        var info = new Label
        {
            Text =
                "AI teče lokalno na tvojem računalniku prek Ollama. Ni API ključa, ni plačila na klic in podatki o tekmi ostanejo na tvojem PC-ju.\n\n" +
                $"Model: {AiCoachService.ModelName}",
            Left = 24,
            Top = 60,
            Width = 490,
            Height = 78,
            ForeColor = Color.FromArgb(175, 185, 200)
        };

        _status.Left = 24;
        _status.Top = 148;
        _status.Width = 490;
        _status.Height = 30;
        _status.Text = AiCoachService.FindOllamaExe() != null
            ? "Status: Ollama zaznan — klikni Prepare Local AI."
            : "Status: Ollama še ni nameščen.";
        _status.ForeColor = AiCoachService.FindOllamaExe() != null
            ? Color.FromArgb(126, 240, 174)
            : Color.FromArgb(255, 170, 140);

        var install = MakeButton("Install Ollama", 24, 196, 120);
        install.Click += (_,__) =>
        {
            Process.Start(new ProcessStartInfo("https://ollama.com/download/windows")
            {
                UseShellExecute = true
            });
        };

        _prepare.Text = "Prepare Local AI";
        StyleButton(_prepare, 156, 196, 145, true);
        _prepare.Click += async (_,__) => await PrepareAsync();

        _test.Text = "Test AI";
        StyleButton(_test, 313, 196, 90, false);
        _test.Click += async (_,__) => await TestAsync();

        var close = MakeButton("Close", 415, 196, 99);
        close.Click += (_,__) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var note = new Label
        {
            Text = "Prepare Local AI prvič prenese model na računalnik. To je enkraten prenos in lahko traja nekaj minut.",
            Left = 24,
            Top = 246,
            Width = 490,
            Height = 42,
            ForeColor = Color.FromArgb(120, 132, 151),
            Font = new Font("Segoe UI", 8.5f)
        };

        Controls.AddRange(new Control[] { title, info, _status, install, _prepare, _test, close, note });
    }

    private async Task PrepareAsync()
    {
        try
        {
            _prepare.Enabled = false;
            _test.Enabled = false;
            _status.Text = "Pripravljam lokalni AI model…";
            _status.ForeColor = Color.FromArgb(176, 166, 255);

            await AiCoachService.PrepareLocalAiAsync();

            _status.Text = "LOCAL AI READY — brez API stroškov.";
            _status.ForeColor = Color.FromArgb(126, 240, 174);
        }
        catch (Exception ex)
        {
            _status.Text = "Napaka: " + ex.Message;
            _status.ForeColor = Color.FromArgb(255, 150, 130);
        }
        finally
        {
            _prepare.Enabled = true;
            _test.Enabled = true;
        }
    }

    private async Task TestAsync()
    {
        try
        {
            _test.Enabled = false;
            _status.Text = "Testiram lokalni AI…";
            _status.ForeColor = Color.FromArgb(176, 166, 255);

            var result = await AiCoachService.TestLocalAiAsync();

            _status.Text = "LOCAL AI TEST OK";
            _status.ForeColor = Color.FromArgb(126, 240, 174);
            MessageBox.Show(result, "Local AI Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "Test ni uspel.";
            _status.ForeColor = Color.FromArgb(255, 150, 130);
            MessageBox.Show(ex.Message, "Local AI Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _test.Enabled = true;
        }
    }

    private static Button MakeButton(string text, int left, int top, int width)
    {
        var b = new Button { Text = text };
        StyleButton(b, left, top, width, false);
        return b;
    }

    private static void StyleButton(Button b, int left, int top, int width, bool primary)
    {
        b.Left = left;
        b.Top = top;
        b.Width = width;
        b.Height = 34;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = primary ? Color.FromArgb(104, 92, 255) : Color.FromArgb(27, 33, 44);
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
    }
}
