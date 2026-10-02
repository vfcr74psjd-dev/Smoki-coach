using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class ReviewLabForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _nickname;
    private readonly Label _status = new();
    private readonly Label _summary = new();
    private readonly ListView _deaths = new();
    private readonly DeathRadarCanvas _radar = new();
    private CancellationTokenSource? _cts;

    public ReviewLabForm(string nickname)
    {
        _nickname = nickname;

        Text = "Sm0ki Solo Coach • Review Lab";
        Width = 1120;
        Height = 760;
        MinimumSize = new Size(900, 650);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(9,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI",10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22,18,22,20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));

        root.Controls.Add(new Label
        {
            Text = "REVIEW LAB • DEATH MAP",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",20,FontStyle.Bold)
        },0,0);

        var intro = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2 };
        intro.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        intro.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));

        _status.Text = "Izberi CS2 .dem file. Review Lab bo prebral tvoje smrti in njihove pozicije.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(164,176,194);
        intro.Controls.Add(_status,0,0);

        var import = MakeButton("Import .dem",140,true);
        import.Anchor = AnchorStyles.Right;
        import.Click += async (_,__) => await ImportAsync();
        intro.Controls.Add(import,1,0);
        root.Controls.Add(intro,0,1);

        var body = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));

        var mapCard = new Panel
        {
            Dock=DockStyle.Fill,
            Margin=new Padding(0,6,8,6),
            Padding=new Padding(12),
            BackColor=Color.FromArgb(16,21,29)
        };
        _radar.Dock = DockStyle.Fill;
        mapCard.Controls.Add(_radar);
        body.Controls.Add(mapCard,0,0);

        var side = new TableLayoutPanel
        {
            Dock=DockStyle.Fill,
            RowCount=2,
            ColumnCount=1,
            Margin=new Padding(8,6,0,6),
            Padding=new Padding(14),
            BackColor=Color.FromArgb(16,21,29)
        };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute,88));
        side.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        _summary.Text = $"PROFILE: {_nickname}\nNo demo loaded yet.";
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = Color.FromArgb(211,219,231);
        _summary.Font = new Font("Segoe UI",10,FontStyle.Bold);
        side.Controls.Add(_summary,0,0);

        _deaths.Dock = DockStyle.Fill;
        _deaths.View = View.Details;
        _deaths.FullRowSelect = true;
        _deaths.BorderStyle = BorderStyle.None;
        _deaths.BackColor = Color.FromArgb(12,17,24);
        _deaths.ForeColor = Color.FromArgb(211,219,231);
        _deaths.Columns.Add("R",38);
        _deaths.Columns.Add("Killer",110);
        _deaths.Columns.Add("Weapon",100);
        _deaths.Columns.Add("Position",170);
        _deaths.SelectedIndexChanged += (_,__) =>
        {
            if (_deaths.SelectedItems.Count == 0) return;
            if (_deaths.SelectedItems[0].Tag is DemoDeathPoint point)
                _radar.Highlight(point);
        };
        side.Controls.Add(_deaths,0,1);

        body.Controls.Add(side,1,0);
        root.Controls.Add(body,0,2);

        var footer = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
        footer.Controls.Add(new Label
        {
            Text = "Live GSI position se uporabi samo, če jo CS2 legitimno izpostavi. Review Lab uporablja demo podatke za zanesljiv death map.",
            Dock=DockStyle.Fill,
            ForeColor=Color.FromArgb(105,118,138),
            Font=new Font("Segoe UI",8.5f)
        },0,0);
        var close = MakeButton("Close",90,false);
        close.Anchor=AnchorStyles.Right;
        close.Click += (_,__) => Close();
        footer.Controls.Add(close,1,0);
        root.Controls.Add(footer,0,3);

        Controls.Add(root);

        FormClosing += (_,__) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _radar.DisposeRadar();
        };
    }

    private async Task ImportAsync()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Izberi CS2 demo",
            Filter = "CS2 demo (*.dem)|*.dem|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            UseWaitCursor = true;
            _status.Text = "Analiziram demo…";
            _deaths.Items.Clear();
            _radar.SetData("", Array.Empty<DemoDeathPoint>());

            var result = await DemoReviewService.AnalyzeAsync(
                picker.FileName, _nickname, _cts.Token);

            if (result.Deaths.Count == 0)
            {
                var names = result.Players.Count > 0
                    ? string.Join(", ", result.Players.Take(12))
                    : "ni imen";
                _status.Text = $"Za profil '{_nickname}' nisem našel smrti. Igralci v demu: {names}";
                _summary.Text = $"MAP: {CoachEngine.PrettyMap(result.Map)}\n0 matched deaths";
                return;
            }

            foreach (var d in result.Deaths)
            {
                var item = new ListViewItem(d.Round.ToString());
                item.SubItems.Add(string.IsNullOrWhiteSpace(d.Killer) ? "—" : d.Killer);
                item.SubItems.Add(string.IsNullOrWhiteSpace(d.Weapon) ? "—" : d.Weapon);
                item.SubItems.Add($"{d.X:0}, {d.Y:0}, {d.Z:0}");
                item.Tag = d;
                _deaths.Items.Add(item);
            }

            _summary.Text =
                $"MAP: {CoachEngine.PrettyMap(result.Map)}\n" +
                $"PROFILE: {_nickname} • DEATHS: {result.Deaths.Count}";
            _status.Text = "Demo analiziran. Klikni smrt na desni, da jo označiš na mapi.";

            _radar.SetData(result.Map,result.Deaths);
            await _radar.LoadRadarAsync(result.Map,_cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _status.Text = "Review Lab error";
            MessageBox.Show(ex.Message,"Review Lab",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private static Button MakeButton(string text,int width,bool primary)
    {
        var b = new Button
        {
            Text=text, Width=width, Height=34,
            FlatStyle=FlatStyle.Flat,
            BackColor=primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44),
            ForeColor=Color.White,
            Font=new Font("Segoe UI",9,FontStyle.Bold),
            Cursor=Cursors.Hand
        };
        b.FlatAppearance.BorderSize=0;
        return b;
    }

    private sealed class DeathRadarCanvas : Panel
    {
        private Image? _image;
        private string _map = "";
        private IReadOnlyList<DemoDeathPoint> _points = Array.Empty<DemoDeathPoint>();
        private DemoDeathPoint? _highlight;
        private RadarDefinition? _definition;

        public DeathRadarCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(8,11,16);
        }

        public void SetData(string map,IReadOnlyList<DemoDeathPoint> points)
        {
            _map = map;
            _points = points;
            _highlight = points.LastOrDefault();
            _definition = RadarCatalog.Get(map);
            Invalidate();
        }

        public async Task LoadRadarAsync(string map,CancellationToken ct)
        {
            _definition = RadarCatalog.Get(map);
            _image?.Dispose();
            _image = null;

            if (_definition == null)
            {
                Invalidate();
                return;
            }

            var useLower = _highlight != null &&
                           _definition.LowerBelowZ.HasValue &&
                           _highlight.Z < _definition.LowerBelowZ.Value;
            var url = useLower && !string.IsNullOrWhiteSpace(_definition.LowerRadarUrl)
                ? _definition.LowerRadarUrl!
                : _definition.RadarUrl;

            var bytes = await Http.GetByteArrayAsync(url,ct);
            using var ms = new MemoryStream(bytes);
            using var temp = Image.FromStream(ms);
            _image = new Bitmap(temp);
            Invalidate();
        }

        public void Highlight(DemoDeathPoint point)
        {
            _highlight = point;
            _ = ReloadLevelIfNeededAsync();
            Invalidate();
        }

        private async Task ReloadLevelIfNeededAsync()
        {
            try
            {
                if (_definition == null || !_definition.LowerBelowZ.HasValue) return;
                await LoadRadarAsync(_map,CancellationToken.None);
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = FitSquare(ClientRectangle);
            if (_image != null)
                e.Graphics.DrawImage(_image,rect);
            else
            {
                using var bg = new SolidBrush(Color.FromArgb(18,24,34));
                e.Graphics.FillRectangle(bg,rect);
                using var pen = new Pen(Color.FromArgb(48,58,76));
                for (int i=1;i<4;i++)
                {
                    int x=rect.Left+rect.Width*i/4;
                    int y=rect.Top+rect.Height*i/4;
                    e.Graphics.DrawLine(pen,x,rect.Top,x,rect.Bottom);
                    e.Graphics.DrawLine(pen,rect.Left,y,rect.Right,y);
                }
            }

            if (_definition == null)
            {
                DrawCentered(e.Graphics,rect,"Radar metadata za to mapo še ni dodan.");
                return;
            }

            foreach (var p in _points)
            {
                var n = RadarCatalog.WorldToRadar(_definition,p.X,p.Y);
                var x = rect.Left+n.X*rect.Width;
                var y = rect.Top+n.Y*rect.Height;
                bool active = ReferenceEquals(p,_highlight);
                float dot = active ? 18 : 10;

                using var brush = new SolidBrush(active
                    ? Color.FromArgb(255,86,98)
                    : Color.FromArgb(210,72,84));
                e.Graphics.FillEllipse(brush,x-dot/2,y-dot/2,dot,dot);
                using var border = new Pen(Color.White,active ? 2 : 1);
                e.Graphics.DrawEllipse(border,x-dot/2,y-dot/2,dot,dot);
            }

            if (_highlight != null)
            {
                var n = RadarCatalog.WorldToRadar(_definition,_highlight.X,_highlight.Y);
                var x = rect.Left+n.X*rect.Width;
                var y = rect.Top+n.Y*rect.Height;
                var label = $"R{_highlight.Round} • YOU DIED HERE";
                using var font = new Font("Segoe UI",9,FontStyle.Bold);
                var size = e.Graphics.MeasureString(label,font);
                var left = Math.Clamp(x-size.Width/2-8, rect.Left, rect.Right-size.Width-16);
                var top = Math.Clamp(y-38, rect.Top, rect.Bottom-28);
                var box = new RectangleF(left,top,size.Width+16,26);

                using var bg = new SolidBrush(Color.FromArgb(225,11,14,20));
                e.Graphics.FillRectangle(bg,box);
                e.Graphics.DrawString(label,font,Brushes.White,box.Left+8,box.Top+4);
            }
        }

        private static Rectangle FitSquare(Rectangle r)
        {
            int size = Math.Max(10,Math.Min(r.Width,r.Height)-8);
            return new Rectangle(r.Left+(r.Width-size)/2,r.Top+(r.Height-size)/2,size,size);
        }

        private static void DrawCentered(Graphics g,Rectangle rect,string text)
        {
            using var font = new Font("Segoe UI",11,FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(150,162,181));
            var size = g.MeasureString(text,font);
            g.DrawString(text,font,brush,
                rect.Left+(rect.Width-size.Width)/2,
                rect.Top+(rect.Height-size.Height)/2);
        }

        public void DisposeRadar()
        {
            _image?.Dispose();
            _image=null;
        }
    }
}
