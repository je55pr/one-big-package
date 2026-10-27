using OBP.Core;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

/// <summary>Retained campaign/checkpoint witness with explicit discovery/death injections, not inferred mission producers.</summary>
public sealed class Rac1CampaignScenario(MovementDiagnosticContext context, Rac1CampaignScenarioPorts ports) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        try
        {
            Rac1CampaignScenarioAssertions.IsolatedSave(ports.Save);
            await ProcessFrameAsync();
            var restore = ports.Save.ReadProvenance();
            if (restore.RecoveryWarning) await CheckCorruptProtectionAsync();
            else if (restore.RestoreKind == Rac1CampaignRestoreKind.DefaultedMissingState) await RunFreshAsync();
            else if (restore.RestoreKind == Rac1CampaignRestoreKind.RestoredCurrentSchema) await RunRestoredAsync();
            else throw new InvalidOperationException($"Campaign smoke does not accept restore kind {restore.RestoreKind}.");
            Log("[rac1-campaign-smoke] PASS");
        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime) { throw; }
        catch (Exception error) { LogError($"[rac1-campaign-smoke] FAIL: {error.Message}\n{error.StackTrace}"); throw; }
    }
    private async Task CheckCorruptProtectionAsync()
    {
        var file = ports.Save.CaptureFile();
        Require(!ports.Save.ReadProvenance().WriterActive, "Corrupt-save recovery incorrectly installed a live writer.");
        Require(ports.Actions.Discover(0x25) == Rac1DestinationDiscoveryResult.Discovered, "Recovery smoke progression mutation was not admitted.");
        ports.Actions.TickSaves(5); ports.Actions.FlushSaves("corrupt-save smoke");
        await ProcessFrameAsync();
        Require(file.Unchanged, "Queued progress overwrote the damaged save.");
        Require(!file.TemporaryFileExists, "Recovery created a temporary save at the damaged path.");
        Log("[rac1-campaign-smoke] corrupt-save protection PASS: mutation did not write");
    }
    private async Task RunFreshAsync()
    {
        RequireState(0, []);
        Require(ports.Actions.Discover(0x25) == Rac1DestinationDiscoveryResult.Discovered, "Progression event 0x25 did not discover destination 1.");
        Require(ports.Actions.Discover(0x26) == Rac1DestinationDiscoveryResult.Discovered, "Progression event 0x26 did not discover destination 2.");
        RequireState(0, [1, 2]);
        Log("[rac1-campaign-smoke] discovery PASS: events 0x25,0x26 -> destinations 1,2");
        await TravelAsync(1); await TravelAsync(2);
        ExerciseLevel2CheckpointRestart();
        await ReloadLevelAsync(2); await TravelAsync(1);
        RequireState(1, [1, 2]); Rac1CampaignScenarioAssertions.Persisted(ports.Save, 1, [1, 2]);
        Log("[rac1-campaign-smoke] first pass PASS: 0 -> 1 -> 2 -> 1 persisted");
    }
    private async Task RunRestoredAsync()
    {
        RequireState(1, [1, 2]); Rac1CampaignScenarioAssertions.Persisted(ports.Save, 1, [1, 2]);
        Log("[rac1-campaign-smoke] reload PASS: restored current=1 and unlock order 1,2");
        await TravelAsync(2); await TravelAsync(1);
        RequireState(1, [1, 2]); Rac1CampaignScenarioAssertions.Persisted(ports.Save, 1, [1, 2]);
        Log("[rac1-campaign-smoke] second pass PASS: restored 1 -> 2 -> 1");
    }
    private async Task TravelAsync(int destination)
    {
        var result = ports.Actions.Travel(destination);
        Require(result == Rac1PlanetTravelStartResult.Started, $"Travel to destination {destination} returned {result}.");
        await ProcessFrameAsync();
        Require(World.Read() is { Source: ObpSourceGame.Rac1 } world && world.LevelId == destination, $"Host world did not settle on R&C1 destination {destination}.");
        var campaign = ports.Facts.ReadCampaign();
        Require(campaign.CurrentLevel == destination, $"Campaign CurrentLevel is {campaign.CurrentLevel}, expected {destination}.");
        Require(!campaign.TransitionActive, $"Travel to destination {destination} left transition active.");
        Rac1CampaignScenarioAssertions.FreshCheckpoint(ports.Checkpoints, destination);
        Log($"[rac1-campaign-smoke] travel PASS: current={destination}; checkpoint=fresh");
    }
    private void ExerciseLevel2CheckpointRestart()
    {
        Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 2 }, "Level-2 checkpoint smoke requires the live level-2 player.");
        var placement = new RuntimeSpawn(205.5801544189453, 26.059293746948242, 163.04751586914062, 0.7809665203094482);
        Require(ports.Checkpoints.Activate(new(2, placement)), "Recovered level-2 checkpoint activation was not accepted.");
        var dead = ports.Checkpoints.InjectEnvironmentalDeath();
        Require(dead.Nanotech == 0 && dead.RecoveredEnvironmentalRespawn, "Level-2 environmental death injection did not enter the recovered reset state.");
        Require(ports.Checkpoints.CompleteEnvironmentalRestart(), "Level-2 injected environmental restart did not complete through the recovered checkpoint seam.");
        var respawn = ports.Facts.ReadDeath(); var player = Player.Read(); var expected = ports.Checkpoints.Project(placement);
        Require(respawn.Nanotech == 4 && player.Rac1GameplayAlive, "Level-2 recovered environmental restart did not restore Nanotech/alive state.");
        Require(player.GlobalPosition.DistanceTo(expected) < .001f, $"Level-2 checkpoint restart missed recovered placement: {player.GlobalPosition} vs {expected}.");
        Require(Math.Abs(player.Rac1CurrentYaw - placement.Yaw) < 1e-9, "Level-2 checkpoint restart did not restore the recovered yaw.");
        Log("[rac1-campaign-smoke] level-2 checkpoint PASS: explicit activation -> death -> recovered placement");
    }
    private async Task ReloadLevelAsync(int level)
    {
        int before = ports.Facts.ReadCampaign().CurrentLevel;
        ports.Actions.ReloadLevel(level); await ProcessFrameAsync();
        Require(ports.Facts.ReadCampaign().CurrentLevel == before, "Same-level host reload mutated campaign CurrentLevel.");
        Rac1CampaignScenarioAssertions.FreshCheckpoint(ports.Checkpoints, level);
        var death = ports.Facts.ReadDeath();
        Require(death.Nanotech == 4 && !death.Dead, "Same-level host reload did not start a fresh alive level-local gameplay session.");
        Log($"[rac1-campaign-smoke] level-{level} reload PASS: checkpoint activation cleared");
    }
    private void RequireState(int current, int[] admissions)
    {
        ports.Actions.OpenPlanetMap();
        var world = World.Read(); var map = ports.Facts.ReadCampaign();
        Require(world.Source == ObpSourceGame.Rac1 && world.LevelId == current, $"Host world is {world.Source}:{world.LevelId}, expected rac1:{current}.");
        Require(map.CurrentLevel == current, $"Map CurrentLevel {map.CurrentLevel}, expected {current}.");
        Require(map.SelectedDestination == current, $"Map selection {map.SelectedDestination}, expected {current}.");
        Require(map.Admissions.SequenceEqual(admissions), $"Unlocked order [{string.Join(',', map.Admissions)}], expected [{string.Join(',', admissions)}].");
        Rac1CampaignScenarioAssertions.FreshCheckpoint(ports.Checkpoints, current);
    }
}
