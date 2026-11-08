using System.Text.Json;

namespace CraftingCalculator;

internal sealed partial class MainForm
{
    private readonly RecipeCatalog catalog;
    private readonly PlanStore? planStore;
    private readonly PlanBook planBook;
    private readonly Dictionary<string, long> inventory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox planPicker = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 202, AccessibleName = "Saved plan",
        DisplayMember = nameof(SavedPlan.Name)
    };
    private readonly Button deletePlan = new() { Text = "Delete", AutoSize = true };
    private readonly LinkLabel saveStatus = new() { AutoSize = true, LinkColor = Teal, Margin = new Padding(6, 8, 3, 0) };
    private string? saveError;
    private bool storageUnavailable;
    private bool loadingPlan;
    private CraftTarget? examplePreview;

    private SavedPlan ActivePlan => planBook.Plans.Single(plan => plan.Id == planBook.ActivePlanId);

    private PlanBook LoadPlans()
    {
        try
        {
            return planStore?.Load() ?? PlanBook.Create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            storageUnavailable = true;
            ReportSaveError($"Could not open your saved plans.\n\n{ex.Message}");
            return PlanBook.Create();
        }
    }

    private Control CreatePlanBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var add = new Button { Text = "New…", AutoSize = true };
        var rename = new Button { Text = "Rename…", AutoSize = true };
        var stock = new Button { Text = "Inventory…", AutoSize = true };
        bar.Controls.Add(new Label { Text = "Plan", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        bar.Controls.AddRange([planPicker, add, rename, deletePlan, stock, saveStatus]);

        add.Click += (_, _) =>
        {
            if (PromptForName("New plan", "") is not { } name)
                return;
            CaptureActivePlan();
            var plan = new SavedPlan { Name = name };
            planBook.Plans.Add(plan);
            ActivatePlan(plan);
            SaveActivePlan();
        };
        rename.Click += (_, _) =>
        {
            if (PromptForName("Rename plan", ActivePlan.Name, ActivePlan.Id) is not { } name)
                return;
            ActivePlan.Name = name;
            LoadPlanPicker(ActivePlan);
            SaveActivePlan();
        };
        deletePlan.Click += (_, _) =>
        {
            if (planBook.Plans.Count <= 1
                || MessageBox.Show(this, $"Delete “{ActivePlan.Name}” and its progress?", "Delete plan",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var index = planBook.Plans.IndexOf(ActivePlan);
            planBook.Plans.RemoveAt(index);
            ActivatePlan(planBook.Plans[Math.Min(index, planBook.Plans.Count - 1)]);
            SaveActivePlan();
        };
        stock.Click += (_, _) => EditInventory();
        planPicker.SelectedIndexChanged += (_, _) =>
        {
            if (loadingPlan || planPicker.SelectedItem is not SavedPlan plan || plan.Id == planBook.ActivePlanId)
                return;
            CaptureActivePlan();
            ActivatePlan(plan);
            SaveActivePlan();
        };
        saveStatus.LinkClicked += (_, _) =>
            MessageBox.Show(this, saveError, "Saved plans", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return bar;
    }

    private void LoadPlanPicker(SavedPlan plan)
    {
        planPicker.BeginUpdate();
        try
        {
            planPicker.Items.Clear();
            planPicker.Items.AddRange(planBook.Plans.ToArray());
            planPicker.SelectedItem = plan;
            deletePlan.Enabled = planBook.Plans.Count > 1;
        }
        finally
        {
            planPicker.EndUpdate();
        }
    }

    private void ActivatePlan(SavedPlan plan)
    {
        loadingPlan = true;
        try
        {
            planBook.ActivePlanId = plan.Id;
            LoadPlanPicker(plan);
            targets.Clear();
            foreach (var saved in plan.Targets)
            {
                if (saved?.Resolve(catalog) is { } target)
                    targets[target.Recipe.Item] = target;
            }
            inventory.Clear();
            foreach (var (item, amount) in plan.Inventory)
            {
                if (!string.IsNullOrWhiteSpace(item) && amount > 0)
                    inventory[item] = amount;
            }
            steps.RestoreCompleted(plan.Completed
                .Where(batch => batch is { Recipe: not null, Batches: > 0 })
                .Select(batch => (Recipe: catalog.FindUnchangedRecipe(batch.Recipe), batch.Batches))
                .Where(batch => batch.Recipe is not null)
                .Select(batch => new CompletedBatch(batch.Recipe!, batch.Batches)));

            if (targets.Count == 0 && (plan.Preview?.Resolve(catalog) ?? examplePreview) is { } preview)
            {
                professions.SelectedTab = pickers.Keys.First(page => page.Text == preview.Recipe.Profession);
                ActivePicker.EditTarget(preview, focusQuantity: false);
            }

            emptyList.Text = plan.Targets.Length > 0 && targets.Count == 0
                ? "Saved items are unavailable in this recipe catalog."
                : "Add items from any profession to plan them together.";
            RefreshCraftList();
        }
        finally
        {
            loadingPlan = false;
        }
    }

    internal bool SaveActivePlan()
    {
        CaptureActivePlan();
        if (storageUnavailable)
            return false;

        try
        {
            planStore?.Save(planBook);
            saveError = null;
            saveStatus.Text = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportSaveError($"Could not save your plans.\n\n{ex.Message}");
            return false;
        }
    }

    private void CaptureActivePlan()
    {
        var plan = ActivePlan;
        // Keep saved items that this catalog cannot resolve so a later catalog can restore them.
        plan.Targets = targets.Values.Select(SavedTarget.From)
            .Concat(plan.Targets.Where(saved => saved is not null && !string.IsNullOrWhiteSpace(saved.Item)
                && saved.Quantity is >= 1 and <= CraftTarget.MaximumQuantity
                && saved.Resolve(catalog) is null && !targets.ContainsKey(saved.Item))).ToArray();
        plan.Inventory = new Dictionary<string, long>(inventory, StringComparer.OrdinalIgnoreCase);
        plan.Completed = steps.Completed.ToArray();
        plan.Preview = targets.Count == 0 && ActivePicker.SelectedRecipe is { } recipe
            ? SavedTarget.From(new CraftTarget(recipe, ActivePicker.Quantity)) : null;
    }

    internal bool RestoreCraftList(IEnumerable<CraftTarget> savedTargets)
    {
        var recovered = savedTargets.Select(SavedTarget.From).ToArray();
        if (recovered.Length == 0 || planBook.Plans.Any(plan => plan.Targets.SequenceEqual(recovered)))
            return true;

        CaptureActivePlan();
        var plan = ActivePlan;
        if (plan.Targets.Length > 0 || plan.Completed.Length > 0 || plan.Inventory.Count > 0)
        {
            const string title = "Recovered plan";
            var name = title;
            for (var number = 2; NameExists(name); number++)
                name = $"{title} {number}";
            plan = new SavedPlan { Name = name };
            planBook.Plans.Add(plan);
        }
        plan.Targets = recovered;
        ActivatePlan(plan);
        return SaveActivePlan();
    }

    private void ReportSaveError(string message)
    {
        saveError = message;
        saveStatus.Text = "Save unavailable";
    }

    private void EditInventory()
    {
        var names = catalog.Recipes.SelectMany(recipe => recipe.Materials.Keys);
        var relevant = currentPlan is { } plan
            ? plan.Steps.SelectMany(step => step.Recipe.Materials.Keys).Concat(plan.UsedInventory.Keys)
            : Enumerable.Empty<string>();
        using var dialog = new InventoryForm(ActivePlan.Name, names, relevant, inventory);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        inventory.Clear();
        foreach (var (item, amount) in dialog.Inventory)
            inventory[item] = amount;
        RefreshPlan();
        SaveActivePlan();
    }

    private bool NameExists(string name, Guid? exclude = null) =>
        planBook.Plans.Any(plan => plan.Id != exclude && plan.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private string? PromptForName(string title, string initial, Guid? exclude = null)
    {
        using var dialog = new Form
        {
            Text = title, Font = Font, ClientSize = new Size(330, 105), Padding = new Padding(12),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false
        };
        var input = new TextBox
        {
            Text = initial, PlaceholderText = "Plan name", MaxLength = 64, Dock = DockStyle.Top, AccessibleName = "Plan name"
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var confirm = new Button { Text = "Save", AutoSize = true };
        actions.Controls.AddRange([cancel, confirm]);
        dialog.Controls.AddRange([actions, input]);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        confirm.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (name.Length == 0 || NameExists(name, exclude))
            {
                MessageBox.Show(dialog, "Enter a unique plan name.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            dialog.DialogResult = DialogResult.OK;
        };
        input.SelectAll();
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }
}
