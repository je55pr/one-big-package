using OBP.IO;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1RetailMenuMediaTests
{
    [Fact]
    public void SupportedDiscDecodesNativeFlashingPanelLayersAndTwoStartupCues()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var assets = Rac1UiAssets.Read(disc);
        Assert.Equal(new[] { 0x19, 0x1A, 0x1C }, assets.MenuEffects.Keys.Order());
        foreach (var texture in assets.MenuEffects.Values)
        {
            Assert.True(texture.Width > 1 && texture.Height > 1);
            Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length);
        }
        Assert.Equal(new[] { 0, 1 }, assets.MenuSounds.Keys.Order());
        Assert.All(assets.MenuSounds.Values, clip =>
        {
            Assert.True(clip.FrameCount > 1000);
            Assert.Equal(1, clip.ChannelCount);
        });
        Assert.NotNull(assets.Hud.DecodeFrame(0xE99E, 7));
    }
}
