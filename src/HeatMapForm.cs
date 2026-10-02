using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class HeatMapForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _nickname;
    private readonly AutoDemoInboxService _inbox;
    private readonly ComboBox _map = new();
    private readonly ComboBox _side = new();
    private readonly ComboBox _window = new();
    private readonly Label _status = new();
    private readonly Label _summary = new();
    private readonly ListView _hotspots = new();
    private readonly HeatCanvas _canvas = new();
    private CancellationTokenSource? _radarCts;

    public HeatMapForm(string nickname, AutoDemoInboxService inbox)
    {
        _nickname = nickname;
        _inbox = inbox;

        Text = "Sm0ki Solo Coach • Heat Map";
        Width = 1180;
        Height = 780;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(9,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI",10);

        BuildUi();

        _inbox.DemoImported += OnDemoImported;
        _inbox.StatusChanged += OnInboxStatus;

        Shown += async (_,__) =>
        {
            ReloadData();
            await _inbox.ScanNowAsync();
            ReloadData();
        };

        FormClosed += (_,__) =>
        {
            _inbox.DemoImported -= OnDemoImported;
            _inbox.StatusChanged -= OnInboxStatus;
            _radarCts?.Cancel();
            _radarCts?.Dispose();
            _canvas.DisposeRadar();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22,18,22,20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));

        root.Controls.Add(new Label
        {
            Text = "HEAT MAP • AUTO DEMO INBOX",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",20,FontStyle.Bold)
        },0,0);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            Margin = Padding.Empty
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,54));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,22));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,58));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));

        controls.Controls.Add(FilterLabel("MAP"),0,0);
        ConfigureCombo(_map);
        _map.SelectedIndexChanged += async (_,__) => await RefreshViewAsync();
        controls.Controls.Add(_map,1,0);

        controls.Controls.Add(FilterLabel("SIDE"),2,0);
        ConfigureCombo(_side);
        _side.Items.AddRange(new object[] { "All", "T", "CT" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += async (_,__) => await RefreshViewAsync();
        controls.Controls.Add(_side,3,0);

        controls.Controls.Add(FilterLabel("RANGE"),4,0);
        ConfigureCombo(_window);
        _window.Items.AddRange(new object[] { "Last match", "Last 10", "Last 20", "Last 50" });
        _window.SelectedItem = "Last 20";
        _window.SelectedIndexChanged += async (_,__) => await RefreshViewAsync();
        controls.Controls.Add(_window,5,0);

        var scan = MakeButton("SCAN NOW",88,true);
        var open = MakeButton("OPEN INBOX",92,false);
        scan.Click += async (_,__) =>
        {
            scan.Enabled = false;
            try
            {
                await _inbox.ScanNowAsync();
                ReloadData();
            }
            finally { scan.Enabled = true; }
        };
        open.Click += (_,__) =>
        {
            Directory.CreateDirectory(AutoDemoInboxService.InboxFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", AutoDemoInboxService.InboxFolder)
            {
                UseShellExecute = true
            });
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0,6,0,0)
        };
        actions.Controls.Add(scan);
        actions.Controls.Add(open);
        controls.Controls.Add(actions,6,0);

        root.Controls.Add(controls,0,1);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,70));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));

        var mapCard = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0,6,8,6),
            Padding = new Padding(12),
            BackColor = Color.FromArgb(16,21,29)
        };
        _canvas.Dock = DockStyle.Fill;
        mapCard.Controls.Add(_canvas);
        body.Controls.Add(mapCard,0,0);

        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Margin = new Padding(8,6,0,6),
            Padding = new Padding(14),
            BackColor = Color.FromArgb(16,21,29)
        };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute,96));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        side.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        _summary.Text = $"PROFILE: {_nickname}\nWaiting for demo history…";
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = Color.FromArgb(221,227,236);
        _summary.Font = new Font("Segoe UI",10,FontStyle.Bold);
        side.Controls.Add(_summary,0,0);

        side.Controls.Add(new Label
        {
            Text = "TOP HOTSPOTS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",8,FontStyle.Bold),
            ForeColor = Color.FromArgb(111,123,144)
        },0,1);

        _hotspots.Dock = DockStyle.Fill;
        _hotspots.View = View.Details;
        _hotspots.FullRowSelect = true;
        _hotspots.BorderStyle = BorderStyle.None;
        _hotspots.BackColor = Color.FromArgb(12,17,24);
        _hotspots.ForeColor = Color.FromArgb(211,219,231);
        _hotspots.Columns.Add("#",34);
        _hotspots.Columns.Add("Sector",92);
        _hotspots.Columns.Add("Deaths",62);
        _hotspots.Columns.Add("Side",55);
        side.Controls.Add(_hotspots,0,2);

        body.Controls.Add(side,1,0);
        root.Controls.Add(body,0,2);

        var footer = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));

        _status.Text =
            $"Watching Downloads + {AutoDemoInboxService.InboxFolder}. Demo parsing stays local.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(105,118,138);
        _status.Font = new Font("Segoe UI",8.5f);
        footer.Controls.Add(_status,0,0);

        var close = MakeButton("Close",90,false);
        close.Anchor = AnchorStyles.Right;
        close.Click += (_,__) => Close();
        footer.Controls.Add(close,1,0);

        root.Controls.Add(footer,0,3);
        Controls.Add(root);
    }

    private void ReloadData()
    {
        var all = HeatMapStore.LoadForPlayer(_nickname);
        var selected = _map.SelectedItem?.ToString();

        var maps = all
            .Select(x => x.Map)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CoachEngine.PrettyMap)
            .ToList();

        _map.BeginUpdate();
        _map.Items.Clear();
        foreach (var item in maps)
            _map.Items.Add(item);
        _map.EndUpdate();

        if (selected != null && maps.Contains(selected, StringComparer.OrdinalIgnoreCase))
            _map.SelectedItem = maps.First(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase));
        else if (maps.Count > 0)
            _map.SelectedItem = maps[0];
        else
        {
            _summary.Text =
                $"PROFILE: {_nickname}\n0 analyzed matches. Download a FACEIT .dem/.dem.gz or drop it in DemoInbox.";
            _hotspots.Items.Clear();
            _canvas.SetData("", Array.Empty<DemoDeathPoint>());
        }
    }

    private async Task RefreshViewAsync()
    {
        var map = _map.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(map))
            return;

        var side = _side.SelectedItem?.ToString() ?? "All";
        var take = WindowCount();

        var matches = HeatMapStore.LoadForPlayer(_nickname)
            .Where(x => x.Map.Equals(map, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ImportedUtc)
            .Take(take)
            .ToList();

        var points = matches
            .SelectMany(x => x.Deaths)
            .Where(x => side == "All" || x.Side.Equals(side, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ct = points.Count(x => x.Side.Equals("CT", StringComparison.OrdinalIgnoreCase));
        var t = points.Count(x => x.Side.Equals("T", StringComparison.OrdinalIgnoreCase));

        _summary.Text =
            $"MAP: {CoachEngine.PrettyMap(map).ToUpperInvariant()}\n" +
            $"{matches.Count} match(es) • {points.Count} deaths • CT {ct} / T {t}\n" +
            $"PROFILE: {_nickname}";

        _canvas.SetData(map, points);
        FillHotspots(map, points);

        _radarCts?.Cancel();
        _radarCts?.Dispose();
        _radarCts = new CancellationTokenSource();

        try
        {
            await _canvas.LoadRadarAsync(map, _radarCts.Token);
        }
        catch (OperationCanceledException) { }
        catch
        {
            _status.Text = "Radar image ni dosegljiv; heatmap podatki so vseeno shranjeni lokalno.";
        }
    }

    private void FillHotspots(string map, IReadOnlyList<DemoDeathPoint> points)
    {
        _hotspots.Items.Clear();
        var def = RadarCatalog.Get(map);
        if (def == null || points.Count == 0)
        {
            _hotspots.Items.Add(new ListViewItem(new[] { "—", "No data", "0", "—" }));
            return;
        }

        const int grid = 6;
        var clusters = points
            .Select(p =>
            {
                var n = RadarCatalog.WorldToRadar(def, p.X, p.Y);
                var col = Math.Clamp((int)(n.X * grid), 0, grid - 1);
                var row = Math.Clamp((int)(n.Y * grid), 0, grid - 1);
                return new { Point = p, Col = col, Row = row };
            })
            .GroupBy(x => (x.Col, x.Row))
            .Select(g => new
            {
                g.Key.Col,
                g.Key.Row,
                Deaths = g.Count(),
                Ct = g.Count(x => x.Point.Side.Equals("CT", StringComparison.OrdinalIgnoreCase)),
                T = g.Count(x => x.Point.Side.Equals("T", StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => x.Deaths)
            .Take(6)
            .ToList();

        int rank = 1;
        foreach (var h in clusters)
        {
            var dominant = h.Ct == h.T ? "Mix" : h.Ct > h.T ? "CT" : "T";
            var item = new ListViewItem(rank.ToString());
            item.SubItems.Add($"S{h.Col + 1}-{h.Row + 1}");
            item.SubItems.Add(h.Deaths.ToString());
            item.SubItems.Add(dominant);
            _hotspots.Items.Add(item);
            rank++;
        }
    }

    private int WindowCount()
    {
        return _window.SelectedItem?.ToString() switch
        {
            "Last match" => 1,
            "Last 10" => 10,
            "Last 50" => 50,
            _ => 20
        };
    }

    private void OnDemoImported(HeatMapMatchRecord record)
    {
        if (IsDisposed) return;
        BeginInvoke(async () =>
        {
            _status.Text =
                $"Auto imported {CoachEngine.PrettyMap(record.Map)} • {record.Deaths.Count} deaths";
            ReloadData();
            await RefreshViewAsync();
        });
    }

    private void OnInboxStatus(string text)
    {
        if (IsDisposed) return;
        BeginInvoke(() => _status.Text = text);
    }

    private static Label FilterLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(111,123,144),
        Font = new Font("Segoe UI",7.5f,FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void ConfigureCombo(ComboBox box)
    {
        box.Dock = DockStyle.Fill;
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.BackColor = Color.FromArgb(24,29,39);
        box.ForeColor = Color.White;
        box.FlatStyle = FlatStyle.Flat;
        box.Margin = new Padding(0,8,10,8);
    }

    private static Button MakeButton(string text,int width,bool primary)
    {
        var b = new Button
        {
            Text=text,
            Width=width,
            Height=34,
            FlatStyle=FlatStyle.Flat,
            BackColor=primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44),
            ForeColor=Color.White,
            Font=new Font("Segoe UI",8.5f,FontStyle.Bold),
            Cursor=Cursors.Hand
        };
        b.FlatAppearance.BorderSize=0;
        return b;
    }

    private sealed class HeatCanvas : Panel
    {
        private Image? _image;
        private string _map = "";
        private IReadOnlyList<DemoDeathPoint> _points = Array.Empty<DemoDeathPoint>();
        private RadarDefinition? _definition;

        public HeatCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(8,11,16);
        }

        public void SetData(string map, IReadOnlyList<DemoDeathPoint> points)
        {
            _map = map;
            _points = points;
            _definition = RadarCatalog.Get(map);
            Invalidate();
        }

        public async Task LoadRadarAsync(string map, CancellationToken ct)
        {
            if (_map.Equals(map, StringComparison.OrdinalIgnoreCase) && _image != null)
            {
                Invalidate();
                return;
            }

            _definition = RadarCatalog.Get(map);
            _image?.Dispose();
            _image = null;

            if (_definition == null)
            {
                Invalidate();
                return;
            }

            var url = _definition.RadarUrl;
            var bytes = await Http.GetByteArrayAsync(url, ct);
            using var ms = new MemoryStream(bytes);
            using var temp = Image.FromStream(ms);
            _image = new Bitmap(temp);
            _map = map;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            var rect = FitSquare(ClientRectangle);

            if (_image != null)
                e.Graphics.DrawImage(_image, rect);
            else
            {
                using var bg = new SolidBrush(Color.FromArgb(18,24,34));
                e.Graphics.FillRectangle(bg, rect);
            }

            if (_definition == null)
            {
                DrawCentered(e.Graphics, rect, "No heatmap data yet.");
                return;
            }

            if (_points.Count == 0)
            {
                DrawCentered(e.Graphics, rect, "No deaths for this filter.");
                return;
            }

            foreach (var p in _points)
            {
                var n = RadarCatalog.WorldToRadar(_definition, p.X, p.Y);
                var x = rect.Left + n.X * rect.Width;
                var y = rect.Top + n.Y * rect.Height;
                DrawHeatPoint(e.Graphics, x, y, Math.Max(34, rect.Width * 0.065f));
            }

            using var font = new Font("Segoe UI",9,FontStyle.Bold);
            using var bgText = new SolidBrush(Color.FromArgb(195,8,11,16));
            var label = $"{_points.Count} DEATHS";
            var size = e.Graphics.MeasureString(label,font);
            var box = new RectangleF(rect.Left + 12, rect.Top + 12, size.Width + 18, 26);
            e.Graphics.FillRectangle(bgText,box);
            e.Graphics.DrawString(label,font,Brushes.White,box.Left+9,box.Top+4);
        }

        private static void DrawHeatPoint(Graphics g, float x, float y, float radius)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(x-radius, y-radius, radius*2, radius*2);

            using var brush = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(x,y),
                CenterColor = Color.FromArgb(155,255,72,58),
                SurroundColors = new[] { Color.FromArgb(0,255,120,40) }
            };
            g.FillPath(brush,path);

            using var core = new SolidBrush(Color.FromArgb(120,255,205,92));
            g.FillEllipse(core,x-3,y-3,6,6);
        }

        private static Rectangle FitSquare(Rectangle r)
        {
            int size = Math.Max(10, Math.Min(r.Width,r.Height)-8);
            return new Rectangle(
                r.Left+(r.Width-size)/2,
                r.Top+(r.Height-size)/2,
                size,
                size);
        }

        private static void DrawCentered(Graphics g, Rectangle rect, string text)
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
            _image = null;
        }
    }
}
