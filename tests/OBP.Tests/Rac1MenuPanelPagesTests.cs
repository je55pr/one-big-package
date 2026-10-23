using OBP.IO;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

/// <summary>Original NTSC-U Veldin overlay source page tables, not mock animation IDs.</summary>
public sealed class Rac1MenuPanelPagesTests
{
    [Fact]
    public void RetailOverlayContainsNativeSlotSequencesForAllSixEntrypointPages()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var disc = new FileRandomAccessReader(path);
        var assets = Rac1UiAssets.Read(disc);
        var pages = Assert.IsType<Rac1MenuPanelPages>(assets.NativeMenuPages);
        Assert.Equal(6, pages.Pages.Count);
        var ordinary = pages[Rac1MenuPanelPages.Page.OrdinaryPause];
        Assert.Equal(0x001B2588, ordinary.NativeAddress);
        Assert.Equal(2, ordinary.State);
        Assert.Equal([14, 15, 16, 17, 18, 19, 20, 7, 8, 9, 10, 11, 12, 13],
            ordinary.Animations.ToArray());
        Assert.Equal([0, 1, 16, 17, 18, 5, 6, 7, 8, 9, 10, 11, 12, 13],
            pages[Rac1MenuPanelPages.Page.Opening].Animations.ToArray());
        Assert.Equal([21, 22, 23, 24, 25, 5, 6, 7, 8, 9, 10, 11, 12, 13],
            pages[Rac1MenuPanelPages.Page.Startup].Animations.ToArray());
        Assert.Equal([99, 100, 101, 102, 103, 5, 6, 7, 8, 9, 10, 11, 12, 13],
            pages[Rac1MenuPanelPages.Page.Save].Animations.ToArray());
        Assert.Equal([134, 135, 136, 137, 157, 5, 6, 7, 8, 9, 10, 11, 12, 13],
            pages[Rac1MenuPanelPages.Page.Context].Animations.ToArray());
        Assert.Equal([158, 159, 2, 161, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13],
            pages[Rac1MenuPanelPages.Page.Progress].Animations.ToArray());
        var model = Assert.IsType<Rac1MenuPanelModel>(assets.NativeMenuPanels);
        Assert.All(pages.Pages.Values, page =>
        {
            Assert.Equal(14, page.Animations.Count);
            Assert.All(page.Animations, id => Assert.Contains(id, model.Sequences.Keys));
        });
    }

    [Fact]
    public void APageRejectsPanelSlotOutsideNativeFourteen()
    {
        var page = new Rac1MenuPanelPages.Assignment(
           Rac1MenuPanelPages.Page.OrdinaryPause, 0x1B2588,
           Enumerable.Range(0, 14).ToArray(), 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => page.ForSlot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.ForSlot(14));
    }
}
