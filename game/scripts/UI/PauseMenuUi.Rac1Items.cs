using Godot;
using OBP.RAC1.Presentation;

namespace OBP.Game;

public sealed partial class PauseMenuUi
{
    private bool BuildNativeItemsPage()
    {
        var native = Rac1RetailUiAssets.NativeMenuPages?.NativeItemsGrid;
        if (native is null) return false;
        // Unlike the source Weapons grid, Items has flag 1: the retail
        // updater explicitly disables Cross equipment changes. This is
        // a read-only display of item and alternate progression flags.
        var grid = new GridContainer
        {
            Name = "NativeItemsGrid",
            Columns = native.Columns,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(490, 330),
        };
        grid.AddThemeConstantOverride("h_separation", 5);
        grid.AddThemeConstantOverride("v_separation", 5);
        _retailColumn.AddChild(grid);
        for (int i = 0; i < native.Cells.Count; i++)
        {
            var source = native.Cells[i];
            bool available = native.IsAvailable(source, NativePrimaryItemAvailability,
                NativeAlternateItemAvailability);
            // Alternate IDs belong to a DIFFERENT retail flags table.
            // Some use index zero; do not use the primary inventory or
            // invent an entitlement from a named weapon with the same ID.
            string description = source.Kind == 0
                ? Items.FirstOrDefault(item => item.NativeId == source.NativeId)?.Name ??
                  $"Native item {source.NativeId}"
                : $"Alternate item {source.NativeId}";
            var cell = new Rac1RetailEquipmentCell
            {
                Name = $"NativeItemsCell_{i}",
                NativeAnimationId = source.Icon,
                NativeFrame = source.Frame,
                TooltipText = description,
                Owned = available,
                SuppressEquippedFrame = true,
                CustomMinimumSize = new Vector2(132, 98),
            };
            grid.AddChild(cell);
            _retailFocusOrder.Add(cell);
        }
        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
        return true;
    }
}
