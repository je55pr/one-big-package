using OBP.IO;
using OBP.Tests.Helpers;

namespace OBP.Tests;

public class IoSplitTests
{
    [Fact]
    public void ConcatenatedReader_ReadsAcrossPartBoundariesWithoutReassembling()
    {
        byte[] a = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        byte[] b = Enumerable.Range(100, 100).Select(i => (byte)i).ToArray();
        byte[] c = Enumerable.Range(200, 56).Select(i => (byte)i).ToArray();

        var cat = new ConcatenatedRandomAccessReader(
            [new InMemoryReader("a", a), new InMemoryReader("b", b), new InMemoryReader("c", c)],
            "split");

        Assert.Equal(256, cat.Length);
        Assert.Equal(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), cat.Read(0, 256));
        Assert.Equal(Enumerable.Range(90, 120).Select(i => (byte)i).ToArray(), cat.Read(90, 120)); // spans a→b→c
        Assert.Throws<ArgumentOutOfRangeException>(() => cat.Read(250, 20));
    }

    [Theory]
    [InlineData(new[] { "game.iso.002", "game.iso.001", "game.iso.003" }, new[] { "game.iso.001", "game.iso.002", "game.iso.003" })]
    public void SplitParts_OrdersByNumericSuffix(string[] input, string[] expected)
    {
        Assert.Equal(expected, SplitParts.Order(input));
    }

    [Theory]
    [InlineData("game.iso.001", "other.iso.002")]   // mixed stems
    [InlineData("game.iso.001", "game.iso.003")]    // gap
    [InlineData("game.iso", "game.iso.001")]        // no suffix on one
    public void SplitParts_RejectsInconsistentSequences(string first, string second)
    {
        Assert.Throws<ArgumentException>(() => SplitParts.Order([first, second]));
    }
}
