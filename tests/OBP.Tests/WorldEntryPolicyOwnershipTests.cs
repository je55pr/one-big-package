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
        Assert.Contains("WorldEntryPreparationPolicy.CreateWorldSource(load)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.RequireUnchangedSourceFile(sourcePath, worldSourceStamp)", entry);
        Assert.DoesNotContain("class WorldSource", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorldSource(", entry, StringComparison.Ordinal);
        Assert.Contains("WorldEntryPreparationPolicy.SourceEntrySnapReach(plan.Prepared.World)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.WorldOptions(plan.Prepared.World, _args)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.WorldOptions(world, _args)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.SourceFileStamp", root);
        Assert.DoesNotContain("private WorldHost.Options WorldOptions(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static RuntimeSpawn ValidateSourceEntry(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static float SourceEntrySnapReach(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (long Length, long Written)? SourceFileStamp(", entry, StringComparison.Ordinal);

        Assert.Contains("WorldEntryPlanning.ResolveEntryPoint(world)", policy);
        Assert.Contains("private sealed class PlannedWorldSource(Func<RuntimeWorld> load) : IWorldEntrySource", policy);
        Assert.Contains("var world = load();", policy);
        Assert.Contains("var spawn = ValidateSourceEntry(world);", policy);
        Assert.Contains("return new(world, spawn, []);", policy);
        Assert.Contains("if (SourceFileStamp(path) != expected)", policy);
        Assert.Contains("The world source changed during preparation.", policy);

        int plan = entry.IndexOf("_worldEntries.Prepare(request,", StringComparison.Ordinal);
        int staged = entry.IndexOf("WorldScenes.Prepare(plan,", StringComparison.Ordinal);
        int secondCheck = entry.IndexOf("RequireUnchangedSourceFile(sourcePath, worldSourceStamp)", StringComparison.Ordinal);
        int commit = entry.IndexOf("_worldEntries.Commit(plan, WorldScenes);", StringComparison.Ordinal);
        Assert.True(plan >= 0 && plan < staged && staged < secondCheck && secondCheck < commit,
            "World source and selected player assets must be checked before the destructive world commit.");
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
