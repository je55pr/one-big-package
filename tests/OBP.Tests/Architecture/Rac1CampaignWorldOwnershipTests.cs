using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>Structural boundaries for the first #372 campaign/world extraction.</summary>
public sealed class Rac1CampaignWorldOwnershipTests
{
    private static string Script(string filename) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", filename));

    private static string Method(string file, string method) =>
        HostOwnershipAudit.Host.Value.SyntaxTrees
            .Single(tree => tree.FilePath == "game/scripts/" + file).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == method).ToString();

    [Fact]
    public void CampaignSaveBatcherAndRecoveredStateBelongToPersistentSourceOwner()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1CampaignPersistence.cs")));
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string name in new[]
        {
            "_rac1SaveBatcher", "_rac1SaveFailureReported", "_rac1CampaignPersistence",
            "_rac1CampaignPersistenceInitialized", "_rac1CollectedNovalisGoldBolts",
        })
            Assert.Empty(root.GetMembers(name));

        string persistent = Script("Rac1CampaignPersistenceHost.cs");
        Assert.Contains("Rac1CampaignSaveWriterFactory.CreateIfEnabled(_persistence)", persistent,
            StringComparison.Ordinal);
        Assert.Contains("RestoreWithRecovery()", persistent, StringComparison.Ordinal);
        Assert.Contains("campaign.CollectedNovalisGoldBolts", persistent, StringComparison.Ordinal);
        Assert.Contains("Rac1CampaignSaveBatcher? _batcher", persistent, StringComparison.Ordinal);
        Assert.Contains("new(this)", Script("Rac1CampaignHost.cs"), StringComparison.Ordinal);
        Assert.Contains("() => _rac1BoltWallet", CompositionSources.Application, StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignProgressionEventInitializesPersistenceBeforeApplyingDiscovery()
    {
        // The first persistence restore replaces the live campaign session.
        // Discovering first would report success and then lose the admission.
        string progression = Method("OBPGame.cs", "ApplyRac1CampaignProgressionEvent");
        int sources = progression.IndexOf("Sources.Initialize();", StringComparison.Ordinal);
        int restore = progression.IndexOf("EnsureRac1CampaignPersistenceInitialized();", StringComparison.Ordinal);
        int mutation = progression.IndexOf("_rac1Campaign.ApplyProgressionEvent(progressionEventId, PersistRac1CampaignState)",
            StringComparison.Ordinal);
        Assert.True(sources >= 0 && restore > sources && mutation > restore,
            "Campaign persistence must restore before a progression event mutates the live session.");
        string owner = Method("Rac1CampaignHost.cs", "ApplyProgressionEvent");
        Assert.Contains("Session.Campaign.ApplyProgressionEvent(id)", owner, StringComparison.Ordinal);
        Assert.Contains("if (result == Rac1DestinationDiscoveryResult.Discovered) persist(", owner, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckpointRegistrationFollowsEntryResetAndIsNotClearedMidWorld()
    {
        // World module reset happens before actor registration during Configure,
        // and again only when the world retires. A second live Configure is rejected.
        // This guards the reset/registration concern without claiming live retail replay.
        string configure = Method("Rac1WorldModule.cs", "Configure");
        int reset = configure.IndexOf("ResetBindings();", StringComparison.Ordinal);
        int actors = configure.IndexOf("ConfigureActors(_world, _scene);", StringComparison.Ordinal);
        Assert.True(reset >= 0 && actors > reset,
            "Checkpoint volumes must register after the one-time entry reset.");
        Assert.Contains("if (_configured) throw", configure, StringComparison.Ordinal);

        string module = Script("Rac1WorldModule.cs");
        Assert.Equal(3, module.Split("ResetBindings(", StringSplitOptions.None).Length - 1);
        Assert.Contains("ResetBindings(retiring: true)", Method("Rac1WorldModule.cs", "Dispose"),
            StringComparison.Ordinal);
        Assert.Contains("Checkpoints.Reset()", Method("Rac1WorldModule.cs", "ResetBindings"),
            StringComparison.Ordinal);
        string checkpointReset = Method("Rac1CheckpointHost.cs", "Reset");
        Assert.Contains("_triggers?.Reset()", checkpointReset, StringComparison.Ordinal);
        Assert.DoesNotContain("_triggers = null", checkpointReset, StringComparison.Ordinal);
        Assert.Contains("_actors.Checkpoints(Checkpoints)", Method("Rac1WorldModule.cs", "ConfigureActors"),
            StringComparison.Ordinal);
        Assert.Contains("_triggers = new(Rac1ClipVolumes.Read(gameplay))",
            Method("Rac1CheckpointHost.cs", "Configure"), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckpointHostStructureIsWorldScoped()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1Checkpoints.cs")));
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_rac1CheckpointTriggers"));

        var world = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1CheckpointHost",
            Assert.Single(world.GetMembers("Checkpoints").OfType<IPropertySymbol>()).Type.Name);
        string module = Script("Rac1WorldModule.cs");
        Assert.Contains("Checkpoints = new(world, Context, Loot, Enemies)", module, StringComparison.Ordinal);
        Assert.Contains("Checkpoints.Reset()", Method("Rac1WorldModule.cs", "ResetBindings"), StringComparison.Ordinal);
        Assert.Contains("Checkpoints.Dispose()", Method("Rac1WorldModule.cs", "Dispose"), StringComparison.Ordinal);
        Assert.Contains("_actors.Checkpoints(Checkpoints)", Method("Rac1WorldModule.cs", "ConfigureActors"), StringComparison.Ordinal);
        Assert.Contains("_ticks.Checkpoints(Checkpoints)", Method("Rac1WorldModule.cs", "Tick"), StringComparison.Ordinal);

        string tick = Method("Rac1CheckpointHost.cs", "Tick");
        Assert.Contains("_disposed || _triggers is null", tick, StringComparison.Ordinal);
        Assert.Contains("checkpoint.NativeLevelId != world.LevelId", tick, StringComparison.Ordinal);
        Assert.Contains("_triggers.Step(nativePlayer)", tick, StringComparison.Ordinal);
        Assert.Contains("checkpoint.Activate(new Rac1CheckpointActivation(world.LevelId, spawn))",
            tick, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", Script("Rac1CheckpointHost.cs"), StringComparison.Ordinal);
    }
}
