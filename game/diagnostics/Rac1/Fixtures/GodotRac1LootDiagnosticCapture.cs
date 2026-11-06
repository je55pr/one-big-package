using Godot;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1LootDiagnosticCapture(Node owner, Func<int> level, Func<CanvasLayer> ui,
    Func<PlayerHost?> player, IDiagnosticFrameClock clock) : IRac1LootDiagnosticCapture
{
    public async Task CaptureAsync(string name, NVector3 focus, CancellationToken token)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_CRATE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        if (DisplayServer.GetName() == "headless") { GD.Print("[crate-capture] unavailable: optional image capture requires a rendered post-draw signal."); return; }
        Directory.CreateDirectory(directory);
        var actor = player() ?? throw new DiagnosticUnavailableException("The loot capture player is unavailable.");
        var overlay = ui(); var hud = actor.GetNode<CanvasLayer>("PlayerHud");
        bool visible = overlay.Visible, hudVisible = hud.Visible;
        var previous = owner.GetViewport().GetCamera3D();
        var camera = new Camera3D { Current = true, Fov = 55, Far = 10000 }; owner.AddChild(camera);
        try
        {
            var point = GodotMovementPlayer.Engine(focus);
            overlay.Visible = false; hud.Visible = false;
            // Optional OBP_CRATE_CAPTURE_OFFSETS="x,y,z;x,y,z" writes one image per camera offset (default 2.5,1.5,3.5).
            var offsets = DiagnosticCaptureOffsets.Read("OBP_CRATE_CAPTURE_OFFSETS", "2.5,1.5,3.5");
            for (int index = 0; index < offsets.Length; index++)
            {
                camera.GlobalPosition = point + offsets[index]; camera.LookAt(point);
                await clock.NextAsync(DiagnosticFramePhase.Process, token); await clock.NextAsync(DiagnosticFramePhase.PostDraw, token);
                using var image = owner.GetViewport().GetTexture().GetImage();
                string file = offsets.Length == 1 ? $"level{level()}-{name}.png" : $"level{level()}-{name}-{index}.png";
                if (image.SavePng(Path.Combine(directory, file)) != Error.Ok) throw new IOException("Loot capture failed.");
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(overlay)) overlay.Visible = visible;
            if (GodotObject.IsInstanceValid(hud)) hud.Visible = hudVisible;
            if (GodotObject.IsInstanceValid(camera)) camera.QueueFree();
            if (GodotObject.IsInstanceValid(previous)) previous.MakeCurrent();
        }
    }
}
