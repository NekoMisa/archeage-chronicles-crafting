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

            while (!form.IsDisposed && Application.OpenForms.Cast<Form>().Any(window => window.Modal))
                await Task.Delay(250);

            if (form.IsDisposed || !form.SaveActivePlan())
                return;

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

    private static void RestoreCraftList(string resumePath, MainForm form, RecipeCatalog catalog)
    {
        if (!File.Exists(resumePath))
            return;

        try
        {
            var snapshot = JsonSerializer.Deserialize<SavedTarget[]>(File.ReadAllText(resumePath), JsonSerializerOptions.Web) ?? [];
            var restored = snapshot.Select(saved => saved?.Resolve(catalog)).OfType<CraftTarget>().ToArray();
            if (form.RestoreCraftList(restored))
                File.Delete(resumePath);

            if (restored.Length < snapshot.Length)
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
}
