using OBP.Core;
using OBP.Core.Math;
using OBP.Game;
using OBP.RAC1.Progression;
using OBP.Runtime;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class Rac1CampaignWorldEntryHostTests
{
    [Fact]
    public void EntryBeginsFreshCheckpointAndReturnsTheAuthoredPlacement()
    {
        var campaign = new Rac1CampaignHost();
        var authored = new RuntimeSpawn(1, 2, 3, 4);
        var checkpoint = new RuntimeSpawn(5, 6, 7, 8);

        Assert.Equal(authored, campaign.BeginWorldEntry(0, authored));
        var first = Assert.IsType<Rac1LevelCheckpointSession>(campaign.Session.LevelCheckpoint);
        Assert.True(first.Activate(new Rac1CheckpointActivation(0, checkpoint)));

        Assert.Equal(authored, campaign.BeginWorldEntry(0, authored));
        var second = Assert.IsType<Rac1LevelCheckpointSession>(campaign.Session.LevelCheckpoint);
        Assert.NotSame(first, second);
        Assert.Null(second.ActiveCheckpoint);
        Assert.Equal(authored, second.AuthoredClass0);
    }

    [Fact]
    public void ExternalHostLoadDoesNotChangeCampaignStateOrRequestTravelPersistence()
    {
        var campaign = new Rac1CampaignHost();
        campaign.Session.Campaign.AdmitDestination(7);
        var original = campaign.Session.Campaign.CapturePersistentState();
        var authored = new RuntimeSpawn(11, 12, 13, 0);

        Assert.Equal(authored, campaign.BeginWorldEntry(7, authored));
        Assert.False(campaign.FinishWorldEntry(7));
        var after = campaign.Session.Campaign.CapturePersistentState();
        Assert.Equal(original.CurrentLevel, after.CurrentLevel);
        Assert.Equal(original.VisitedPlanets.ToArray(), after.VisitedPlanets.ToArray());
        Assert.Equal(original.GalacticMap.ToArray(), after.GalacticMap.ToArray());
        Assert.False(campaign.Session.Travel.TransitionActive);
    }

    [Fact]
    public void CampaignTravelCommitsOnlyAtFinishAndRequestsPersistence()
    {
        var campaign = new Rac1CampaignHost();
        campaign.Session.Campaign.AdmitDestination(1);
        var session = campaign.Session;
        var wallet = campaign.Wallet;
        var weapons = session.Weapons;

        Assert.Equal(Rac1PlanetTravelStartResult.Started, session.BeginTravel(1));
        campaign.BeginWorldEntry(1, new RuntimeSpawn(10, 20, 30, 0));
        Assert.Equal(0, session.Campaign.CurrentLevel);
        Assert.True(session.Travel.TransitionActive);

        Assert.True(campaign.FinishWorldEntry(1));
        Assert.Equal(1, session.Campaign.CurrentLevel);
        Assert.False(session.Travel.TransitionActive);
        Assert.Same(wallet, campaign.Wallet);
        Assert.Same(weapons, campaign.Session.Weapons);
    }

    [Fact]
    public void WrongLoadedLevelCannotFinishPendingTravel()
    {
        var campaign = new Rac1CampaignHost();
        campaign.Session.Campaign.AdmitDestination(1);
        Assert.Equal(Rac1PlanetTravelStartResult.Started, campaign.Session.BeginTravel(1));
        campaign.BeginWorldEntry(2, new RuntimeSpawn(1, 2, 3, 0));

        Assert.Throws<InvalidOperationException>(() => campaign.FinishWorldEntry(2));
        Assert.Equal(0, campaign.Session.Campaign.CurrentLevel);
        Assert.True(campaign.Session.Travel.TransitionActive);
        Assert.False(campaign.Session.Travel.CurrentLevelCommitted);
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac2)]
    [InlineData(ObpSourceGame.Rac3)]
    [InlineData(ObpSourceGame.Synthetic)]
    public void OtherSourceWorldsCannotStartOrCommitTheRac1Campaign(ObpSourceGame game)
    {
        var campaign = new Rac1CampaignHost();
        campaign.Session.Campaign.AdmitDestination(1);
        Assert.Equal(Rac1PlanetTravelStartResult.Started, campaign.Session.BeginTravel(1));
        var world = new RuntimeWorld(game, "fixture", 1, null, null, [], [], 0, [],
            new ObpBounds(Vec3.Zero, Vec3.Zero), null, null, PlayerStart: new RuntimeSpawn(1, 2, 3, 0));

        Assert.Null(campaign.BeginWorldEntry(world));
        Assert.False(campaign.FinishWorldEntry(world));
        Assert.Null(campaign.Session.LevelCheckpoint);
        Assert.Equal(0, campaign.Session.Campaign.CurrentLevel);
        Assert.True(campaign.Session.Travel.TransitionActive);
        Assert.False(campaign.Session.Travel.CurrentLevelCommitted);
        Assert.Equal(1, campaign.Session.Travel.LoaderDestination);
    }

    [Fact]
    public void Rac1WorldAdapterForwardsTheAuthoredCheckpoint()
    {
        var campaign = new Rac1CampaignHost();
        var authored = new RuntimeSpawn(1, 2, 3, 0);
        var world = new RuntimeWorld(ObpSourceGame.Rac1, "fixture", 0, null, null, [], [], 0, [],
            new ObpBounds(Vec3.Zero, Vec3.Zero), null, null, PlayerStart: authored);

        Assert.Equal(authored, campaign.BeginWorldEntry(world));
        Assert.False(campaign.FinishWorldEntry(world));
        Assert.Equal(0, campaign.Session.Campaign.CurrentLevel);
        Assert.Equal(authored, campaign.Session.LevelCheckpoint!.AuthoredClass0);
    }

    [Fact]
    public void RootOnlyWiresTheSourceCampaignEntryBoundary()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/GodotWorldResources.cs"));
        string owner = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Rac1CampaignHost.cs"));

        Assert.DoesNotContain("BeginSourceWorldEntry(", root, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitSourceWorldEntry(", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.StartLevelCheckpointSession(", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.CommitLoadedLevel(", root, StringComparison.Ordinal);
        Assert.Contains("bindings.Campaign.BeginWorldEntry(world)", root, StringComparison.Ordinal);
        Assert.Contains("bindings.Campaign.FinishWorldEntry(world)", root, StringComparison.Ordinal);
        Assert.DoesNotContain("if (world.Game == ObpSourceGame.Rac1)", root, StringComparison.Ordinal);

        // A source that cannot commit travel must not trigger a campaign parity save.
        var activate = CSharpSyntaxTree.ParseText(root).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Activate");
        var persist = Assert.Single(activate.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            call => call.Expression.ToString() == "bindings.Persist");
        var guard = Assert.IsType<IfStatementSyntax>(persist.Ancestors()
            .First(node => node is IfStatementSyntax or MethodDeclarationSyntax));
        Assert.Equal("bindings.Campaign.FinishWorldEntry(world)", guard.Condition.ToString());
        Assert.Null(guard.Else);
        Assert.Equal("Rac1CampaignPersistenceHost.TravelPersistenceReason",
            Assert.Single(persist.ArgumentList.Arguments).Expression.ToString());
        Assert.Contains("Session.StartLevelCheckpointSession(levelId, authoredStart)", owner, StringComparison.Ordinal);
        Assert.Contains("Session.CommitLoadedLevel(levelId)", owner, StringComparison.Ordinal);
        Assert.Contains("Session.FinishLoadedLevel(entry)", owner, StringComparison.Ordinal);
    }
}
