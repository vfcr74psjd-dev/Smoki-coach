using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using QRCoder;

namespace Sm0kiSoloCoach;

public sealed class PhoneQrForm : Form
{
    private Bitmap? _qr;

    public PhoneQrForm(string url)
    {
        Text = "Sm0ki Solo Coach • Phone QR";
        Width = 520;
        Height = 610;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(9,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        Controls.Add(new Label
        {
            Text = "PHONE MODE",
            Left = 28, Top = 22, Width = 430, Height = 32,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "Poskeniraj QR s kamero telefona. Telefon in PC morata biti na istem Wi‑Fi/LAN omrežju.",
            Left = 28, Top = 61, Width = 440, Height = 44,
            ForeColor = Color.FromArgb(170,181,198)
        });

        var box = new PictureBox
        {
            Left = 92, Top = 118, Width = 320, Height = 320,
            BackColor = Color.White,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(8);
        using var ms = new MemoryStream(bytes);
        using var temp = new Bitmap(ms);
        _qr = new Bitmap(temp);
        box.Image = _qr;
        Controls.Add(box);

        Controls.Add(new Label
        {
            Text = url,
            Left = 28, Top = 455, Width = 440, Height = 44,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(157,169,188),
            Font = new Font("Segoe UI", 8.5f)
        });

        var copy = MakeButton("Copy link", 28, 512, 130, true);
        copy.Click += (_,__) =>
        {
            Clipboard.SetText(url);
            copy.Text = "Copied ✓";
        };
        var open = MakeButton("Open on PC", 170, 512, 130, false);
        open.Click += (_,__) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        var close = MakeButton("Close", 312, 512, 130, false);
        close.Click += (_,__) => Close();

        Controls.AddRange(new Control[] { copy, open, close });
        FormClosed += (_,__) => _qr?.Dispose();
    }

    private static Button MakeButton(string text, int left, int top, int width, bool primary)
    {
        var b = new Button
        {
            Text=text, Left=left, Top=top, Width=width, Height=36,
            FlatStyle=FlatStyle.Flat,
            BackColor=primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44),
            ForeColor=Color.White,
            Font=new Font("Segoe UI",9,FontStyle.Bold),
            Cursor=Cursors.Hand
        };
        b.FlatAppearance.BorderSize=0;
        return b;
    }
}
