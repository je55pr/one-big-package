using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1LevelCheckpointSessionTests
{
    [Fact]
    public void InactiveVeldinDeathUsesRecoveredAuthoredClass0Placement()
    {
        RuntimeSpawn class0 = FromNative(
            132.09,
            115.48,
            31.4266167,
            0.6627014875);
        var session = new Rac1LevelCheckpointSession(0, class0);

        Rac1RestartPlacement restart = session.ResolveEnvironmentalRestart();

        Assert.Equal(Rac1RestartPlacementKind.AuthoredClass0, restart.Kind);
        Assert.Equal(0, restart.NativeLevelId);
        Assert.Equal(class0, restart.Placement);
        Assert.Null(session.ActiveCheckpoint);
    }

    [Fact]
    public void ExplicitLevel2ActivationRedirectsRecoveredDeathPlacement()
    {
        RuntimeSpawn class0 = FromNative(
            210.5431518555,
            170.2037963867,
            25.3327236176,
            2.3095138073);
        RuntimeSpawn checkpoint = FromNative(
            205.5801544189,
            163.0475158691,
            26.0592937469,
            0.7809665203);
        var session = new Rac1LevelCheckpointSession(2, class0);
        var activation = new Rac1CheckpointActivation(2, checkpoint);

        Assert.True(session.Activate(activation));
        Assert.False(session.Activate(activation));

        Rac1RestartPlacement restart = session.ResolveEnvironmentalRestart();

        Assert.Equal(Rac1RestartPlacementKind.ActiveCheckpoint, restart.Kind);
        Assert.Equal(2, restart.NativeLevelId);
        Assert.Equal(checkpoint, restart.Placement);
        Assert.NotEqual(class0, restart.Placement);
    }

    [Fact]
    public void FullReloadStartsFreshInactiveCheckpointSession()
    {
        var runtime = CreateRuntimeSession();
        RuntimeSpawn class0 = FromNative(
            210.5431518555, 170.2037963867, 25.3327236176, 2.3095138073);
        RuntimeSpawn checkpoint = FromNative(
            205.5801544189, 163.0475158691, 26.0592937469, 0.7809665203);

        Rac1LevelCheckpointSession first =
            runtime.StartLevelCheckpointSession(2, class0);
        Assert.True(first.Activate(new Rac1CheckpointActivation(2, checkpoint)));
        Assert.Equal(
            Rac1RestartPlacementKind.ActiveCheckpoint,
            first.ResolveEnvironmentalRestart().Kind);

        Rac1LevelCheckpointSession reloaded =
            runtime.StartLevelCheckpointSession(2, class0);

        Assert.NotSame(first, reloaded);
        Assert.Null(reloaded.ActiveCheckpoint);
        Assert.Equal(
            Rac1RestartPlacementKind.AuthoredClass0,
            reloaded.ResolveEnvironmentalRestart().Kind);
        Assert.Equal(class0, reloaded.ResolveEnvironmentalRestart().Placement);
    }

    [Fact]
    public void RevisitStartsFreshSessionWithoutChangingPersistentCampaignProgress()
    {
        var campaign = new Rac1CampaignState();
        Assert.True(campaign.AdmitDestination(1));
        Assert.True(campaign.AdmitDestination(2));
        Assert.True(campaign.BeginTravel(2));
        Rac1CampaignPersistentState before = campaign.CapturePersistentState();
        var runtime = new Rac1CampaignRuntimeSession(
            campaign,
            Rac1WeaponInventory.CreateOpeningVeldinWitness());

        RuntimeSpawn level2Start = FromNative(
            210.5431518555, 170.2037963867, 25.3327236176, 2.3095138073);
        RuntimeSpawn checkpoint = FromNative(
            205.5801544189, 163.0475158691, 26.0592937469, 0.7809665203);
        runtime.StartLevelCheckpointSession(2, level2Start)
            .Activate(new Rac1CheckpointActivation(2, checkpoint));

        RuntimeSpawn level1Start = new(1, 2, 3, 0.25);
        runtime.StartLevelCheckpointSession(1, level1Start);
        Rac1LevelCheckpointSession revisited =
            runtime.StartLevelCheckpointSession(2, level2Start);

        Assert.Null(revisited.ActiveCheckpoint);
        Assert.Equal(level2Start, revisited.ResolveEnvironmentalRestart().Placement);
        Rac1CampaignPersistentState after = campaign.CapturePersistentState();
        Assert.Equal(before.CurrentLevel, after.CurrentLevel);
        Assert.Equal(before.VisitedPlanets, after.VisitedPlanets);
        Assert.Equal(before.GalacticMap, after.GalacticMap);
        Assert.Equal(before.LevelStates, after.LevelStates);
    }

    [Fact]
    public void Schema2SaveRoundTripDoesNotPersistLevelCheckpointSession()
    {
        var campaign = new Rac1CampaignState();
        Assert.True(campaign.AdmitDestination(2));
        Assert.True(campaign.BeginTravel(2));
        Rac1WeaponInventory weapons =
            Rac1WeaponInventory.CreateOpeningVeldinWitness();
        var runtime = new Rac1CampaignRuntimeSession(campaign, weapons);
        RuntimeSpawn class0 = FromNative(
            210.5431518555, 170.2037963867, 25.3327236176, 2.3095138073);
        RuntimeSpawn checkpoint = FromNative(
            205.5801544189, 163.0475158691, 26.0592937469, 0.7809665203);
        runtime.StartLevelCheckpointSession(2, class0)
            .Activate(new Rac1CheckpointActivation(2, checkpoint));

        Rac1CampaignSaveEnvelope save =
            Rac1CampaignSavePolicy.Capture(campaign, weapons);
        Rac1CampaignRestoreResult restored =
            Rac1CampaignSavePolicy.RestoreOrDefault(save);
        var nextProcess =
            new Rac1CampaignRuntimeSession(restored.Campaign, restored.Weapons);

        Assert.Equal(2, Rac1CampaignSavePolicy.CurrentSchemaVersion);
        Assert.Equal(
            Rac1CampaignRestoreKind.RestoredCurrentSchema,
            restored.Kind);
        Assert.Null(nextProcess.LevelCheckpoint);

        Rac1LevelCheckpointSession fresh =
            nextProcess.StartLevelCheckpointSession(2, class0);
        Assert.Null(fresh.ActiveCheckpoint);
        Assert.Equal(
            Rac1RestartPlacementKind.AuthoredClass0,
            fresh.ResolveEnvironmentalRestart().Kind);
    }

    [Fact]
    public void ActivationCannotLeakAcrossLevelIdentity()
    {
        var session =
            new Rac1LevelCheckpointSession(2, new RuntimeSpawn(1, 2, 3, 0));

        Assert.Throws<ArgumentException>(
            () => session.Activate(
                new Rac1CheckpointActivation(
                    1,
                    new RuntimeSpawn(4, 5, 6, 1))));
    }

    private static Rac1CampaignRuntimeSession CreateRuntimeSession() =>
        new(
            new Rac1CampaignState(),
            Rac1WeaponInventory.CreateOpeningVeldinWitness());

    private static RuntimeSpawn FromNative(
        double x,
        double y,
        double z,
        double yawRadians) =>
        new(x, z, y, yawRadians);
}
