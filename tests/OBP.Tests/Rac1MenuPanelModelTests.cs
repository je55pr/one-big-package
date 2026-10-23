using System.Numerics;
using OBP.IO;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1MenuPanelModelTests
{
    [Fact]
    public void OriginalDiscHasNativeFiveJointPanelModelAndAllAnimationSlots()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var model = Rac1UiAssets.Read(disc).NativeMenuPanels;
        Assert.NotNull(model);
        Assert.Equal(5, model.JointCount);
        Assert.Equal(48, model.RestPositions.Count);
        Assert.Equal(32 * 3, model.TriangleIndices.Count);
        Assert.Equal(48, model.Uvs.Count);
        Assert.Equal(32, model.TriangleTextureIds.Count);
        Assert.All(model.TriangleTextureIds, id => Assert.Equal(91, id));
        Assert.Equal(216, model.Sequences.Count);
        Assert.Single(model.Sequences[0].Frames);
        Assert.Equal(7, model.Sequences[14].Frames.Count);
        Assert.Equal(7, model.Sequences[215].Frames.Count);
        // The native 0x472 panel movement lives in per-joint scale and
        // translation channels. Rigid-only pose decoding erases all motion.
        double[] opening = model.Sequences[14].Frames[0];
        double[] endpoint = model.Sequences[14].Frames[^1];
        Assert.True(opening.Zip(endpoint, (a, b) => Math.Abs(a - b)).Max() > 5d,
            "Native menu panel animation was flattened into the bind pose.");
        Assert.True(model.Max.Y > model.Min.Y);
        Assert.True(model.Max.Z > model.Min.Z);
        Assert.All(model.RestPositions, position => Assert.Equal(0f, position.X));
        Assert.All(model.Sequences.Values, clip =>
        {
            Assert.True(clip.Frames.Count is 1 or 7);
            Assert.All(clip.Frames, vertices => Assert.Equal(48 * 3, vertices.Length));
        });
    }

    [Fact]
    public void NativeTwoCornerProjectionPreservesPerspectiveAndScreenQuarterScale()
    {
        static Vector4 View(Vector4 value) => new(value.X, value.Y, value.Z, value.Z);
        var bounds = Rac1MenuPanelProjection.ProjectFloat(
            new Vector3(-1, -1, 5), new Vector3(1, 1, 5), Vector3.Zero,
            View, new Vector2(800, 800), new Vector2(480, 360));
        Assert.Equal(440f, bounds.X, 3);
        Assert.Equal(320f, bounds.Y, 3);
        Assert.Equal(80f, bounds.Width, 3);
        Assert.Equal(80f, bounds.Height, 3);
    }

    [Theory]
    [InlineData(1, 1, 128, 128)]
    [InlineData(128, 128, 128, 128)]
    [InlineData(129, 129, 256, 256)]
    [InlineData(256, 750, 256, 512)]
    [InlineData(1024, 1024, 1024, 128)]
    public void NativeOffscreenTextureSelectionMatchesOriginalPacketThresholds(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        var target = Rac1MenuPanelProjection.OffscreenTarget(width, height);
        Assert.Equal(expectedWidth, target.Width);
        Assert.Equal(expectedHeight, target.Height);
    }

    [Fact]
    public void ProjectorRejectsUnboundedInputAndZeroPerspectiveW()
    {
        Assert.Throws<InvalidDataException>(() => Rac1MenuPanelProjection.ProjectFloat(
            Vector3.Zero, Vector3.One, Vector3.Zero,
            static _ => Vector4.Zero, Vector2.One, Vector2.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1MenuPanelProjection.OffscreenTarget(0, 1));
    }
}
