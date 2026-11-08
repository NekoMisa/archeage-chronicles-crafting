namespace CraftingCalculator;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, GatheredMaterial> gathered = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkLabel gatheredToggle = new()
    {
        Dock = DockStyle.Bottom, Height = 24, LinkColor = Teal, Visible = false,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private IReadOnlyDictionary<string, long> afterCrafts = new Dictionary<string, long>();
    private IReadOnlyDictionary<string, long> remainingMaterials = new Dictionary<string, long>();
    private bool showingGathered;
    private bool updatingMaterials;

    private Control CreateMaterialChecklist()
    {
        SetQuantityColumns(materials, "Material");
        materials.ItemCheck += MaterialChecked;
        materials.ItemActivate += (_, _) => OpenMaterialSource(materials.FocusedItem?.Tag);
        gatheredToggle.LinkClicked += (_, _) =>
        {
            showingGathered = !showingGathered;
            RefreshMaterials();
        };

        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(materials);
        panel.Controls.Add(emptyMaterials);
        panel.Controls.Add(gatheredToggle);
        return panel;
    }

    private void RefreshMaterials()
    {
        var plan = currentPlan;
        if (plan is null)
            gathered.Clear();
        else
        {
            foreach (var item in gathered.Keys
                .Where(item => plan.Materials.GetValueOrDefault(item) != gathered[item].PlanQuantity).ToArray())
            {
                gathered.Remove(item);
            }
        }

        afterCrafts = plan?.MaterialsAfterCrafts(steps.Completed) ?? new Dictionary<string, long>();
        remainingMaterials = afterCrafts
            .Select(item => (item.Key, Quantity: item.Value - (gathered.GetValueOrDefault(item.Key)?.Quantity ?? 0)))
            .Where(item => item.Quantity > 0)
            .ToDictionary(item => item.Key, item => item.Quantity, StringComparer.OrdinalIgnoreCase);

        showingGathered &= gathered.Count > 0;
        gatheredToggle.Visible = gathered.Count > 0;
        gatheredToggle.Text = showingGathered ? "Back to needed materials" : $"Gathered ({gathered.Count:N0})";

        updatingMaterials = true;
        materials.BeginUpdate();
        try
        {
            var rows = showingGathered
                ? gathered.Select(item => new KeyValuePair<string, long>(item.Key, item.Value.Quantity))
                : remainingMaterials;
            materials.Items.Clear();
            foreach (var (item, amount) in rows.OrderBy(material => material.Key))
            {
                var tooltip = MaterialToolTip(item) + (showingGathered
                    ? "\nUncheck to undo gathering." : "\nCheck off this quantity when gathered.");
                var used = plan?.UsedInventory.GetValueOrDefault(item) ?? 0;
                if (!showingGathered && used > 0)
                    tooltip += $"\nThe full plan uses {used:N0} from inventory.";

                var row = new ListViewItem([item, amount.ToString("N0")])
                {
                    Tag = item, ToolTipText = tooltip, UseItemStyleForSubItems = false, Checked = showingGathered
                };
                row.SubItems[0].ForeColor = Teal;
                materials.Items.Add(row);
            }

            materials.Visible = plan is null || materials.Items.Count > 0;
            emptyMaterials.Visible = plan is not null && materials.Items.Count == 0;
        }
        finally
        {
            materials.EndUpdate();
            updatingMaterials = false;
        }
    }

    private void MaterialChecked(object? sender, ItemCheckEventArgs e)
    {
        if (updatingMaterials || currentPlan is not { } plan || materials.Items[e.Index].Tag is not string item
            || e.NewValue == e.CurrentValue)
            return;

        if (showingGathered && e.NewValue == CheckState.Unchecked)
            gathered.Remove(item);
        else if (!showingGathered && e.NewValue == CheckState.Checked
            && plan.Materials.TryGetValue(item, out var original) && afterCrafts.TryGetValue(item, out var needed))
        {
            // Gathered quantities cover the outstanding part of the plan, after work already checked off.
            gathered[item] = new GatheredMaterial(original, needed);
        }
        else
            return;

        // Rebuild after the native checkbox event finishes using this row.
        BeginInvoke(() =>
        {
            if (IsDisposed)
                return;
            RefreshMaterials();
            SaveActivePlan();
        });
    }
}
