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

    public static RecipeCatalog Load(string path) =>
        new(JsonSerializer.Deserialize<Recipe[]>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Expected a list of recipes."));
}

internal sealed record CraftTarget(Recipe Recipe, long Quantity);

internal sealed record CraftStep(Recipe Recipe, long Batches, long Needed, long Requested)
{
    public long Produced => Batches * Recipe.Output;
    public long Gold => Batches * Recipe.Gold;
}

internal sealed record CraftingPlan(IReadOnlyList<CraftTarget> Targets, IReadOnlyList<CraftStep> Steps,
    IReadOnlyDictionary<string, long> Materials)
{
    public long Gold => Steps.Sum(step => step.Gold);
}

internal sealed class CraftingPlanner(RecipeCatalog catalog)
{
    public CraftingPlan Calculate(Recipe target, long quantity) => Calculate([new CraftTarget(target, quantity)]);

    public CraftingPlan Calculate(IEnumerable<CraftTarget> targets)
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
        var steps = new List<CraftStep>();

        // Process consumers before their ingredients so shared demand is summed before rounding.
        for (var index = craftOrder.Count - 1; index >= 0; index--)
        {
            var recipe = craftOrder[index];
            var needed = demand[recipe.Item];
            demand.Remove(recipe.Item);
            var batches = Math.DivRem(needed, recipe.Output, out var remainder);
            if (remainder > 0)
                batches++;

            checked
            {
                _ = batches * recipe.Output;
                _ = batches * recipe.Gold;
                foreach (var (item, amount) in recipe.Materials)
                    demand[item] = demand.GetValueOrDefault(item) + batches * amount;
            }

            steps.Add(new CraftStep(recipe, batches, needed, requested.GetValueOrDefault(recipe.Item)?.Quantity ?? 0));
        }

        steps.Reverse();
        return new CraftingPlan(requested.Values.ToArray(), steps, demand);

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
}
