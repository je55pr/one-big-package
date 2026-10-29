using Godot;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

internal sealed class GodotShotListCaptureFixture(Node owner, Camera3D camera, Action<Camera3D> activate,
    Action<string> open, Func<RuntimeWorld?> world, Action<RuntimeWorld> showcase, Action<string?> overlay,
    Action updateHud, Action<bool> setHudVisible, Func<IDictionary<string, object?>> metadata) : IShotListCaptureFixture
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
                if (shot.Focus is { Length: 3 } focus && shot.Distance is { } distance)
                {
                    // Opt-in capture-only camera, independent of the planet's
                    // bounds and of the ordinary player's camera controller.
                    var target = RuntimeWorldScene.ToScene(focus[0], focus[1], focus[2]);
                    float azimuth = Mathf.DegToRad((float)shot.AzimuthDegrees);
                    float elevation = Mathf.DegToRad((float)shot.ElevationDegrees);
                    float planar = Mathf.Cos(elevation);
                    var offset = new Vector3(Mathf.Sin(azimuth) * planar,
                        Mathf.Sin(elevation), Mathf.Cos(azimuth) * planar) * (float)distance;
                    camera.Position = target + offset;
                    camera.Far = Mathf.Max(camera.Far, (float)distance + 2000f);
                    camera.LookAt(target, Vector3.Up);
                }
                else
                {
                    RuntimeWorldScene.FrameCamera(camera, current.Bounds, (float)shot.AzimuthDegrees, (float)shot.ElevationDegrees);
                }
                break;
            default:
                showcase(current);
                break;
        }
        overlay(shot.Overlay);
        updateHud();
        setHudVisible(!shot.HideHud);
    }
    public IDictionary<string, object?> ReadMetadata() => metadata();
    public async Task<bool> CaptureAsync(string output, int settleFrames, Func<IDictionary<string, object?>> metadata, CancellationToken token) =>
        (await CaptureHarness.CaptureAsync(owner, output, settleFrames, metadata, token)).Ok;
}
