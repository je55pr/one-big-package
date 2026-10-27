using Godot;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotWeaponDiagnosticCapture(Node owner, Func<PlayerHost?> readPlayer,
    Func<Node3D?> readWorld, Func<CanvasLayer> readOverlay, IDiagnosticFrameClock clock) : IWeaponDiagnosticCapture
{
    public async Task CaptureAsync(WeaponDiagnosticCaptureKind kind, string name, NVector3 focus, bool close, CancellationToken token)
    {
        string? directory = kind == WeaponDiagnosticCaptureKind.Wrench
            ? System.Environment.GetEnvironmentVariable("OBP_WRENCH_CAPTURE_DIR")
            : System.Environment.GetEnvironmentVariable("OBP_BOMB_CAPTURE_DIR") ?? System.Environment.GetEnvironmentVariable("OBP_WEAPON_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[weapon-capture] unavailable: optional image capture requires a rendered post-draw signal.");
            return;
        }
        var player = readPlayer() ?? throw new DiagnosticUnavailableException("The capture player is unavailable.");
        var world = readWorld() ?? throw new DiagnosticUnavailableException("The capture world is unavailable.");
        var overlay = readOverlay(); var hud = player.GetNode<CanvasLayer>("PlayerHud");
        Directory.CreateDirectory(directory);
        bool worldVisible = world.Visible, overlayVisible = overlay.Visible, hudVisible = hud.Visible;
        var previousCamera = owner.GetViewport().GetCamera3D();
        var camera = kind == WeaponDiagnosticCaptureKind.Wrench ? player.Camera : new Camera3D { Current = true, Fov = 50, Far = 10000 };
        if (kind != WeaponDiagnosticCaptureKind.Wrench) owner.AddChild(camera);
        var saved = camera.GlobalTransform;
        try
        {
            if (kind == WeaponDiagnosticCaptureKind.Wrench) await clock.NextAsync(DiagnosticFramePhase.Process, token);
            var point = GodotMovementPlayer.Engine(focus);
            camera.GlobalPosition = point + (kind == WeaponDiagnosticCaptureKind.Wrench ? new Vector3(1.8f, .7f, 2.1f)
                : close ? new Vector3(-1.8f, .7f, 2.1f) : new Vector3(4, 2.5f, 5));
            camera.LookAt(point); overlay.Visible = false;
            if (kind == WeaponDiagnosticCaptureKind.Wrench) world.Visible = false;
            else hud.Visible = false;
            await clock.NextAsync(DiagnosticFramePhase.PostDraw, token);
            using var image = owner.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(directory, name + ".png"));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(world)) world.Visible = worldVisible;
            if (GodotObject.IsInstanceValid(overlay)) overlay.Visible = overlayVisible;
            if (GodotObject.IsInstanceValid(hud)) hud.Visible = hudVisible;
            if (GodotObject.IsInstanceValid(camera))
            {
                if (kind == WeaponDiagnosticCaptureKind.Wrench) camera.GlobalTransform = saved;
                else camera.QueueFree();
            }
            if (kind != WeaponDiagnosticCaptureKind.Wrench && GodotObject.IsInstanceValid(previousCamera)) previousCamera.MakeCurrent();
        }
    }
}
