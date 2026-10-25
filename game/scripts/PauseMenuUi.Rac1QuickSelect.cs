using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OBP.Game;

public sealed partial class PauseMenuUi
{
    private readonly List<Rac1RetailEquipmentCell[]> _nativeQuickSelectGroups = [];
    private Button[] _quickSelectSlotButtons = [];
    private int[] _quickSelectSlotIds = [];
    private int _quickSelectSlotCursor;
    private Control? _quickSelectKeepCandidateFocus;
    private int _quickSelectKeepCandidateFrames;
    private readonly List<(StringName Action, InputEvent Event)> _savedQuickShoulderBindings = [];

    private void InstallQuickSelectShoulderBindings()
    {
        if (_savedQuickShoulderBindings.Count != 0) return;
        // Godot maps L1/R1 to ui_focus_prev/next by default. While this
        // modal page owns them for the retail eight-slot cursor, suspend
        // only those two specific mappings, leaving keyboard Tab alone.
        foreach (string action in new[] { "ui_focus_next", "ui_focus_prev" })
        {
            foreach (var source in InputMap.ActionGetEvents(action).OfType<InputEventJoypadButton>())
            {
                if (source.ButtonIndex is not (JoyButton.LeftShoulder or JoyButton.RightShoulder))
                    continue;
                _savedQuickShoulderBindings.Add((action, source));
                InputMap.ActionEraseEvent(action, source);
            }
        }
    }

    private void RestoreQuickSelectShoulderBindings()
    {
        foreach (var (action, source) in _savedQuickShoulderBindings)
            InputMap.ActionAddEvent(action, source);
        _savedQuickShoulderBindings.Clear();
    }

    public override void _ExitTree() => RestoreQuickSelectShoulderBindings();

    private void HandleRetailQuickSelectInput(InputEvent @event)
    {
        if (!RetailEnabled || _retailNavigation.Page != Rac1RetailMenuPage.QuickSelect ||
            _retailNavigation.IsTransitioning ||
            @event is not InputEventJoypadButton { Pressed: true } joy)
            return;
        if (joy.ButtonIndex is JoyButton.DpadUp or JoyButton.DpadDown or
            JoyButton.DpadLeft or JoyButton.DpadRight)
        {
            _quickSelectKeepCandidateFrames = 0;
            return;
        }
        if (joy.ButtonIndex is not (JoyButton.LeftShoulder or JoyButton.RightShoulder))
            return;
        _quickSelectKeepCandidateFocus = GetViewport().GuiGetFocusOwner();
        _quickSelectKeepCandidateFrames = 10;
        GetViewport().SetInputAsHandled();
        ChangeQuickSelectSlot(joy.ButtonIndex == JoyButton.LeftShoulder ? -1 : 1);
    }

    private void RetainQuickSelectCandidateFocus()
    {
        if (_retailNavigation.Page != Rac1RetailMenuPage.QuickSelect ||
            _quickSelectKeepCandidateFrames <= 0) return;
        _quickSelectKeepCandidateFrames--;
        if (_quickSelectKeepCandidateFocus is { } focus && IsInstanceValid(focus) &&
            focus.IsInsideTree() && GetViewport().GuiGetFocusOwner() != focus)
            focus.GrabFocus();
    }

    private bool BuildNativeQuickSelectPage()
    {
        var native = Rac1RetailUiAssets.NativeMenuPages?.NativeQuickSelectPage;
        if (native is null) return false;
        _quickSelectSlotIds = new int[Rac1NativeInventoryLayout.QuickSelectCapacity];
        for (int i = 0; i < Math.Min(QuickSlots.Count, _quickSelectSlotIds.Length); i++)
            _quickSelectSlotIds[i] = QuickSlots[i].NativeId;
        _quickSelectSlotCursor = 0;
        _quickSelectKeepCandidateFrames = 0;
        _quickSelectKeepCandidateFocus = null;

        // The source slot painter uses a wheel preview. Until its Moby
        // projection is recovered, display eight explicit host controls,
        // separate from the source-backed inventory candidate grids.
        var slotStrip = new HBoxContainer
        {
            Name = "QuickSelectSlotStrip",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        slotStrip.AddThemeConstantOverride("separation", 3);
        _retailColumn.AddChild(slotStrip);
        _quickSelectSlotButtons = new Button[_quickSelectSlotIds.Length];
        for (int slot = 0; slot < _quickSelectSlotButtons.Length; slot++)
        {
            int capturedSlot = slot;
            var button = new Button
            {
                Name = $"QuickSlot_{slot}",
                CustomMinimumSize = new Vector2(76, 36),
                FocusMode = Control.FocusModeEnum.None,
            };
            button.Pressed += () =>
            {
                _quickSelectSlotCursor = capturedSlot;
                UpdateQuickSelectSlots();
            };
            slotStrip.AddChild(button);
            _quickSelectSlotButtons[slot] = button;
        }
        UpdateQuickSelectSlots();

        foreach (var group in native.Groups)
        {
            var grid = new GridContainer
            {
                Name = $"NativeQuickCandidates_{group.PanelSlot}",
                Columns = group.Columns,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(group.Columns * 86, 0),
            };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 2);
            _retailColumn.AddChild(grid);
            var cells = new Rac1RetailEquipmentCell[group.Cells.Count];
            for (int index = 0; index < cells.Length; index++)
            {
                var source = group.Cells[index];
                bool isWeapon = group.PanelSlot == native.Groups[0].PanelSlot;
                var item = (isWeapon ? Weapons : Gadgets).FirstOrDefault(
                    entry => entry.NativeId == source.NativeId);
                item ??= new RetailItem(source.NativeId, $"Native item {source.NativeId}",
                    false, false, source.Icon);
                var localItem = item;
                var cell = new Rac1RetailEquipmentCell
                {
                    Name = $"QuickCandidate_{source.NativeId}",
                    TooltipText = localItem.Name,
                    NativeAnimationId = source.Icon,
                    Owned = localItem.Owned,
                    SuppressEquippedFrame = (group.Flags & 0x20) != 0,
                    CustomMinimumSize = new Vector2(82, 49),
                };
                cell.Pressed += () =>
                {
                    if (!localItem.Owned ||
                        QuickSelectAssignRequested?.Invoke(_quickSelectSlotCursor, source.NativeId) != true)
                    {
                        Rac1RetailMenuAudio.Play(2);
                        return;
                    }
                    for (int slot = 0; slot < _quickSelectSlotIds.Length; slot++)
                        if (_quickSelectSlotIds[slot] == source.NativeId)
                            _quickSelectSlotIds[slot] = 0;
                    _quickSelectSlotIds[_quickSelectSlotCursor] = source.NativeId;
                    _quickSelectSlotCursor = (_quickSelectSlotCursor + 1) %
                        Rac1NativeInventoryLayout.QuickSelectCapacity;
                    UpdateQuickSelectSlots();
                    Rac1RetailMenuAudio.Play(1);
                };
                grid.AddChild(cell);
                _retailFocusOrder.Add(cell);
                cells[index] = cell;
            }
            _nativeQuickSelectGroups.Add(cells);
        }

        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
        InstallQuickSelectShoulderBindings();
        return true;
    }

    private void UpdateQuickSelectSlots()
    {
        for (int slot = 0; slot < _quickSelectSlotButtons.Length; slot++)
        {
            int nativeId = _quickSelectSlotIds[slot];
            var button = _quickSelectSlotButtons[slot];
            button.Text = $"{slot + 1}: {(nativeId == 0 ? "-" : nativeId.ToString())}";
            button.TooltipText = nativeId == 0 ? "Empty" :
                Weapons.Concat(Gadgets).FirstOrDefault(item => item.NativeId == nativeId)?.Name ??
                $"Native item {nativeId}";
            button.AddThemeColorOverride("font_color", slot == _quickSelectSlotCursor
                ? Rac1RetailUiSkin.NativeSelected : Rac1RetailUiSkin.NativeNormal);
        }
    }

    private void ChangeQuickSelectSlot(int direction)
    {
        _quickSelectSlotCursor = (_quickSelectSlotCursor + direction +
            Rac1NativeInventoryLayout.QuickSelectCapacity) %
            Rac1NativeInventoryLayout.QuickSelectCapacity;
        UpdateQuickSelectSlots();
        Rac1RetailMenuAudio.Play(0);
    }

    /// <summary>
    /// Source grid updater shifts column by +/- 1 when crossing 5x3,
    /// and uses the destination last row on Up or first row on Down.
    /// Candidate navigation does not alter the slot cursor.
    /// </summary>
    private void WireNativeQuickSelectFocus()
    {
        var source = Rac1RetailUiAssets.NativeMenuPages!.NativeQuickSelectPage.Groups;
        for (int g = 0; g < _nativeQuickSelectGroups.Count; g++)
        {
            var current = _nativeQuickSelectGroups[g];
            var next = _nativeQuickSelectGroups[1 - g];
            int columns = source[g].Columns;
            int nextColumns = source[1 - g].Columns;
            for (int i = 0; i < current.Length; i++)
            {
                int row = i / columns, column = i % columns;
                int start = row * columns, end = Math.Min(start + columns, current.Length);
                int shifted = Math.Clamp(column + (columns == 3 ? 1 : -1), 0,
                    nextColumns - 1);
                int up = row > 0 ? i - columns : -1;
                int down = i + columns < current.Length ? i + columns : -1;
                var cell = current[i];
                cell.FocusNeighborLeft = cell.GetPathTo(current[i == start ? end - 1 : i - 1]);
                cell.FocusNeighborRight = cell.GetPathTo(current[i + 1 == end ? start : i + 1]);
                cell.FocusNeighborTop = cell.GetPathTo(up >= 0 ? current[up] :
                    next[next.Length - nextColumns + shifted]);
                cell.FocusNeighborBottom = cell.GetPathTo(down >= 0 ? current[down] :
                    next[shifted]);
            }
        }
    }
}
