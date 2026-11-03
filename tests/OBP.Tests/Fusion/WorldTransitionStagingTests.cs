using OBP.Godot;

namespace OBP.Tests;

public sealed class WorldTransitionStagingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedImportOrPresentationBuildDoesNotCommitExistingWorld(bool failImport)
    {
        string activeWorld = "original-level";
        int state = 42;
        int builds = 0;

        bool ok = WorldTransitionStaging.TryPrepare(
            () => failImport ? throw new InvalidDataException("damaged source") : "new-level",
            staged =>
            {
                builds++;
                throw new InvalidOperationException("bad mesh");
            },
            out string? candidate,
            out string? scene,
            out Exception? failure);
        if (ok)
        {
            activeWorld = candidate!;
            state = 0;
        }

        Assert.False(ok);
        Assert.Equal(failImport ? 0 : 1, builds);
        Assert.Null(scene);
        Assert.NotNull(failure);
        Assert.Equal("original-level", activeWorld);
        Assert.Equal(42, state);
    }

    [Fact]
    public void SuccessfulPreparationRunsExactlyOnceAndCanBeCommitted()
    {
        int imports = 0;
        int builds = 0;
        bool ok = WorldTransitionStaging.TryPrepare(
            () => { imports++; return "new-world"; },
            world => { builds++; return world + "-scene"; },
            out string? prepared,
            out string? scene,
            out Exception? failure);

        Assert.True(ok);
        Assert.Equal(1, imports);
        Assert.Equal(1, builds);
        Assert.Null(failure);
        Assert.Equal("new-world", prepared);
        Assert.Equal("new-world-scene", scene);
    }
}
