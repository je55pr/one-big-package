using OBP.Core;

namespace OBP.Tests;

/// <summary>Characterises which <see cref="ObpSourceGame"/> values are retail trilogy sources.</summary>
public class ObpSourceGameExtensionsTests
{
    [Theory]
    [InlineData(ObpSourceGame.Rac1, true)]
    [InlineData(ObpSourceGame.Rac2, true)]
    [InlineData(ObpSourceGame.Rac3, true)]
    [InlineData(ObpSourceGame.Deadlocked, false)]
    [InlineData(ObpSourceGame.Synthetic, false)]
    [InlineData((ObpSourceGame)99, false)]
    public void OnlyTheThreeTrilogyGamesAreTrilogySources(ObpSourceGame game, bool expected) =>
        Assert.Equal(expected, game.IsTrilogySource());
}
