using System.Globalization;

namespace CraftingCalculator;

internal sealed class InventoryForm : Form
{
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search materials…", AccessibleName = "Search materials" };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Material inventory"
    };
    private readonly string[] names;

    public InventoryForm(string planName, IEnumerable<string> materials, IEnumerable<string> needed,
        IReadOnlyDictionary<string, long> inventory)
    {
        Inventory = new Dictionary<string, long>(inventory, StringComparer.OrdinalIgnoreCase);
        var relevant = needed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        names = materials.Concat(Inventory.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(relevant.Contains).ThenBy(item => item).ToArray();

        Text = $"Inventory · {planName}";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(490, 450);
        MinimumSize = new Size(420, 330);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Padding = new Padding(12);

        var help = new Label
        {
            Dock = DockStyle.Top, Height = 30, Text = "Enter on-hand counts. Blank or 0 clears."
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0), WrapContents = false
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, CausesValidation = false };
        var save = new Button { Text = "Apply", AutoSize = true };
        actions.Controls.Add(cancel);
        actions.Controls.Add(save);
        CancelButton = cancel;
        AcceptButton = save;
        Controls.Add(grid);
        Controls.Add(search);
        Controls.Add(help);
        Controls.Add(actions);

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Material", ReadOnly = true, FillWeight = 76, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "On hand", FillWeight = 24, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.CellValidating += ValidateAmount;
        grid.CellValidated += RememberAmount;
        search.TextChanged += (_, _) =>
        {
            if (grid.EndEdit())
                PopulateRows();
        };
        save.Click += (_, _) =>
        {
            if (grid.EndEdit() && ValidateChildren())
                DialogResult = DialogResult.OK;
        };
        PopulateRows();
        ActiveControl = search;
    }

    public Dictionary<string, long> Inventory { get; }

    private void PopulateRows()
    {
        grid.Rows.Clear();
        var query = search.Text.Trim();
        foreach (var item in names.Where(item => item.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var amount = Inventory.GetValueOrDefault(item);
            grid.Rows.Add(item, amount == 0 ? "" : amount.ToString("N0"));
        }
    }

    private void ValidateAmount(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != 1 || e.RowIndex < 0)
            return;

        e.Cancel = !TryAmount(e.FormattedValue, out _);
        grid.Rows[e.RowIndex].ErrorText = e.Cancel ? "Enter a whole number of zero or more." : "";
    }

    private void RememberAmount(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex != 1 || e.RowIndex < 0)
            return;
        var row = grid.Rows[e.RowIndex];
        if (row.Cells[0].Value is not string item || !TryAmount(row.Cells[1].Value, out var amount))
            return;

        if (amount == 0)
            Inventory.Remove(item);
        else
            Inventory[item] = amount;
    }

    private static bool TryAmount(object? value, out long amount)
    {
        var text = value?.ToString();
        amount = 0;
        return string.IsNullOrWhiteSpace(text) || (long.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture, out amount) && amount >= 0);
    }
}
