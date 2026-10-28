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
    private Rac1EquipmentAssets.Catalogue? _rac1EquipmentAssets;
    private string? _rac1EquipmentSource;
    private WorldMaterialFactory? _rac1EquipmentMaterials;
    private Rac1AdditionalWeaponSession? _rac1AdditionalWeapons;
    private readonly Dictionary<Rac1WeaponId, Rac1EquipmentView> _rac1EquipmentViews = [];
    private readonly Dictionary<Rac1WeaponId, Rac1EquipmentView> _rac1StagedEquipment = [];
    private readonly List<HostedEquipmentShot> _rac1EquipmentShots = [];
    private readonly List<HostedDoomAgent> _rac1DoomAgents = [];
    private readonly List<HostedCapturedMoby> _rac1Captures = [];
    private readonly List<HostedWeaponParticle> _rac1WeaponMotes = [];
    private readonly List<HostedNativeFlame> _rac1NativeFlames = [];
    private readonly Dictionary<Rac1MobyRuntimeKey, ulong> _rac1FlameHitTicks = [];
    private readonly Dictionary<Rac1MobyRuntimeKey, Node3D> _rac1CargoModels = [];
    private readonly Dictionary<Rac1WeaponId, long> _rac1WeaponGenerations = [];
    private bool _rac1AdditionalFireRequested;
    private bool _rac1PyroEmitting;
    private Rac1WeaponId _rac1EquipmentUseWeapon;
    private int _rac1EquipmentUseTicks, _rac1EquipmentUseAge;
    private int _rac1WeaponShotsFired;
    private ulong _rac1EquipmentGeometryFrame = ulong.MaxValue;
    private (RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)[] _rac1EquipmentGeometry = [];

    private sealed class HostedEquipmentShot(Rac1WeaponId weapon, long id, Node3D node, Vector3 position, Vector3 step)
    {
        public Rac1WeaponId Weapon { get; } = weapon;
        public long Id { get; } = id;
        public Node3D Node { get; } = node;
        public Vector3 Position = position;
        public Vector3 Step = step;
        public int Age;
        public bool Grounded;
        public float GroundHeight;
        public Rac1DoomCanisterSession? Canister;
        public Rac1ArsenalProjectile? Motion;
        public NVector3? AimPoint;
        public bool Taunted;
    }
    private sealed record HostedDoomAgent(long Id, CharacterBody3D Body, Rac1EquipmentView View)
    {
        public Rac1DoomAgentSession Motion { get; } = new();
    }
    private sealed record HostedCapturedMoby(Rac1MobyRuntimeKey Key, Node3D Visual, Vector3 Origin, Vector3 Scale)
    {
        public int Age;
    }
    private sealed record HostedWeaponParticle(Node3D Node, Vector3 Step)
    {
        public int Age;
    }
    private sealed record HostedNativeFlame(Rac1ParticleSession.Particle Motion)
    {
        public HashSet<Rac1MobyRuntimeKey> Victims { get; } = [];
    }

    private void ResetRac1AdditionalWeapons()
    {
        ResetRac1WeaponAudio();
        ResetRac1Arsenal();
        foreach (var node in _rac1EquipmentShots.Select(shot => shot.Node).Concat(_rac1DoomAgents.Select(agent => agent.Body))
            .Concat(_rac1Captures.Select(capture => capture.Visual)).Concat(_rac1CargoModels.Values).Concat(_rac1WeaponMotes.Select(mote => mote.Node)))
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1EquipmentShots.Clear(); _rac1DoomAgents.Clear(); _rac1Captures.Clear(); _rac1CargoModels.Clear();
        _rac1WeaponMotes.Clear(); _rac1FlameHitTicks.Clear(); _rac1WeaponGenerations.Clear();
        _rac1NativeFlames.Clear(); _rac1Particles?.RemoveType(12); _rac1Particles?.RemoveType(2);
        _rac1ChickenCargo.Clear();
        _rac1AdditionalWeapons = new(_rac1Weapons);
        _rac1AdditionalWeaponsInventory = _rac1Weapons;
        foreach (var flash in _rac1WeaponFlashes) if (IsInstanceValid(flash.Node)) flash.Node.QueueFree();
        _rac1WeaponFlashes.Clear(); _rac1WeaponWorldTick = 0;
        _rac1AdditionalFireRequested = _rac1PyroEmitting = false;
        _rac1EquipmentUseTicks = _rac1EquipmentUseAge = 0;
        _rac1WeaponShotsFired = 0;
        _rac1EquipmentGeometryFrame = ulong.MaxValue; _rac1EquipmentGeometry = [];
    }

    private void ToggleRac1WeaponPreview()
        => SetRac1WeaponPreview(Input.IsKeyPressed(Key.Shift));

    private void SetRac1WeaponPreview(bool disable)
    {
        if (FusionWeapons.TestActive) { ReportMovementState("Leave the fusion test before changing the R&C1 arsenal preview."); return; }
        _playerNative?.SetEquipmentPreview(!disable);
        if (disable)
        {
            _persistentPlayer.WeaponPreview = null;
            Rac1World.Context.BombGlove = new(_rac1Weapons); ResetRac1AdditionalWeapons();
            ReportMovementState("Weapon preview disabled; campaign equipment restored.");
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
            ReportMovementState("Full arsenal preview: hold Triangle to select, Circle to use. Refill or restore campaign equipment from Pause → Testing Equipment.");
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
            _rac1DetectorEquipped = false;
            _rac1DetectorNearest = null;
            ConfigureRac1MetalDetector(world);
        }
    }
    private Rac1WeaponInventory? _rac1AdditionalWeaponsInventory;

    private void AttachRac1EquipmentViews(Node3D avatarView)
    {
        FusionWeapons.AttachViews(avatarView);
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
        if (_movementStateLoading || _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets) is null || _player is null || _rac1AdditionalWeapons is null) return;
        bool held = PlayerAlive && !_player.Rac1WrenchFrame.Active && !_rac1WrenchThrow.Active &&
            _rac1AdditionalFireRequested && !_rac1DetectorEquipped;
        _rac1AdditionalFireRequested = false;
        _rac1ArsenalHeld = held;
        if (_rac1EquipmentUseWeapon != _rac1Weapons.Equipped)
        {
            _rac1EquipmentUseWeapon = _rac1Weapons.Equipped;
            _rac1EquipmentUseTicks = _rac1EquipmentUseAge = 0;
        }
        if (_rac1EquipmentUseTicks > 0) { _rac1EquipmentUseTicks--; _rac1EquipmentUseAge++; }
        var shot = _rac1AdditionalWeapons.Step(held, AdmitRac1ArsenalUse());
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
                _rac1BombThrowGeneration++; _rac1BombThrowActive = true;
                SpawnRac1ThrownEquipment(shot);
            }
            else if (shot.Weapon == Rac1WeaponId.Blaster) FireRac1Blaster(shot);
            else if (shot.Weapon == Rac1WeaponId.SuckCannon) FireRac1CapturedMoby(shot);
            else if (Rac1ArsenalRules.IsNew(shot.Weapon)) FireRac1Arsenal(shot);
            if (shot.Weapon is Rac1WeaponId.DecoyGlove or Rac1WeaponId.DroneDevice)
            { _rac1BombThrowGeneration++; _rac1BombThrowActive = true; }
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
                view.Play(_rac1ArsenalHeld ? 3 : 1);
            if (profile.Item == Rac1WeaponId.SuckCannon)
            {
                if (_rac1AdditionalWeapons?.Vacuuming == true) view.Play(3);
                else if (view.Sequence != 5)
                    view.Play(_rac1AdditionalWeapons?.Cargo.Count > 0 ? 6 : 1);
            }
            foreach (int sound in view.Step(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(profile.HandSemantic))))
                if (_equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets)?.Sounds?.TryGetValue((profile.ModelClass, sound), out var voice) != true || voice?.Loop != true)
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

    private IReadOnlyList<(RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)> EquipmentGeometry
    {
        get
        {
            ulong frame = Engine.GetPhysicsFrames();
            if (_rac1EquipmentGeometryFrame == frame) return _rac1EquipmentGeometry;
            _rac1EquipmentGeometryFrame = frame;
            var geometry = new List<(RuntimeWorldScene.DynamicObjectNode, Aabb)>();
            foreach (var node in Rac1EnemyNodes.Concat(Rac1World.Presentation.CrateNodes).Concat(Rac1QueenDamageTargets))
            {
                if (!IsInstanceValid(node.Root) ||
                    (node.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId
                        ? _rac1QueenRecovery?.CanReceiveHostWeaponContact != true
                        : !IsRac1GameplayActive(node)) ||
                    _rac1Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy) && enemy.RewardEmitted) continue;
                Aabb? bounds = null;
                var meshes = _rac1Chickens.TryGetValue(node.Source.InstanceIndex, out var chicken)
                    ? chicken.View.GetChildren().OfType<MeshInstance3D>() : node.Root.GetChildren().OfType<MeshInstance3D>();
                foreach (var mesh in meshes)
                {
                    var world = mesh.GlobalTransform * mesh.GetAabb();
                    bounds = bounds?.Merge(world) ?? world;
                }
                if (bounds is { } box) geometry.Add((node, box));
            }
            return _rac1EquipmentGeometry = geometry.ToArray();
        }
    }
    private IEnumerable<RuntimeWorldScene.DynamicObjectNode> EquipmentTargets => EquipmentGeometry
        .Select(entry => entry.Node).Where(node =>
            node.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId
                ? _rac1QueenRecovery?.CanReceiveHostWeaponContact == true
                : IsRac1GameplayActive(node));
    private bool IsRac1WeaponAimTarget(RuntimeWorldScene.DynamicObjectNode node) =>
        node.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId
            ? _rac1QueenNode == node && _rac1QueenRecovery?.CanReceiveHostWeaponContact == true
            : IsRac1GameplayActive(node) && (!_rac1Enemies.TryGetValue(node.Source.InstanceIndex, out var enemy) || !enemy.RewardEmitted);

    private Vector3 EquipmentAim(Vector3 origin, float distance, float coneDot)
    {
        var target = Rac1EnemyNodes.Concat(Rac1QueenDamageTargets).Where(IsRac1WeaponAimTarget)
            .Select(node => node.Root.GlobalPosition + Vector3.Up * .45f)
            .Where(point => point.DistanceSquaredTo(origin) is > .1f && point.DistanceSquaredTo(origin) < distance * distance &&
                (point - origin).Normalized().Dot(EquipmentForward) > coneDot && BombHasLineOfSight(origin, point))
            .OrderBy(point => point.DistanceSquaredTo(origin)).FirstOrDefault();
        return target == Vector3.Zero ? origin + EquipmentForward * distance : target;
    }

    private (Vector3 Point, RuntimeWorldScene.DynamicObjectNode? Target)? EquipmentContact(Vector3 from, Vector3 to, float radius)
    {
        if (_player is null || from.DistanceSquaredTo(to) < .000001f) return null;
        var ray = PhysicsRayQueryParameters3D.Create(from, to); ray.Exclude = [_player.GetRid()];
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        float closest = hit.TryGetValue("position", out var point) ? from.DistanceTo(point.AsVector3()) : float.PositiveInfinity;
        RuntimeWorldScene.DynamicObjectNode? target = null;
        Vector3 position = hit.Count > 0 ? point.AsVector3() : to;
        foreach (var (candidate, bounds) in EquipmentGeometry)
        {
            if (!IsRac1WeaponAimTarget(candidate) || !SegmentBoxFraction(from, to, bounds.Grow(radius), out float fraction)) continue;
            var contact = from.Lerp(to, fraction); float length = from.DistanceTo(contact);
            if (length > closest + .001f) continue;
            closest = length; target = candidate; position = contact;
        }
        return float.IsFinite(closest) ? (position, target) : null;
    }

    private static bool SegmentBoxFraction(Vector3 a, Vector3 b, Aabb bounds, out float fraction)
        => ActorContactGeometry.SegmentBoxFraction(a, b, bounds, out fraction);

    private bool ApplyRac1AdditionalDamage(RuntimeWorldScene.DynamicObjectNode target, Rac1GameplayDamageEvent damage)
    {
        if (target.Source.NativeClassId == Rac1FrogQueenGroup.QueenClassId)
            return ApplyRac1QueenWeaponDamage(target, damage);
        if (!Rac1World.Context.MobyRuntime.CanDispatchDamage(damage)) return false;
        bool applied;
        if (Rac1BoltCrate.IsCrateClass(target.Source.NativeClassId))
        {
            CompleteRac1CrateDamage(target, Rac1World.Context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage));
            applied = true;
        }
        else if (target.Source.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
            applied = TryTriggerRac1ExplosiveDamage(target, damage);
        else applied = ApplyRac1EnemyWeaponDamage(target, damage);
        if (applied) Rac1World.Context.DamageTransport.Publish(damage);
        return applied;
    }

    private void FireRac1Blaster(Rac1AdditionalWeaponSession.Shot shot)
    {
        Vector3 origin = EquipmentMuzzle(shot.Weapon);
        Vector3 end = origin + (EquipmentAim(origin, Rac1WeaponReconstructionPolicy.BlasterAimRange, .96f) - origin).Normalized() * Rac1WeaponReconstructionPolicy.BlasterAimRange;
        var node = EquipmentParticle(27, EquipmentColor(Rac1WeaponProfiles.BlasterImpactColor), .18f);
        node.Name = "Rac1Blaster_Class305_Type27";
        _sceneResult!.Root.AddChild(node);
        node.GlobalPosition = origin;
        _rac1EquipmentShots.Add(new(shot.Weapon, shot.Generation, node, origin, (end - origin).Normalized() * Rac1WeaponProfiles.BlasterSpeed)
        {
            Motion = new(ToEffectNative(origin), ToEffectNative((end - origin).Normalized()), 40, Rac1WeaponProfiles.BlasterCarrierTicks),
            AimPoint = ToEffectNative(EquipmentAim(origin, Rac1WeaponReconstructionPolicy.BlasterAimRange, .96f)),
        });
        SpawnEquipmentFlash(origin, 27, EquipmentColor(Rac1WeaponProfiles.BlasterMuzzleColor), .35f, 4);
    }

    private void SpawnRac1ThrownEquipment(Rac1AdditionalWeaponSession.Shot shot)
    {
        FusionWeapons.PlayNativeSound(0, 26, _player!.GlobalPosition); // hero_sound_start(0x1a,0), shared glove throw.
        float yaw = (float)_player!.Rac1CurrentYaw;
        NVector3 body = ToEffectNative(_player.GlobalPosition) + new NVector3(0, 0, (float)-(_avatarPresentation.Avatar?.BaseHeight ?? -.7));
        var origin = Rac1BombProjectileSession.LaunchOrigin(body, yaw);
        var target = ToEffectNative(EquipmentAim(FromEffectNative(origin), 8.5f, .94f));
        var velocity = Rac1BombProjectileSession.LaunchVelocity(origin, target, yaw);
        // Shared glove-origin family is recovered; this flight adapter currently
        // uses the recovered Bomb arc pending the complete class74/230 integrator.
        int nativeClass = shot.Weapon == Rac1WeaponId.MineGlove ? 74 : 230;
        var view = CreateEquipmentWorldView(nativeClass);
        view.Play(0); view.GlobalPosition = FromEffectNative(origin);
        if (nativeClass == 230) view.Scale = Vector3.One * Rac1ArsenalRules.DoomCanisterHeldScale;
        var flying = new HostedEquipmentShot(shot.Weapon, shot.Generation, view, view.GlobalPosition,
            new(-velocity.X, velocity.Z, velocity.Y))
        { Canister = nativeClass == 230 ? new() : null };
        _rac1EquipmentShots.Add(flying);
        var start = FromEffectNative(new(body.X, body.Y, origin.Z));
        if (EquipmentContact(start, view.GlobalPosition, .12f) is { } obstruction)
        { flying.Position = obstruction.Point; flying.GroundHeight = obstruction.Point.Y; flying.Grounded = true; flying.Step = Vector3.Zero; }
        if (nativeClass == 74)
        {
            var mines = _rac1EquipmentShots.Where(item => item.Weapon == Rac1WeaponId.MineGlove).ToArray();
            if (mines.Length > 8) ExplodeEquipment(mines[0], 2, Rac1WeaponProfiles.MineDamage, Rac1WeaponProfiles.MineFlags, 74);
        }
    }

    private Rac1EquipmentView CreateEquipmentWorldView(int nativeClass)
    {
        var view = new Rac1EquipmentView(_rac1EquipmentAssets!.Models[nativeClass], _rac1EquipmentMaterials!);
        _sceneResult!.Root.AddChild(view); return view;
    }
}
