using Godot;

namespace OneBigPackage;

/// <summary>Stable analogue/D-pad menu repeat without changing gameplay stick response.</summary>
public partial class ControllerMenuNavigation : Node
{
    private static readonly (StringName Ui, StringName Pad)[] Directions =
        new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_focus_next", "ui_focus_prev" }
            .Select(action => ((StringName)action, ControllerBindings.PadAction(action))).ToArray();
    private readonly Dictionary<StringName, ulong> _held = [];
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    private bool Active => !ControllerBindings.MenuNavigationSuspended &&
        !GetTree().Root.GetEmbeddedSubwindows().Any(window => window.Visible) &&
        MenuFocus.Available(GetViewport().GuiGetFocusOwner()) && GetViewport().GuiGetFocusOwner()!.CanProcess();

    public override void _Input(InputEvent @event)
    {
        if (@event is not (InputEventJoypadMotion or InputEventJoypadButton)) return;
        if (ControllerBindings.MenuNavigationSuspended && @event is InputEventJoypadMotion)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!Active && !ControllerBindings.MenuNavigationSuspended) return;
        foreach (var direction in Directions)
            if (@event.IsActionPressed(direction.Ui) || @event.IsActionReleased(direction.Ui))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
    }

    public override void _Process(double delta)
    {
        if (!Active) { _held.Clear(); return; }
        ulong now = PlaytestClock.Milliseconds;
        foreach (var direction in Directions)
        {
            if (!Input.IsActionPressed(direction.Pad)) { _held.Remove(direction.Pad); continue; }
            bool first = !_held.TryGetValue(direction.Pad, out var next);
            if (!first && now < next) continue;
            _held[direction.Pad] = now + (first ? 350UL : 75UL);
            // Viewport dispatch avoids feeding synthetic UI presses back into
            // Input's device state or any of the raw movement actions.
            GetViewport().PushInput(new InputEventAction { Action = direction.Ui, Pressed = true });
            GetViewport().PushInput(new InputEventAction { Action = direction.Ui, Pressed = false });
        }
    }
}
