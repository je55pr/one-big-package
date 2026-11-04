namespace OBP.Tests;

public sealed class WorldEntryPolicyOwnershipTests
{
    [Fact]
    public void EntryRootBorrowsWorldPreparationPolicyInsteadOfOwningIt()
    {
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        string policy = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "World/WorldEntryPreparationPolicy.cs"));
        string lifetime = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/World/WorldSceneLifetime.cs"));
        string resources = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/World/GodotWorldResources.cs"));
        string planning = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OBP.Fusion", "WorldEntryContracts.cs"));

        Assert.Contains("WorldSourceFileStamp.Capture(sourcePath)", lifetime);
        Assert.Contains("WorldEntryPreparationPolicy.CreateWorldSource(load)", entry);
        Assert.Contains("WorldSourceFileStamp.RequireUnchanged(sourcePath, stamp)", lifetime);
        Assert.DoesNotContain("class WorldSource", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorldSource(", entry, StringComparison.Ordinal);
        Assert.Contains("SnapReach: WorldEntryPreparationPolicy.SourceEntrySnapReach", entry);
        Assert.Contains("WorldEntryPreparationPolicy.WorldOptions(world, args)", resources);
        Assert.Contains("WorldEntryPreparationPolicy.WorldOptions(world, _args)", entry);
        Assert.Contains("WorldEntryPreparationPolicy.SourceFileStamp", CompositionSources.Application);
        // The activation admission must stay bound to the ordinary-source check, not just be called.
        Assert.Contains("AdmitActivation: () => Sources.RequireOrdinary(\"world activation\")", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private WorldHost.Options WorldOptions(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static RuntimeSpawn ValidateSourceEntry(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static float SourceEntrySnapReach(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private static (long Length, long Written)? SourceFileStamp(", entry, StringComparison.Ordinal);

        Assert.Contains("WorldEntryPlanning.ResolveEntryPoint(world)", policy);
        Assert.Contains("WorldEntryPlanning.FromLoader(load, ValidateSourceEntry)", policy);
        Assert.DoesNotContain("class PlannedWorldSource", policy, StringComparison.Ordinal);
        Assert.Contains("private sealed class LoadedWorldSource(", planning);
        Assert.Contains("ArgumentNullException.ThrowIfNull(request);", planning);
        Assert.Contains("var world = load();", planning);
        Assert.Contains("var spawn = validateEntry(world);", planning);
        Assert.Contains("return new(world, spawn, []);", planning);
        Assert.Contains("WorldSourceFileStamp.Capture(path)", policy);
        Assert.Contains("WorldSourceFileStamp.RequireUnchanged(path, expected)", policy);
        Assert.DoesNotContain("new FileInfo(", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("if (SourceFileStamp(path) != expected)", policy, StringComparison.Ordinal);

        int firstAssets = lifetime.IndexOf("preparation.PreparePlayer();", StringComparison.Ordinal);
        int plan = lifetime.IndexOf("session.Prepare(request,", StringComparison.Ordinal);
        int staged = lifetime.IndexOf("using var staged = Prepare(plan,", StringComparison.Ordinal);
        int secondAssets = lifetime.IndexOf("preparation.PreparePlayer();", firstAssets + 1, StringComparison.Ordinal);
        int secondCheck = lifetime.IndexOf("WorldSourceFileStamp.RequireUnchanged(sourcePath, stamp)", StringComparison.Ordinal);
        int sourceAdmission = lifetime.IndexOf("preparation.AdmitActivation()", StringComparison.Ordinal);
        int commit = lifetime.IndexOf("session.Commit(plan, this);", StringComparison.Ordinal);
        Assert.True(firstAssets >= 0 && firstAssets < plan && plan < staged && staged < secondAssets &&
            secondAssets < secondCheck && secondCheck < sourceAdmission && sourceAdmission < commit,
            "World source and selected player assets must be checked before the destructive world commit.");
        Assert.Contains("Rac1NovalisWaterMechanism.AdmitCollisionFace", policy);
        Assert.Contains("IncludeMobyMarkers", policy);
        Assert.Contains("OnlyAnimatedMobies", policy);
        Assert.DoesNotContain("OBPGame", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerAvatarPresentationHost", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldEntryFailuresDelegateBrowserRecoveryAndSupersededQuitPolicy()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        string navigation = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "Application/ApplicationNavigationCoordinator.cs"));
        string lifecycle = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OBP.Godot", "ApplicationLifecycle.cs"));

        Assert.Contains("_navigation.EnterPreparedWorld(destinationId,", entry, StringComparison.Ordinal);
        Assert.Contains("ReportWorldEntryFailure(destinationId, error, _ports.World() is not null)", navigation, StringComparison.Ordinal);
        Assert.Contains("_ports.AbandonFailedTravel();", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ReportWorldPreparationFailure(", entry, StringComparison.Ordinal);
        int begin = entry.IndexOf("private bool EnterWorld(", StringComparison.Ordinal);
        int end = entry.IndexOf("private void ActivateOwnedWorld(", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        Assert.DoesNotContain("ShowDestinationSelector();", entry[begin..end], StringComparison.Ordinal);
        Assert.Contains("ReportFailure: (error, superseded) => ApplicationLifecycle.ReportWorldPreparationFailure(this, error, superseded)", root, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportFailure: ReportWorldPreparationFailure", root, StringComparison.Ordinal);

        int method = navigation.IndexOf("internal void ReportWorldEntryFailure(", StringComparison.Ordinal);
        int next = navigation.IndexOf("internal void SetSelectorHint(", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && next > method);
        string recovery = navigation[method..next];
        Assert.Contains("Error($\"{destinationId} could not load: {error.Message}\")", recovery, StringComparison.Ordinal);
        Assert.Contains("if (!hasActiveWorld)", recovery, StringComparison.Ordinal);
        Assert.Contains("ShowSelector();", recovery, StringComparison.Ordinal);
        Assert.Contains("SetSelectorHint(error.Message);", recovery, StringComparison.Ordinal);
        Assert.True(recovery.IndexOf("Error(", StringComparison.Ordinal)
            < recovery.IndexOf("ShowSelector();", StringComparison.Ordinal));

        Assert.Contains("deferred world preparation failed (superseded={superseded})", lifecycle, StringComparison.Ordinal);
        Assert.Contains("if (!superseded && owner.IsInsideTree()) RequestQuit(owner, \"world-preparation-failed\", 2)", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredDuplicateIsHiddenByAvatarPresentationOwner()
    {
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        string avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "Player/PlayerAvatarPresentationHost.cs"));

        Assert.Contains("_avatarPresentation.AttachWorldVisual(player, new(", entry);
        Assert.Contains("HideAuthoredWorldRatchetPresentation(bindings.Destination(), bindings.Scene())", avatar);
        Assert.DoesNotContain("private void HideAuthoredWorldRatchetPresentation(", entry, StringComparison.Ordinal);
        Assert.Contains("void HideAuthoredWorldRatchetPresentation(ObpDestination? destination, RuntimeWorldScene.Result? scene)", avatar);
        Assert.Contains("animated.Name.StartsWith(\"ratchet_i\"", avatar);
        Assert.Contains("animated.Instance.Visible = false", avatar);
    }
}
