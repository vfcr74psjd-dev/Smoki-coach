using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed class FaceitHistoryForm : Form
{
    private readonly string _nickname;
    private readonly AutoDemoInboxService _inbox;
    private readonly ComboBox _depth = new();
    private readonly Label _summary = new();
    private readonly Label _status = new();
    private readonly ListView _matches = new();
    private readonly Button _sync = new();
    private readonly Button _scanLocal = new();
    private readonly Button _openMatch = new();
    private CancellationTokenSource? _cts;

    public FaceitHistoryForm(string nickname, AutoDemoInboxService inbox)
    {
        _nickname = nickname;
        _inbox = inbox;

        Text = "Sm0ki Solo Coach • FACEIT History";
        Width = 1080;
        Height = 720;
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(9,13,19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI",10);

        BuildUi();
        Reload();

        FormClosed += (_,__) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22,18,22,20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));

        root.Controls.Add(new Label
        {
            Text = "FACEIT HISTORY SYNC",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI",20,FontStyle.Bold)
        },0,0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0,8,0,0)
        };

        toolbar.Controls.Add(new Label
        {
            Text = "DEPTH",
            Width = 54,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(111,123,144),
            Font = new Font("Segoe UI",8,FontStyle.Bold)
        });

        _depth.DropDownStyle = ComboBoxStyle.DropDownList;
        _depth.Items.AddRange(new object[] { "100", "250", "500", "1000" });
        _depth.SelectedItem = "250";
        _depth.Width = 92;
        _depth.Height = 34;
        _depth.BackColor = Color.FromArgb(24,29,39);
        _depth.ForeColor = Color.White;
        _depth.FlatStyle = FlatStyle.Flat;
        toolbar.Controls.Add(_depth);

        _sync.Text = "SYNC FACEIT";
        StyleButton(_sync,true,130);
        _sync.Click += async (_,__) => await SyncAsync();
        toolbar.Controls.Add(_sync);

        _scanLocal.Text = "SCAN LOCAL DEMOS";
        StyleButton(_scanLocal,false,148);
        _scanLocal.Click += async (_,__) =>
        {
            _scanLocal.Enabled = false;
            try
            {
                _status.Text = "Scanning Downloads + DemoInbox…";
                await _inbox.ScanNowAsync();
                Reload();
            }
            finally { _scanLocal.Enabled = true; }
        };
        toolbar.Controls.Add(_scanLocal);

        _openMatch.Text = "OPEN SELECTED MATCH";
        StyleButton(_openMatch,false,168);
        _openMatch.Enabled = false;
        _openMatch.Click += (_,__) => OpenSelectedMatch();
        toolbar.Controls.Add(_openMatch);

        root.Controls.Add(toolbar,0,1);

        _summary.Dock = DockStyle.Fill;
        _summary.BackColor = Color.FromArgb(17,22,30);
        _summary.ForeColor = Color.FromArgb(220,226,235);
        _summary.Padding = new Padding(14,10,14,8);
        _summary.Font = new Font("Segoe UI",10,FontStyle.Bold);
        root.Controls.Add(_summary,0,2);

        _matches.Dock = DockStyle.Fill;
        _matches.View = View.Details;
        _matches.FullRowSelect = true;
        _matches.GridLines = false;
        _matches.BorderStyle = BorderStyle.None;
        _matches.BackColor = Color.FromArgb(12,17,24);
        _matches.ForeColor = Color.FromArgb(211,219,231);
        _matches.Columns.Add("Date",125);
        _matches.Columns.Add("Map",110);
        _matches.Columns.Add("Demo",95);
        _matches.Columns.Add("Local heatmap",110);
        _matches.Columns.Add("Status",100);
        _matches.Columns.Add("Match ID",360);
        _matches.SelectedIndexChanged += (_,__) =>
        {
            var hasSelection =
                _matches.SelectedItems.Count > 0 &&
                _matches.SelectedItems[0].Tag is FaceitHistoryMatch selected &&
                selected.DemoReady &&
                !string.IsNullOrWhiteSpace(selected.FaceitUrl);

            _openMatch.Enabled = hasSelection;

            if (hasSelection)
            {
                _status.Text =
                    "READY demo selected → OPEN SELECTED MATCH → download demo on FACEIT. " +
                    "Leave it in Downloads; Sm0ki Solo Coach will auto-import it.";
            }
        };
        _matches.DoubleClick += (_,__) => OpenSelectedMatch();
        root.Controls.Add(_matches,0,3);

        var footer = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));

        _status.Text =
            "Data API can index match/demo availability. Demo file download still requires FACEIT Downloads API access.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(105,118,138);
        _status.Font = new Font("Segoe UI",8.5f);
        footer.Controls.Add(_status,0,0);

        var close = new Button { Text="Close" };
        StyleButton(close,false,90);
        close.Anchor = AnchorStyles.Right;
        close.Click += (_,__) => Close();
        footer.Controls.Add(close,1,0);

        root.Controls.Add(footer,0,4);
        Controls.Add(root);
    }

    private async Task SyncAsync()
    {
        if (!FaceitSettingsStore.HasKey)
        {
            MessageBox.Show(
                "Najprej dodaj FACEIT API key v FACEIT settings.",
                "FACEIT History",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var max = int.TryParse(_depth.SelectedItem?.ToString(), out var value) ? value : 250;
        _sync.Enabled = false;
        _scanLocal.Enabled = false;

        try
        {
            var progress = new Progress<string>(text => _status.Text = text);
            var result = await new FaceitHistoryService().SyncAsync(
                _nickname, max, progress, _cts.Token);

            _status.Text =
                $"Synced {result.HistoryCount} matches • demos ready {result.DemoReadyCount} • " +
                $"detail errors {result.ApiErrors}.";
            Reload();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Sync canceled.";
        }
        catch (Exception ex)
        {
            _status.Text = "FACEIT history sync failed.";
            MessageBox.Show(ex.Message,"FACEIT History",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        finally
        {
            _sync.Enabled = true;
            _scanLocal.Enabled = true;
        }
    }

    private void Reload()
    {
        var history = FaceitHistoryStore.LoadForPlayer(_nickname);
        var local = HeatMapStore.LoadForPlayer(_nickname);

        _summary.Text =
            $"PROFILE: {_nickname}   •   INDEXED MATCHES: {history.Count}   •   " +
            $"DEMO READY: {history.Count(x => x.DemoReady)}   •   LOCAL HEATMAP MATCHES: {local.Count}";

        _matches.BeginUpdate();
        _matches.Items.Clear();

        foreach (var match in history.Take(1000))
        {
            var localMatch =
                !string.IsNullOrWhiteSpace(match.MatchId) &&
                local.Any(x =>
                    !string.IsNullOrWhiteSpace(x.SourceFileName) &&
                    x.SourceFileName.Contains(match.MatchId, StringComparison.OrdinalIgnoreCase));

            var item = new ListViewItem(
                match.FinishedUtc == default
                    ? "—"
                    : match.FinishedUtc.ToLocalTime().ToString("dd.MM.yy HH:mm"));
            item.SubItems.Add(string.IsNullOrWhiteSpace(match.Map)
                ? "—"
                : CoachEngine.PrettyMap(match.Map));
            item.SubItems.Add(match.DemoReady ? "READY" : match.DetailsChecked ? "—" : "?");
            item.SubItems.Add(localMatch ? "ANALYZED" : match.DemoReady ? "NEEDS DOWNLOAD" : "—");
            item.SubItems.Add(string.IsNullOrWhiteSpace(match.Status) ? "—" : match.Status);

            item.UseItemStyleForSubItems = false;
            if (match.DemoReady)
                item.SubItems[2].ForeColor = Color.FromArgb(126,240,174);
            if (localMatch)
                item.SubItems[3].ForeColor = Color.FromArgb(126,240,174);
            else if (match.DemoReady)
                item.SubItems[3].ForeColor = Color.FromArgb(255,190,132);
            item.SubItems.Add(match.MatchId);
            item.Tag = match;
            _matches.Items.Add(item);
        }

        _matches.EndUpdate();
    }

    private void OpenSelectedMatch()
    {
        if (_matches.SelectedItems.Count == 0 ||
            _matches.SelectedItems[0].Tag is not FaceitHistoryMatch match)
            return;

        if (!string.IsNullOrWhiteSpace(match.FaceitUrl))
        {
            var url = match.FaceitUrl
                .Replace("{lang}", "en", StringComparison.OrdinalIgnoreCase);

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _status.Text =
                "FACEIT match opened. Download the demo and leave the file in Downloads. " +
                "The app scans Downloads automatically; no manual import is needed.";
        }
    }

    private static void StyleButton(Button button, bool primary, int width)
    {
        button.Width = width;
        button.Height = 34;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = primary ? Color.FromArgb(104,92,255) : Color.FromArgb(27,33,44);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI",8.5f,FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(6,0,0,0);
    }
}
