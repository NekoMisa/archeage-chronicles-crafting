using System.Windows.Forms.VisualStyles;

namespace CraftingCalculator;

internal sealed class CraftStepTree : TreeView
{
    private const int UncheckedImage = 0;
    private const int CheckedImage = 1;

    private readonly HashSet<StepKey> completed = [];
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

    public void SetSteps(IReadOnlyList<CraftStep> steps, Func<CraftStep, int, TreeNode> createNode)
    {
        var expandDone = doneGroup.TreeView is not null && doneGroup.IsExpanded;
        completed.IntersectWith(steps.Select(step => new StepKey(step.Recipe, step.Batches)));

        BeginUpdate();
        try
        {
            Nodes.Clear();
            doneGroup.Nodes.Clear();
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var key = new StepKey(step.Recipe, step.Batches);
                var node = createNode(step, index);
                node.Tag = new StepEntry(key, index);
                if (completed.Contains(key))
                {
                    node.StateImageIndex = CheckedImage;
                    doneGroup.Nodes.Add(node);
                }
                else
                {
                    node.StateImageIndex = UncheckedImage;
                    Nodes.Add(node);
                    node.Expand();
                }
            }
            UpdateDoneGroup(expandDone);
            if (Nodes.Count > 0)
                TopNode = Nodes[0];
        }
        finally
        {
            EndUpdate();
        }
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseClick(e);
        if (e.Button == MouseButtons.Left && e.Node?.Tag is StepEntry entry
            && (HitTest(e.Location).Location & TreeViewHitTestLocations.StateImage) != 0)
        {
            ToggleStep(e.Node, entry);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && SelectedNode is { Tag: StepEntry entry } node)
        {
            e.SuppressKeyPress = true;
            ToggleStep(node, entry);
        }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            checkboxes.Dispose();
        base.Dispose(disposing);
    }

    private void ToggleStep(TreeNode node, StepEntry entry)
    {
        var wasDone = completed.Remove(entry.Key);
        if (!wasDone)
            completed.Add(entry.Key);

        var expandDone = doneGroup.TreeView is not null && doneGroup.IsExpanded;
        var neighbor = node.NextNode ?? node.PrevNode;
        BeginUpdate();
        try
        {
            node.Remove();
            node.StateImageIndex = wasDone ? UncheckedImage : CheckedImage;
            InsertInOrder(wasDone ? Nodes : doneGroup.Nodes, node, entry.Order);
            if (wasDone)
                node.Expand();
            else
                node.Collapse();

            UpdateDoneGroup(expandDone);
            SelectedNode = neighbor ?? (wasDone ? node : doneGroup);
        }
        finally
        {
            EndUpdate();
        }
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

    private static void InsertInOrder(TreeNodeCollection siblings, TreeNode node, int order)
    {
        var index = 0;
        while (index < siblings.Count && siblings[index].Tag is StepEntry entry && entry.Order < order)
            index++;
        siblings.Insert(index, node);
    }

    private static Bitmap DrawCheckbox(CheckBoxState state)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        var glyph = CheckBoxRenderer.GetGlyphSize(graphics, state);
        CheckBoxRenderer.DrawCheckBox(graphics, new Point((bitmap.Width - glyph.Width) / 2,
            (bitmap.Height - glyph.Height) / 2), state);
        return bitmap;
    }

    private readonly record struct StepKey(Recipe Recipe, long Batches);
    private sealed record StepEntry(StepKey Key, int Order);
}
