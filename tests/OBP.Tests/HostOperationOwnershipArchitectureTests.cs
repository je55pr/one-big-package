using Microsoft.CodeAnalysis;

namespace OBP.Tests;

public sealed class HostOperationOwnershipArchitectureTests
{
    [Fact]
    public void CriticalLaunchesAreOwnedAndShutdownDrainsThemBeforeResourceRelease()
    {
        foreach (string file in new[] { "OBPGame.cs", "OBPGame.Destinations.cs", "OBPGame.MovementRepro.cs", "OBPGame.BugReports.cs" })
        {
            string source = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts", file));
            foreach (string operation in new[] { "RunCaptureAsync", "RunShotsAsync", "RunStressSwitchAsync", "LoadMovementStateAsync",
                "ExportSelectedBugAsync", "ReplaySelectedBugAsync" })
                Assert.DoesNotContain("_ = " + operation, source, StringComparison.Ordinal);
        }
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        int stop = root.IndexOf("await hostStopped;", StringComparison.Ordinal);
        Assert.True(stop > 0);
        Assert.True(root.IndexOf("ReleaseAudioForExit();", stop, StringComparison.Ordinal) > stop);
    }

    [Fact]
    public void SourceVerificationDoesNotPublishIntoSharedRootProgressFields()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_verifyOutcome"));
        Assert.Empty(root.GetMembers("_verifyProgressBits"));
        Assert.Single(root.GetMembers("_verification").OfType<IFieldSymbol>());
    }
}
