using System.Numerics;
using OBP.IO;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1MenuPanelCameraTests
{
    [Fact]
    public void ResetNativePauseCameraPointsWorldXForwardAndNegatesYAndZ()
    {
        Assert.Equal(new Vector3(256, 256, 64), Rac1MenuPanelCamera.NativeCameraPosition);
        Assert.Equal(.63f, Rac1MenuPanelCamera.HorizontalTanHalfFov);
        Assert.Equal(.63f * .775f, Rac1MenuPanelCamera.VerticalTanHalfFov);
        Vector2 size = new(1280, 720);
        Assert.Equal(new Vector2(640, 360),
            Rac1MenuPanelCamera.ProjectRelative(new Vector3(6, 0, 0), size));
        var point = Rac1MenuPanelCamera.ProjectRelative(new Vector3(6, 1, 1), size);
        Assert.True(point.X < 640);
        Assert.True(point.Y < 360);
        Assert.Equal(640f - 640f / (6f * .63f), point.X, 3);
        Assert.Equal(360f - 360f / (6f * .63f * .775f), point.Y, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1MenuPanelCamera.ProjectRelative(new Vector3(0, 0, 0), size));
    }

    [Fact]
    public void OriginalPauseFirstFrameProjectsIntoSixDistinctVisiblePanelStrips()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var assets = Rac1UiAssets.Read(disc);
        var motion = new Rac1MenuPanelMotion(assets.NativeMenuPanels!, assets.NativeMenuPages!);
        motion.BeginTransition(Rac1MenuPanelPages.Page.OrdinaryPause, backwards: true);
        for (int i = 0; i < 12; i++) motion.Tick();
        var screen = new Vector2(1280, 720);
        var rows = Enumerable.Range(0, 6)
            .Select(slot => Rac1MenuPanelCamera.ProjectPanel(motion.Pose(slot), screen))
            .ToArray();
        Assert.All(rows, row =>
        {
            Assert.True(row.HasArea);
            Assert.InRange(row.X, 100, 1000);
            Assert.InRange(row.Width, 350, 650);
            Assert.InRange(row.Height, 45, 130);
        });
        for (int i = 1; i < rows.Length; i++)
        {
            Assert.True(rows[i].Y > rows[i - 1].Y + rows[i - 1].Height,
                "Native source 3D pause panels should be vertically separated.");
        }
    }
}
