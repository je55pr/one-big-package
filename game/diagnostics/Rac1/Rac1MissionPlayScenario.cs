using OBP.Core;
using OBP.RAC1.Progression;

namespace OBP.Game.Diagnostics;

/// <summary>Read-only ordinary-input observer. It never injects travel, currency, discovery events or player input.</summary>
public sealed class Rac1MissionPlayScenario(MovementDiagnosticContext context, IRac1CampaignDiagnosticFacts facts,
    IRac1CampaignDiagnosticSave save, IRac1MissionDiagnosticUi ui) : MovementScenarioSupport(context)
{
    public async Task RunAsync()
    {
        try
        {
            Rac1CampaignScenarioAssertions.IsolatedSave(save);
            await WaitForGroundedAsync(Player, 360);
            var kind = save.ReadProvenance().RestoreKind;
            if (kind == Rac1CampaignRestoreKind.RestoredCurrentSchema)
            {
                Admissions(1, [1, 2]); Rac1CampaignScenarioAssertions.Persisted(save, 1, [1, 2]);
                await PhysicsFramesAsync(3); PlumberRetired();
                Log("[rac1-mission-play] fresh process: current Novalis, admission order [1,2], purchased actor retired PASS");
            }
            else if (kind == Rac1CampaignRestoreKind.DefaultedMissingState) await ObserveAsync();
            else throw new InvalidOperationException($"Unexpected mission play restore kind {kind}.");
            Log("[rac1-mission-play] PASS");
        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime) { throw; }
        catch (Exception error) { LogError($"[rac1-mission-play] FAIL: {error.Message}\n{error.StackTrace}"); throw; }
    }
    private async Task ObserveAsync()
    {
        if (ui.Headless) throw new DiagnosticUnavailableException("The mission play gate needs a player; use the interactive wrapper.");
        Admissions(0, []);
        using var hint = ui.ShowHint(); string? last = null;
        bool enteredNovalis = false, purchasedAridia = false, enteredAridia = false, sawDeath = false, sawRestart = false;
        while (true)
        {
            await ProcessFrameAsync();
            var campaign = facts.ReadCampaign(); var player = Player.Read(); var death = facts.ReadDeath();
            if (player.FlyEnabled) throw new InvalidOperationException("Development flight invalidates the ordinary-input check.");
            if (campaign.NovalisAvailable && (death.Dead || death.EnvironmentalDeathGeneration > 0)) sawDeath = true;
            if (sawDeath && !death.Dead && player.Rac1GameplayAlive) sawRestart = true;
            var world = World.Read();
            if (campaign.TransitionActive || world.Source != ObpSourceGame.Rac1) continue;
            if (world.LevelId != campaign.CurrentLevel) throw new InvalidOperationException("External world travel invalidates the ordinary-input check.");
            if (!enteredNovalis && campaign.CurrentLevel == 1)
            {
                Admissions(1, [1]);
                Require(campaign.LevelStates[0] == Rac1LevelVisitState.Completed, "Opening actor did not commit Veldin completion.");
                Rac1CampaignScenarioAssertions.Persisted(save, 1, [1]); enteredNovalis = true;
                Log("[rac1-mission-play] native Veldin encounter -> Novalis entry and admission PASS");
            }
            if (enteredNovalis && !purchasedAridia && campaign.AridiaAvailable)
            {
                Admissions(1, [1, 2]); Rac1CampaignScenarioAssertions.Persisted(save, 1, [1, 2]); purchasedAridia = true;
                Log("[rac1-mission-play] native Plumber purchase -> Aridia admission PASS");
            }
            if (purchasedAridia)
            {
                Admissions(campaign.CurrentLevel, [1, 2]);
                if (campaign.CurrentLevel == 2) enteredAridia = true;
                if (enteredAridia && campaign.CurrentLevel == 1 && sawRestart)
                {
                    await PhysicsFramesAsync(3); PlumberRetired(); Rac1CampaignScenarioAssertions.Persisted(save, 1, [1, 2]);
                    Log("[rac1-mission-play] death/retry, map travel 1 -> 2 -> 1, durable/idempotent discovery PASS");
                    return;
                }
            }
            string instruction = !enteredNovalis ? "Play Veldin to the Clank encounter; acknowledge the conversation cards."
                : !purchasedAridia ? "Collect 500 bolts; approach the Novalis Plumber and press Triangle / Q to buy the Infobot."
                : !sawRestart ? "Die and retry normally, then use Select / R3 to travel to Aridia and back to Novalis."
                : !enteredAridia ? "Use Select / R3 to travel to Aridia, then return to Novalis."
                : "Return to Novalis through the planet map to finish this pass.";
            hint.SetText(instruction);
            if (instruction != last) { Log("[rac1-mission-play] " + instruction); last = instruction; }
        }
    }
    private void Admissions(int current, int[] expected)
    {
        var state = facts.ReadCampaign();
        Require(state.CurrentLevel == current && state.Admissions.SequenceEqual(expected),
            $"Unexpected ordinary mission state: level={state.CurrentLevel}, admissions=[{string.Join(',', state.Admissions)}].");
    }
    private void PlumberRetired() => Require(facts.PurchasedPlumberRetired, "Purchased native Plumber encounter restarted on revisit.");
}
