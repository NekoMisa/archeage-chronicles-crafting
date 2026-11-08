using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace CraftingCalculator;

internal static class AppUpdates
{
    private static readonly string ResumePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArcheAgeChroniclesCrafting", "update-craft-list.json");

    public static void Enable(MainForm form, RecipeCatalog catalog)
    {
        var repository = typeof(AppUpdates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "ReleaseRepository").Value;
        var manager = new UpdateManager(new GithubSource($"https://github.com/{repository}", null, false));
        if (!manager.IsInstalled)
            return;

        RestoreCraftList(ResumePath, form, catalog);
        form.Shown += async (_, _) => await DownloadAndInstallAsync(form, manager);
    }

    private static async Task DownloadAndInstallAsync(MainForm form, UpdateManager manager)
    {
        var title = form.Text;
        try
        {
            var package = manager.UpdatePendingRestart;
            if (package is null)
            {
                var update = await manager.CheckForUpdatesAsync();
                if (update is null || form.IsDisposed)
                    return;

                form.Text = $"{title} — downloading update";
                await manager.DownloadUpdatesAsync(update);
                package = update.TargetFullRelease;
            }

            if (form.IsDisposed)
                return;

            SaveCraftList(ResumePath, form.QueuedTargets);
            form.Text = $"{title} — installing update";
            manager.ApplyUpdatesAndRestart(package);
        }
        catch (Exception ex)
        {
            // An offline check or a failed update must leave the calculator usable.
            Trace.WriteLine($"App update failed: {ex.Message}");
        }
        finally
        {
            if (!form.IsDisposed)
                form.Text = title;
        }
    }

    private static void SaveCraftList(string resumePath, IEnumerable<CraftTarget> targets)
    {
        var snapshot = targets.Select(target => new SavedTarget(target.Recipe.Item, target.Recipe.Profession,
            target.Recipe.Level, target.Recipe.Output, target.Quantity));
        Directory.CreateDirectory(Path.GetDirectoryName(resumePath)!);
        var pendingPath = resumePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(pendingPath, JsonSerializer.Serialize(snapshot));
            File.Move(pendingPath, resumePath, overwrite: true);
        }
        finally
        {
            File.Delete(pendingPath);
        }
    }

    private static void RestoreCraftList(string resumePath, MainForm form, RecipeCatalog catalog)
    {
        if (!File.Exists(resumePath))
            return;

        try
        {
            var snapshot = JsonSerializer.Deserialize<SavedTarget[]>(File.ReadAllText(resumePath)) ?? [];
            var restored = new List<CraftTarget>();
            foreach (var saved in snapshot)
            {
                if (saved is null || string.IsNullOrWhiteSpace(saved.Item)
                    || saved.Quantity is < 1 or > RecipePicker.MaximumQuantity)
                    continue;

                var recipe = catalog.Recipes.FirstOrDefault(candidate =>
                    candidate.Item == saved.Item && candidate.Profession == saved.Profession
                    && candidate.Level == saved.Level && candidate.Output == saved.Output)
                    ?? catalog.DefaultRecipe(saved.Item);
                if (recipe is not null)
                    restored.Add(new CraftTarget(recipe, saved.Quantity));
            }
            form.RestoreCraftList(restored);
            File.Delete(resumePath);

            if (restored.Count < snapshot.Length)
            {
                form.Shown += (_, _) => MessageBox.Show(form,
                    "Some items from your craft list are unavailable in this version.",
                    form.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.WriteLine($"Could not restore the craft list after updating: {ex.Message}");
        }
    }

    private sealed record SavedTarget(string Item, string Profession, int Level, int Output, long Quantity);
}
