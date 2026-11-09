using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CraftingCalculator;

internal sealed partial class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(26, 44, 56);
    private static readonly Color Muted = Color.FromArgb(75, 94, 102);
    private static readonly Color Teal = Color.FromArgb(20, 98, 91);
    private static readonly Color Canvas = Color.FromArgb(239, 244, 243);
    private const string SourceUrl = "https://archeagecentral.com/crafting";
    private const string BatchRule = "Unlisted ingredients use their lowest-level recipe.";

    private readonly CraftingPlanner planner;
    private readonly TabControl professions = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<TabPage, RecipePicker> pickers = [];
    private readonly Dictionary<string, CraftTarget> targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListView craftList = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, View = View.Details,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, FullRowSelect = true, HideSelection = false,
        MultiSelect = false, AccessibleName = "Craft list", ShowItemToolTips = true
    };
    private readonly Label emptyList = new()
    {
        Text = "Add items from any profession to plan them together.",
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 5, 0, 0),
        ForeColor = Muted
    };
    private readonly Button editTarget = new() { Text = "Edit selected", AutoSize = true, Enabled = false };
    private readonly Button removeTarget = new() { Text = "Remove", AutoSize = true, Enabled = false };
    private readonly Label summary = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        ForeColor = Teal, Font = new Font("Segoe UI", 11, FontStyle.Bold)
    };
    private readonly Button copy = new() { Text = "Copy plan", Dock = DockStyle.Right, Width = 104 };
    private readonly CraftStepTree steps = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowNodeToolTips = true,
        AccessibleName = "Crafting steps", HideSelection = false, FullRowSelect = true, ItemHeight = 23
    };
    private readonly ListView materials = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, View = View.Details,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, FullRowSelect = true, GridLines = true,
        MultiSelect = false, CheckBoxes = true, AccessibleName = "Total materials to obtain", ShowItemToolTips = true
    };
    private readonly Label emptyMaterials = new()
    {
        Text = "No materials left to obtain.", Dock = DockStyle.Fill, Visible = false, ForeColor = Muted
    };
    private CraftingPlan? currentPlan;
    private CraftingProgress? currentProgress;

    public MainForm(RecipeCatalog catalog, PlanStore? planStore = null)
    {
        this.catalog = catalog;
        this.planStore = planStore;
        planBook = LoadPlans();
        planner = new CraftingPlanner(catalog);
        Text = "ArcheAge Chronicles Crafting";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Canvas;
        ForeColor = Ink;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 800);
        MinimumSize = new Size(840, 730);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.Controls.Add(CreateHeader(), 0, 0);

        var views = new TabControl
        {
            Dock = DockStyle.Fill, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(154, 38),
            DrawMode = TabDrawMode.OwnerDrawFixed, AccessibleName = "Crafting and trading"
        };
        var craftingTab = new TabPage("Crafting") { BackColor = Canvas };
        var tradingTab = new TabPage("Trading") { BackColor = Canvas };
        views.TabPages.AddRange([craftingTab, tradingTab]);
        views.DrawItem += (_, e) =>
        {
            var selected = e.Index == views.SelectedIndex;
            using var background = new SolidBrush(selected ? Teal : Canvas);
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, views.TabPages[e.Index].Text, views.Font, e.Bounds,
                selected ? Color.White : Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (views.Focused && selected)
                e.DrawFocusRectangle();
        };
        var tradingLoaded = false;
        views.SelectedIndexChanged += (_, _) =>
        {
            if (views.SelectedTab != tradingTab || tradingLoaded)
                return;

            tradingLoaded = true;
            tradingTab.Controls.Add(CreateTradeWorkspace());
        };
        layout.Controls.Add(views, 0, 1);

        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20, 10, 20, 4), Margin = Padding.Empty,
            ColumnCount = 1, RowCount = 5
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        craftingTab.Controls.Add(workspace);
        workspace.Controls.Add(CreatePlanBar(), 0, 0);

        foreach (var group in catalog.Recipes.GroupBy(recipe => recipe.Profession).OrderBy(group => group.Key))
        {
            var tab = new TabPage(group.Key) { BackColor = Color.White, Padding = new Padding(10, 8, 10, 6) };
            var picker = new RecipePicker(group);
            tab.Controls.Add(picker);
            pickers.Add(tab, picker);
            professions.TabPages.Add(tab);
        }
        professions.AccessibleName = "Crafting professions";
        workspace.Controls.Add(professions, 0, 1);
        var firstTab = pickers.Keys.FirstOrDefault(page => page.Text == "Engineering") ?? professions.TabPages[0];
        pickers[firstTab].SelectItem("Flugel's Refined Glider");
        professions.SelectedTab = firstTab;
        if (pickers[firstTab].SelectedRecipe is { } example)
            examplePreview = new CraftTarget(example, 1);

        var summaryBar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
        summaryBar.Controls.Add(summary);
        summaryBar.Controls.Add(new Label
        {
            Text = "Double-click for sources", ForeColor = Muted,
            Dock = DockStyle.Right, Width = 264, TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 12, 0)
        });
        summaryBar.Controls.Add(copy);
        copy.Click += (_, _) => CopyPlan();
        workspace.Controls.Add(summaryBar, 0, 2);

        var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        results.Controls.Add(CreateSection("Craft in this order", steps), 0, 0);
        var lists = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        lists.Controls.Add(CreateSection("Craft list", CreateCraftList()), 0, 0);
        lists.Controls.Add(CreateSection("Materials to obtain", CreateMaterialChecklist()), 0, 1);
        results.Controls.Add(lists, 1, 0);
        workspace.Controls.Add(results, 0, 3);

        steps.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenMaterialSource(e.Node?.Tag);
        };
        steps.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && steps.SelectedNode?.Tag is string item)
            {
                e.SuppressKeyPress = true;
                OpenMaterialSource(item);
            }
        };

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        var source = new LinkLabel { Text = "October 2026 demo recipe data", AutoSize = true, LinkColor = Teal };
        source.LinkClicked += (_, _) => OpenWebPage(SourceUrl);
        footer.Controls.Add(source);
        footer.Controls.Add(new Label { Text = $"·  {BatchRule}", ForeColor = Muted, AutoSize = true });
        workspace.Controls.Add(footer, 0, 4);

        foreach (var picker in pickers.Values)
        {
            picker.Changed += (_, _) => PickerChanged();
            picker.ListRequested += (_, _) => AddOrUpdateTarget(picker);
        }
        professions.SelectedIndexChanged += (_, _) => PickerChanged();
        steps.CompletionChanged += (_, _) =>
        {
            RefreshProgress(keepPosition: true);
            SaveActivePlan();
        };
        FormClosing += (_, _) => SaveActivePlan();
        ActivatePlan(ActivePlan);
    }

    private Control CreateTradeWorkspace()
    {
        try
        {
            var trades = TradeCatalog.Load(Path.Combine(AppContext.BaseDirectory, "trade-routes.json"));
            return new TradeWorkspace(trades, OpenWebPage, Ink, Muted, Teal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or InvalidDataException)
        {
            return new Label
            {
                Dock = DockStyle.Fill, Padding = new Padding(20),
                Text = $"Could not load trade-routes.json.\n\n{ex.Message}"
            };
        }
    }

    private static Control CreateHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Margin = Padding.Empty };
        header.Controls.Add(new Label
        {
            Text = "ARCHEAGE CHRONICLES", Location = new Point(23, 12), AutoSize = true,
            ForeColor = Color.FromArgb(167, 220, 210), Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        header.Controls.Add(new Label
        {
            Text = "Crafting Calculator", Location = new Point(20, 29), AutoSize = true,
            ForeColor = Color.White, Font = new Font("Segoe UI", 24, FontStyle.Bold)
        });
        return header;
    }

    private static Control CreateSection(string title, Control content)
    {
        var section = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10, 4, 10, 8) };
        var heading = new Label
        {
            Text = title, Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        section.Controls.Add(content);
        section.Controls.Add(heading);
        return section;
    }

    private Control CreateCraftList()
    {
        SetQuantityColumns(craftList, "Item");
        craftList.Visible = false;
        craftList.SelectedIndexChanged += (_, _) =>
            editTarget.Enabled = removeTarget.Enabled = craftList.SelectedItems.Count == 1;
        craftList.DoubleClick += (_, _) => EditSelectedTarget();
        editTarget.Click += (_, _) => EditSelectedTarget();
        removeTarget.Click += (_, _) =>
        {
            if (SelectedTarget is not { } target)
                return;
            targets.Remove(target.Recipe.Item);
            RefreshCraftList();
            SaveActivePlan();
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 34, WrapContents = false, Margin = Padding.Empty
        };
        actions.Controls.Add(editTarget);
        actions.Controls.Add(removeTarget);
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(craftList);
        panel.Controls.Add(emptyList);
        panel.Controls.Add(actions);
        return panel;
    }

    private static void SetQuantityColumns(ListView list, string label)
    {
        list.Columns.Add(label, 180);
        var quantity = list.Columns.Add("Quantity", 82, HorizontalAlignment.Right);
        list.SizeChanged += (_, _) =>
            list.Columns[0].Width = Math.Max(100, list.Width - quantity.Width - SystemInformation.VerticalScrollBarWidth - 4);
    }

    private RecipePicker ActivePicker => pickers[professions.SelectedTab!];

    private CraftTarget? SelectedTarget => craftList.SelectedItems.Count == 1
        ? (CraftTarget?)craftList.SelectedItems[0].Tag : null;

    private void PickerChanged()
    {
        if (loadingPlan)
            return;

        UpdateListAction();
        if (targets.Count == 0)
            RefreshPlan();
    }

    private void UpdateListAction()
    {
        if (professions.SelectedTab is null)
            return;
        var picker = ActivePicker;
        var recipe = picker.SelectedRecipe;
        picker.SetListAction(recipe is not null, recipe is not null && targets.ContainsKey(recipe.Item));
    }

    private void AddOrUpdateTarget(RecipePicker picker)
    {
        if (picker.SelectedRecipe is not { } recipe)
            return;
        targets[recipe.Item] = new CraftTarget(recipe, picker.Quantity);
        RefreshCraftList(recipe.Item);
        SaveActivePlan();
    }

    private void EditSelectedTarget()
    {
        if (SelectedTarget is not { } target)
            return;
        professions.SelectedTab = pickers.Keys.First(page => page.Text == target.Recipe.Profession);
        ActivePicker.EditTarget(target);
    }

    private void RefreshCraftList(string? selectItem = null)
    {
        craftList.BeginUpdate();
        try
        {
            emptyList.Visible = targets.Count == 0;
            craftList.Visible = targets.Count > 0;
            craftList.Items.Clear();
            foreach (var target in targets.Values)
            {
                var recipe = target.Recipe;
                var row = new ListViewItem([recipe.Item, target.Quantity.ToString("N0")])
                {
                    Tag = target,
                    ToolTipText = $"{recipe.Item} · {recipe.Profession} Lv {recipe.Level} · {recipe.Output} per craft"
                };
                craftList.Items.Add(row);
                if (string.Equals(recipe.Item, selectItem, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    row.EnsureVisible();
                }
            }
        }
        finally
        {
            craftList.EndUpdate();
        }

        UpdateListAction();
        RefreshPlan();
    }

    private void RefreshPlan()
    {
        try
        {
            currentPlan = null;
            copy.Enabled = false;
            CraftingPlan plan;
            string output;
            if (targets.Count > 0)
            {
                plan = planner.Calculate(targets.Values, inventory);
                output = targets.Count == 1 ? "1 item in list" : $"{targets.Count:N0} items in list";
            }
            else if (professions.SelectedTab is not null && ActivePicker.SelectedRecipe is { } recipe)
            {
                plan = planner.Calculate(recipe, ActivePicker.Quantity, inventory);
                output = $"{plan.Steps[^1].Produced:N0} made";
            }
            else
            {
                summary.Text = "No matching recipes. Try a different search.";
                return;
            }

            summary.Text = $"{output}  ·  {plan.Steps.Count:N0} steps  ·  {plan.Gold:N0} gold total";

            currentPlan = plan;
            copy.Enabled = true;
        }
        catch (InvalidDataException ex)
        {
            summary.Text = ex.Message;
        }
        catch (OverflowException)
        {
            summary.Text = "The requested quantities are too large. Try a smaller amount.";
        }
        finally
        {
            RefreshProgress();
        }
    }

    private void RefreshProgress(bool keepPosition = false)
    {
        currentProgress = currentPlan?.AfterCrafts(steps.Completed);
        steps.SetSteps(currentProgress, CreateStepNode, keepPosition);
        RefreshMaterials();
    }

    private static TreeNode CreateStepNode(CraftStep step, int index)
    {
        var title = StepTitle(step, index);
        var node = new TreeNode(title) { ToolTipText = $"{title}\nClick the checkbox or press Space to toggle completion." };
        node.Nodes.Add(StepDetails(step)).ForeColor = Muted;
        if (StepOutputNote(step) is { } note)
            node.Nodes.Add(note).ForeColor = Teal;
        foreach (var (item, amount) in step.Recipe.Materials)
        {
            node.Nodes.Add(new TreeNode($"{item}  ×{step.Batches * amount:N0}")
            {
                Tag = item, ForeColor = Teal, ToolTipText = MaterialToolTip(item)
            });
        }
        return node;
    }

    private static string MaterialToolTip(string item) => $"{item}\nDouble-click or press Enter to find sources.";

    private void OpenMaterialSource(object? tag)
    {
        if (tag is string item)
            OpenWebPage(MaterialSources.GetUrl(item));
    }

    private void OpenWebPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, $"Windows couldn't open the link.\n\n{url}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static string StepTitle(CraftStep step, int index) =>
        $"{index + 1}. {step.Recipe.Item}  ×{step.Produced:N0}";

    private static string StepDetails(CraftStep step) =>
        $"{step.Recipe.Profession} Lv {step.Recipe.Level}  ·  {step.Batches:N0} craft(s)  ·  {step.Gold:N0} gold";

    private static string? StepOutputNote(CraftStep step)
    {
        var extra = step.Produced == step.Needed ? null : $"{step.Produced - step.Needed:N0} extra";
        if (step.Requested == 0 || step.Requested == step.Needed)
            return extra;

        var keep = $"Keep {step.Requested:N0} for your list";
        return extra is null ? keep : $"{keep}  ·  {extra}";
    }

    private void CopyPlan()
    {
        if (currentPlan is not { } plan || currentProgress is not { } progress)
            return;

        var text = new StringBuilder().AppendLine("ArcheAge Chronicles Crafting")
            .AppendLine(ActivePlan.Name).AppendLine(summary.Text).AppendLine().AppendLine("Items requested");
        foreach (var target in plan.Targets)
            text.AppendLine($"  {target.Recipe.Item} ×{target.Quantity:N0} ({target.Recipe.Profession} Lv {target.Recipe.Level})");

        text.AppendLine().AppendLine("Materials to obtain");
        foreach (var (item, amount) in remainingMaterials.OrderBy(material => material.Key))
            text.AppendLine($"  {item} ×{amount:N0}");

        if (plan.UsedInventory.Count > 0)
        {
            text.AppendLine().AppendLine("Use from inventory");
            foreach (var (item, amount) in plan.UsedInventory.OrderBy(material => material.Key))
                text.AppendLine($"  {item} ×{amount:N0}");
        }

        text.AppendLine().AppendLine("Remaining crafting steps");
        foreach (var pending in progress.Pending)
        {
            var step = pending.Step;
            text.AppendLine(StepTitle(step, pending.Order)).AppendLine($"  {StepDetails(step)}");
            if (StepOutputNote(step) is { } note)
                text.AppendLine($"  {note}");
            foreach (var (item, amount) in step.Recipe.Materials)
                text.AppendLine($"  {item} ×{step.Batches * amount:N0}");
        }

        text.AppendLine().AppendLine(BatchRule).AppendLine($"October 2026 demo recipe data · {SourceUrl}");
        try
        {
            Clipboard.SetText(text.ToString());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "The clipboard is busy. Try copying again.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

internal sealed class RecipePicker : TableLayoutPanel
{
    private readonly TextBox search = new() { PlaceholderText = "Filter items…" };
    private readonly ComboBox items = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 16 };
    private readonly ComboBox variants = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown quantity = new() { Minimum = 1, Maximum = CraftTarget.MaximumQuantity, ThousandsSeparator = true, Value = 1 };
    private readonly Button listAction = new() { Text = "Add to list", Dock = DockStyle.Top, Height = 30 };
    private readonly RecipeGroup[] groups;
    private bool loading;

    public RecipePicker(IEnumerable<Recipe> recipes)
    {
        groups = recipes.GroupBy(recipe => recipe.Item, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RecipeGroup(group.Key, group.OrderBy(recipe => recipe.Level)
                .ThenBy(recipe => recipe.Output).Select(recipe => new Variant(recipe)).ToArray()))
            .OrderBy(group => group.Item).ToArray();

        Dock = DockStyle.Fill;
        ColumnCount = 5;
        RowCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField("Search", search, 0);
        AddField("Item to craft", items, 1);
        AddField("Recipe variant", variants, 2);
        AddField("Units wanted", quantity, 3);
        Controls.Add(listAction, 4, 1);

        listAction.Click += (_, _) => ListRequested?.Invoke(this, EventArgs.Empty);
        search.TextChanged += (_, _) => FilterItems();
        items.SelectedIndexChanged += (_, _) =>
        {
            if (!loading)
                LoadVariants();
        };
        variants.SelectedIndexChanged += (_, _) =>
        {
            if (!loading)
                Changed?.Invoke(this, EventArgs.Empty);
        };
        quantity.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        FilterItems();
    }

    public event EventHandler? Changed;
    public event EventHandler? ListRequested;
    public Recipe? SelectedRecipe => (variants.SelectedItem as Variant)?.Recipe;
    public long Quantity => (long)quantity.Value;

    public void SelectItem(string name)
    {
        var group = groups.FirstOrDefault(group => group.Item == name);
        if (group is not null)
            items.SelectedItem = group;
    }

    public void SetListAction(bool enabled, bool isListed)
    {
        listAction.Enabled = enabled;
        listAction.Text = isListed ? "Update item" : "Add to list";
    }

    public void EditTarget(CraftTarget target, bool focusQuantity = true)
    {
        search.Clear();
        SelectItem(target.Recipe.Item);
        variants.SelectedItem = variants.Items.OfType<Variant>().First(variant => variant.Recipe == target.Recipe);
        quantity.Value = target.Quantity;
        if (focusQuantity)
        {
            quantity.Focus();
            quantity.Select(0, quantity.Text.Length);
        }
    }

    private void AddField(string label, Control control, int column)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 1, 3, 0) }, column, 0);
        control.Dock = DockStyle.Top;
        control.AccessibleName = label;
        Controls.Add(control, column, 1);
    }

    private void FilterItems()
    {
        var selected = items.SelectedItem as RecipeGroup;
        var query = search.Text.Trim();
        var matches = groups.Where(group => group.Item.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        loading = true;
        items.Items.Clear();
        items.Items.AddRange(matches);
        items.Enabled = matches.Length > 0;
        items.SelectedItem = matches.Contains(selected) ? selected : matches.FirstOrDefault();
        loading = false;
        LoadVariants();
    }

    private void LoadVariants()
    {
        var selected = variants.SelectedItem as Variant;
        loading = true;
        variants.Items.Clear();
        if (items.SelectedItem is RecipeGroup group)
        {
            variants.Items.AddRange(group.Variants);
            variants.SelectedItem = group.Variants.Contains(selected) ? selected : group.Variants[0];
        }
        variants.Enabled = variants.Items.Count > 1;
        loading = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record RecipeGroup(string Item, Variant[] Variants)
    {
        public override string ToString() => Item;
    }

    private sealed record Variant(Recipe Recipe)
    {
        public override string ToString() => $"Lv {Recipe.Level}  ·  ×{Recipe.Output}";
    }
}
