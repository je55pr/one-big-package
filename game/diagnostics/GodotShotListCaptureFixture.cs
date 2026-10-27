using Godot;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

internal sealed class GodotShotListCaptureFixture(Node owner, Camera3D camera, Action<Camera3D> activate,
    Action<string> open, Func<RuntimeWorld?> world, Action<RuntimeWorld> showcase, Action<string?> overlay,
    Action updateHud, Func<IDictionary<string, object?>> metadata) : IShotListCaptureFixture
{
    public bool OpenWorld(string destinationId)
    {
        open(destinationId);
        if (world() is null) return false;
        Engine.MaxFps = 60;
        return true;
    }
    public void Apply(ShotSpec shot)
    {
        var current = world() ?? throw new DiagnosticUnavailableException("The screenshot world was retired.");
        camera.Current = true;
        activate(camera);
        switch (shot.Camera)
        {
            case ShotCamera.TopDown:
                var bounds = current.Bounds;
                var centre = RuntimeWorldScene.ToScene((bounds.Min.X + bounds.Max.X) * .5,
                    (bounds.Min.Y + bounds.Max.Y) * .5, (bounds.Min.Z + bounds.Max.Z) * .5);
                float span = (float)Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z);
                camera.Position = centre + Vector3.Up * (span * .75f + 50f);
                camera.LookAt(centre, Vector3.Forward);
                camera.Far = span * 20f + 2000f;
                break;
            case ShotCamera.Orbit:
                RuntimeWorldScene.FrameCamera(camera, current.Bounds, (float)shot.AzimuthDegrees, (float)shot.ElevationDegrees);
                break;
            default:
                showcase(current);
                break;
        }
        overlay(shot.Overlay);
        updateHud();
    }
    public IDictionary<string, object?> ReadMetadata() => metadata();
    public async Task<bool> CaptureAsync(string output, int settleFrames, Func<IDictionary<string, object?>> metadataFactory, CancellationToken token) =>
        (await CaptureHarness.CaptureAsync(owner, output, settleFrames, metadataFactory, token)).Ok;
}
