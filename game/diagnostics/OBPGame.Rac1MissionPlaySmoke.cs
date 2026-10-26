using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Progression;

namespace OBP.Game;

/// <summary>
/// Interactive integration gate. Observes an ordinary playthrough without
/// generating input, staging transforms, granting currency or injecting events.
/// The wrapper launches a second process to verify the resulting durable save.
/// </summary>
public partial class OBPGame
{
    private async Task RunRac1MissionPlaySmokeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath) || _player is null)
                throw new InvalidOperationException("Mission play check requires an isolated campaign save and live player.");
            await WaitForGroundedAsync(_player, 360);
            if (_rac1CampaignRestoreKind == Rac1CampaignRestoreKind.RestoredCurrentSchema)
            {
                RequireMissionPlayAdmissions(1, [1, 2]);
                RequirePersistedRac1CampaignSmokeState(1, [1, 2]);
                await PhysicsFramesAsync(3);
                RequireMissionPlayPlumberRetired();
                GD.Print("[rac1-mission-play] fresh process: current Novalis, admission order [1,2], purchased actor retired PASS");
            }
            else if (_rac1CampaignRestoreKind == Rac1CampaignRestoreKind.DefaultedMissingState)
                await ObserveRac1MissionPlayAsync();
            else throw new InvalidOperationException($"Unexpected mission play restore kind {_rac1CampaignRestoreKind}.");

            GD.Print("[rac1-mission-play] PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-mission-play-smoke-pass", 0);
        }
        catch (OperationCanceledException error) when (Diagnostics.LegacyDiagnosticCancellation.IsOwned(error))
        {
            throw;
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[rac1-mission-play] FAIL: {exception.Message}\n{exception.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-mission-play-smoke-fail", 4);
        }
    }

    private async Task ObserveRac1MissionPlayAsync()
    {
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("The mission play gate needs a player; use the interactive wrapper.");
        RequireMissionPlayAdmissions(0, []);
        var status = new Label { Name = "MissionPlayCheck", Position = new(24, 72) };
        _ui.AddChild(status);
        string? last = null;
        bool enteredNovalis = false, purchasedAridia = false, enteredAridia = false;
        bool sawDeath = false, sawRestart = false;
        while (IsInsideTree())
        {
            await Diagnostics.LegacyDiagnosticWait.SignalAsync(GetTree(), SceneTree.SignalName.ProcessFrame);
            var campaign = _rac1CampaignSession.Campaign;
            if (_player?.DevelopmentControls.FlyEnabled == true)
                throw new InvalidOperationException("Development flight invalidates the ordinary-input check.");
            if (campaign.CanSelectDestination(1) &&
                (_rac1Nanotech.Probe().IsDead || _rac1Nanotech.EnvironmentalDeathGeneration > 0)) sawDeath = true;
            if (sawDeath && !_rac1Nanotech.Probe().IsDead && _player?.Rac1GameplayAlive == true) sawRestart = true;
            if (_rac1CampaignSession.Travel.TransitionActive || _world?.Game != ObpSourceGame.Rac1) continue;
            if (_world.LevelId != campaign.CurrentLevel)
                throw new InvalidOperationException("External world travel invalidates the ordinary-input check.");
            if (!enteredNovalis && campaign.CurrentLevel == 1)
            {
                RequireMissionPlayAdmissions(1, [1]);
                if (campaign.GetLevelState(0) != Rac1LevelVisitState.Completed)
                    throw new InvalidOperationException("Opening actor did not commit Veldin completion.");
                RequirePersistedRac1CampaignSmokeState(1, [1]);
                enteredNovalis = true;
                GD.Print("[rac1-mission-play] native Veldin encounter -> Novalis entry and admission PASS");
            }
            if (enteredNovalis && !purchasedAridia && campaign.CanSelectDestination(2))
            {
                RequireMissionPlayAdmissions(1, [1, 2]);
                RequirePersistedRac1CampaignSmokeState(1, [1, 2]);
                purchasedAridia = true;
                GD.Print("[rac1-mission-play] native Plumber purchase -> Aridia admission PASS");
            }
            if (purchasedAridia)
            {
                RequireMissionPlayAdmissions(campaign.CurrentLevel, [1, 2]);
                if (campaign.CurrentLevel == 2) enteredAridia = true;
                if (enteredAridia && campaign.CurrentLevel == 1 && sawRestart)
                {
                    await PhysicsFramesAsync(3);
                    RequireMissionPlayPlumberRetired();
                    RequirePersistedRac1CampaignSmokeState(1, [1, 2]);
                    GD.Print("[rac1-mission-play] death/retry, map travel 1 -> 2 -> 1, durable/idempotent discovery PASS");
                    status.QueueFree();
                    return;
                }
            }
            string instruction = !enteredNovalis
                ? "Play Veldin to the Clank encounter; acknowledge the conversation cards."
                : !purchasedAridia
                    ? "Collect 500 bolts; approach the Novalis Plumber and press Triangle / Q to buy the Infobot."
                    : !sawRestart
                        ? "Die and retry normally, then use Select / R3 to travel to Aridia and back to Novalis."
                        : !enteredAridia
                            ? "Use Select / R3 to travel to Aridia, then return to Novalis."
                            : "Return to Novalis through the planet map to finish this pass.";
            status.Text = "MISSION PLAY CHECK\n" + instruction;
            if (instruction != last) { GD.Print("[rac1-mission-play] " + instruction); last = instruction; }
        }
    }

    private void RequireMissionPlayAdmissions(int current, int[] expected)
    {
        var campaign = _rac1CampaignSession.Campaign;
        if (campaign.CurrentLevel != current || !campaign.GalacticMap.Take(campaign.AdmittedDestinationCount).SequenceEqual(expected))
            throw new InvalidOperationException($"Unexpected ordinary mission state: level={campaign.CurrentLevel}, admissions=[{string.Join(',', campaign.GalacticMap.Take(campaign.AdmittedDestinationCount))}].");
    }

    private void RequireMissionPlayPlumberRetired()
    {
        if (_rac1Missions?.Probe().SingleOrDefault(p => p.Key.NativeClassId == 774)?.Retired != true)
            throw new InvalidOperationException("Purchased native Plumber encounter restarted on revisit.");
    }
}
