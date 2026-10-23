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
        // The first seven R&C1 root screens use their own overlay draw
        // callback, not the shared text-label/shadow callback. The first
        // six are ordinary choices; slot 6 requires menu_system.unkD8.
        var rootEntries = pages.NativeRootEntries;
        Assert.Equal(7, rootEntries.Count);
        Assert.Equal([20194, 20195, 20196, 20197, 20198, 20199, 20418],
            rootEntries.Select(entry => entry.TextId).ToArray());
        Assert.Equal(Enumerable.Range(0, 7), rootEntries.Select(entry => entry.Slot));
        Assert.All(rootEntries, entry =>
        {
            Assert.Equal(3, entry.Action);
            Assert.Equal(4, entry.Flags);
            Assert.Equal(0x00278E10u, entry.DrawCallback);
        });
        Assert.All(rootEntries.Take(6), entry => Assert.False(entry.RequiresNativeAdmission));
        Assert.True(rootEntries[6].RequiresNativeAdmission);
        // The ordinary root's Weapons action points to its own authored
        // MenuPage. The focus is the original 3x5 item-grid screen, not an
        // inventory-order-derived list of weapon profiles.
        var weapons = pages.NativeWeaponsGrid;
        Assert.Equal(0x001B2DB8, weapons.PageAddress);
        Assert.Equal(0x001B2F58, weapons.ScreenAddress);
        Assert.Equal(3, weapons.Rows);
        Assert.Equal(5, weapons.Columns);
        Assert.Equal(0x20, weapons.Flags); // native suppress-equipped-icon variant
        Assert.Equal(.08f, weapons.MarginX, 5);
        Assert.Equal(.08f, weapons.MarginY, 5);
        Assert.Equal(15, weapons.Cells.Count);
        Assert.Equal([10, 16, 15, 20, 17, 14, 9, 11, 18, 13, 25, 24, 19, 21, 23],
            weapons.Cells.Select(cell => cell.NativeId).ToArray());
        Assert.Equal([0xEA6A, 0xEA70, 0xEA6F, 0xEA74, 0xEA71, 0xEA6E, 0xEA69,
            0xEA6B, 0xEA72, 0xEA6D, 0xEA79, 0xEA78, 0xEA73, 0xEA75, 0xEA77],
            weapons.Cells.Select(cell => (int)cell.Icon).ToArray());
        Assert.All(weapons.Cells, cell =>
        {
            Assert.Equal(0, cell.Kind);
            Assert.Equal(0, cell.Frame);
        });
        // Native Gadgets is four linked grid screens, not a flat 5x3
        // equipment page. The second group is initially focused, even
        // though the first group appears first in screen display order.
        var gadgets = pages.NativeGadgetsGrid;
        Assert.Equal(0x001B28E8, gadgets.PageAddress);
        Assert.Equal(0x001B2BE8, gadgets.InitialFocusScreen);
        Assert.Equal([2, 3, 9, 10], gadgets.Groups.Select(group => group.PanelSlot));
        Assert.Equal([2, 1, 1, 1], gadgets.Groups.Select(group => group.Rows));
        Assert.Equal([3, 3, 3, 2], gadgets.Groups.Select(group => group.Columns));
        Assert.Equal([0x20, 4, 8, 0x10], gadgets.Groups.Select(group => group.Flags));
        Assert.Equal([26, 22, 12, 32, 27, 31, 2, 3, 4, 6, 5, 7, 29, 28],
            gadgets.Groups.SelectMany(group => group.Cells).Select(cell => cell.NativeId));
        Assert.Equal([0xEA7A, 0xEA76, 0xEA6C, 0xEA80, 0xEA7B, 0xEA7F,
            0xEA62, 0xEA63, 0xEA64, 0xEA66, 0xEA65, 0xEA67, 0xEA7D, 0xEA7C],
            gadgets.Groups.SelectMany(group => group.Cells).Select(cell => (int)cell.Icon));
        Assert.All(gadgets.Groups, group =>
        {
            Assert.Equal(0, group.Selected);
            Assert.Equal(.1f, group.MarginX, 5);
            Assert.Equal(.1f, group.MarginY, 5);
            Assert.All(group.Cells, cell =>
            {
                Assert.Equal(0, cell.Kind);
                Assert.Equal(0, cell.Frame);
            });
        });
        for (int groupIndex = 0; groupIndex < gadgets.Groups.Count; groupIndex++)
        {
            var group = gadgets.Groups[groupIndex];
            Assert.Equal(gadgets.Groups[(groupIndex + gadgets.Groups.Count - 1) %
                gadgets.Groups.Count].ScreenAddress, group.UpScreen);
            Assert.Equal(gadgets.Groups[(groupIndex + 1) % gadgets.Groups.Count].ScreenAddress,
                group.DownScreen);
        }
        // Quick Select shares all fifteen Weapons candidates, plus a
        // separate two-row gadget candidate grid. The save-backed wheel
        // is EIGHT slots, not this six-cell gadget screen.
        var quick = pages.NativeQuickSelectPage;
        Assert.Equal(0x001B32B8, quick.PageAddress);
        Assert.Equal(0x001B3398, quick.InitialFocusScreen);
        Assert.Equal([2, 3], quick.Groups.Select(group => group.PanelSlot));
        Assert.Equal([3, 2], quick.Groups.Select(group => group.Rows));
        Assert.Equal([5, 3], quick.Groups.Select(group => group.Columns));
        Assert.Equal([0x20, 0], quick.Groups.Select(group => group.Flags));
        Assert.Equal(weapons.Cells, quick.Groups[0].Cells);
        Assert.Equal([26, 22, 12, 32, 27, 31],
            quick.Groups[1].Cells.Select(cell => cell.NativeId));
        Assert.Equal([0xEA7A, 0xEA76, 0xEA6C, 0xEA80, 0xEA7B, 0xEA7F],
            quick.Groups[1].Cells.Select(cell => (int)cell.Icon));
        Assert.Equal(quick.Groups[1].ScreenAddress, quick.Groups[0].UpScreen);
        Assert.Equal(quick.Groups[1].ScreenAddress, quick.Groups[0].DownScreen);
        Assert.Equal(quick.Groups[0].ScreenAddress, quick.Groups[1].UpScreen);
        Assert.Equal(quick.Groups[0].ScreenAddress, quick.Groups[1].DownScreen);

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
