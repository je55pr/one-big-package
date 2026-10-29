using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1AridiaOceanAnimationTests
{
    [Theory]
    [InlineData(0L, 40, 41, 0f)]
    [InlineData(1L, 40, 41, 1f / 30)]
    [InlineData(15L, 40, 41, 0.5f)]
    [InlineData(29L, 40, 41, 29f / 30)]
    [InlineData(30L, 41, 42, 0f)]
    [InlineData(63L * 30, 103, 40, 0f)]
    [InlineData(64L * 30 - 1, 103, 40, 29f / 30)]
    [InlineData(64L * 30, 40, 41, 0f)]
    [InlineData(64L * 30 + 30, 41, 42, 0f)]
    public void NativeFrameClockSelectsAdjacentFxTexturesAndInterpolates(
        long tick, int first, int second, float blend)
    {
        var sample = Rac1AridiaOceanAnimation.AtGameTick(tick);
        Assert.Equal(first, sample.FirstTextureId);
        Assert.Equal(second, sample.NextTextureId);
        Assert.Equal(blend, sample.NextWeight, 6);
    }

    [Fact]
    public void InvalidClockIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1AridiaOceanAnimation.AtGameTick(-1));
    }

    [SkippableFact]
    public void RetailAridiaFramesMatchNativeAnimatedBank()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 2);
        var frames = Rac1AridiaOceanAnimation.ReadSourceFrames(Rac1LevelCore.Open(disc, level));
        Assert.Equal(64, frames.Count);
        Assert.Equal(Enumerable.Range(40, 64), frames.Select(f => f.TextureId));
        Assert.All(frames, f =>
        {
            Assert.Equal("fx", f.AssetKind);
            Assert.Equal(64 * 64 * 4, f.Rgba.Length);
        });

        // FX frames are not interchangeable with one still texture.
        Assert.Contains(frames.Skip(1), f => !f.Rgba.SequenceEqual(frames[0].Rgba));
    }
}