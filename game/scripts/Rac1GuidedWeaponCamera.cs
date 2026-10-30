using Godot;
using OBP.Godot;

namespace OBP.Game;

/// <summary>
/// One guided-camera lease for an R&C1 world. The scene owns the camera node;
/// the host owns its focus and the borrowed player's remote-control flag.
/// Retiring an old lease must not steal focus from a successor world.
/// </summary>
internal sealed class Rac1GuidedWeaponCamera : IDisposable
{
    private Camera3D? _camera;
    private PlayerHost? _player;
    private bool _disposed;

    internal Camera3D? Camera => _camera is not null && GodotObject.IsInstanceValid(_camera) ? _camera : null;

    internal void Begin(Node3D sceneRoot, PlayerHost player)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        End();
        _player = player;
        _camera = new Camera3D { Name = "VisibombCamera", Fov = 65, Near = .05f };
        sceneRoot.AddChild(_camera);
        _camera.MakeCurrent();
        player.Rac1RemoteControlActive = true;
    }

    internal void Step(Vector3 position, Vector3 direction)
    {
        if (Camera is not { } camera) return;
        camera.GlobalPosition = position - direction * .7f + Vector3.Up * .15f;
        camera.LookAt(position + direction * 4);
    }

    internal void End()
    {
        var camera = Camera;
        bool wasCurrent = camera?.Current == true;
        if (camera is not null) camera.QueueFree();
        _camera = null;
        var player = _player;
        _player = null;
        if (player is not null && GodotObject.IsInstanceValid(player))
        {
            player.Rac1RemoteControlActive = false;
            if (wasCurrent && GodotObject.IsInstanceValid(player.Camera))
                player.Camera.MakeCurrent();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        End();
        _disposed = true;
    }
}
