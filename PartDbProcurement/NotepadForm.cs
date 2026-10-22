using PartDbProcurement.Core;

namespace PartDbProcurement;

/// <summary>Edits the notepad: ad hoc items to remember when ordering. Changes are kept only when the user presses OK.</summary>
public sealed class NotepadForm : Form
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AllowUserToResizeRows = false,
        RowHeadersWidth = 24, SelectionMode = DataGridViewSelectionMode.CellSelect, BackgroundColor = SystemColors.Window,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
    };
    private readonly Button _btnRemove = new() { Text = "Remove", AutoSize = true };
    private readonly Button _btnOk = new() { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(75, 0) };
    private readonly Button _btnCancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(75, 0) };

    /// <summary>The edited items, valid after OK.</summary>
    public List<RememberItem> Items { get; private set; } = new();

    public NotepadForm(IEnumerable<RememberItem> items)
    {
        Text = "Notepad: remember when ordering";
        Width = 1000; Height = 420;
        MinimumSize = new Size(600, 250);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var help = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4), MaximumSize = new Size(960, 0),
            Text = "Things to order next time, e.g. consumables that are not in a schematic. Items ticked \"On order list\" are shown " +
                   $"after a pull and offered when you export, as lines with the reference {RememberItem.Reference}. " +
                   "Type in the last row to add an item; select a row and press Del or Remove to delete it.",
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        buttons.Controls.Add(_btnCancel);
        buttons.Controls.Add(_btnOk);
        buttons.Controls.Add(_btnRemove);
        _btnRemove.Margin = new Padding(3, 3, 24, 3);

        AddColumns();
        foreach (var i in items)
        {
            var row = _grid.Rows[_grid.Rows.Add(i.OnOrderList, i.Description, i.Supplier, i.OrderNumber, i.Quantity, i.Remarks,
                i.Added.ToString("yyyy-MM-dd"), i.LastOrdered?.ToString("yyyy-MM-dd") ?? "")];
            row.Tag = i;
        }

        Controls.Add(_grid);
        Controls.Add(help);
        Controls.Add(buttons);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        _grid.DefaultValuesNeeded += (_, e) => { e.Row.Cells["OnOrderList"].Value = true; e.Row.Cells["Quantity"].Value = 1; };
        _grid.CellValidating += ValidateQuantity;
        _btnRemove.Click += (_, _) => RemoveSelected();
        _btnOk.Click += (_, e) => Collect();
    }

    private void AddColumns()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "OnOrderList", HeaderText = "On order list", Width = 80, ToolTipText = "Offer this item for the next order list." });
        DataGridViewTextBoxColumn C(string name, string header, int width, string? tip = null)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ToolTipText = tip ?? "", SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(c);
            return c;
        }
        C("Description", "Description", 260).MinimumWidth = 120;
        C("Supplier", "Supplier", 90, "Optional. The item is listed with this supplier's lines.");
        C("OrderNumber", "Order no.", 130, "Supplier or manufacturer part number, if known.");
        var qty = C("Quantity", "Qty", 50);
        qty.ValueType = typeof(int);
        qty.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        C("Remarks", "Remarks", 200).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        C("Added", "Added", 75).ReadOnly = true;
        C("LastOrdered", "Last ordered", 80, "Last time the item was put on an exported order list.").ReadOnly = true;
    }

    private void ValidateQuantity(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != _grid.Columns["Quantity"]!.Index || _grid.Rows[e.RowIndex].IsNewRow) return;
        if (!int.TryParse(Convert.ToString(e.FormattedValue), out var n) || n < 1 || n > 1000000)
        {
            e.Cancel = true;
            _grid.Rows[e.RowIndex].ErrorText = "Enter a quantity of 1 or more.";
        }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private void RemoveSelected()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        var rows = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow)
            .Concat(_grid.CurrentRow is { } current ? new[] { current } : Array.Empty<DataGridViewRow>())
            .Where(r => !r.IsNewRow).Distinct().ToList();
        foreach (var r in rows) _grid.Rows.Remove(r);
    }

    private void Collect()
    {
        _grid.EndEdit();
        string S(DataGridViewRow r, string column) => Convert.ToString(r.Cells[column].Value)?.Trim() ?? "";
        Items = _grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r =>
        {
            var old = r.Tag as RememberItem;
            return new RememberItem
            {
                OnOrderList = r.Cells["OnOrderList"].Value is true,
                Description = S(r, "Description"),
                Supplier = S(r, "Supplier"),
                OrderNumber = S(r, "OrderNumber"),
                Quantity = int.TryParse(S(r, "Quantity"), out var n) ? Math.Max(1, n) : 1,
                Remarks = S(r, "Remarks"),
                Added = old?.Added ?? DateTime.Now,
                LastOrdered = old?.LastOrdered,
            };
        }).Where(i => !i.IsEmpty).ToList();
    }
}
