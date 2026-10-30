using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private void StepRac1WeaponAudio()
        => _equipmentPresentation.WeaponAudio.Step(new(
            _player is not null && _playerAudio?.Ready == true && PlayerLife.Alive && !MovementReproduction.IsLoading &&
                !_player.Rac1WrenchFrame.Active && !(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true),
            _rac1Weapons.Equipped, _persistentPlayer.Rac1Use.UseTicks, _persistentPlayer.Rac1Use.PyroEmitting,
            _persistentPlayer.Rac1Use.Session?.Vacuuming == true, Rac1World.Weapons.Arsenal.Held, _rac1Weapons.GetAmmo(19) > 0,
            (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), _args.Rac1WeaponsSmoke), _player, _playerAudio?.Random,
            _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets)?.Sounds);

    private void SetPlayerAudioPaused(bool paused)
    {
        _playerAudio?.SetPaused(paused);
        _equipmentPresentation.WeaponAudio.SetPaused(paused);
        FusionWeapons.SetPaused(paused);
    }

    private void ResetRac1AdditionalWeapons()
    {
        _equipmentPresentation.WeaponAudio.Reset();
        Rac1World.Weapons.Arsenal.Reset();
        if (_player is not null)
        {
            _player.Rac1EquipmentActionActive = false;
            _player.Rac1EquipmentLungeVelocity = Vector3.Zero;
        }
        Rac1World.Weapons.Projectiles.RetireShotsAgentsMotes();
        Rac1World.Weapons.SuckCannon.Reset();
        Rac1World.Weapons.Flames.ClearCooldowns();
        Rac1World.Weapons.Flames.ClearTrackedFlames(); _equipmentPresentation.Explosions.Particles?.RemoveType(12); _equipmentPresentation.Explosions.Particles?.RemoveType(2);
        _persistentPlayer.Rac1Use.ResetSession(_rac1Weapons);
        Rac1World.Weapons.Projectiles.ResetFlashes();
        Rac1World.Weapons.ResetWorldTick();
        _persistentPlayer.Rac1Use.ResetInputState();
        // A preview/reset may restore morphed target meshes within this same
        // physics frame. Do not reuse their previous bounds or exclusions.
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.Contacts.Invalidate();
    }

    private void ToggleRac1WeaponPreview()
        => SetRac1WeaponPreview(Input.IsKeyPressed(Key.Shift));

    private void SetRac1WeaponPreview(bool disable)
    {
        if (FusionWeapons.TestActive) { MovementReproduction.Report("Leave the fusion test before changing the R&C1 arsenal preview."); return; }
        bool resetWorld = _persistentPlayer.UpdateRac1WeaponPreview(disable, () => _rac1Weapons, _rac1Campaign.Session.Weapons);
        if (resetWorld)
        {
            Rac1World.Context.BombGlove = new(_rac1Weapons); ResetRac1AdditionalWeapons();
        }
        MovementReproduction.Report(disable ? "Weapon preview disabled; campaign equipment restored." : "Full arsenal preview: hold Triangle to select, Circle to use. Refill or restore campaign equipment from Pause → Testing Equipment.");
        _persistentPlayer.Rac1Use.BindInventory(_rac1Weapons);
        FusionWeapons.SynchronizeNativeSelection();
        RefreshSourceEquipmentContext();
        if (_rac1HeldItem.NativeItemId != (int)Rac1WeaponId.Wrench) HoldSelectedRac1HandItem();
        RefreshRac1HudState(); RefreshRac1WrenchPresentationVisibility();
    }

    private void RefreshSourceEquipmentContext()
    {
        if (_world is { Game.SourceGame: ObpSourceGame.Rac1 } world)
        {
            Rac1World.Weapons.Detector.Refresh(world);
        }
    }

    private void AttachRac1EquipmentViews(Node3D avatarView)
        => _equipmentPresentation.NativeViews.Attach(Sources.Setup.Library.Get(ObpSourceGame.Rac1)!.Path,
            _world!, avatarView, _avatarPresentation.Avatar);

    private void StepRac1AdditionalWeapons()
    {
        var use = _persistentPlayer.Rac1Use;
        if (MovementReproduction.IsLoading || _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is null ||
            _player is null || use.Session is null) return;
        bool eligible = PlayerLife.Alive && !_player.Rac1WrenchFrame.Active &&
            !(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true);
        var frame = use.BeginFrame(eligible, () => Rac1World.Weapons.Detector.Equipped, _rac1Weapons,
            Rac1World.Weapons.Arsenal.SetHeld, Rac1World.Weapons.Arsenal.AdmitUse);
        if (frame.PyroChanged) _equipmentPresentation.NativeViews.PlayPyrocitor(frame.Pyro);
        if (frame.Shot is { } shot)
        {
            // Invisible equipment skips its normal view step; sample the current
            // socket before firing so the first post-hide shot uses today's pose.
            if (_avatarPresentation.Controller is Rac1PlayerAnimationPresentationController controller)
                _equipmentPresentation.NativeViews.RefreshAttachment(shot.Weapon, controller);
            use.RecordShot(shot);
            _equipmentPresentation.NativeViews.PlayShot(shot);
            if (shot.Weapon is Rac1WeaponId.MineGlove or Rac1WeaponId.GloveOfDoom)
            {
                Rac1World.Weapons.ThrownAnimation.Begin();
                Rac1World.Weapons.Projectiles.Throw(shot);
            }
            else if (shot.Weapon == Rac1WeaponId.Blaster) Rac1World.Weapons.Projectiles.FireBlaster(shot);
            else if (shot.Weapon == Rac1WeaponId.SuckCannon) Rac1World.Weapons.SuckCannon.Fire(shot);
            else if (Rac1ArsenalRules.IsNew(shot.Weapon)) Rac1World.Weapons.Arsenal.Fire(shot.Weapon);
            if (shot.Weapon is Rac1WeaponId.DecoyGlove or Rac1WeaponId.DroneDevice)
                Rac1World.Weapons.ThrownAnimation.Begin();
            if (Rac1ItemAmmoDescriptors.Get((int)shot.Weapon).UsesAmmo)
                PersistRac1CampaignState($"{shot.Weapon} ammo consumption");
            RefreshRac1HudState();
        }
        use.CompleteFrame(frame, _rac1Weapons);
        StepRac1WeaponAudio();
    }
    private void StepRac1EquipmentViews(Rac1PlayerAnimationPresentationController controller)
        => _equipmentPresentation.NativeViews.Step(controller, IsRac1HandEquipmentVisible,
            _persistentPlayer.Rac1Use.UseTicks,
            _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.Arsenal.Held == true,
            _persistentPlayer.Rac1Use.Session, _rac1Weapons,
            new(_equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets),
                _player!, FusionWeapons.PlayNativeSound));

    private Vector3 EquipmentForward => _player is null ? Vector3.Forward :
        new(-MathF.Cos((float)_player.Rac1CurrentYaw), 0, MathF.Sin((float)_player.Rac1CurrentYaw));

    private Vector3 EquipmentMuzzle(Rac1WeaponId weapon, int socket = 0)
        => _equipmentPresentation.NativeViews.Muzzle(weapon, socket, _avatarPresentation, _player!, EquipmentForward);

    private Rac1EquipmentView CreateEquipmentWorldView(int nativeClass)
        => _equipmentPresentation.NativeViews.CreateWorldView(nativeClass, _sceneResult!.Root);

}