using Godot;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>
    /// Exercise the actual Veldin gameplay pause path, not a synthetic menu
    /// fixture. The panel shader must remain opaque when it composites over
    /// imported level geometry, with six original projected menu slots.
    /// </summary>
    private async Task RunRac1VeldinRetailPauseSmokeAsync()
    {
        if (!Rac1RetailUiAssets.IsLoaded || _player is null)
            throw new InvalidOperationException("Veldin retail pause requires the attached R&C1 disc and live player.");

        Vector3 before = _player.GlobalPosition;
        if (!TryOpenPauseMenu() || _pauseMenu is not { RetailEnabled: true } || !GetTree().Paused)
            throw new InvalidOperationException("Ordinary Veldin gameplay did not open the source-backed pause menu.");

        try
        {
            for (int i = 0; i < 6; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var overlay = _pauseMenu.GetNodeOrNull<Control>("NativeProjectedRootPanels");
            var buttons = overlay?.GetChildren().OfType<Rac1RetailMenuButton>().ToArray() ?? [];
            if (buttons.Length != 6 || buttons.Any(button =>
                !button.RetailPauseRoot ||
                button.GetChildren().OfType<Rac1RetailPanelSurface>().Count() != 1))
                throw new InvalidOperationException("Real Veldin pause lost its six native projected and shaded panels.");
            if (GetViewport().GuiGetFocusOwner() != buttons[0])
                throw new InvalidOperationException("Real Veldin pause focused a stale menu item instead of Weapons.");

            if (DisplayServer.GetName() != "headless")
            {
                using var image = GetViewport().GetTexture().GetImage();
                foreach (var button in buttons)
                {
                    var rect = button.GetGlobalRect();
                    var point = (Vector2I)(rect.Position + rect.Size * new Vector2(.10f, .48f));
                    if (point.X < 0 || point.Y < 0 ||
                        point.X >= image.GetWidth() || point.Y >= image.GetHeight())
                        throw new InvalidOperationException($"Native Veldin panel {button.Name} projected outside the viewport.");
                    var pixel = image.GetPixel(point.X, point.Y);
                    if (pixel.R >= .26f || pixel.G >= .26f || pixel.B >= .30f)
                        throw new InvalidOperationException(
                            $"Native panel {button.Name} is translucent over Veldin world pixels: {pixel}.");
                }

                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    ProjectSettings.GlobalizePath("res://"), "..", "captures", "rac1-ui-veldin"));
                System.IO.Directory.CreateDirectory(path);
                if (image.SavePng(System.IO.Path.Combine(path, "root.png")) != Error.Ok)
                    throw new InvalidOperationException("Could not capture the source-backed pause over Veldin.");
            }

            if (_player.GlobalPosition.DistanceTo(before) > .001f)
                throw new InvalidOperationException("Real gameplay advanced player position during pause.");
            GD.Print("[rac1-veldin-pause] PASS: six source-projected opaque panels over live Veldin; opening focus and paused world retained");
        }
        finally
        {
            TryClosePauseMenu();
        }

        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (GetTree().Paused || PauseMenuOpen)
            throw new InvalidOperationException("Real Veldin gameplay did not resume after closing retail pause.");
    }
}
