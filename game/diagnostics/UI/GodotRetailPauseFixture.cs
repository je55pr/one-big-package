using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Trusted engine fixture. Scenarios receive immutable panel/focus/pixel facts and exact logical-input actions.</summary>
internal sealed class GodotRetailPauseFixture(Node owner, Func<PlayerHost?> player, Func<PauseMenuUi?> pause,
    Func<bool> isPauseOpen, Func<bool> open, Func<bool> close, Func<Rac1WeaponInventory> weapons,
    IDiagnosticWorldObservations worlds) : IDiagnosticRetailPauseFixture
{
    private Rac1PauseSmokeInputGuard? _inputGuard;
    public DiagnosticRetailPauseSnapshot Read()
    {
        var menu = pause();
        var actor = player();
        var inventory = weapons();
        var viewport = owner.GetViewport();
        var size = viewport.GetVisibleRect().Size;
        var focus = viewport.GuiGetFocusOwner();
        var panels = menu?.GetNodeOrNull<Control>("NativeProjectedRootPanels")?.GetChildren().OfType<Rac1RetailMenuButton>().ToArray() ?? [];
        return new(Rac1RetailUiAssets.IsLoaded, worlds.ReadWorld().Content?.SourceGame == ObpSourceGame.Rac1,
            actor is not null && GodotObject.IsInstanceValid(actor), isPauseOpen(), owner.GetTree().Paused,
            menu?.RetailEnabled == true, actor is null ? default : new(actor.GlobalPosition.X, actor.GlobalPosition.Y, actor.GlobalPosition.Z),
            inventory.CurrentItemId, [.. inventory.QuickSelect], [.. inventory.Ammo],
            [.. panels.Select(button => new DiagnosticRetailPanel(button.GetPath().ToString(), button.Visible, button.RetailPauseRoot,
                button.GetChildren().OfType<Rac1RetailPanelSurface>().Count(), Bounds(button)))],
            focus?.GetPath().ToString(), focus is Button button ? button.Text : null,
            menu is null ? [] : [.. Controls(menu).OfType<Rac1RetailEquipmentCell>()
                .Where(cell => cell.Name.ToString().StartsWith("NativeItemsCell_", StringComparison.Ordinal)).Select(cell => cell.GetPath().ToString())],
            menu?.RetailPage, ApplicationPointer.MouseMode.ToString(), UiPointerModality.ControllerWasLastInput,
            DisplayServer.GetName() == "headless", size.X, size.Y);
    }
    public bool Open() => open();
    public bool Close() => close();
    public IDisposable BeginInputReview()
    {
        if (_inputGuard is not null) throw new InvalidOperationException("A pause review already owns synthetic input.");
        var root = owner.GetTree().Root;
        var previousSize = root.Size;
        var previousPointer = ApplicationPointer.MouseMode;
        var guard = new Rac1PauseSmokeInputGuard();
        root.Size = new Vector2I(960, 720);
        root.AddChild(guard);
        _inputGuard = guard;
        return new ReviewLease(() =>
        {
            try { if (isPauseOpen()) close(); }
            finally
            {
                _inputGuard = null;
                root.RemoveChild(guard); guard.Free();
                root.Size = previousSize;
                ApplicationPointer.MouseMode = previousPointer;
            }
        });
    }
    public void InjectAction(string action, bool pressed, bool admitted)
    {
        using var input = new InputEventAction { Action = action, Pressed = pressed, Strength = pressed ? 1 : 0 };
        if (admitted) (_inputGuard ?? throw new InvalidOperationException("No pause input review lease is active.")).Admit(input);
        Input.ParseInputEvent(input);
    }
    public DiagnosticPanelCapture CapturePanels(string folder, string fileName)
    {
        if (DisplayServer.GetName() == "headless") throw new DiagnosticUnavailableException("Rendered pause evidence is unavailable headlessly.");
        var menu = pause() ?? throw new DiagnosticUnavailableException("The pause menu is missing.");
        var panels = menu.GetNodeOrNull<Control>("NativeProjectedRootPanels")?.GetChildren().OfType<Rac1RetailMenuButton>().ToArray() ?? [];
        using var image = owner.GetViewport().GetTexture().GetImage();
        var samples = panels.Select(button =>
        {
            var bounds = button.GetGlobalRect();
            var point = (Vector2I)(bounds.Position + bounds.Size * new Vector2(.10f, .48f));
            var pixel = point.X >= 0 && point.Y >= 0 && point.X < image.GetWidth() && point.Y < image.GetHeight()
                ? image.GetPixel(point.X, point.Y) : new Color(float.NaN, float.NaN, float.NaN);
            return new DiagnosticPanelPixel(button.Name, Bounds(button), point.X, point.Y, pixel.R, pixel.G, pixel.B);
        }).ToImmutableArray();
        string directory = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", folder));
        Directory.CreateDirectory(directory);
        if (image.SavePng(Path.Combine(directory, fileName)) != Error.Ok) throw new IOException("Could not capture the source-backed pause.");
        return new(image.GetWidth(), image.GetHeight(), samples);
    }
    private static DiagnosticRect Bounds(Control control)
    {
        var rect = control.GetGlobalRect();
        return new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
    }
    private static IEnumerable<Control> Controls(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control) yield return control;
            foreach (var nested in Controls(child)) yield return nested;
        }
    }
    private sealed class ReviewLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
