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
    private void ResetRac1WeaponAudio(bool freeImmediately = false)
        => _equipmentPresentation.WeaponAudio.Reset(freeImmediately);

    private void RecordRac1WeaponSound(int cls, int sound, bool loop = false)
        => _equipmentPresentation.WeaponAudio.Record(cls, sound, _rac1WeaponWorldTick, _args.Rac1WeaponsSmoke, loop);

    private void StepRac1WeaponAudio()
        => _equipmentPresentation.WeaponAudio.Step(new(
            _player is not null && _playerAudio?.Ready == true && PlayerLife.Alive && !MovementReproduction.IsLoading &&
                !_player.Rac1WrenchFrame.Active && !(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true),
            _rac1Weapons.Equipped, _rac1EquipmentUseTicks, _rac1PyroEmitting,
            _rac1AdditionalWeapons?.Vacuuming == true, Rac1World.Weapons.Arsenal.Held, _rac1Weapons.GetAmmo(19) > 0,
            _rac1WeaponWorldTick, _args.Rac1WeaponsSmoke), _player, _playerAudio?.Random,
            _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets)?.Sounds);

    private void SetPlayerAudioPaused(bool paused)
    {
        _playerAudio?.SetPaused(paused);
        _equipmentPresentation.WeaponAudio.SetPaused(paused);
        FusionWeapons.SetPaused(paused);
    }

    private Rac1EquipmentAssets.Catalogue? _rac1EquipmentAssets;
    private string? _rac1EquipmentSource;
    private WorldMaterialFactory? _rac1EquipmentMaterials;
    private Rac1AdditionalWeaponSession? _rac1AdditionalWeapons;
    private readonly Dictionary<Rac1WeaponId, Rac1EquipmentView> _rac1EquipmentViews = [];
    private readonly Dictionary<Rac1WeaponId, Rac1EquipmentView> _rac1StagedEquipment = [];
    private readonly List<HostedNativeFlame> _rac1NativeFlames = [];
    private readonly Dictionary<Rac1MobyRuntimeKey, ulong> _rac1FlameHitTicks = [];
    private readonly Dictionary<Rac1WeaponId, long> _rac1WeaponGenerations = [];
    private bool _rac1AdditionalFireRequested;
    private bool _rac1PyroEmitting;
    private Rac1WeaponId _rac1EquipmentUseWeapon;
    private int _rac1EquipmentUseTicks, _rac1EquipmentUseAge;
    private int _rac1WeaponShotsFired;

    private sealed record HostedNativeFlame(Rac1ParticleSession.Particle Motion)
    {
        public HashSet<Rac1MobyRuntimeKey> Victims { get; } = [];
    }

    private void ResetRac1AdditionalWeapons()
    {
        ResetRac1WeaponAudio();
        ResetRac1Arsenal();
        Rac1World.Weapons.Projectiles.RetireShotsAgentsMotes();
        Rac1World.Weapons.SuckCannon.Reset();
        _rac1FlameHitTicks.Clear(); _rac1WeaponGenerations.Clear();
        _rac1NativeFlames.Clear(); _equipmentPresentation.Explosions.Particles?.RemoveType(12); _equipmentPresentation.Explosions.Particles?.RemoveType(2);
        _rac1AdditionalWeapons = new(_rac1Weapons);
        _rac1AdditionalWeaponsInventory = _rac1Weapons;
        Rac1World.Weapons.Projectiles.ResetFlashes(); _rac1WeaponWorldTick = 0;
        _rac1AdditionalFireRequested = _rac1PyroEmitting = false;
        _rac1EquipmentUseTicks = _rac1EquipmentUseAge = 0;
        _rac1WeaponShotsFired = 0;
        // A preview/reset may restore morphed target meshes within this same
        // physics frame. Do not reuse their previous bounds or exclusions.
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.Contacts.Invalidate();
    }

    private void ToggleRac1WeaponPreview()
        => SetRac1WeaponPreview(Input.IsKeyPressed(Key.Shift));

    private void SetRac1WeaponPreview(bool disable)
    {
        if (FusionWeapons.TestActive) { MovementReproduction.Report("Leave the fusion test before changing the R&C1 arsenal preview."); return; }
        _playerNative?.SetEquipmentPreview(!disable);
        if (disable)
        {
            _persistentPlayer.WeaponPreview = null;
            Rac1World.Context.BombGlove = new(_rac1Weapons); ResetRac1AdditionalWeapons();
            MovementReproduction.Report("Weapon preview disabled; campaign equipment restored.");
        }
        else
        {
            _persistentPlayer.WeaponPreview ??= _playerNative?.Inventory ?? new(_rac1Campaign.Session.Weapons.CapturePersistentState(), _rac1Weapons.CurrentItemId);
            _rac1Weapons.AcquireNativeItem(Rac1MetalDetectorSession.NativeGadgetItem);
            foreach (var weapon in Rac1WeaponProfiles.Additional.Select(profile => profile.Item).Append(Rac1WeaponId.FirstRanged))
            {
                _rac1Weapons.AcquireNativeItem((int)weapon);
                _rac1Weapons.GrantAmmoClamped((int)weapon, Rac1ItemAmmoDescriptors.Get((int)weapon).MaxAmmo);
            }
            // Rebind once when entering preview; refilling does not erase live cargo.
            if (_rac1AdditionalWeaponsInventory != _rac1Weapons)
            {
                Rac1World.Context.BombGlove = new(_rac1Weapons); ResetRac1AdditionalWeapons();
            }
            MovementReproduction.Report("Full arsenal preview: hold Triangle to select, Circle to use. Refill or restore campaign equipment from Pause → Testing Equipment.");
        }
        _rac1AdditionalWeaponsInventory = _rac1Weapons;
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
    private Rac1WeaponInventory? _rac1AdditionalWeaponsInventory;

    private void AttachRac1EquipmentViews(Node3D avatarView)
    {
        var source = Sources.Setup.Library.Get(OBP.Core.ObpSourceGame.Rac1)!;
        if (_rac1EquipmentAssets is null || _rac1EquipmentSource != source.Path)
        {
            _rac1EquipmentAssets = Rac1EquipmentAssets.Load(source.Path);
            _rac1EquipmentSource = source.Path;
        }
        _rac1EquipmentMaterials = new(_world! with { Textures = _rac1EquipmentAssets.Textures });
        _rac1EquipmentViews.Clear(); _rac1StagedEquipment.Clear();
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            var view = new Rac1EquipmentView(_rac1EquipmentAssets.Models[profile.ModelClass], _rac1EquipmentMaterials, _avatarPresentation.Avatar);
            avatarView.AddChild(view); view.Visible = false; _rac1EquipmentViews.Add(profile.Item, view);
            if (profile.Item is Rac1WeaponId.MineGlove or Rac1WeaponId.GloveOfDoom)
            {
                var staged = new Rac1EquipmentView(_rac1EquipmentAssets.Models[profile.Item == Rac1WeaponId.MineGlove ? 74 : 230], _rac1EquipmentMaterials);
                view.AddChild(staged); staged.Rotation = new(0, MathF.PI, 0);
                if (profile.Item == Rac1WeaponId.GloveOfDoom) staged.Scale = Vector3.One * Rac1ArsenalRules.DoomCanisterHeldScale;
                if (profile.Item == Rac1WeaponId.MineGlove) staged.Play(0);
                _rac1StagedEquipment.Add(profile.Item, staged);
            }
        }
    }

    private void StepRac1AdditionalWeapons()
    {
        if (MovementReproduction.IsLoading || _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is null || _player is null || _rac1AdditionalWeapons is null) return;
        bool held = PlayerLife.Alive && !_player.Rac1WrenchFrame.Active && !(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true) &&
            _rac1AdditionalFireRequested && !Rac1World.Weapons.Detector.Equipped;
        _rac1AdditionalFireRequested = false;
        Rac1World.Weapons.Arsenal.SetHeld(held);
        if (_rac1EquipmentUseWeapon != _rac1Weapons.Equipped)
        {
            _rac1EquipmentUseWeapon = _rac1Weapons.Equipped;
            _rac1EquipmentUseTicks = _rac1EquipmentUseAge = 0;
        }
        if (_rac1EquipmentUseTicks > 0) { _rac1EquipmentUseTicks--; _rac1EquipmentUseAge++; }
        var shot = _rac1AdditionalWeapons.Step(held, Rac1World.Weapons.Arsenal.AdmitUse());
        bool pyro = held && _rac1Weapons.Equipped == Rac1WeaponId.Pyrocitor && (_rac1Weapons.GetAmmo(16) > 0 || shot is not null);
        if (pyro != _rac1PyroEmitting)
        {
            _rac1PyroEmitting = pyro;
            if (_rac1EquipmentViews.TryGetValue(Rac1WeaponId.Pyrocitor, out var pyroView))
                pyroView.Play(pyro ? 3 : 5, false, ++_rac1WeaponPoseGeneration, pyro ? 4 : 1);
        }
        if (shot is not null)
        {
            if (_rac1EquipmentUseTicks == 0) _rac1EquipmentUseAge = 0;
            _rac1EquipmentUseTicks = shot.Weapon switch
            {
                Rac1WeaponId.Blaster => 6,
                Rac1WeaponId.Devastator => 35,
                Rac1WeaponId.Ryno => Rac1ArsenalRules.FireTicks(shot.Weapon),
                Rac1WeaponId.Walloper => Rac1ArsenalRules.WalloperDurationTicks,
                _ => 2,
            };
            _rac1WeaponShotsFired++;
            _rac1WeaponGenerations[shot.Weapon] = shot.Generation;
            if (shot.Weapon is not (Rac1WeaponId.Pyrocitor or Rac1WeaponId.Blaster) && _rac1EquipmentViews.TryGetValue(shot.Weapon, out var view) &&
                shot.Weapon is not (Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter or Rac1WeaponId.TeslaClaw))
                view.Play(Rac1ArsenalRules.HandFireSequence(shot.Weapon), false, shot.Generation);
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
        bool continuous = pyro || held && (_rac1Weapons.Equipped is Rac1WeaponId.Taunter or Rac1WeaponId.MorphORay ||
            _rac1Weapons.Equipped == Rac1WeaponId.TeslaClaw && _rac1Weapons.GetAmmo(19) > 0 ||
            _rac1Weapons.Equipped == Rac1WeaponId.SuckCannon && _rac1AdditionalWeapons.Vacuuming);
        if (continuous) _rac1EquipmentUseTicks = Math.Max(_rac1EquipmentUseTicks, 2);
        StepRac1WeaponAudio();
    }
    private long _rac1WeaponPoseGeneration;

    private void StepRac1EquipmentViews(Rac1PlayerAnimationPresentationController controller)
    {
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            if (!_rac1EquipmentViews.TryGetValue(profile.Item, out var view) || !IsInstanceValid(view)) continue;
            bool visible = IsRac1HandEquipmentVisible(profile.Item);
            view.Visible = visible;
            if (!visible) continue;
            if (profile.Item == Rac1WeaponId.Blaster)
                view.Play(_rac1EquipmentUseTicks > 0 ? 4 : 1);
            if (profile.Item is Rac1WeaponId.Taunter or Rac1WeaponId.MorphORay or Rac1WeaponId.TeslaClaw)
                view.Play(_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.Arsenal.Held == true ? 3 : 1);
            if (profile.Item == Rac1WeaponId.SuckCannon)
            {
                if (_rac1AdditionalWeapons?.Vacuuming == true) view.Play(3);
                else if (view.Sequence != 5)
                    view.Play(_rac1AdditionalWeapons?.Cargo.Count > 0 ? 6 : 1);
            }
            foreach (int sound in view.Step(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(profile.HandSemantic))))
                if (_equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets)?.Sounds?.TryGetValue((profile.ModelClass, sound), out var voice) != true || voice?.Loop != true)
                    FusionWeapons.PlayNativeSound(profile.ModelClass, sound, _player!.GlobalPosition);
            if (_rac1StagedEquipment.TryGetValue(profile.Item, out var staged))
            {
                staged.Position = view.MuzzleLocal; staged.Step();
                staged.Visible = _rac1Weapons.GetAmmo((int)profile.Item) > 0 &&
                    (profile.Item != Rac1WeaponId.MineGlove || _rac1AdditionalWeapons?.MineRearmTicks == 0);
            }
        }
    }

    private Vector3 EquipmentForward => _player is null ? Vector3.Forward :
        new(-MathF.Cos((float)_player.Rac1CurrentYaw), 0, MathF.Sin((float)_player.Rac1CurrentYaw));

    private Vector3 EquipmentMuzzle(Rac1WeaponId weapon, int socket = 0)
    {
        if (_rac1EquipmentViews.TryGetValue(weapon, out var view) &&
            _avatarPresentation.TryEquipmentMuzzle(view, socket, out var muzzle))
        {
            var start = _player!.GlobalPosition + Vector3.Up * .8f;
            var ray = PhysicsRayQueryParameters3D.Create(start, muzzle); ray.Exclude = [_player.GetRid()];
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            return hit.TryGetValue("position", out var point) ? point.AsVector3() - (muzzle - start).Normalized() * .02f : muzzle;
        }
        return _player!.GlobalPosition + Vector3.Up * .9f + EquipmentForward * .7f;
    }

    private Rac1EquipmentView CreateEquipmentWorldView(int nativeClass)
    {
        var view = new Rac1EquipmentView(_rac1EquipmentAssets!.Models[nativeClass], _rac1EquipmentMaterials!);
        _sceneResult!.Root.AddChild(view); return view;
    }
}