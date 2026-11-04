namespace OBP.Tests;

/// <summary>Retirement guard for capture-only camera policy in the Godot root.</summary>
public sealed class WorldCaptureCameraArchitectureTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    [Fact]
    public void NativeWorldAndAnimatedMobyCameraFramingBelongToPresentationAdapter()
    {
        string root = CompositionSources.Application;
        string entry = Script("World/GodotWorldResources.cs");
        string camera = Script("World/GodotWorldCaptureCamera.cs");

        Assert.DoesNotContain("private void FrameShowcaseCamera(", root, StringComparison.Ordinal);
        Assert.DoesNotContain("private void FrameAnimatedMobies(", root, StringComparison.Ordinal);

        Assert.Contains("GodotWorldCaptureCamera.FrameShowcase(application.Camera, world)", entry);
        Assert.Contains("GodotWorldCaptureCamera.FrameShowcase(_camera, world)", root);
        Assert.Contains("GodotWorldCaptureCamera.FrameAnimated(application.Camera, world)", entry);
        Assert.Contains("application.ActiveCamera = application.Camera;", entry);

        // The authored capture framings are preserved, not recalibrated.
        Assert.Contains("azimuthDegrees: 38f, elevationDegrees: 26f", camera);
        Assert.Contains("azimuthDegrees: 35f, elevationDegrees: 18f", camera);
        Assert.Contains("(camera.Position - centre) * 1.15f", camera);
        Assert.Contains("(camera.Position - centre) * 0.55f", camera);
        Assert.Contains("AnimatedMeshFraming.MeasureFirstInstance(anim)", camera);
        Assert.DoesNotContain("StartsWith(firstInstance)", camera, StringComparison.Ordinal);
        Assert.Contains("camera.Current = true", camera);
    }

    [Fact]
    public void AnimFocusSpawnUsesValidatedFramingAndReportsFailure()
    {
        string spawn = Script("Player/GodotPlayerSpawnPlacement.cs");

        Assert.Contains("AnimatedMeshFraming.MeasureFirstInstance(world.AnimatedMeshes)", spawn);
        Assert.Contains("anim-focus spawn not applied", spawn);
        Assert.DoesNotContain("Frames[0]", spawn, StringComparison.Ordinal);
    }
}
