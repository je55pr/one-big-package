using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1RetailMenuNavigationTests
{
    [Fact]
    public void NativeTwelveTickPageTransitionIsAtomicAndRestoresPageFocus()
    {
        var menu = new Rac1RetailMenuNavigation();
        menu.Focus(4, 7);
        Assert.True(menu.Navigate(Rac1RetailMenuPage.Weapons));
        Assert.True(menu.IsTransitioning);
        Assert.False(menu.Navigate(Rac1RetailMenuPage.Items));
        menu.Step(11);
        Assert.Equal(Rac1RetailMenuPage.Root, menu.Page);
        menu.Focus(0, 10); // navigation cannot mutate focus while transitioning
        Assert.Equal(4, menu.SelectedIndex);
        menu.Step();
        Assert.Equal(Rac1RetailMenuPage.Weapons, menu.Page);
        Assert.Equal(0, menu.SelectedIndex);
        menu.Focus(6, 10);
        Assert.True(menu.Back());
        menu.Step(12);
        Assert.Equal(Rac1RetailMenuPage.Root, menu.Page);
        Assert.Equal(4, menu.SelectedIndex);
        Assert.True(menu.Navigate(Rac1RetailMenuPage.Weapons));
        menu.Step(12);
        Assert.Equal(6, menu.SelectedIndex);
    }

    [Fact]
    public void LockedEquipmentIsNotEquippableAndIndicesClamp()
    {
        Assert.False(Rac1RetailMenuNavigation.MayEquip(false));
        Assert.True(Rac1RetailMenuNavigation.MayEquip(true));
        var menu = new Rac1RetailMenuNavigation();
        menu.Focus(42, 7);
        Assert.Equal(6, menu.SelectedIndex);
        menu.Focus(-20, 4);
        Assert.Equal(0, menu.SelectedIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => menu.Step(-1));
    }

    [Fact]
    public void HelpDeskPromptExpansionTextFadeAndDismissalAreDistinctStates()
    {
        var help = new Rac1HelpDeskPresentation();
        help.ShowPrompt("Use the Swingshot");
        Assert.Equal(Rac1HelpDeskPresentation.Phase.Opening, help.Current);
        help.Advance(8);
        Assert.Equal(Rac1HelpDeskPresentation.Phase.Prompt, help.Current);
        help.Expand();
        help.Advance(8);
        Assert.Equal(Rac1HelpDeskPresentation.Phase.FadeIn, help.Current);
        help.Advance(4);
        Assert.Equal(Rac1HelpDeskPresentation.Phase.Text, help.Current);
        Assert.Equal(1f, help.Opacity);
        help.ShowPrompt("New hint");
        help.Advance(12);
        Assert.Equal(Rac1HelpDeskPresentation.Phase.Opening, help.Current);
        Assert.Equal("New hint", help.Message);
        help.Expand();
        help.Advance(12);
        Assert.Equal(Rac1HelpDeskPresentation.Phase.Text, help.Current);
        help.Dismiss();
        help.Advance(12);
        Assert.Equal(Rac1HelpDeskPresentation.Phase.Hidden, help.Current);
        Assert.Equal(string.Empty, help.Message);
    }

    [Fact]
    public void RepeatedPromptIsIdempotentAndZeroStepDoesNotAdvance()
    {
        var help = new Rac1HelpDeskPresentation();
        help.ShowPrompt("Same hint");
        help.Advance(3);
        float progress = help.Expansion;
        help.ShowPrompt("Same hint");
        help.Advance(0);
        Assert.Equal(progress, help.Expansion);
        Assert.Throws<ArgumentOutOfRangeException>(() => help.Advance(-1));
    }
}
