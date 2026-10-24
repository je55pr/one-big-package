using Godot;
using OBP.Godot;
using OBP.Core;
using OBP.IO;
using OBP.RAC1.Camera;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Explicit staged lifecycle test; not an original-game parity or cross-world replay claim.</summary>
    private async Task RunPlayerSessionSmokeAsync()
    {
        var originalOptions = CameraSettingsStore.Current;
        try
        {
            Require(OrdinaryPlayerSession && _playerSession is not null && _playerNative is not null, "ordinary player owner required");
            var owner = _playerSession!;
            var native = _playerNative!;
            var controller = native.Weapons;
            var configuration = owner.Capture().Configuration;
            SelectWeaponKey(FusionWeaponSession.Lancer);
            native.Held.HoldWrench();
            native.Health.ApplyHostAridiaContactDamage();
            owner.SetPreference("camera/horizontal", originalOptions.Horizontal == Rac1CameraDirection.Normal ? "Inverted" : "Normal");
            var initial = owner.Capture();
            foreach (var id in new[] { "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0", "obp:fusion-lab", "rac1:LEVEL0" })
            {
                bool entered = id == "obp:fusion-lab" ? EnterFusionLab() : OnDestinationChosen(TrilogyWorldProviders.Resolve(id)!);
                Require(entered, "world entry " + id);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(ReferenceEquals(owner, _playerSession) && ReferenceEquals(controller, _fusion), "player/controller owner replaced");
                var current = owner.Capture();
                Require(current.Configuration == configuration && current.Generation == initial.Generation, "player profile/generation changed");
                Require(current.Loadout.SelectedWeapon == initial.Loadout.SelectedWeapon && current.Loadout.HandEquipment is null,
                    "selection or wrench hand ownership changed");
                Require(current.Health == initial.Health, "world entry reset player health");
                Require(current.Preferences.Count == initial.Preferences.Count && initial.Preferences.All(pair =>
                    current.Preferences.GetValueOrDefault(pair.Key) == pair.Value), "world entry reset player input/UI preferences");
                Require(_playerAvatarView is not null && _playerAvatar!.Identity == configuration.Avatar, "selected real avatar missing");
                Require(_playerRoot.GetChildCount() == 1 && _worldRoot.GetChildCount() == 1, "old scene/player survived teardown");
                Require(_fusionProjectiles.Count == 0 && _fusionAudioPlayers.Count == 0 && _fusionEffects.Count == 0, "old player effects/audio survived");
                Require(id != "obp:fusion-lab" || _activeDestination is null && _currentDestinationId == id, "Lab impersonates a retail destination");
                GD.Print($"[player-session-smoke] {id}: same player/profile/selection/health; one player/scene; no old effects");
            }
            var liveWorld = _world;
            var livePlayer = _player;
            var avatarSource = _sources.Get(ObpSourceGame.Rac1)!;
            _sources.Remove(ObpSourceGame.Rac1);
            try
            {
                Require(!EnterFusionLab(), "missing required player source was accepted");
                Require(ReferenceEquals(liveWorld, _world) && ReferenceEquals(livePlayer, _player),
                    "missing avatar source destroyed the live player/world");
                Require(owner.Capture().Loadout.SelectedWeapon == initial.Loadout.SelectedWeapon, "source removal reset desired equipment");
            }
            finally
            {
                using var reader = new FileRandomAccessReader(avatarSource.Path);
                _sources.Restore(new(avatarSource.Game, avatarSource.Identity.BuildId, avatarSource.Path), reader);
                RefreshPlayerSourceAvailability();
            }
            Require(!EnterWorld("test:failed-preparation", () => throw new InvalidDataException("Intentional staged-load failure"), null, null),
                "invalid source was admitted");
            Require(ReferenceEquals(liveWorld, _world) && ReferenceEquals(livePlayer, _player) && ReferenceEquals(owner, _playerSession),
                "failed preparation destroyed the live world/player");
            bool ownedBlaster = native.Inventory.Owns(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster);
            SetRac1WeaponPreview(false);
            Require(native.Inventory.Owns(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster), "diagnostic preview did not bind to the player owner");
            SetRac1WeaponPreview(true);
            Require(native.Inventory.Owns(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster) == ownedBlaster, "preview granted permanent ownership");
            owner.SetPreference("camera/horizontal", originalOptions.Horizontal.ToString());
            owner.Restore(initial);
            Require(CameraSettingsStore.Current.Horizontal.ToString() == initial.Preferences["camera/horizontal"], "restore did not apply host preferences");
            GD.Print("[player-session-smoke] PASS: persistent owner, native/custom round-trip, retained selections/health and failed-stage isolation");
            ApplicationLifecycle.RequestQuit(this, "player-session-smoke-pass", 0);
        }
        catch (Exception error)
        {
            GD.PrintErr("[player-session-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "player-session-smoke-fail", 3);
        }
        finally { CameraSettingsStore.Set(originalOptions, persist: false); }
    }
}
