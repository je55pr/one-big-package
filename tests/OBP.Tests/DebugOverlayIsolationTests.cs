using OBP.Godot;

namespace OBP.Tests;

public sealed class DebugOverlayIsolationTests
{
    [Theory]
    [InlineData("tfrag", 1)]
    [InlineData("moby", 4)]
    [InlineData("moby-marker", 5)]
    [InlineData("sky", 6)]
    [InlineData("MOBY", 4)]
    [InlineData("MoBy-MaRkEr", 5)]
    public void KnownKindsResolveToTheirCanonicalCyclePosition(string requested, int expectedIndex)
    {
        Assert.True(DebugOverlay.TryGetIsolationIndex(requested, out int index));
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("moby-extra")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void UnknownOrEmptyKindsFailWithoutSearchingTheCycle(string? requested)
    {
        Assert.False(DebugOverlay.TryGetIsolationIndex(requested, out int index));
        Assert.Equal(0, index);
    }
}
