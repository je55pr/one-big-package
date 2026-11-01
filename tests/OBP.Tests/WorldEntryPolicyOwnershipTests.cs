namespace OBP.Tests;

public sealed class WorldEntryPolicyOwnershipTests
{
    [Fact]
    public void EntryRootBorrowsWorldPreparationPolicyInsteadOfOwningIt()
    {
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.WorldEntry.cs"));
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        string policy = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "WorldEntryPreparationPolicy.cs"));

        Assert.Contains("WorldEntryPreparationPolicy.SourceFileStamp(sourcePath)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.ValidateSourceEntry(world)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.SourceEntrySnapReach(plan.Prepared.World)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.WorldOptions(plan.Prepared.World, _args)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.WorldOptions(world, _args)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.SourceFileStamp", root);
        Assert.DoesNotContain("private WorldHost.Options WorldOptions(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static RuntimeSpawn ValidateSourceEntry(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static float SourceEntrySnapReach(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (long Length, long Written)? SourceFileStamp(", entry, StringComparison.Ordinal);

        Assert.Contains("WorldEntryPlanning.ResolveEntryPoint(world)", policy);
        Assert.Contains("Rac1NovalisWaterMechanism.AdmitCollisionFace", policy);
        Assert.Contains("IncludeMobyMarkers", policy);
        Assert.Contains("OnlyAnimatedMobies", policy);
        Assert.DoesNotContain("OBPGame", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerAvatarPresentationHost", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredDuplicateIsHiddenByAvatarPresentationOwner()
    {
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.WorldEntry.cs"));
        string avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "PlayerAvatarPresentationHost.cs"));

        Assert.Contains("_avatarPresentation.HideAuthoredWorldRatchetPresentation(_activeDestination, _sceneResult)", entry);
        Assert.DoesNotContain("private void HideAuthoredWorldRatchetPresentation(", entry, StringComparison.Ordinal);
        Assert.Contains("void HideAuthoredWorldRatchetPresentation(ObpDestination? destination, RuntimeWorldScene.Result? scene)", avatar);
        Assert.Contains("animated.Name.StartsWith(\"ratchet_i\"", avatar);
        Assert.Contains("animated.Instance.Visible = false", avatar);
    }
}
