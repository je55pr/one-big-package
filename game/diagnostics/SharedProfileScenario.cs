using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class SharedProfileScenario(DiagnosticScenarioDefinition definition, string? mode,
    IDiagnosticSharedProfileFixture profile, IDiagnosticPersistentPlayerFixture player, IDiagnosticPlayerEquipmentFixture equipment,
    IDiagnosticPlayerInterfaceFixture ui, IDiagnosticProfileEvidence storage, IDiagnosticSourceAttachmentFixture sources,
    IDiagnosticNavigationActions navigation, IDiagnosticAcceptanceReporter report, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        try
        {
            var state = profile.Read();
            report.Assert(state.Ordinary && state.PathAvailable && !state.WriteBlocked, "profile-storage", "isolated ordinary profile is writable");
            if (mode == "write")
            {
                while (profile.Read().EquipmentPreparing) await run.WaitAsync(DiagnosticFramePhase.Process);
                report.Assert(equipment.Select("rac2/item/30").Accepted, "native-selection", "source-qualified Lancer selected");
                profile.StageSavedPlayer();
                report.Assert(equipment.Assign(7, "rac3/item/43").Accepted && equipment.Unequip().Accepted, "profile-loadout", "mixed quick slot and wrench hand retained");
                foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
                {
                    report.Assert((await navigation.NavigateAsync(destination, run.CancellationToken)).Accepted, "profile-world-entry", destination);
                    AssertSavedPlayer();
                    report.Assert(ui.ToggleMetalDetector() && !ui.Read().MetalDetectorEquipped, "unsupported-shortcut",
                        "Metal Detector shortcut respects its unavailable catalog capability on every world");
                }
                using (sources.Detach(ObpSourceGame.Rac2, refreshOnDetach: true))
                {
                    var current = player.Read().State!;
                    report.Assert(equipment.Read().Single(entry => entry.Definition.Id == "rac2/item/30").Usability == EquipmentUsability.MissingSource &&
                        current.SelectedWeapon == FusionWeaponSession.Lancer && current.HandEquipment is null, "missing-source-retained", "desired Lancer remains remembered and unavailable");
                    report.Assert(storage.TrySave("missing-source fixture"), "profile-missing-save", "missing source dependency retained");
                }
                report.Assert(storage.TrySave("restart fixture"), "profile-atomic-save", "native campaigns remain separate opaque contributors");
            }
            else if (mode == "read")
            {
                state = profile.Read();
                report.Assert(state.SavedDestination is not null && state.SavedDestination == state.Destination && state.SavedSources == 3,
                    "profile-restart", "new process resumed the saved destination and all source dependencies");
                AssertSavedPlayer();
            }
            else throw new InvalidDataException("Profile smoke mode must be write or read.");
            report.Write("profile-" + mode);
            log("[shared-profile-smoke] PASS: " + mode);
        }
        catch (OperationCanceledException error) when (run.CancellationToken.IsCancellationRequested && error.CancellationToken == run.CancellationToken) { throw; }
        catch (Exception error) { report.Fail("profile-" + mode, error, "shared-profile-smoke-fail"); throw; }

        void AssertSavedPlayer()
        {
            var observation = player.Read(); var current = observation.State!; var state = profile.Read();
            report.Assert(observation.AvatarPresented && observation.PresentedAvatar == current.Configuration.Avatar && observation.NativeProfile,
                "profile-real-avatar", "selected animated Ratchet and native control profile");
            report.Assert(current.SelectedWeapon == FusionWeaponSession.Lancer && state.LancerAmmo == 17 && state.CannonAmmo == 9 &&
                current.QuickSelect[7] == FusionWeaponSession.ShockCannon && current.HandEquipment is null,
                "profile-ammo-slots-hand", "17/9 native ammo, qualified slot 7 and wrench hand survived");
            report.Assert(state.NativeNanotech == 3 && current.Currencies["obp/bolts"] == 42, "profile-health-wallet", "shared health and wallet survived world changes/restart");
        }
    }
}
