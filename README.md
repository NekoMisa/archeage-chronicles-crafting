# ArcheAge Chronicles Crafting Calculator

A Windows Forms calculator for planning crafts from raw materials through the finished items.

## Install

Download the **Setup.exe** asset from the [latest GitHub release](https://github.com/NekoMisa/archeage-chronicles-crafting/releases/latest) and run it on 64-bit Windows. The installer includes the .NET runtime.

The installed app checks for a newer release at startup. When a download finishes, the app saves your plan, closes, installs the update, and reopens the saved plan.

[Privacy policy](PRIVACY.md)

## Run from source

With the .NET 10 SDK installed, run this command from the project folder.

```powershell
dotnet run --project CraftingCalculator.csproj
```

Select a profession, search for an item, and enter how many finished units you want. Choose a recipe variant when the item has more than one. Use **Add to list** to queue it, then repeat for items in any profession. The results show a preview of the selected item while the list is empty.

Use **Edit selected** to load an item from the list into the picker. Change its quantity or variant and choose **Update item** to apply the change. **Remove** deletes the selected item from the plan.

The guide combines shared ingredients across the list before rounding up to whole batches. It orders intermediate crafts before the steps that consume them and tells you how many to keep when an intermediate is also on your list. **Copy plan** copies the requested items, the combined shopping list, the ordered steps, and the total gold cost.

Check off a crafting step to move it into **Done**. Expand **Done** and uncheck a step to put it back in order. You can also select a step and press **Space**. Checkmarks reset for any step whose recipe or batch count changes.

## Saved plans and inventory

Use **New…** to start a separate named plan and the **Plan** dropdown to switch between plans. The app saves each plan's craft list, checkmarks, and inventory locally and reopens the last active plan.

Open **Inventory…** and enter how many materials or intermediate items you have on hand. The guide uses those counts before calculating extra ingredient crafts and shows the remaining shopping list. Requested items are still made in full. **Copy plan** lists the stock used alongside the materials you still need.

Checking off a craft updates **Materials to obtain** for the unfinished work. Check a material there to mark the displayed quantity as gathered. Open **Gathered** and uncheck it to undo. Gathered marks save with the plan and reset for an item when an edit changes its original shopping quantity.

Double-click a material in the shopping list or under a crafting step to open its source page in your browser. You can also select the material and press **Enter**. The linked pages show gathering locations, vendors, enemy drops, or recipes depending on the item. Materials added to your catalog without a direct link open a search for the item name.

## Recipe data

`recipes.json` contains 238 recipe entries for 196 items across nine professions. The catalog comes from the October 2026 Next Fest demo, so quantities and ingredient names can differ in Early Access. [ArcheAge Central demo catalog](https://archeagecentral.com/crafting)

Each listed item uses its selected recipe variant for every batch, including batches needed for other crafts. For intermediate items absent from the list, the planner uses the lowest-level recipe in the catalog, breaking level ties by smaller batch size. Ingredients with no catalog recipe go on the shopping list.

Each recipe records the output item, profession, required level, units per craft, gold per craft, and ingredient amounts per craft. The app reads the copy of `recipes.json` beside the executable at startup. Edit that copy to update a built app, or edit the source copy and rebuild.

For a catalog correction, record the in-game build, item and profession level, batch output, gold, and ingredient quantities from the crafting screen. A screenshot or a source that names the game build helps verify each change before publishing a new snapshot. [Report a recipe correction](https://github.com/NekoMisa/archeage-chronicles-crafting/issues/new)

## Publish a release

Push a new version tag such as `v0.2.0`. The Windows release workflow builds the installer and publishes it with the update packages and feed. Keep the package ID and update channel consistent across releases so installed copies can upgrade.
