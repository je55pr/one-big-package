using Godot;

namespace OneBigPackage;

internal static class MenuFocus
{
    public static void WireVertical(IEnumerable<Control> controls)
    {
        var enabled = controls.Where(Available).ToArray();
        for (int i = 0; i < enabled.Length; i++)
        {
            var current = enabled[i];
            var previous = current.GetPathTo(enabled[(i + enabled.Length - 1) % enabled.Length]);
            var next = current.GetPathTo(enabled[(i + 1) % enabled.Length]);
            current.FocusMode = Control.FocusModeEnum.All;
            current.FocusNeighborTop = current.FocusPrevious = previous;
            current.FocusNeighborBottom = current.FocusNext = next;
        }
    }

    public static bool Available(Control? control) => control is not null && GodotObject.IsInstanceValid(control) &&
        !control.IsQueuedForDeletion() && control.IsVisibleInTree() && control.FocusMode != Control.FocusModeEnum.None &&
        (control is not BaseButton button || !button.Disabled) && (control is not Slider slider || slider.Editable);

    public static void Restore(Control? preferred, IEnumerable<Control> fallback)
    {
        var target = Available(preferred) ? preferred : fallback.FirstOrDefault(Available);
        if (target is not null) Callable.From(() => { if (Available(target)) target.GrabFocus(); }).CallDeferred();
    }
}
