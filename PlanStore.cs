using System.Text.Json;

namespace CraftingCalculator;

internal sealed record SavedTarget(string Item, string Profession, int Level, int Output, long Quantity)
{
    public static SavedTarget From(CraftTarget target) =>
        new(target.Recipe.Item, target.Recipe.Profession, target.Recipe.Level, target.Recipe.Output, target.Quantity);

    public CraftTarget? Resolve(RecipeCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(Item) || Quantity is < 1 or > CraftTarget.MaximumQuantity)
            return null;

        var recipe = catalog.Recipes.FirstOrDefault(candidate =>
            candidate.Item == Item && candidate.Profession == Profession
            && candidate.Level == Level && candidate.Output == Output) ?? catalog.DefaultRecipe(Item);
        return recipe is null ? null : new CraftTarget(recipe, Quantity);
    }
}

internal sealed record CompletedBatch(Recipe Recipe, long Batches, long? PlanBatches = null);
internal sealed record GatheredMaterial(long PlanQuantity, long Quantity);

internal sealed class SavedPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "My plan";
    public SavedTarget[] Targets { get; set; } = [];
    public SavedTarget? Preview { get; set; }
    public Dictionary<string, long> Inventory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CompletedBatch[] Completed { get; set; } = [];
    public Dictionary<string, GatheredMaterial> Gathered { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PlanBook
{
    public int Format { get; init; } = 1;
    public Guid ActivePlanId { get; set; }
    public List<SavedPlan> Plans { get; init; } = [];

    public static PlanBook Create()
    {
        var plan = new SavedPlan();
        return new PlanBook { ActivePlanId = plan.Id, Plans = [plan] };
    }
}

internal sealed class PlanStore(string path)
{
    private static readonly JsonSerializerOptions FormatOptions = new(JsonSerializerOptions.Web) { WriteIndented = true };
    private string? lastRead;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcheAgeChroniclesCraftingData");

    public PlanBook Load()
    {
        var contents = ReadContents();
        if (contents is null)
        {
            lastRead = null;
            return PlanBook.Create();
        }

        var book = JsonSerializer.Deserialize<PlanBook>(contents, FormatOptions);
        if (book is null || book.Format != 1 || book.Plans is null || book.Plans.Count == 0
            || book.Plans.Any(plan => plan is null || plan.Id == Guid.Empty || string.IsNullOrWhiteSpace(plan.Name)
                || plan.Targets is null || plan.Inventory is null || plan.Completed is null || plan.Gathered is null)
            || book.Plans.Select(plan => plan.Id).Distinct().Count() != book.Plans.Count
            || book.Plans.Select(plan => plan.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != book.Plans.Count)
        {
            throw new InvalidDataException("The saved plans file has an unsupported format.");
        }

        if (!book.Plans.Any(plan => plan.Id == book.ActivePlanId))
            book.ActivePlanId = book.Plans[0].Id;
        lastRead = contents;
        return book;
    }

    public void Save(PlanBook book)
    {
        var contents = JsonSerializer.Serialize(book, FormatOptions);
        if (contents == lastRead)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writeLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (ReadContents() != lastRead)
            throw new IOException("Another window changed your saved plans. Reopen the calculator to load those changes.");

        var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(pending, contents);
            File.Move(pending, path, overwrite: true);
            lastRead = contents;
        }
        finally
        {
            File.Delete(pending);
        }
    }

    private string? ReadContents()
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
