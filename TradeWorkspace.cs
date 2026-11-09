using System.Text;

namespace CraftingCalculator;

internal sealed class TradeWorkspace : UserControl
{
    private readonly TradeCatalog catalog;
    private readonly RadioButton get = new() { Text = "Get a material", AutoSize = true, Checked = true };
    private readonly RadioButton offer = new() { Text = "Trade what I have", AutoSize = true };
    private readonly ComboBox item = new() { AccessibleName = "Trade item", DropDownStyle = ComboBoxStyle.DropDown };
    private readonly NumericUpDown quantity = new()
    {
        AccessibleName = "Trade quantity", Minimum = 1, Maximum = 999_999, Value = 1, ThousandsSeparator = true
    };
    private readonly CheckBox chains = new() { Text = "Include two-trade routes", AutoSize = true, Checked = true };
    private readonly Label itemLabel = new() { AutoSize = true };
    private readonly Label quantityLabel = new() { AutoSize = true };
    private readonly Label summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ListView results = new()
    {
        AccessibleName = "Trade routes", Dock = DockStyle.Fill, View = View.Details, GridLines = true,
        FullRowSelect = true, MultiSelect = false, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        ShowItemToolTips = true
    };
    private readonly TextBox details = new()
    {
        AccessibleName = "Selected trade steps", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        BackColor = Color.White, ScrollBars = ScrollBars.Vertical
    };
    private readonly LinkLabel firstSource = new() { AutoSize = true, Visible = false };
    private readonly LinkLabel secondSource = new() { AutoSize = true, Text = "Second trade on RaidDex", Visible = false };
    private readonly Dictionary<bool, (string Item, decimal Quantity)> selections = [];
    private bool showingOffer;
    private bool changingMode;

    public TradeWorkspace(TradeCatalog catalog, Action<string> openWebPage, Color ink, Color muted, Color teal)
    {
        this.catalog = catalog;
        Dock = DockStyle.Fill;
        ForeColor = ink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(20, 12, 20, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(layout);

        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        get.Margin = new Padding(0, 4, 20, 0);
        offer.Margin = new Padding(0, 4, 0, 0);
        chains.Margin = new Padding(24, 4, 0, 0);
        modes.Controls.AddRange([get, offer, chains]);
        layout.Controls.Add(modes, 0, 0);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        fields.Controls.Add(CreateField(itemLabel, item), 0, 0);
        fields.Controls.Add(CreateField(quantityLabel, quantity), 1, 0);
        layout.Controls.Add(fields, 0, 1);

        summary.ForeColor = muted;
        layout.Controls.Add(summary, 0, 2);
        results.Columns.AddRange([
            new ColumnHeader { Text = "Receive" }, new ColumnHeader { Text = "Trade good" },
            new ColumnHeader { Text = "From" }, new ColumnHeader { Text = "To" },
            new ColumnHeader { Text = "Packs", TextAlign = HorizontalAlignment.Right },
            new ColumnHeader { Text = "Fee", TextAlign = HorizontalAlignment.Right }
        ]);
        results.Resize += (_, _) => ResizeColumns();
        layout.Controls.Add(results, 0, 3);
        layout.Controls.Add(new Label
        {
            Text = "Route steps", Dock = DockStyle.Fill, ForeColor = teal, TextAlign = ContentAlignment.BottomLeft
        }, 0, 4);
        layout.Controls.Add(details, 0, 5);

        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var source = new LinkLabel { AutoSize = true, Text = "RaidDex trade data" };
        foreach (var link in new[] { source, firstSource, secondSource })
        {
            link.LinkColor = teal;
            link.Margin = new Padding(3, 3, 15, 0);
            links.Controls.Add(link);
        }
        source.LinkClicked += (_, _) => openWebPage(TradeCatalog.SourceUrl);
        firstSource.LinkClicked += (_, _) =>
        {
            if (SelectedOption is { } option)
                openWebPage(TradeCatalog.PackUrl(option.First.Route.Pack));
        };
        secondSource.LinkClicked += (_, _) =>
        {
            if (SelectedOption?.Second is { } leg)
                openWebPage(TradeCatalog.PackUrl(leg.Route.Pack));
        };
        links.Controls.Add(new Label { AutoSize = true, ForeColor = muted, Text = "Early Access snapshot" });
        layout.Controls.Add(links, 0, 6);

        get.CheckedChanged += (_, _) => { if (get.Checked) SetMode(false); };
        offer.CheckedChanged += (_, _) => { if (offer.Checked) SetMode(true); };
        item.TextChanged += (_, _) => RefreshResults();
        quantity.ValueChanged += (_, _) => RefreshResults();
        chains.CheckedChanged += (_, _) => RefreshResults();
        results.SelectedIndexChanged += (_, _) => ShowDetails();
        SetMode(false);
    }

    private TradeOption? SelectedOption => results.SelectedItems.Count == 1 ? results.SelectedItems[0].Tag as TradeOption : null;

    private static Control CreateField(Label label, Control input)
    {
        var field = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        label.Dock = DockStyle.Top;
        input.Dock = DockStyle.Top;
        field.Controls.Add(input);
        field.Controls.Add(label);
        return field;
    }

    private void SetMode(bool isOffer)
    {
        selections[showingOffer] = (item.Text, quantity.Value);
        showingOffer = isOffer;
        var choices = isOffer ? catalog.Inputs : catalog.Rewards;
        var remembered = selections.GetValueOrDefault(isOffer);
        var initialItem = isOffer ? "Grain Liquor" : "Fine Lumber";
        var selected = choices.FirstOrDefault(name => string.Equals(name, remembered.Item, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(name => name == initialItem) ?? choices[0];

        changingMode = true;
        itemLabel.Text = isOffer ? "Item or packed good you have" : "Reward you want";
        quantityLabel.Text = isOffer ? "Quantity owned" : "Quantity needed";
        item.AutoCompleteMode = AutoCompleteMode.None;
        item.Items.Clear();
        item.Items.AddRange(choices);
        item.AutoCompleteSource = AutoCompleteSource.ListItems;
        item.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        item.SelectedItem = selected;
        quantity.Value = remembered.Quantity > 0 && !string.IsNullOrWhiteSpace(remembered.Item)
            ? remembered.Quantity : isOffer ? catalog.MinimumToTrade(selected) : 1;
        changingMode = false;
        RefreshResults();
    }

    private void RefreshResults()
    {
        if (changingMode)
            return;

        var choices = showingOffer ? catalog.Inputs : catalog.Rewards;
        var selected = choices.FirstOrDefault(name => string.Equals(name, item.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var count = decimal.ToInt64(quantity.Value);
        var options = selected is null ? [] : showingOffer
            ? catalog.Offer(selected, count, chains.Checked) : catalog.Get(selected, count, chains.Checked);

        results.BeginUpdate();
        try
        {
            results.Items.Clear();
            results.Items.AddRange(options.Select(CreateRow).ToArray());
            if (results.Items.Count > 0)
                results.Items[0].Selected = true;
        }
        finally
        {
            results.EndUpdate();
        }
        summary.Text = selected is null ? "Choose a matching item from the dropdown."
            : options.Length == 0 ? $"At least {catalog.MinimumToTrade(selected):N0} {selected} needed to start a trade."
            : $"{options.Length:N0} routes  ·  {(showingOffer ? "Grouped by reward, with gold ranked after packing fees" : "Direct routes first, then fewest packs and lowest fees")}";
        ShowDetails();
    }

    private static ListViewItem CreateRow(TradeOption option)
    {
        var first = option.First.Route;
        var second = option.Second?.Route;
        var pack = second is null ? first.Pack.Name : $"Two trades · {first.Pack.Name} → {second.Pack.Name}";
        var destination = (second ?? first).Payout.Destination;
        var fee = option.Fee is { } known ? $"{known:N0} G" : "Unknown";
        var reward = $"{option.Amount:N0} {option.Reward}";
        return new ListViewItem([reward, pack, first.Pack.Origin, destination,
            $"{option.Packs:N0}", fee])
        {
            Tag = option, ToolTipText = $"{reward}\n{pack}\n{first.Pack.Origin} → {destination}"
        };
    }

    private void ShowDetails()
    {
        var option = SelectedOption;
        firstSource.Visible = option is not null;
        secondSource.Visible = option?.Second is not null;
        if (option is null)
        {
            details.Clear();
            return;
        }

        firstSource.Text = option.Second is null ? "Trade good on RaidDex" : "First trade on RaidDex";
        var text = new StringBuilder();
        AppendLeg(text, option.First, 1);
        if (option.Second is { } second)
        {
            text.AppendLine();
            AppendLeg(text, second, 2, option.First.Route.Payout.Reward);
        }
        if (option.Reward == TradeCatalog.Gold && option.Fee is > 0)
            text.AppendLine($"Receive {option.Amount - option.Fee:N0} Gold after packing fees. Materials and certificate costs are separate.");

        details.Text = text.ToString().TrimEnd();
        details.SelectionStart = 0;
        details.ScrollToCaret();
    }

    private static void AppendLeg(StringBuilder text, TradeLeg leg, int number, string? intermediate = null)
    {
        var (pack, payout) = leg.Route;
        text.AppendLine($"{number}. {pack.Origin} → {payout.Destination}  ·  {pack.Company}");
        text.AppendLine($"{(leg.AlreadyPacked || pack.Ingredients.Count == 0 ? "Bring" : "Pack")} {leg.Packs:N0} × {pack.Name}");
        if (!leg.AlreadyPacked)
        {
            if (pack.Ingredients.Count == 0)
                text.AppendLine("RaidDex lists no packing recipe for this good.");
            else
                text.AppendLine(string.Join(", ", pack.Ingredients.Select(input =>
                    $"{input.Value * leg.Packs:N0} × {input.Key}{(string.Equals(input.Key, intermediate, StringComparison.OrdinalIgnoreCase) ? " from trade 1" : "")}")));
            if (leg.Fee is { } fee)
                text.AppendLine($"Packing fee · {fee:N0} Gold");
        }
        text.AppendLine($"Receive {leg.Amount:N0} × {payout.Reward}");
    }

    private void ResizeColumns()
    {
        var width = results.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
        if (width < 350)
            return;

        int[] widths = [21, 23, 19, 19, 8, 10];
        for (var index = 0; index < widths.Length; index++)
            results.Columns[index].Width = width * widths[index] / 100;
    }
}
