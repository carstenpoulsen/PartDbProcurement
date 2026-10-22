using System.Diagnostics;
using PartDbProcurement.Core;

namespace PartDbProcurement;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;

    private readonly DataGridView _projects = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 104,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = SystemColors.Window,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, AllowDrop = true,
    };
    private readonly Button _btnAdd = new() { Text = "Add…", AutoSize = true, Dock = DockStyle.Top };
    private readonly Button _btnRecent = new() { Text = "Recent ▾", AutoSize = true, Dock = DockStyle.Top };
    private readonly Button _btnRemove = new() { Text = "Remove", AutoSize = true, Dock = DockStyle.Top };
    private readonly ContextMenuStrip _recentMenu = new() { ShowItemToolTips = true };
    private readonly CheckBox _chkDnp = new() { Text = "Count DNP parts", AutoSize = true, Margin = new Padding(0, 3, 3, 3) };
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

    /// <param name="projectArgs">.kicad_pro files from the command line; empty to restore the last purchase.</param>
    public MainForm(IReadOnlyList<string> projectArgs)
    {
        _settings = AppSettings.Load();
        Text = "Part-DB Procurement";
        Width = 1250; Height = 720;
        MinimumSize = new Size(800, 450);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();

        if (projectArgs.Count > 0)
            foreach (var a in projectArgs) AddProject(a, null);
        else
            foreach (var e in _settings.Projects) AddProject(e.Path, e.Builds, remember: false);
        _chkDnp.Checked = _settings.IncludeDnp;
        _txtUrl.Text = _settings.PartDbUrl;
        _txtToken.Text = _settings.Token;

        _btnAdd.Click += (_, _) => Browse();
        _btnRecent.Click += (_, _) => ShowRecentMenu();
        _btnRemove.Click += (_, _) => RemoveSelectedProject();
        _projects.CellValidating += ValidateBuildsCell;
        _projects.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == _projects.Columns["Builds"]!.Index) BuildsChanged(); };
        _projects.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete && !_projects.IsCurrentCellInEditMode) RemoveSelectedProject(); };
        _projects.DragEnter += (_, e) => { if (DroppedProjects(e).Any()) e.Effect = DragDropEffects.Copy; };
        _projects.DragDrop += (_, e) => { foreach (var f in DroppedProjects(e)) AddProject(f, null); };
        _btnTest.Click += async (_, _) => await TestConnection();
        _btnPull.Click += async (_, _) => await Pull();
        _btnExport.Click += (_, _) => Export();
        _chkDnp.CheckedChanged += (_, _) => MarkStale("DNP setting changed. Pull again to apply.");
        _grid.CellDoubleClick += (_, e) => OpenPartInBrowser(e.RowIndex);
        FormClosing += (_, _) => SaveSettings();

        SetStatus("Add one or more KiCad projects and press \"Pull from Part-DB\".");
    }

    private void BuildLayout()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) };

        var projectsLabel = L("KiCad projects:");
        projectsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        table.Controls.Add(projectsLabel, 0, 0);
        table.Controls.Add(_projects, 1, 0);
        var projectButtons = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        projectButtons.Controls.Add(_btnAdd);
        projectButtons.Controls.Add(_btnRecent);
        projectButtons.Controls.Add(_btnRemove);
        table.Controls.Add(projectButtons, 2, 0);

        table.Controls.Add(_chkDnp, 1, 1);

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

        AddProjectColumns();
        AddColumns();

        var strip = new StatusStrip();
        strip.Items.Add(_status);
        strip.Items.Add(_progress);

        Controls.Add(_grid);
        Controls.Add(table);
        Controls.Add(strip);
        AcceptButton = _btnPull;
    }

    private void AddProjectColumns()
    {
        _projects.Columns.Add(new DataGridViewTextBoxColumn { Name = "Project", HeaderText = "Project", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.NotSortable });
        var builds = new DataGridViewTextBoxColumn { Name = "Builds", HeaderText = "Builds", Width = 70, ValueType = typeof(int), SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = "Number of builds. Type to change." };
        builds.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _projects.Columns.Add(builds);
        _projects.Columns.Add(new DataGridViewTextBoxColumn { Name = "Folder", HeaderText = "Folder", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
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
        C("Need", "Need", true); C("Stock", "Stock", true); C("Min", "Min. stock", true);
        C("ToOrder", "To order", true); C("OrderQty", "Order qty", true); C("Supplier", "Supplier"); C("Spn", "SPN");
        C("Price", "Unit price"); C("Refs", "References");
        _grid.Columns["Refs"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns["Refs"]!.MinimumWidth = 150;
    }

    // ------------------------------------------------------------------ actions

    private void Browse()
    {
        using var dlg = new OpenFileDialog { Filter = "KiCad project (*.kicad_pro)|*.kicad_pro", Title = "Add KiCad project", Multiselect = true };
        var near = CurrentProjects().Select(p => p.Path).Concat(_settings.Recent.Select(e => e.Path)).FirstOrDefault(File.Exists);
        if (near != null) dlg.InitialDirectory = Path.GetDirectoryName(near);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            foreach (var f in dlg.FileNames) AddProject(f, null);
    }

    // ------------------------------------------------------------------ projects

    /// <param name="builds">Null: the number remembered in the recent list, else 1.</param>
    private void AddProject(string path, int? builds, bool remember = true)
    {
        path = SafePath(path);
        if (path == "" || !path.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase)) return;
        var existing = ProjectRows().FirstOrDefault(r => string.Equals((string)r.Tag!, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { existing.Selected = true; return; }

        var n = Math.Clamp(builds ?? _settings.Recent.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))?.Builds ?? 1, 1, 10000);
        var row = _projects.Rows[_projects.Rows.Add()];
        row.Tag = path; // set before the values: CellValueChanged reads it
        row.SetValues(Path.GetFileName(path), n, Path.GetDirectoryName(path));
        row.Cells["Project"].ToolTipText = path;
        if (!File.Exists(path)) row.Cells["Project"].Style.ForeColor = Color.Firebrick;
        row.Selected = true;
        if (remember) _settings.Remember(path, n);
        ProjectsChanged();
    }

    private void RemoveSelectedProject()
    {
        if (_projects.SelectedRows.Count == 0 || _projects.IsCurrentCellInEditMode) return;
        _projects.Rows.Remove(_projects.SelectedRows[0]);
        ProjectsChanged();
    }

    private void ShowRecentMenu()
    {
        _recentMenu.Items.Clear();
        if (_settings.Recent.Count == 0) _recentMenu.Items.Add(new ToolStripMenuItem("(no recent projects)") { Enabled = false });
        var current = CurrentProjects().Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _settings.Recent.ToList())
        {
            var exists = File.Exists(e.Path);
            var item = new ToolStripMenuItem($"{Path.GetFileName(e.Path)}   ×{e.Builds}{(exists ? "" : "   (not found)")}")
            {
                ToolTipText = e.Path,
                ShortcutKeyDisplayString = Path.GetDirectoryName(e.Path),
                Checked = current.Contains(e.Path),
                Enabled = exists && !current.Contains(e.Path),
            };
            item.Click += (_, _) => AddProject(e.Path, e.Builds);
            _recentMenu.Items.Add(item);
        }
        _recentMenu.Show(_btnRecent, new Point(0, _btnRecent.Height));
    }

    private void ValidateBuildsCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != _projects.Columns["Builds"]!.Index) return;
        if (!int.TryParse(Convert.ToString(e.FormattedValue), out var n) || n < 1 || n > 10000)
        {
            e.Cancel = true;
            _projects.Rows[e.RowIndex].ErrorText = "Enter a number of builds from 1 to 10000.";
        }
        else _projects.Rows[e.RowIndex].ErrorText = "";
    }

    private void BuildsChanged()
    {
        foreach (var p in CurrentProjects()) _settings.UpdateRecentBuilds(p.Path, p.Builds);
        Recalculate();
    }

    private void ProjectsChanged()
    {
        if (_report == null) return;
        if (SameProjects()) Recalculate();
        MarkStale(SameProjects() ? null : "Projects changed. Pull again.");
    }

    private IEnumerable<DataGridViewRow> ProjectRows() => _projects.Rows.Cast<DataGridViewRow>();

    private List<ProjectEntry> CurrentProjects() => ProjectRows()
        .Select(r => new ProjectEntry
        {
            Path = (string)r.Tag!,
            Builds = int.TryParse(Convert.ToString(r.Cells["Builds"].Value), out var n) ? Math.Clamp(n, 1, 10000) : 1,
        }).ToList();

    /// <summary>True when the pulled data belongs to exactly the projects in the list.</summary>
    private bool SameProjects() => _report != null &&
        _report.Projects.Select(p => p.Bom.ProjectFile).SequenceEqual(CurrentProjects().Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> DroppedProjects(DragEventArgs e) =>
        (e.Data?.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>())
        .Where(f => f.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase));

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
        var projects = CurrentProjects();
        if (projects.Count == 0)
        {
            ShowError("Add a KiCad project (.kicad_pro): use Add… or Recent.");
            return;
        }
        if (projects.FirstOrDefault(p => !File.Exists(p.Path)) is { } gone)
        {
            ShowError($"Project not found:\n{gone.Path}\n\nRemove it from the list, or add it again from its new location.");
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
            SetStatus(projects.Count == 1 ? "Reading schematic…" : $"Reading {projects.Count} schematics…");
            var includeDnp = _chkDnp.Checked;
            var boms = await Task.Run(() => projects.Select(p => KiCadBomReader.Read(p.Path, includeDnp)).ToList(), ct);
            var runs = boms.Select((b, i) => new ProjectRun { Bom = b, Builds = projects[i].Builds }).ToList();
            var combined = runs.Count > 1;

            using var client = new PartDbClient(_txtUrl.Text, _txtToken.Text);
            var ids = boms.SelectMany(b => b.WithId).Select(g => g.PartDbId!).Distinct().ToList();
            _progress.Maximum = Math.Max(1, ids.Count);
            _progress.Value = 0;
            _progress.Visible = true;
            SetStatus($"Fetching {ids.Count} parts from Part-DB…");
            var progress = new Progress<int>(n => _progress.Value = Math.Min(n, _progress.Maximum));
            var (parts, missing) = await client.GetPartsAsync(ids, progress, ct);

            _report = ProcurementReport.Build(runs, parts, missing, client.BaseUrl, DateTime.Now);
            FillGrid();
            _btnExport.Enabled = true;
            foreach (var p in Enumerable.Reverse(projects)) _settings.Remember(p.Path, p.Builds); // first project ends up on top

            var status = $"Pulled {parts.Count} parts at {_report.PulledAt:HH:mm:ss} from {string.Join(", ", boms.Select(b => Path.GetFileName(b.ProjectFile)))} " +
                         $"({boms.Sum(b => b.SchematicFiles.Count)} sheet file(s), {string.Join(" + ", boms.Select(b => b.Counted.Count))} parts per build).";
            foreach (var b in boms)
                if (_settings.LastExportFor(b.ProjectFile) is { } last)
                    status += $"  {(combined ? b.ProjectName + ": l" : "L")}ast order list exported {last.ExportedAt:yyyy-MM-dd HH:mm} for {last.Builds} build(s).";
            SetStatus(status);

            var notes = new List<string>();
            if (missing.Count > 0) notes.Add($"Part-DB IDs not found: {string.Join(", ", missing)}");
            foreach (var bom in boms)
            {
                var prefix = combined ? bom.ProjectName + ": " : "";
                if (bom.WithoutId.Count > 0)
                    notes.Add($"{prefix}{bom.WithoutId.Count} BOM line(s) have no Part-DB ID: " +
                              string.Join("; ", bom.WithoutId.Take(8).Select(g => $"{g.ReferenceList} ({g.Value})")) +
                              (bom.WithoutId.Count > 8 ? " …" : "") + "\nThey are listed in the Excel sheet \"No Part-DB ID\".");
                notes.AddRange(bom.Warnings.Select(w => prefix + w));
            }
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
        if (_report == null || !SameProjects()) return;
        _report = _report.WithBuilds(CurrentProjects().Select(p => p.Builds).ToList());
        FillGrid();
    }

    /// <summary>Export is only allowed for the projects the data was pulled for.</summary>
    private void MarkStale(string? reason)
    {
        if (_report == null) return;
        if (reason != null) SetStatus(reason);
        _btnExport.Enabled = SameProjects();
    }

    private void Export()
    {
        if (_report == null) return;
        var r = _report;

        // 1) Schematic saved after the pull? The numbers may be outdated.
        if (r.Projects.Any(p => p.Bom.SchematicFiles.Where(File.Exists).Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue).Max() > p.Bom.NewestSchematicWriteUtc))
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
        // 3) An order list for one of the projects was exported before: risk of ordering twice.
        var earlier = r.Projects.Select(p => (p.Bom.ProjectName, Last: _settings.LastExportFor(p.Bom.ProjectFile)))
                                .Where(x => x.Last != null).ToList();
        if (earlier.Count > 0)
        {
            var msg = (r.IsCombined ? "Order lists were already exported for:\n" : "An order list for this project was already exported:\n") +
                      string.Join("\n", earlier.Select(x => $"• {(r.IsCombined ? x.ProjectName + ": " : "")}{x.Last!.ExportedAt:yyyy-MM-dd HH:mm} " +
                          $"for {x.Last.Builds} build(s) ({x.Last.OrderLines} lines)" +
                          (x.Last.CombinedWith.Length > 0 ? $", combined with {x.Last.CombinedWith}" : ""))) + "\n\n" +
                      "If you placed that order and the parts are not booked into Part-DB yet, " +
                      "they will be ordered a second time.\n\nExport a new order list anyway?";
            if (MessageBox.Show(this, msg, "Already exported", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        var projectDir = Path.GetDirectoryName(r.Projects[0].Bom.ProjectFile)!;
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

        var exportedAt = DateTime.Now;
        foreach (var p in r.Projects)
        {
            _settings.Exports.Add(new ExportRecord
            {
                Project = p.Bom.ProjectFile, Builds = p.Builds, ExportedAt = exportedAt,
                File = dlg.FileName, OrderLines = r.OrderLines.Count(),
                CombinedWith = string.Join(", ", r.Projects.Where(o => o != p).Select(o => o.Bom.ProjectName)),
            });
        }
        foreach (var p in r.Projects.Reverse()) _settings.Remember(p.Bom.ProjectFile, p.Builds);
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
        var r = _report;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        SyncPerBuildColumns(r);
        foreach (var l in r.Lines)
        {
            var row = _grid.Rows[_grid.Rows.Add()];
            void Set(string column, object? value) => row.Cells[column].Value = value;
            Set("Status", l.StatusText); Set("Id", l.Bom.PartDbId); Set("Name", l.Part?.Name ?? ""); Set("Mpn", l.Part?.Mpn ?? l.Bom.Mpn);
            Set("Value", l.Bom.Value);
            for (int i = 0; i < r.Projects.Count; i++)
            {
                var n = l.PerBuildIn(r.Projects[i]);
                Set("PerBuild" + i, n == 0 && r.IsCombined ? null : (double)n);
            }
            Set("Need", (double)l.Need); Set("Stock", l.Stock); Set("Min", l.MinStock); Set("ToOrder", l.ToOrder); Set("OrderQty", l.OrderQty);
            Set("Supplier", l.Offer?.Supplier ?? ""); Set("Spn", l.Offer?.Spn ?? "");
            Set("Price", l.Price == null ? "" : $"{l.Price.UnitPrice:0.0000} {l.Price.Currency}");
            Set("Refs", l.ReferenceText(r.IsCombined));
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

        var order = r.OrderLines.ToList();
        var shortCount = r.Lines.Count(l => l.Status is LineStatus.Short or LineStatus.NotInPartDb);
        var withoutId = r.Projects.Sum(p => p.Bom.WithoutId.Count);
        _lblSummary.Text = $"{r.Lines.Count} lines   ·   {order.Count} to order   ·   {shortCount} short for " +
                           (r.IsCombined ? r.Title : $"{r.Projects[0].Builds} build(s)") +
                           (withoutId > 0 ? $"   ·   {withoutId} without Part-DB ID" : "");
    }

    /// <summary>One "per build" column per project, placed before "Need".</summary>
    private void SyncPerBuildColumns(ProcurementReport r)
    {
        var headers = r.IsCombined ? r.Projects.Select(p => $"{p.Bom.ProjectName} per build").ToList() : new List<string> { "Per build" };
        var existing = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Name.StartsWith("PerBuild")).ToList();
        if (existing.Select(c => c.HeaderText).SequenceEqual(headers)) return;
        foreach (var c in existing) _grid.Columns.Remove(c);
        var at = _grid.Columns["Need"]!.Index;
        for (int i = 0; i < headers.Count; i++)
        {
            var col = new DataGridViewTextBoxColumn { Name = "PerBuild" + i, HeaderText = headers[i], SortMode = DataGridViewColumnSortMode.Automatic, ValueType = typeof(double) };
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns.Insert(at + i, col);
        }
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
        _settings.Projects = CurrentProjects();
        _settings.IncludeDnp = _chkDnp.Checked;
        _settings.PartDbUrl = _txtUrl.Text.Trim();
        _settings.Token = _txtToken.Text.Trim();
        try { _settings.Save(); } catch (IOException) { /* not critical */ }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _btnPull.Enabled = _btnAdd.Enabled = _btnRecent.Enabled = _btnRemove.Enabled = _btnTest.Enabled = !busy;
        // No edits while a pull runs: the result is computed for the projects and builds it started with.
        _projects.ReadOnly = busy;
        _projects.AllowDrop = !busy;
        if (!busy) _projects.Columns["Project"]!.ReadOnly = _projects.Columns["Folder"]!.ReadOnly = true;
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
