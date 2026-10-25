using Godot;
using OBP.RAC1.Presentation;

namespace OBP.Game;

public sealed partial class PauseMenuUi
{
    /// <summary>
    /// The Veldin Gadgets page owns four linked grid screens (2x3, 1x3,
    /// 1x3, 1x2). Do not flatten them into the host's five-column Weapons
    /// grid. Runtime flag-4/flag-8 group suppression remains pending the
    /// corresponding native menu_system admission conditions.
    /// </summary>
    private bool BuildNativeGadgetsPage()
    {
        var native = Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid;
        if (native is null) return false;
        var byId = Gadgets.ToDictionary(item => item.NativeId);
        var detail = new Label
        {
            Name = "NativeGadgetsSelectionLabel",
            CustomMinimumSize = new Vector2(0, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        detail.AddThemeFontOverride("font", Rac1RetailUiSkin.Font("regular"));
        detail.AddThemeColorOverride("font_color", Rac1RetailUiSkin.NativeNormal);

        foreach (var group in native.Groups)
        {
            var rowGrid = new GridContainer
            {
                Name = $"NativeGadgetGroup_{group.PanelSlot}",
                Columns = group.Columns,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(360, 0),
            };
            rowGrid.AddThemeConstantOverride("h_separation", 6);
            rowGrid.AddThemeConstantOverride("v_separation", 2);
            _retailColumn.AddChild(rowGrid);

            var buttons = new Rac1RetailEquipmentCell[group.Cells.Count];
            for (int index = 0; index < group.Cells.Count; index++)
            {
                var source = group.Cells[index];
                bool known = byId.TryGetValue(source.NativeId, out var item);
                item ??= new RetailItem(source.NativeId, $"Native item {source.NativeId}",
                    false, false, source.Icon);
                var cell = new Rac1RetailEquipmentCell
                {
                    Name = $"RetailItem_{source.NativeId}",
                    TooltipText = item.Name,
                    NativeAnimationId = source.Icon,
                    Owned = known && item.Owned,
                    Equipped = known && item.Equipped,
                    SuppressEquippedFrame = (group.Flags & 0x20) != 0,
                    CustomMinimumSize = new Vector2(112, 58),
                };
                var currentItem = item;
                cell.FocusEntered += () =>
                {
                    detail.Text = currentItem.Name +
                        (currentItem.Equipped ? "  (ON)" :
                         !currentItem.Owned ? "  [LOCKED]" : "");
                    Rac1RetailMenuAudio.Play(0);
                };
                // Gadgets are inspected, not equipped by this host widget.
                // The gameplay equipment/session remains the only authority.
                cell.Pressed += () => Rac1RetailMenuAudio.Play(currentItem.Owned ? 1 : 2);
                rowGrid.AddChild(cell);
                _retailFocusOrder.Add(cell);
                buttons[index] = cell;
            }
            _nativeGadgetGroups.Add(buttons);
        }

        // Original page.focus = group at native screen 0x1B2BE8 (slot 3);
        // every source group's selected_cell starts at zero.
        if (_retailNavigation.SelectedIndex == 0)
            _retailNavigation.Focus(native.Groups[0].Cells.Count +
                native.Groups[1].Selected, _retailFocusOrder.Count);
        _retailColumn.AddChild(detail);
        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
        return true;
    }

    /// <summary>
    /// Source update_menu_grid_selection changes rows inside each screen
    /// and follows authored up/down screen pointers at the edge. The
    /// horizontal index is clamped when transferring into a narrower
    /// group. Flags 4/8 can skip a whole group on retail hardware; until
    /// their runtime admission bits are hosted, do not guess a skip.
    /// </summary>
    private void WireNativeGadgetsFocus()
    {
        int groupCount = _nativeGadgetGroups.Count;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var current = _nativeGadgetGroups[groupIndex];
            var above = _nativeGadgetGroups[(groupIndex + groupCount - 1) % groupCount];
            var below = _nativeGadgetGroups[(groupIndex + 1) % groupCount];
            int columns = Rac1RetailUiAssets.NativeMenuPages!.NativeGadgetsGrid.Groups[groupIndex].Columns;
            int aboveColumns = Rac1RetailUiAssets.NativeMenuPages.NativeGadgetsGrid.Groups[
                (groupIndex + groupCount - 1) % groupCount].Columns;
            for (int index = 0; index < current.Length; index++)
            {
                int row = index / columns;
                int column = index % columns;
                int rowStart = row * columns;
                int rowEnd = Math.Min(rowStart + columns, current.Length);
                int leftIndex = index == rowStart ? rowEnd - 1 : index - 1;
                int rightIndex = index + 1 == rowEnd ? rowStart : index + 1;
                var top = row > 0 ? current[index - columns] :
                    above[(above.Length - aboveColumns) + Math.Min(column, aboveColumns - 1)];
                var bottom = index + columns < current.Length ? current[index + columns] :
                    below[Math.Min(column, below.Length - 1)];
                var cell = current[index];
                cell.FocusNeighborLeft = cell.GetPathTo(current[leftIndex]);
                cell.FocusNeighborRight = cell.GetPathTo(current[rightIndex]);
                cell.FocusNeighborTop = cell.GetPathTo(top);
                cell.FocusNeighborBottom = cell.GetPathTo(bottom);
            }
        }
    }
}
