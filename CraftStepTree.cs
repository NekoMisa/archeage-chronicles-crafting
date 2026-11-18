using System.Windows.Forms.VisualStyles;

namespace CraftingCalculator;

internal sealed record StepIngredient(ProgressStep Progress, string Item, long Quantity, bool Ready);

internal sealed class CraftStepTree : TreeView
{
    private const int UncheckedImage = 0;
    private const int CheckedImage = 1;

    private readonly Dictionary<Recipe, CompletedBatch> completed = [];
    private readonly TreeNode doneGroup = new() { ToolTipText = "Expand to review or uncheck completed steps." };
    private readonly ImageList checkboxes;

    public CraftStepTree()
    {
        checkboxes = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        using var uncheckedImage = DrawCheckbox(CheckBoxState.UncheckedNormal);
        using var checkedImage = DrawCheckbox(CheckBoxState.CheckedNormal);
        checkboxes.Images.Add(uncheckedImage);
        checkboxes.Images.Add(checkedImage);
        _ = checkboxes.Handle;
        StateImageList = checkboxes;
    }

    public event EventHandler? CompletionChanged;
    public event Action<StepIngredient>? IngredientToggled;
    public IEnumerable<CompletedBatch> Completed => completed.Values;

    public void RestoreCompleted(IEnumerable<CompletedBatch> batches)
    {
        completed.Clear();
        foreach (var batch in batches)
            completed[batch.Recipe] = batch;
        doneGroup.Collapse();
    }

    public void SetSteps(CraftingProgress? progress, Func<ProgressStep, bool, TreeNode> createNode, bool keepPosition)
    {
        var expandDone = doneGroup.TreeView is not null && doneGroup.IsExpanded;
        var ingredient = keepPosition ? SelectedNode?.Tag as StepIngredient : null;
        var selected = keepPosition ? (SelectedNode?.Tag as StepEntry ?? SelectedNode?.Parent?.Tag as StepEntry) : null;
        var expanded = keepPosition
            ? Nodes.Cast<TreeNode>().Concat(doneGroup.Nodes.Cast<TreeNode>())
                .Where(node => node.Tag is StepEntry)
                .ToDictionary(node => (((StepEntry)node.Tag!).Progress.Order, ((StepEntry)node.Tag!).Done), node => node.IsExpanded)
            : [];

        BeginUpdate();
        try
        {
            Nodes.Clear();
            doneGroup.Nodes.Clear();
            completed.Clear();
            if (progress is not null)
            {
                foreach (var pending in progress.Pending)
                    AddNode(Nodes, pending, isDone: false);
                foreach (var done in progress.Done)
                {
                    completed[done.Step.Recipe] = new CompletedBatch(done.Step.Recipe, done.Step.Batches, done.PlanBatches);
                    AddNode(doneGroup.Nodes, done, isDone: true);
                }
            }
            UpdateDoneGroup(expandDone);
            if (selected is not null)
            {
                var order = selected.Progress.Order;
                SelectedNode = (selected.Done && expandDone ? ClosestNode(doneGroup.Nodes, order) : null)
                    ?? ClosestNode(Nodes, order) ?? Nodes.Cast<TreeNode>().FirstOrDefault();
                if (ingredient is not null && SelectedNode?.Tag is StepEntry { Done: false } parent
                    && parent.Progress.Order == order)
                {
                    SelectedNode = SelectedNode.Nodes.Cast<TreeNode>()
                        .FirstOrDefault(node => node.Tag is StepIngredient child && child.Item == ingredient.Item) ?? SelectedNode;
                }
            }
            else if (Nodes.Count > 0)
                TopNode = Nodes[0];
        }
        finally
        {
            EndUpdate();
        }

        void AddNode(TreeNodeCollection parent, ProgressStep item, bool isDone)
        {
            var node = createNode(item, isDone);
            node.Tag = new StepEntry(item, isDone);
            node.StateImageIndex = isDone ? CheckedImage : UncheckedImage;
            foreach (TreeNode child in node.Nodes)
            {
                if (child.Tag is StepIngredient ingredient)
                    child.StateImageIndex = ingredient.Ready ? CheckedImage : UncheckedImage;
            }
            parent.Add(node);
            if (expanded.GetValueOrDefault((item.Order, isDone), !isDone))
                node.Expand();
        }
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseClick(e);
        if (e.Button == MouseButtons.Left && e.Node is { } node
            && (HitTest(e.Location).Location & TreeViewHitTestLocations.StateImage) != 0)
        {
            ToggleNode(node);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && SelectedNode is { } node)
            e.SuppressKeyPress = ToggleNode(node);
        base.OnKeyDown(e);
    }

    private bool ToggleNode(TreeNode node)
    {
        switch (node.Tag)
        {
            case StepEntry entry:
                ToggleStep(node, entry);
                return true;
            case StepIngredient ingredient:
                SelectedNode = node;
                IngredientToggled?.Invoke(ingredient);
                return true;
            default:
                return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            checkboxes.Dispose();
        base.Dispose(disposing);
    }

    private void ToggleStep(TreeNode node, StepEntry entry)
    {
        var progress = entry.Progress;
        var recipe = progress.Step.Recipe;
        if (entry.Done)
            completed.Remove(recipe);
        else
        {
            var batches = completed.GetValueOrDefault(recipe)?.Batches ?? 0;
            completed[recipe] = new CompletedBatch(recipe, checked(batches + progress.Step.Batches), progress.PlanBatches);
        }
        SelectedNode = node;
        CompletionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDoneGroup(bool expand)
    {
        if (doneGroup.Nodes.Count == 0)
        {
            doneGroup.Remove();
            return;
        }

        doneGroup.Text = $"Done ({doneGroup.Nodes.Count:N0})";
        if (doneGroup.TreeView is null)
            Nodes.Add(doneGroup);
        if (expand)
            doneGroup.Expand();
        else
            doneGroup.Collapse();
    }

    private static TreeNode? ClosestNode(TreeNodeCollection nodes, int order) =>
        nodes.Cast<TreeNode>().FirstOrDefault(node => node.Tag is StepEntry entry && entry.Progress.Order >= order)
        ?? nodes.Cast<TreeNode>().LastOrDefault(node => node.Tag is StepEntry);

    private static Bitmap DrawCheckbox(CheckBoxState state)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        var glyph = CheckBoxRenderer.GetGlyphSize(graphics, state);
        CheckBoxRenderer.DrawCheckBox(graphics, new Point((bitmap.Width - glyph.Width) / 2,
            (bitmap.Height - glyph.Height) / 2), state);
        return bitmap;
    }

    private sealed record StepEntry(ProgressStep Progress, bool Done);
}
