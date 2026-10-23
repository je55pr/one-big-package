using OBP.IO;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1MenuPanelMotionTests
{
    [Fact]
    public void NativeTwelveTickPageTransitionUsesActualSevenFrameMenuTrack()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var assets = Rac1UiAssets.Read(disc);
        var source = new Rac1MenuPanelMotion(assets.NativeMenuPanels!, assets.NativeMenuPages!);
        var initial = source.Pose(0);
        Assert.Equal(14, initial.NativeSequence);
        Assert.Equal(6, initial.Frame);
        source.BeginTransition(Rac1MenuPanelPages.Page.Startup, backwards: false);
        Assert.Equal(12, source.RemainingUpdates);
        Assert.Equal(21, source.Pose(0).NativeSequence);
        Assert.Equal(0, source.Pose(0).Frame);
        var first = source.Pose(0);
        for (int i = 0; i < 12; i++) source.Tick();
        var final = source.Pose(0);
        Assert.Equal(0, source.RemainingUpdates);
        Assert.Equal(Rac1MenuPanelPages.Page.Startup, source.Page);
        Assert.Equal(6, final.Frame);
        Assert.NotEqual(first.Min.Y, final.Min.Y);
        source.BeginTransition(Rac1MenuPanelPages.Page.OrdinaryPause, backwards: true);
        Assert.Equal(21, source.Pose(0).NativeSequence);
        Assert.Equal(6, source.Pose(0).Frame);
        for (int i = 0; i < 12; i++) source.Tick();
        Assert.Equal(0, source.Pose(0).Frame);
        Assert.Equal(Rac1MenuPanelPages.Page.OrdinaryPause, source.Page);
        Assert.Equal(0, source.RemainingUpdates);
    }

    [Fact]
    public void PanelMotionRetainsFourteenSlotsAndRejectsOverlappingChanges()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var assets = Rac1UiAssets.Read(disc);
        var motion = new Rac1MenuPanelMotion(assets.NativeMenuPanels!, assets.NativeMenuPages!);
        Assert.Equal(7, motion.Pose(7).NativeSequence);
        Assert.Equal(13, motion.Pose(13).NativeSequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Pose(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Pose(14));
        motion.BeginTransition(Rac1MenuPanelPages.Page.Save, backwards: false);
        Assert.Throws<InvalidOperationException>(() =>
            motion.BeginTransition(Rac1MenuPanelPages.Page.Context, backwards: false));
    }
}
