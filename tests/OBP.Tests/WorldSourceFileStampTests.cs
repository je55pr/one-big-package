using OBP.Fusion;

namespace OBP.Tests;

public sealed class WorldSourceFileStampTests
{
    [Fact]
    public void UnchangedWorldSourcePassesButDifferentFileLengthIsRejectedBeforeCommit()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original");
            var expected = WorldSourceFileStamp.Capture(path);
            Assert.NotNull(expected);
            WorldSourceFileStamp.RequireUnchanged(path, expected);

            File.AppendAllText(path, "changed");
            var failure = Assert.Throws<InvalidDataException>(
                () => WorldSourceFileStamp.RequireUnchanged(path, expected));
            Assert.Equal(WorldSourceFileStamp.ChangedMessage, failure.Message);
            Assert.Equal("The world source changed during preparation. Reattach it in Game Sources.", failure.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingSourceFailsClosedAndNullSourceRequiresMatchingNullStamp()
    {
        Assert.Null(WorldSourceFileStamp.Capture(null));
        WorldSourceFileStamp.RequireUnchanged(null, null);
        var missing = Path.Combine(Path.GetTempPath(), "obp-nonexistent-world-source", Guid.NewGuid().ToString("N"));
        Assert.Throws<FileNotFoundException>(() => WorldSourceFileStamp.Capture(missing));
        Assert.Throws<InvalidDataException>(() =>
            WorldSourceFileStamp.RequireUnchanged(null, (1L, 2L)));
    }
}
