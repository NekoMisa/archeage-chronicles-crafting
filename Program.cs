using Velopack;

namespace CraftingCalculator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        ApplicationConfiguration.Initialize();
        try
        {
            var catalog = RecipeCatalog.Load(Path.Combine(AppContext.BaseDirectory, "recipes.json"));
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            using var form = new MainForm(catalog) { Icon = icon };
            AppUpdates.Enable(form, catalog);
            Application.Run(form);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show($"Could not load recipes.json.\n\n{ex.Message}", "ArcheAge Chronicles Crafting",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
