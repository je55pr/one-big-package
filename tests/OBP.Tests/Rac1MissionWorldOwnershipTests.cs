using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>Portable guards for the R&C1 per-world mission extraction.</summary>
public sealed class Rac1MissionWorldOwnershipTests
{
    private static string Script(string filename) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", filename));

    private static string Method(string file, string method) =>
        HostOwnershipAudit.Host.Value.SyntaxTrees
            .Single(tree => tree.FilePath == "game/scripts/" + file).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == method).ToString();

    [Fact]
    public void RootNoLongerStoresMissionPresentationActorOrExitState()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1Missions.cs")));
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[] { "_rac1Missions", "_rac1MissionNodes", "_rac1MissionSceneUi",
            "_rac1MissionOwnsPause", "_rac1MissionInteractPressed", "_rac1MissionExitQueued",
            "_rac1MissionTicks", "_rac1MissionPrompt" })
            Assert.Empty(root.GetMembers(member));

        var module = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1MissionWorldHost",
            Assert.Single(module.GetMembers("Missions").OfType<IPropertySymbol>()).Type.Name);
        Assert.Contains("Missions = new(world, scene, Context, missionPorts)", Script("Rac1WorldModule.cs"),
            StringComparison.Ordinal);
        Assert.Contains("Missions.Reset()", Method("Rac1WorldModule.cs", "ResetBindings"), StringComparison.Ordinal);
        Assert.Contains("Missions.Dispose()", Method("Rac1WorldModule.cs", "Dispose"), StringComparison.Ordinal);
        Assert.Contains("Missions.Configure()", Method("Rac1WorldModule.cs", "ConfigureActors"), StringComparison.Ordinal);
        Assert.Contains("Missions.Tick(delta)", Method("Rac1WorldModule.cs", "Tick"), StringComparison.Ordinal);
    }

    [Fact]
    public void MissionSceneAndTravelCallbacksRejectRetiredWorldBeforeTouchingCampaignOrUi()
    {
        string host = Script("Rac1MissionWorldHost.cs");
        string reset = Method("Rac1MissionWorldHost.cs", "Reset");
        string tick = Method("Rac1MissionWorldHost.cs", "Tick");
        string present = Method("Rac1MissionWorldHost.cs", "PresentScene");
        string travel = Method("Rac1MissionWorldHost.cs", "ApplyLevelExit");
        Assert.Contains("++_generation", reset, StringComparison.Ordinal);
        Assert.Contains("currentScene.Completed -= CompleteScene", reset, StringComparison.Ordinal);
        Assert.Contains("if (_ownsPause) ports.Pause(false)", reset, StringComparison.Ordinal);
        Assert.Contains("int generation = _generation", tick, StringComparison.Ordinal);
        Assert.Contains("Callable.From(() => PresentScene(request, generation)).CallDeferred()", tick,
            StringComparison.Ordinal);
        Assert.Contains("Callable.From(() => ApplyLevelExit(exit, generation)).CallDeferred()", tick,
            StringComparison.Ordinal);
        Assert.Contains("_disposed || generation != _generation || !ports.WorldStillCurrent()", present,
            StringComparison.Ordinal);
        int guard = travel.IndexOf("_disposed || generation != _generation || !ports.WorldStillCurrent()",
            StringComparison.Ordinal);
        int mutation = travel.IndexOf("Session.BeginMissionLevelExit(exit)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && mutation > guard,
            "A retired world must not advance native campaign travel on deferred exit.");
        Assert.Contains("world.LevelId != exit.Owner.LevelId", travel, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", host, StringComparison.Ordinal);
    }

    [Fact]
    public void MissionAdmissionAndStepStillUseOriginalNativeCampaignAndWorldClock()
    {
        string configure = Method("Rac1MissionWorldHost.cs", "Configure");
        string tick = Method("Rac1MissionWorldHost.cs", "Tick");
        Assert.Contains("world.LevelId != campaign.Session.Campaign.CurrentLevel", configure,
            StringComparison.Ordinal);
        Assert.Contains("new(world.LevelId, campaign.Session.Campaign, ports.Wallet(), Rac1ClipVolumes.Read(gameplay))",
            configure, StringComparison.Ordinal);
        Assert.Contains("Rac1TalkProgram.Read(overlay, world.LevelId, placement)", configure,
            StringComparison.Ordinal);
        Assert.Contains("Rac1RatchetMovementController.UpdateHz", tick, StringComparison.Ordinal);
        Assert.Contains("_session.Step(PlayerFacts(player, _interactPressed))", tick,
            StringComparison.Ordinal);
        Assert.Contains("context.MobyRuntime.SetPresence", tick, StringComparison.Ordinal);
        Assert.Contains("ports.Persist(", tick, StringComparison.Ordinal);
    }
}
