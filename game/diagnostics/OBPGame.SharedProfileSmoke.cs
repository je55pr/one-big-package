using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private async Task RunSharedProfileSmokeAsync()
    {
        try
        {
            AcceptanceAssert(OrdinaryPlayerSession && SharedProfilePath is not null && !_profileWriteBlocked, "profile-storage", "isolated ordinary profile is writable");
            if (_args.ProfileSmoke == "write")
            {
                while (_fusionPreparation is not null) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                AcceptanceAssert(SelectPlayerEquipment("rac2/item/30").Accepted, "native-selection", "source-qualified Lancer selected");
                _playerNative!.Weapons.LancerSession.ResetWorld(17); _playerNative.Weapons.CannonSession.Reset(9);
                _playerNative.Health.ApplyAdmittedDamageUnits(1); _playerNative.Wallet.Credit(42);
                AcceptanceAssert(AssignPlayerQuickSelect(7, "rac3/item/43").Accepted && UnequipPlayerEquipment().Accepted, "profile-loadout", "mixed quick slot and wrench hand retained");
                foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
                {
                    AcceptanceAssert(destination == "obp:fusion-lab" ? EnterFusionLab() : OnDestinationChosen(TrilogyWorldProviders.Resolve(destination)!), "profile-world-entry", destination);
                    AssertSavedPlayer();
                    AcceptanceAssert(TryToggleRac1MetalDetector() && !_rac1DetectorEquipped, "unsupported-shortcut", "Metal Detector shortcut respects its unavailable catalog capability on every world");
                }
                var source = _sources.Get(ObpSourceGame.Rac2)!;
                _sources.Remove(ObpSourceGame.Rac2); RefreshPlayerSourceAvailability();
                AcceptanceAssert(ReadPlayerEquipment().Single(entry => entry.Definition.Id == "rac2/item/30").Usability == EquipmentUsability.MissingSource &&
                    _fusion!.Selected == FusionWeaponSession.Lancer && _fusion.Hand.WrenchHeld, "missing-source-retained", "desired Lancer remains remembered and unavailable");
                AcceptanceAssert(SaveSharedProfile("missing-source fixture"), "profile-missing-save", "missing source dependency retained");
                using (var reader = new FileRandomAccessReader(source.Path)) _sources.Restore(new(source.Game, source.Identity.BuildId, source.Path), reader);
                RefreshPlayerSourceAvailability();
                AcceptanceAssert(SaveSharedProfile("restart fixture"), "profile-atomic-save", "native campaigns remain separate opaque contributors");
            }
            else if (_args.ProfileSmoke == "read")
            {
                AcceptanceAssert(_profileDocument is { } loaded && loaded.Destination == _currentDestinationId && loaded.Sources.Count == 3, "profile-restart", "new process resumed the saved destination and all source dependencies");
                AssertSavedPlayer();
            }
            else throw new InvalidDataException("Profile smoke mode must be write or read.");
            WriteAcceptanceEvidence("profile-" + _args.ProfileSmoke);
            GD.Print("[shared-profile-smoke] PASS: " + _args.ProfileSmoke);
            ApplicationLifecycle.RequestQuit(this, "shared-profile-smoke-pass");
        }
        catch (Exception error)
        {
            FinishAcceptanceFailure("profile-" + _args.ProfileSmoke, error, "shared-profile-smoke-fail");
        }

        void AssertSavedPlayer()
        {
            var player = _playerSession!.Capture();
            AcceptanceAssert(_playerAvatarView is not null && _playerAvatar?.Identity == player.Configuration.Avatar && _player!.ControlProfile == PlayerHostProfiles.Native,
                "profile-real-avatar", "selected animated Ratchet and native control profile");
            AcceptanceAssert(_fusion!.Selected == FusionWeaponSession.Lancer && _fusion.LancerSession.Ammo == 17 && _fusion.CannonSession.Ammo == 9 &&
                player.Loadout.QuickSelect[7] == FusionWeaponSession.ShockCannon && _rac1HeldItem.Hand.WrenchHeld, "profile-ammo-slots-hand", "17/9 native ammo, qualified slot 7 and wrench hand survived");
            AcceptanceAssert(_playerNative!.Health.Probe().Nanotech == 3 && player.Currencies["obp/bolts"] == 42, "profile-health-wallet", "shared health and wallet survived world changes/restart");
        }
    }
}
