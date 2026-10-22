using Godot;

namespace OneBigPackage;

/// <summary>Controller navigation for the desktop file dialog's embedded control scene.</summary>
public partial class ControllerPopupInput : Node
{
    private static readonly (string Action, Key Key)[] Navigation = [("ui_accept", Key.Enter), ("ui_cancel", Key.Escape),
        ("ui_up", Key.Up), ("ui_down", Key.Down), ("ui_left", Key.Left), ("ui_right", Key.Right),
        ("ui_focus_next", Key.Tab), ("ui_focus_prev", Key.Tab)];
    private static readonly StringName[] PadActions = Navigation.Select(binding => ControllerBindings.PadAction(binding.Action)).ToArray();
    private readonly Dictionary<string, ulong> _held = [];
    private Window? _activePopup;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < Navigation.Length; i++)
            if (Input.IsActionPressed(PadActions[i])) _held[Navigation[i].Action] = ulong.MaxValue;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not (InputEventJoypadButton or InputEventJoypadMotion) || GetParent() is not Window { Visible: true } owner) return;
        var popup = GetTree().Root.GetEmbeddedSubwindows().LastOrDefault(window => window.Visible);
        if (popup is null || popup is PopupMenu || popup != owner && !owner.IsAncestorOf(popup)) return;
        foreach (var binding in Navigation)
            if (@event.IsActionPressed(binding.Action) || @event.IsActionReleased(binding.Action))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
    }

    public override void _Process(double delta)
    {
        if (GetParent() is not Window owner || !owner.Visible) { _held.Clear(); _activePopup = null; return; }
        var popup = GetTree().Root.GetEmbeddedSubwindows().LastOrDefault(window => window.Visible);
        if (popup is null || popup is PopupMenu || popup != owner && !owner.IsAncestorOf(popup)) return;
        if (popup != _activePopup)
        {
            _activePopup = popup; _held.Clear();
            for (int i = 0; i < Navigation.Length; i++)
                if (Input.IsActionPressed(PadActions[i])) _held[Navigation[i].Action] = ulong.MaxValue;
            return;
        }
        ulong now = Time.GetTicksMsec();
        for (int i = 0; i < Navigation.Length; i++)
        {
            var binding = Navigation[i];
            // One controller press has one result, independent of native dialog
            // focus heuristics. Keyboard/mouse input keeps its ordinary path.
            bool pressed = Input.IsActionPressed(PadActions[i]);
            if (!pressed) { _held.Remove(binding.Action); continue; }
            if (_held.TryGetValue(binding.Action, out var next) && (next == ulong.MaxValue || now < next)) continue;
            bool first = !_held.ContainsKey(binding.Action);
            _held[binding.Action] = binding.Key is Key.Enter or Key.Escape ? ulong.MaxValue : now + (first ? 350UL : 75UL);
            if (binding.Key == Key.Escape && popup is AcceptDialog dialog)
            {
                dialog.Hide();
                dialog.EmitSignal(AcceptDialog.SignalName.Canceled);
                return;
            }
            Dispatch(popup, binding.Action, binding.Key);
            if (!owner.Visible || owner.IsQueuedForDeletion()) return;
        }
    }

    private static void Dispatch(Window popup, string action, Key key)
    {
        var focus = popup.GuiGetFocusOwner();
        if (focus is null) return;
        if (key == Key.Tab)
        {
            (action == "ui_focus_prev" ? focus.FindPrevValidFocus() : focus.FindNextValidFocus())?.GrabFocus();
            return;
        }
        if (focus is ItemList list && list.ItemCount > 0)
        {
            var selected = list.GetSelectedItems();
            int index = selected.Length > 0 ? selected[0] : -1;
            if (key == Key.Enter && index >= 0)
                list.EmitSignal(ItemList.SignalName.ItemActivated, index);
            else if (key is Key.Down or Key.Up or Key.Left or Key.Right)
            {
                int step = key is Key.Up or Key.Left ? -1 : 1;
                index = Math.Clamp(index + step, 0, list.ItemCount - 1);
                list.Select(index); list.EnsureCurrentIsVisible();
                list.EmitSignal(ItemList.SignalName.ItemSelected, index);
            }
            return;
        }
        if (focus is BaseButton { Disabled: false } button && key == Key.Enter)
        {
            if (button is OptionButton options) options.ShowPopup();
            else
            {
                if (button.ToggleMode)
                {
                    button.SetPressedNoSignal(!button.ButtonPressed);
                    button.EmitSignal(BaseButton.SignalName.Toggled, button.ButtonPressed);
                }
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
            return;
        }
        if (focus is LineEdit line && key == Key.Enter) { line.EmitSignal(LineEdit.SignalName.TextSubmitted, line.Text); return; }
        Side? side = key switch { Key.Up => Side.Top, Key.Down => Side.Bottom, Key.Left => Side.Left, Key.Right => Side.Right, _ => null };
        if (side is { } direction) { focus.FindValidFocusNeighbor(direction)?.GrabFocus(); return; }
        popup.PushInput(new InputEventKey { Keycode = key, Pressed = true });
        popup.PushInput(new InputEventKey { Keycode = key, Pressed = false });
    }
}
