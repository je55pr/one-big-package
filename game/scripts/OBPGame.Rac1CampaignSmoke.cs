using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Two-process host smoke for the recovered R&C1 campaign/checkpoint slice. The
/// first pass injects proven destination-discovery values plus the retained level-2
/// checkpoint/death witness, verifies reload clearing, revisits, and saves. The
/// second pass restores that host file and repeats a revisit with fresh checkpoints.
/// </summary>
public partial class OBPGame
{
    private async Task RunRac1CampaignSmokeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath))
                throw new InvalidOperationException(
                    "Campaign smoke requires --rac1-campaign-save so it cannot modify the normal host save.");

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            switch (_rac1CampaignRestoreKind)
            {
                case Rac1CampaignRestoreKind.DefaultedMissingState:
                    await RunFreshRac1CampaignSmokeAsync();
                    break;
                case Rac1CampaignRestoreKind.RestoredCurrentSchema:
                    await RunRestoredRac1CampaignSmokeAsync();
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Campaign smoke does not accept restore kind {_rac1CampaignRestoreKind}.");
            }

            GD.Print("[rac1-campaign-smoke] PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-campaign-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-campaign-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-campaign-smoke-fail", 4);
        }
    }

    private async Task RunFreshRac1CampaignSmokeAsync()
    {
        RequireRac1CampaignSmokeState(0, []);

        RequireRac1CampaignSmoke(
            ApplyRac1CampaignProgressionEvent(0x25) == Rac1DestinationDiscoveryResult.Discovered,
            "Progression event 0x25 did not discover destination 1.");
        RequireRac1CampaignSmoke(
            ApplyRac1CampaignProgressionEvent(0x26) == Rac1DestinationDiscoveryResult.Discovered,
            "Progression event 0x26 did not discover destination 2.");
        RequireRac1CampaignSmokeState(0, [1, 2]);
        GD.Print("[rac1-campaign-smoke] discovery PASS: events 0x25,0x26 -> destinations 1,2");

        await Rac1CampaignSmokeTravelAsync(1);
        await Rac1CampaignSmokeTravelAsync(2);
        ExerciseRecoveredLevel2CheckpointRestart();
        await ReloadCurrentRac1CampaignSmokeLevelAsync(2);
        await Rac1CampaignSmokeTravelAsync(1);

        RequireRac1CampaignSmokeState(1, [1, 2]);
        RequirePersistedRac1CampaignSmokeState(1, [1, 2]);
        GD.Print("[rac1-campaign-smoke] first pass PASS: 0 -> 1 -> 2 -> 1 persisted");
    }

    private async Task RunRestoredRac1CampaignSmokeAsync()
    {
        RequireRac1CampaignSmokeState(1, [1, 2]);
        RequirePersistedRac1CampaignSmokeState(1, [1, 2]);
        GD.Print("[rac1-campaign-smoke] reload PASS: restored current=1 and unlock order 1,2");

        await Rac1CampaignSmokeTravelAsync(2);
        await Rac1CampaignSmokeTravelAsync(1);

        RequireRac1CampaignSmokeState(1, [1, 2]);
        RequirePersistedRac1CampaignSmokeState(1, [1, 2]);
        GD.Print("[rac1-campaign-smoke] second pass PASS: restored 1 -> 2 -> 1");
    }

    private async Task Rac1CampaignSmokeTravelAsync(int destinationId)
    {
        Rac1PlanetTravelStartResult result = TravelRac1CampaignTo(destinationId);
        RequireRac1CampaignSmoke(
            result == Rac1PlanetTravelStartResult.Started,
            $"Travel to destination {destinationId} returned {result}.");

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        RequireRac1CampaignSmoke(
            _world is { Game: "rac1" } world && world.LevelId == destinationId,
            $"Host world did not settle on R&C1 destination {destinationId}.");
        RequireRac1CampaignSmoke(
            _rac1CampaignSession.Campaign.CurrentLevel == destinationId,
            $"Campaign CurrentLevel is {_rac1CampaignSession.Campaign.CurrentLevel}, expected {destinationId}.");
        RequireRac1CampaignSmoke(
            !_rac1CampaignSession.Travel.TransitionActive,
            $"Travel to destination {destinationId} left transition active.");
        RequireFreshRac1CheckpointSmoke(destinationId);

        GD.Print($"[rac1-campaign-smoke] travel PASS: current={destinationId}; checkpoint=fresh");
    }

    private void ExerciseRecoveredLevel2CheckpointRestart()
    {
        const int levelId = 2;
        RequireRac1CampaignSmoke(
            _world is { Game: "rac1", LevelId: levelId } && _player is not null,
            "Level-2 checkpoint smoke requires the live level-2 player.");

        Rac1LevelCheckpointSession checkpoint = _rac1CampaignSession.LevelCheckpoint
            ?? throw new InvalidOperationException("Level-2 checkpoint session is missing.");
        var recoveredPlacement = new RuntimeSpawn(
            205.5801544189453,
            26.059293746948242,
            163.04751586914062,
            0.7809665203094482);
        RequireRac1CampaignSmoke(
            checkpoint.Activate(new Rac1CheckpointActivation(levelId, recoveredPlacement)),
            "Recovered level-2 checkpoint activation was not accepted.");

        // Test-only injection of the retained level-2 environmental reset witness.
        // The gameplay writer/trigger that creates this checkpoint remains unknown.
        var dead = _rac1Nanotech.ApplyEnvironmentalDeathReset();
        _player!.Rac1GameplayAlive = false;
        RequireRac1CampaignSmoke(
            dead.Nanotech == 0 && dead.HasRecoveredEnvironmentalRespawn,
            "Level-2 environmental death injection did not enter the recovered reset state.");

        OnRac1RespawnRequested();

        var respawn = _rac1Nanotech.Probe();
        RuntimeSpawnScenePose scenePose = RuntimeSpawnSceneAdapter.ToScenePose(recoveredPlacement);
        RequireRac1CampaignSmoke(
            respawn.Nanotech == 4 && _player.Rac1GameplayAlive,
            "Level-2 recovered environmental restart did not restore Nanotech/alive state.");
        RequireRac1CampaignSmoke(
            _player.GlobalPosition.DistanceTo(scenePose.Position) < 0.001f,
            $"Level-2 checkpoint restart missed recovered placement: {_player.GlobalPosition} vs {scenePose.Position}.");
        RequireRac1CampaignSmoke(
            Math.Abs(_player.Rac1CurrentYaw - recoveredPlacement.Yaw) < 1e-9,
            "Level-2 checkpoint restart did not restore the recovered yaw.");

        GD.Print("[rac1-campaign-smoke] level-2 checkpoint PASS: explicit activation -> death -> recovered placement");
    }

    private async Task ReloadCurrentRac1CampaignSmokeLevelAsync(int levelId)
    {
        int currentBefore = _rac1CampaignSession.Campaign.CurrentLevel;
        OpenDestinationFromBootstrap($"rac1:LEVEL{levelId}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        RequireRac1CampaignSmoke(
            _rac1CampaignSession.Campaign.CurrentLevel == currentBefore,
            "Same-level host reload mutated campaign CurrentLevel.");
        RequireFreshRac1CheckpointSmoke(levelId);
        RequireRac1CampaignSmoke(
            _rac1Nanotech.Probe().Nanotech == 4 && !_rac1Nanotech.Probe().IsDead,
            "Same-level host reload did not start a fresh alive level-local gameplay session.");
        GD.Print($"[rac1-campaign-smoke] level-{levelId} reload PASS: checkpoint activation cleared");
    }

    private void RequireFreshRac1CheckpointSmoke(int expectedLevel)
    {
        RuntimeSpawn authoredClass0 = _world?.PlayerStart
            ?? throw new InvalidOperationException("R&C1 smoke world has no authored class-0 start.");
        Rac1LevelCheckpointSession checkpoint = _rac1CampaignSession.LevelCheckpoint
            ?? throw new InvalidOperationException("R&C1 smoke checkpoint session is missing.");

        RequireRac1CampaignSmoke(
            checkpoint.NativeLevelId == expectedLevel,
            $"Checkpoint session level {checkpoint.NativeLevelId}, expected {expectedLevel}.");
        RequireRac1CampaignSmoke(
            checkpoint.ActiveCheckpoint is null,
            $"Level {expectedLevel} unexpectedly carried an active checkpoint across full entry.");
        RequireRac1CampaignSmoke(
            checkpoint.AuthoredClass0 == authoredClass0,
            $"Level {expectedLevel} checkpoint baseline does not match RuntimeWorld.PlayerStart.");
        RequireRac1CampaignSmoke(
            checkpoint.ResolveEnvironmentalRestart().Kind == Rac1RestartPlacementKind.AuthoredClass0,
            $"Level {expectedLevel} fresh checkpoint session did not resolve to authored class 0.");
    }

    private void RequireRac1CampaignSmokeState(
        int expectedCurrent,
        int[] expectedUnlocked)
    {
        RequireRac1CampaignSmoke(
            _world is { Game: "rac1" } world && world.LevelId == expectedCurrent,
            $"Host world is {_world?.Game}:{_world?.LevelId}, expected rac1:{expectedCurrent}.");

        Rac1PlanetMapSnapshot map = _rac1CampaignSession.Travel.OpenPlanetMap();
        RequireRac1CampaignSmoke(
            map.CurrentLevel == expectedCurrent,
            $"Map CurrentLevel {map.CurrentLevel}, expected {expectedCurrent}.");
        RequireRac1CampaignSmoke(
            map.SelectedDestination == expectedCurrent,
            $"Map selection {map.SelectedDestination}, expected {expectedCurrent}.");
        RequireRac1CampaignSmoke(
            map.UnlockedDestinations.SequenceEqual(expectedUnlocked),
            $"Unlocked order [{string.Join(",", map.UnlockedDestinations)}], " +
            $"expected [{string.Join(",", expectedUnlocked)}].");
        RequireFreshRac1CheckpointSmoke(expectedCurrent);
    }

    private void RequirePersistedRac1CampaignSmokeState(
        int expectedCurrent,
        int[] expectedUnlocked)
    {
        string savePath = _rac1CampaignPersistence.SavePath
            ?? throw new InvalidOperationException(
                "Campaign smoke persistence path was not initialized.");
        Rac1CampaignRestoreResult restored =
            Rac1CampaignSaveFile.LoadOrDefault(savePath);
        RequireRac1CampaignSmoke(
            restored.Kind == Rac1CampaignRestoreKind.RestoredCurrentSchema,
            $"Persisted restore kind was {restored.Kind}.");
        RequireRac1CampaignSmoke(
            restored.Campaign.CurrentLevel == expectedCurrent,
            $"Persisted CurrentLevel {restored.Campaign.CurrentLevel}, expected {expectedCurrent}.");

        int[] unlocked = restored.Campaign.GalacticMap
            .Take(restored.Campaign.AdmittedDestinationCount)
            .ToArray();
        RequireRac1CampaignSmoke(
            unlocked.SequenceEqual(expectedUnlocked),
            $"Persisted unlock order [{string.Join(",", unlocked)}], " +
            $"expected [{string.Join(",", expectedUnlocked)}].");
    }

    private static void RequireRac1CampaignSmoke(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
