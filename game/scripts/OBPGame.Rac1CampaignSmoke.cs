using Godot;
using OBP.RAC1.Progression;

namespace OneBigPackage;

/// <summary>
/// Two-process host smoke for the recovered R&C1 campaign slice. The first pass
/// injects only proven destination-discovery dispatcher values, travels, revisits,
/// and saves. The second pass restores that host file and repeats a revisit.
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
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-campaign-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            GetTree().Quit(4);
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

        GD.Print($"[rac1-campaign-smoke] travel PASS: current={destinationId}");
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
    }

    private void RequirePersistedRac1CampaignSmokeState(
        int expectedCurrent,
        int[] expectedUnlocked)
    {
        Rac1CampaignRestoreResult restored =
            Rac1CampaignSaveFile.LoadOrDefault(_rac1CampaignSavePath);
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
