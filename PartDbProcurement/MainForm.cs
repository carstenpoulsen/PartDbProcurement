using System.Diagnostics;
using PartDbProcurement.Core;

namespace PartDbProcurement;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;

    private readonly TextBox _txtProject = new() { Dock = DockStyle.Fill };
    private readonly Button _btnBrowse = new() { Text = "Browse…", AutoSize = true };
    private readonly NumericUpDown _numBuilds = new() { Minimum = 1, Maximum = 10000, Width = 80 };
    private readonly CheckBox _chkDnp = new() { Text = "Count DNP parts", AutoSize = true, Margin = new Padding(16, 6, 3, 3) };
    private readonly TextBox _txtUrl = new() { Dock = DockStyle.Fill };
    private readonly Button _btnTest = new() { Text = "Test", AutoSize = true };
    private readonly TextBox _txtToken = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button _btnPull = new() { Text = "Pull from Part-DB", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _btnExport = new() { Text = "Export to Excel…", AutoSize = true, Enabled = false, Padding = new Padding(8, 2, 8, 2) };
    private readonly Label _lblSummary = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, BackgroundColor = SystemColors.Window,
    };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 200 };

    private ProcurementReport? _report;
    private CancellationTokenSource? _cts;
    private Font? _boldFont;

    private static readonly Color Green = Color.FromArgb(0xC6, 0xEF, 0xCE);
    private static readonly Color Yellow = Color.FromArgb(0xFF, 0xE6, 0x99);
    private static readonly Color Red = Color.FromArgb(0xF8, 0xCB, 0xAD);

    public MainForm(string? projectArg)
    {
        _settings = AppSettings.Load();
        Text = "Part-DB Procurement";
        Width = 1250; Height = 720;
        MinimumSize = new Size(800, 450);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();

        _txtProject.Text = !string.IsNullOrEmpty(projectArg) ? projectArg : _settings.ProjectPath;
        _numBuilds.Value = Math.Clamp(_settings.Builds, 1, 10000);
        _chkDnp.Checked = _settings.IncludeDnp;
        _txtUrl.Text = _settings.PartDbUrl;
        _txtToken.Text = _settings.Token;

        _btnBrowse.Click += (_, _) => Browse();
        _btnTest.Click += async (_, _) => await TestConnection();
        _btnPull.Click += async (_, _) => await Pull();
        _btnExport.Click += (_, _) => Export();
        _numBuilds.ValueChanged += (_, _) => Recalculate();
        _chkDnp.CheckedChanged += (_, _) => MarkStale("DNP setting changed. Pull again to apply.");
        _txtProject.TextChanged += (_, _) => MarkStale(null);
        _grid.CellDoubleClick += (_, e) => OpenPartInBrowser(e.RowIndex);
        FormClosing += (_, _) => SaveSettings();

        SetStatus("Choose a KiCad project and press \"Pull from Part-DB\".");
    }

    private void BuildLayout()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) };

        table.Controls.Add(L("KiCad project (.kicad_pro):"), 0, 0);
        table.Controls.Add(_txtProject, 1, 0);
        table.Controls.Add(_btnBrowse, 2, 0);

        var buildsRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        buildsRow.Controls.Add(_numBuilds);
        buildsRow.Controls.Add(_chkDnp);
        table.Controls.Add(L("Number of builds:"), 0, 1);
        table.Controls.Add(buildsRow, 1, 1);

        table.Controls.Add(L("Part-DB URL:"), 0, 2);
        table.Controls.Add(_txtUrl, 1, 2);
        table.Controls.Add(_btnTest, 2, 2);

        table.Controls.Add(L("API token (read-only):"), 0, 3);
        table.Controls.Add(_txtToken, 1, 3);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0), WrapContents = false };
        actions.Controls.Add(_btnPull);
        actions.Controls.Add(_btnExport);
        actions.Controls.Add(_lblSummary);
        table.Controls.Add(actions, 1, 4);
        table.SetColumnSpan(actions, 2);

        AddColumns();

        var strip = new StatusStrip();
        strip.Items.Add(_status);
        strip.Items.Add(_progress);

        Controls.Add(_grid);
        Controls.Add(table);
        Controls.Add(strip);
        AcceptButton = _btnPull;
    }

    private void AddColumns()
    {
        void C(string name, string header, bool numeric = false)
        {
            var col = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, SortMode = DataGridViewColumnSortMode.Automatic };
            if (numeric)
            {
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.ValueType = typeof(double);
            }
            _grid.Columns.Add(col);
        }
        C("Status", "Status"); C("Id", "ID"); C("Name", "Name"); C("Mpn", "MPN"); C("Value", "Value");
        C("PerBuild", "Per build", true); C("Need", "Need", true); C("Stock", "Stock", true); C("Min", "Min. stock", true);
        C("ToOrder", "To order", true); C("OrderQty", "Order qty", true); C("Supplier", "Supplier"); C("Spn", "SPN");
        C("Price", "Unit price"); C("Refs", "References");
        _grid.Columns["Refs"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns["Refs"]!.MinimumWidth = 150;
    }

    // ------------------------------------------------------------------ actions

    private void Browse()
    {
        using var dlg = new OpenFileDialog { Filter = "KiCad project (*.kicad_pro)|*.kicad_pro", Title = "Select KiCad project" };
        if (File.Exists(_txtProject.Text)) dlg.InitialDirectory = Path.GetDirectoryName(_txtProject.Text);
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtProject.Text = dlg.FileName;
    }

    private async Task TestConnection()
    {
        if (!ValidateConnectionInputs()) return;
        UseWaitCursor = true;
        try
        {
            using var client = new PartDbClient(_txtUrl.Text, _txtToken.Text);
            var msg = await client.TestAsync(CancellationToken.None);
            SetStatus(msg);
            MessageBox.Show(this, msg, "Part-DB", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (PartDbException ex) { ShowError(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    private async Task Pull()
    {
        var project = _txtProject.Text.Trim().Trim('"');
        if (!File.Exists(project) || !project.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Select an existing .kicad_pro file.");
            return;
        }
        if (!ValidateConnectionInputs()) return;
        SaveSettings();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        try
        {
            SetStatus("Reading schematic…");
            var includeDnp = _chkDnp.Checked;
            var bom = await Task.Run(() => KiCadBomReader.Read(project, includeDnp), ct);

            using var client = new PartDbClient(_txtUrl.Text, _txtToken.Text);
            var ids = bom.WithId.Select(g => g.PartDbId!).Distinct().ToList();
            _progress.Maximum = Math.Max(1, ids.Count);
            _progress.Value = 0;
            _progress.Visible = true;
            SetStatus($"Fetching {ids.Count} parts from Part-DB…");
            var progress = new Progress<int>(n => _progress.Value = Math.Min(n, _progress.Maximum));
            var (parts, missing) = await client.GetPartsAsync(ids, progress, ct);

            _report = ProcurementReport.Build(bom, parts, missing, (int)_numBuilds.Value, client.BaseUrl, DateTime.Now);
            FillGrid();
            _btnExport.Enabled = true;

            var status = $"Pulled {parts.Count} parts at {_report.PulledAt:HH:mm:ss} from {Path.GetFileName(project)} " +
                         $"({bom.SchematicFiles.Count} sheet file(s), {bom.Counted.Count} parts per build).";
            var last = _settings.LastExportFor(project);
            if (last != null) status += $"  Last order list exported {last.ExportedAt:yyyy-MM-dd HH:mm} for {last.Builds} build(s).";
            SetStatus(status);

            var notes = new List<string>();
            if (missing.Count > 0) notes.Add($"Part-DB IDs not found: {string.Join(", ", missing)}");
            if (bom.WithoutId.Count > 0)
                notes.Add($"{bom.WithoutId.Count} BOM line(s) have no Part-DB ID: " +
                          string.Join("; ", bom.WithoutId.Take(8).Select(g => $"{g.ReferenceList} ({g.Value})")) +
                          (bom.WithoutId.Count > 8 ? " …" : "") + "\nThey are listed in the Excel sheet \"No Part-DB ID\".");
            notes.AddRange(bom.Warnings);
            if (notes.Count > 0)
                MessageBox.Show(this, string.Join("\n\n", notes), "Check these", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { SetStatus("Cancelled."); }
        catch (PartDbException ex) { ShowError(ex.Message); SetStatus("Pull failed."); }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            ShowError("Could not read the schematic:\n" + ex.Message);
            SetStatus("Pull failed.");
        }
        finally
        {
            _progress.Visible = false;
            SetBusy(false);
        }
    }

    private void Recalculate()
    {
        if (_report == null) return;
        _report = _report.WithBuilds((int)_numBuilds.Value);
        FillGrid();
    }

    /// <summary>Export is only allowed for the project the data was pulled for.</summary>
    private void MarkStale(string? reason)
    {
        if (_report == null) return;
        if (reason != null) SetStatus(reason);
        _btnExport.Enabled = string.Equals(_report.Bom.ProjectFile, SafePath(_txtProject.Text), StringComparison.OrdinalIgnoreCase);
    }

    private void Export()
    {
        if (_report == null) return;
        var r = _report;

        // 1) Schematic saved after the pull? The numbers may be outdated.
        var newest = r.Bom.SchematicFiles.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (newest > r.Bom.NewestSchematicWriteUtc)
        {
            var a = MessageBox.Show(this, "The schematic was saved after the data was pulled.\n\nPull again before exporting?",
                "Schematic changed", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (a == DialogResult.Cancel) return;
            if (a == DialogResult.Yes) { _ = Pull(); return; }
        }
        // 2) Old Part-DB data.
        if (DateTime.Now - r.PulledAt > TimeSpan.FromMinutes(30))
        {
            if (MessageBox.Show(this, $"The Part-DB data is from {r.PulledAt:HH:mm}. Stock may have changed since.\n\nExport anyway?",
                    "Old data", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        // 3) An order list for this project was exported before: risk of ordering twice.
        var last = _settings.LastExportFor(r.Bom.ProjectFile);
        if (last != null)
        {
            var msg = $"An order list for this project was already exported on {last.ExportedAt:yyyy-MM-dd HH:mm} " +
                      $"for {last.Builds} build(s) ({last.OrderLines} lines).\n\n" +
                      "If you placed that order and the parts are not booked into Part-DB yet, " +
                      "they will be ordered a second time.\n\nExport a new order list anyway?";
            if (MessageBox.Show(this, msg, "Already exported", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        var projectDir = Path.GetDirectoryName(r.Bom.ProjectFile)!;
        var orderingDir = Path.Combine(projectDir, "ordering");
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = ReportExporter.DefaultFileName(r),
            InitialDirectory = Directory.Exists(orderingDir) ? orderingDir : projectDir,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        while (true)
        {
            try
            {
                ReportExporter.Save(r, dlg.FileName);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var name = Path.GetFileName(dlg.FileName);
                var msg = IsLocked(dlg.FileName)
                    ? $"\"{name}\" is open in another program, probably Excel.\n\nClose it there and press Retry."
                    : $"\"{name}\" could not be saved:\n{ex.Message}\n\nCheck that the folder is writable, or press Cancel and export to another location.";
                if (MessageBox.Show(this, msg, "Cannot save", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
                {
                    SetStatus("Export cancelled.");
                    return;
                }
            }
        }

        _settings.Exports.Add(new ExportRecord
        {
            Project = r.Bom.ProjectFile, Builds = r.Builds, ExportedAt = DateTime.Now,
            File = dlg.FileName, OrderLines = r.OrderLines.Count(),
        });
        SaveSettings();
        SetStatus($"Exported {r.OrderLines.Count()} order line(s) to {dlg.FileName}");

        if (MessageBox.Show(this, "Order list saved.\n\nOpen it in Excel now?", "Exported",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------------ grid

    private void FillGrid()
    {
        if (_report == null) return;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var l in _report.Lines)
        {
            var idx = _grid.Rows.Add(l.StatusText, l.Bom.PartDbId, l.Part?.Name ?? "", l.Part?.Mpn ?? l.Bom.Mpn, l.Bom.Value,
                (double)l.PerBuild, (double)l.Need, l.Stock, l.MinStock, l.ToOrder, l.OrderQty,
                l.Offer?.Supplier ?? "", l.Offer?.Spn ?? "",
                l.Price == null ? "" : $"{l.Price.UnitPrice:0.0000} {l.Price.Currency}", l.Bom.ReferenceList);
            var row = _grid.Rows[idx];
            row.Tag = l;
            row.Cells["Status"].Style.BackColor = l.Status switch
            {
                LineStatus.Ok => Green,
                LineStatus.BelowMinimum or LineStatus.StockUnknown => Yellow,
                _ => Red,
            };
            if (l.OrderQty > 0) row.Cells["OrderQty"].Style.Font = _boldFont ??= new Font(_grid.Font, FontStyle.Bold);
        }
        _grid.ResumeLayout();

        var order = _report.OrderLines.ToList();
        var shortCount = _report.Lines.Count(l => l.Status is LineStatus.Short or LineStatus.NotInPartDb);
        _lblSummary.Text = $"{_report.Lines.Count} lines   ·   {order.Count} to order   ·   {shortCount} short for {_report.Builds} build(s)" +
                           (_report.Bom.WithoutId.Count > 0 ? $"   ·   {_report.Bom.WithoutId.Count} without Part-DB ID" : "");
    }

    private void OpenPartInBrowser(int rowIndex)
    {
        if (rowIndex < 0 || _grid.Rows[rowIndex].Tag is not ProcurementLine { Part: { } part }) return;
        Process.Start(new ProcessStartInfo(part.Url) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ helpers

    private bool ValidateConnectionInputs()
    {
        if (!Uri.TryCreate(_txtUrl.Text.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
        {
            ShowError("Enter the Part-DB URL, e.g. http://partdb.local:8080");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_txtToken.Text))
        {
            ShowError("Enter a Part-DB API token (User settings → API tokens; a read-only token is enough).");
            return false;
        }
        return true;
    }

    private void SaveSettings()
    {
        _settings.ProjectPath = _txtProject.Text.Trim().Trim('"');
        _settings.Builds = (int)_numBuilds.Value;
        _settings.IncludeDnp = _chkDnp.Checked;
        _settings.PartDbUrl = _txtUrl.Text.Trim();
        _settings.Token = _txtToken.Text.Trim();
        try { _settings.Save(); } catch (IOException) { /* not critical */ }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _btnPull.Enabled = _btnBrowse.Enabled = _btnTest.Enabled = !busy;
        if (busy) _btnExport.Enabled = false;
        else if (_report != null) MarkStale(null);
    }

    private void SetStatus(string text) => _status.Text = text;

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Part-DB Procurement", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>True when another program (typically Excel) holds the file open.</summary>
    private static bool IsLocked(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return !new FileInfo(path).IsReadOnly; }
    }

    private static string SafePath(string p)
    {
        try { return string.IsNullOrWhiteSpace(p) ? "" : Path.GetFullPath(p.Trim().Trim('"')); }
        catch (Exception) { return ""; }
    }
}
