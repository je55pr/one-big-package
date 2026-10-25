using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.RAC1.Progression;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Ordinary source policy under test; no diagnostic access exemption and no disc-file mutations.</summary>
    private async Task RunSourceSetupCheckAsync()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Require(!_diagnosticSourceAccess && _world is not null, "The source-loss check requires a loaded ordinary world.");
            string path = SharedProfilePath ?? throw new InvalidOperationException("The source-loss check requires --profile.");
            var before = _playerSession!.Capture();
            byte[] saved = File.ReadAllBytes(path);
            // Also stage an uncommitted source-campaign transfer. Repair must
            // retire that host transient without advancing the campaign.
            int campaignLevel = _rac1CampaignSession.Campaign.CurrentLevel;
            int travelDestination = campaignLevel == 0 ? 1 : 0;
            _rac1CampaignSession.Campaign.AdmitDestination(travelDestination);
            Require(_rac1CampaignSession.BeginTravel(travelDestination) == Rac1PlanetTravelStartResult.Started, "Could not stage the source-campaign transfer.");
            Require(_sources.Remove(ObpSourceGame.Rac2), "The source-loss check requires the complete trilogy at entry.");
            Require(!RequireOrdinarySourceSetup("source-loss check"), "A detached required source did not block play.");
            Require(_world is null && _mode == Mode.Picker, "World resources were not retired for repair.");
            Require(!_rac1CampaignSession.Travel.TransitionActive && _rac1CampaignSession.Campaign.CurrentLevel == campaignLevel, "Repair retained or committed an interrupted source-campaign transfer.");
            var after = _playerSession.Capture();
            Require(before.Configuration == after.Configuration && JsonSerializer.Serialize(before.Loadout) == JsonSerializer.Serialize(after.Loadout) &&
                before.Health == after.Health && JsonSerializer.Serialize(before.Currencies) == JsonSerializer.Serialize(after.Currencies), "Source loss reset player-global state.");
            Require(!SaveSharedProfile("source-loss check") && saved.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Repair flow rewrote the persistent profile.");
            GD.Print("[source-setup-check] PASS: required source detachment retires the world, retains player state/profile and requests exit 2");
        }
        catch (Exception error)
        {
            GD.PrintErr("[source-setup-check] FAIL: " + error.Message);
            OBP.Godot.ApplicationLifecycle.RequestQuit(this, "source-setup-check-fail", 1);
        }
    }
}
