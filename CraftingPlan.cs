using System.Text.Json;

namespace CraftingCalculator;

internal sealed record Recipe(string Item, string Profession, int Level, int Output,
    int Gold, Dictionary<string, int> Materials);

internal sealed class RecipeCatalog
{
    private readonly Dictionary<string, Recipe[]> byItem;

    public RecipeCatalog(Recipe[] recipes)
    {
        if (recipes.Length == 0)
            throw new InvalidDataException("The recipe catalog is empty.");

        foreach (var recipe in recipes)
        {
            if (recipe is null || string.IsNullOrWhiteSpace(recipe.Item)
                || string.IsNullOrWhiteSpace(recipe.Profession) || recipe.Level < 1
                || recipe.Output < 1 || recipe.Gold < 0 || recipe.Materials is null
                || recipe.Materials.Count == 0
                || recipe.Materials.Any(material => string.IsNullOrWhiteSpace(material.Key) || material.Value < 1))
            {
                throw new InvalidDataException("Every recipe needs an item, profession, level, output, cost and positive ingredient amounts.");
            }
        }

        Recipes = recipes;
        byItem = recipes.GroupBy(recipe => recipe.Item, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(recipe => recipe.Level).ThenBy(recipe => recipe.Output).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Recipe> Recipes { get; }

    public Recipe? DefaultRecipe(string item) => byItem.GetValueOrDefault(item)?.FirstOrDefault();

    internal static string UpdatedItemName(string item) => item switch
    {
        "Garden's Mirage Shoes" => "Garden's Mirage Slippers",
        "Golden Tree Warden's Hat" => "Golden Tree Warden's Hood",
        "Rift Defender's Longbow" => "Rift Defender's Crossbow",
        "Bellflower Dewdrop" => "Evening Dewdrop",
        "Wind Blade Claw" => "Wind Blade Claws",
        _ => item
    };

    public Recipe? FindUnchangedRecipe(Recipe saved)
    {
        if (string.IsNullOrWhiteSpace(saved.Item) || saved.Materials is null)
            return null;

        var variants = byItem.GetValueOrDefault(saved.Item) ?? byItem.GetValueOrDefault(UpdatedItemName(saved.Item));
        return variants?.FirstOrDefault(recipe =>
            recipe.Profession == saved.Profession && recipe.Level == saved.Level && recipe.Output == saved.Output
            && recipe.Gold == saved.Gold && recipe.Materials.Count == saved.Materials.Count
            && recipe.Materials.All(material => saved.Materials.TryGetValue(material.Key, out var count)
                && count == material.Value));
    }

    public static RecipeCatalog Load(string path) =>
        new(JsonSerializer.Deserialize<Recipe[]>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Expected a list of recipes."));
}

internal sealed record CraftTarget(Recipe Recipe, long Quantity)
{
    public const long MaximumQuantity = 999_999;
}

internal sealed record CraftStep(Recipe Recipe, long Batches, long Needed, long Requested)
{
    public long Produced => Batches * Recipe.Output;
    public long Gold => Batches * Recipe.Gold;
}

internal sealed record ProgressStep(CraftStep Step, int Order, long PlanBatches);

internal sealed record CraftingProgress(IReadOnlyList<ProgressStep> Pending, IReadOnlyList<ProgressStep> Done,
    IReadOnlyDictionary<string, long> Materials, IReadOnlyDictionary<string, long> StepCredits);

internal sealed record CraftingPlan(IReadOnlyList<CraftTarget> Targets, IReadOnlyList<CraftStep> Steps,
    IReadOnlyDictionary<string, long> Materials, IReadOnlyDictionary<string, long> UsedInventory)
{
    public long Gold => Steps.Sum(step => step.Gold);

    public CraftingProgress AfterCrafts(IEnumerable<CompletedBatch> completed,
        IReadOnlyDictionary<(Recipe Recipe, string Item), StepMaterialMark>? stepMaterials = null)
    {
        var completion = completed.ToDictionary(batch => batch.Recipe);
        var covered = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (completion.Count == 0 && (stepMaterials is null || stepMaterials.Count == 0))
            return new CraftingProgress(Steps.Select((step, index) => new ProgressStep(step, index, step.Batches)).ToArray(),
                [], Materials, covered);

        var demand = Targets.ToDictionary(target => target.Recipe.Item, target => target.Quantity,
            StringComparer.OrdinalIgnoreCase);
        var pending = new List<ProgressStep>();
        var done = new List<ProgressStep>();
        for (var index = Steps.Count - 1; index >= 0; index--)
        {
            var step = Steps[index];
            var completedUnits = 0L;
            if (completion.TryGetValue(step.Recipe, out var marked) && marked.Batches > 0
                && marked.Batches <= step.Batches && (marked.PlanBatches ?? marked.Batches) == step.Batches)
            {
                completedUnits = checked(marked.Batches * step.Recipe.Output);
                done.Add(new ProgressStep(step with
                {
                    Batches = marked.Batches, Needed = Math.Min(completedUnits, step.Needed),
                    Requested = Math.Min(completedUnits, step.Requested)
                }, index, step.Batches));
            }

            if (!demand.Remove(step.Recipe.Item, out var needed))
                continue;

            // Preserve the original inventory allocation and count only new credit from ingredient checks.
            var fromCrafting = Math.Min(needed, step.Needed);
            var ready = Math.Max(0, covered.GetValueOrDefault(step.Recipe.Item) - (needed - fromCrafting));
            var remaining = Math.Max(step.Requested, fromCrafting - ready) - completedUnits;
            if (remaining <= 0)
                continue;

            var batches = CraftingPlanner.BatchesFor(remaining, step.Recipe.Output);
            pending.Add(new ProgressStep(step with
            {
                Batches = batches, Needed = remaining, Requested = Math.Max(0, step.Requested - completedUnits)
            }, index, step.Batches));
            foreach (var (item, amount) in step.Recipe.Materials)
            {
                var quantity = batches * amount;
                demand[item] = checked(demand.GetValueOrDefault(item) + quantity);
                if (stepMaterials?.GetValueOrDefault((step.Recipe, item)) is { } mark && mark.PlanBatches == step.Batches)
                    covered[item] = checked(covered.GetValueOrDefault(item) + Math.Min(quantity, mark.Quantity));
            }
        }

        var remainingMaterials = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var stepCredits = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, original) in Materials)
        {
            var total = demand.GetValueOrDefault(item);
            var needed = Math.Min(original, total);
            var ready = Math.Clamp(covered.GetValueOrDefault(item) - (total - needed), 0, needed);
            if (ready > 0)
                stepCredits[item] = ready;
            if (needed > ready)
                remainingMaterials[item] = needed - ready;
        }
        pending.Reverse();
        done.Reverse();
        return new CraftingProgress(pending, done, remainingMaterials, stepCredits);
    }
}

internal sealed class CraftingPlanner(RecipeCatalog catalog)
{
    public CraftingPlan Calculate(Recipe target, long quantity, IReadOnlyDictionary<string, long>? inventory = null) =>
        Calculate([new CraftTarget(target, quantity)], inventory);

    public CraftingPlan Calculate(IEnumerable<CraftTarget> targets, IReadOnlyDictionary<string, long>? inventory = null)
    {
        var requested = new Dictionary<string, CraftTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target.Quantity);
            var item = target.Recipe.Item;
            if (requested.TryGetValue(item, out var previous))
            {
                if (previous.Recipe != target.Recipe)
                    throw new InvalidDataException($"Choose one recipe variant for {item}.");

                requested[item] = previous with { Quantity = checked(previous.Quantity + target.Quantity) };
            }
            else
            {
                requested.Add(item, target);
            }
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var craftOrder = new List<Recipe>();
        foreach (var target in requested.Values)
            Visit(target.Recipe);

        var demand = requested.ToDictionary(entry => entry.Key, entry => entry.Value.Quantity,
            StringComparer.OrdinalIgnoreCase);
        var usedInventory = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<CraftStep>();

        // Process consumers before their ingredients so shared demand is summed before rounding.
        for (var index = craftOrder.Count - 1; index >= 0; index--)
        {
            var recipe = craftOrder[index];
            if (!demand.Remove(recipe.Item, out var needed))
                continue;

            var reserved = requested.GetValueOrDefault(recipe.Item)?.Quantity ?? 0;
            var neededFromCrafting = Math.Max(reserved, needed - OnHand(recipe.Item));
            var batches = BatchesFor(neededFromCrafting, recipe.Output);

            // A batch's surplus can cover ingredients before any stock is used.
            var fromStock = Math.Max(0, needed - checked(batches * recipe.Output));
            if (fromStock > 0)
                usedInventory.Add(recipe.Item, fromStock);
            if (batches == 0)
                continue;

            checked
            {
                _ = batches * recipe.Gold;
                foreach (var (item, amount) in recipe.Materials)
                    demand[item] = demand.GetValueOrDefault(item) + batches * amount;
            }

            steps.Add(new CraftStep(recipe, batches, needed - fromStock, reserved));
        }

        var remaining = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, needed) in demand)
        {
            var fromStock = Math.Min(needed, OnHand(item));
            if (fromStock > 0)
                usedInventory.Add(item, fromStock);
            if (needed > fromStock)
                remaining.Add(item, needed - fromStock);
        }

        steps.Reverse();
        return new CraftingPlan(requested.Values.ToArray(), steps, remaining, usedInventory);

        long OnHand(string item) => Math.Max(0, inventory?.GetValueOrDefault(item) ?? 0);

        void Visit(Recipe recipe)
        {
            if (visited.Contains(recipe.Item))
                return;
            if (!visiting.Add(recipe.Item))
                throw new InvalidDataException($"Circular ingredient dependency at {recipe.Item}.");

            foreach (var item in recipe.Materials.Keys)
            {
                var ingredientRecipe = requested.GetValueOrDefault(item)?.Recipe ?? catalog.DefaultRecipe(item);
                if (ingredientRecipe is not null)
                    Visit(ingredientRecipe);
            }

            visiting.Remove(recipe.Item);
            visited.Add(recipe.Item);
            craftOrder.Add(recipe);
        }
    }

    internal static long BatchesFor(long quantity, int output)
    {
        var batches = Math.DivRem(quantity, output, out var remainder);
        return remainder == 0 ? batches : batches + 1;
    }
}
