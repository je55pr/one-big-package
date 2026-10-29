using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private sealed record ArsenalMissile(Rac1WeaponId Weapon, long Id, Rac1EquipmentView View,
        Rac1ArsenalProjectile Motion, RuntimeWorldScene.DynamicObjectNode? Target);
    private sealed record ArsenalDecoy(Rac1EquipmentView View)
    {
        public Vector3 Velocity;
        public int Age;
        public int Hits;
        public bool Grounded;
        public float GroundHeight;
        public int GroundTicks;
        public int RetiringTicks = -1;
        public int LastAttacker = -1;
        public ulong LastHitTick;
    }
    private sealed record ArsenalDrone(Rac1EquipmentView View, int Slot)
    {
        public RuntimeWorldScene.DynamicObjectNode? Target;
        public int Age;
    }
    private sealed record ArsenalChicken(Rac1HostedEnemy Enemy, Rac1EquipmentView View, Shape3D OriginalShape, Vector3 OriginalShapePosition)
    {
        public int Age;
        public Vector3 Direction;
    }

    private readonly List<ArsenalMissile> _rac1ArsenalMissiles = [];
    private readonly List<ArsenalDecoy> _rac1Decoys = [];
    private readonly List<ArsenalDrone> _rac1Drones = [];
    private readonly Dictionary<int, ArsenalChicken> _rac1Chickens = [];
    private readonly Dictionary<int, int> _rac1MorphExposure = [];
    private readonly List<MeshInstance3D> _rac1ArsenalBeams = [];
    private readonly Rac1TeslaPresentation _rac1TeslaPresentation = new();
    private readonly Dictionary<int, ShaderMaterial> _rac1TeslaMaterials = [];
    private Rac1PickupRandom? _rac1TeslaEffectRandom;
    private StandardMaterial3D? _rac1PlainBeamMaterial;
    private readonly Dictionary<int, ulong> _rac1Taunts = [];
    private Camera3D? _rac1MissileCamera;
    private bool _rac1ArsenalHeld;
    private Rac1RynoSalvoSession _rac1RynoSalvo = new();
    private int _rac1WalloperAge;
    private long _rac1ArsenalId;
    private readonly HashSet<int> _rac1RynoTargets = [];
    private readonly HashSet<int> _rac1WalloperVictims = [];
    private readonly HashSet<int> _rac1TeslaSoundTargets = [];

    private void ResetRac1Arsenal()
    {
        EndRac1GuidedCamera();
        foreach (var node in _rac1ArsenalMissiles.Select(item => (Node)item.View)
            .Concat(_rac1Decoys.Select(item => (Node)item.View)).Concat(_rac1Drones.Select(item => (Node)item.View))
            .Concat(_rac1Chickens.Values.Select(item => (Node)item.View)).Concat(_rac1ArsenalBeams))
            if (IsInstanceValid(node)) node.QueueFree();
        foreach (var chicken in _rac1Chickens.Values.Where(chicken => IsInstanceValid(chicken.Enemy.Node.Root) && IsInstanceValid(chicken.Enemy.Body)))
        {
            // Restore only surfaces that had supported materials before morphing.
            // Materialless Moby children must remain hidden after the chicken resets.
            foreach (var mesh in chicken.Enemy.Node.Root.GetChildren().OfType<MeshInstance3D>())
                mesh.Visible = mesh.MaterialOverride is not null;
            var collider = chicken.Enemy.Body.GetChild<CollisionShape3D>(0);
            collider.Shape = chicken.OriginalShape; collider.Position = chicken.OriginalShapePosition;
        }
        _rac1ArsenalMissiles.Clear(); _rac1Decoys.Clear(); _rac1Drones.Clear(); _rac1Chickens.Clear();
        _rac1MorphExposure.Clear(); _rac1ArsenalBeams.Clear(); _rac1Taunts.Clear();
        _rac1TeslaPresentation.Reset(); _rac1TeslaMaterials.Clear(); _rac1TeslaEffectRandom = null;
        _rac1RynoSalvo = new(); _rac1WalloperAge = 0;
        _rac1ArsenalHeld = false; _rac1WalloperVictims.Clear();
        _rac1RynoTargets.Clear();
        _rac1TeslaSoundTargets.Clear();
        if (_player is not null) { _player.Rac1EquipmentActionActive = false; _player.Rac1EquipmentLungeVelocity = Vector3.Zero; }
    }

    private bool AdmitRac1ArsenalUse() => _rac1Weapons.Equipped switch
    {
        Rac1WeaponId.VisibombGun => !_rac1ArsenalMissiles.Any(item => item.Weapon == Rac1WeaponId.VisibombGun),
        Rac1WeaponId.DroneDevice => _rac1Drones.Count < Rac1ArsenalRules.DroneCapacity,
        Rac1WeaponId.Ryno => _rac1RynoSalvo.Remaining == 0,
        Rac1WeaponId.DecoyGlove => _rac1Decoys.Count < Rac1ArsenalHostPolicy.DecoyCapacity,
        _ => true,
    };

    private RuntimeWorldScene.DynamicObjectNode? FindArsenalTarget(Vector3 origin, float range, float cone = -.1f,
        IEnumerable<int>? excluded = null)
    {
        var skip = excluded?.ToHashSet();
        return Rac1World.Enemies.Targets.Where(node => IsRac1WeaponAimTarget(node) && !_rac1Chickens.ContainsKey(node.Source.InstanceIndex) &&
                skip?.Contains(node.Source.InstanceIndex) != true)
            .Where(node =>
            {
                var delta = node.Root.GlobalPosition + Vector3.Up * .5f - origin;
                return delta.LengthSquared() < range * range && delta.LengthSquared() > .00001f &&
                    delta.Normalized().Dot(EquipmentForward) > cone && BombHasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f);
            }).OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault();
    }

    private void FireRac1Arsenal(Rac1AdditionalWeaponSession.Shot shot)
    {
        switch (shot.Weapon)
        {
            case Rac1WeaponId.Devastator: SpawnArsenalMissile(shot.Weapon, FindArsenalTarget(EquipmentMuzzle(shot.Weapon), 70, .85f)); break;
            case Rac1WeaponId.VisibombGun:
                SpawnArsenalMissile(shot.Weapon, null); BeginRac1GuidedCamera(); break;
            case Rac1WeaponId.Ryno:
                _rac1RynoSalvo.Begin(); _rac1RynoTargets.Clear(); break;
            case Rac1WeaponId.DroneDevice:
                while (_rac1Drones.Count < Rac1ArsenalRules.DroneCapacity)
                {
                    int slot = Enumerable.Range(0, Rac1ArsenalRules.DroneCapacity).First(index => _rac1Drones.All(drone => drone.Slot != index));
                    var view = CreateEquipmentWorldView(479); view.Play(0);
                    view.GlobalPosition = EquipmentMuzzle(shot.Weapon); _rac1Drones.Add(new(view, slot));
                }
                FusionWeapons.PlayNativeSound(483, 0, _player!.GlobalPosition); break;
            case Rac1WeaponId.DecoyGlove:
                FusionWeapons.PlayNativeSound(0, 26, _player!.GlobalPosition);
                var decoy = CreateEquipmentWorldView(203); decoy.Play(0); decoy.Scale = Vector3.One * Rac1ArsenalRules.DecoyInitialScale;
                decoy.GlobalPosition = EquipmentMuzzle(shot.Weapon);
                _rac1Decoys.Add(new(decoy) { Velocity = EquipmentForward * 5 + Vector3.Up * 3 }); break;
            case Rac1WeaponId.Walloper:
                _rac1WalloperAge = 1; _rac1WalloperVictims.Clear(); break;
        }
    }

    private void SpawnArsenalMissile(Rac1WeaponId weapon, RuntimeWorldScene.DynamicObjectNode? target, int barrel = 0)
    {
        var origin = EquipmentMuzzle(weapon, barrel);
        Vector3 direction = target is null ? EquipmentForward : (target.Root.GlobalPosition + Vector3.Up * .5f - origin).Normalized();
        int cls = weapon switch { Rac1WeaponId.VisibombGun => 172, Rac1WeaponId.Ryno => 457, _ => 153 };
        var view = CreateEquipmentWorldView(cls); view.Play(0); view.GlobalPosition = origin;
        if (weapon == Rac1WeaponId.Ryno) view.Scale = Vector3.One * Rac1ArsenalRules.RynoRocketScale;
        var motion = new Rac1ArsenalProjectile(Rac1LootPresentation.ToEffectNative(origin), Rac1LootPresentation.ToEffectNative(direction),
            weapon switch { Rac1WeaponId.VisibombGun => Rac1ArsenalHostPolicy.VisibombSpeed, Rac1WeaponId.Ryno => Rac1ArsenalRules.RynoInitialSpeed, _ => Rac1ArsenalRules.RocketSpeed },
            weapon switch { Rac1WeaponId.VisibombGun => Rac1ArsenalRules.VisibombLifetime, Rac1WeaponId.Ryno => Rac1ArsenalRules.RynoRocketLifetime, _ => Rac1ArsenalRules.RocketLifetime });
        _rac1ArsenalMissiles.Add(new(weapon, ++_rac1ArsenalId, view, motion, target));
        SpawnEquipmentFlash(origin, 27, new(1, .7f, .25f), .55f, 6);
        FusionWeapons.PlayNativeSound(weapon == Rac1WeaponId.Ryno ? 457 : Rac1WeaponProfiles.Get(weapon).ModelClass,
            weapon == Rac1WeaponId.Ryno ? 1 : 0, origin);
    }

    private void StepRac1ArsenalWorld()
    {
        if (_player is null) return;
        if (_rac1RynoSalvo.Step(PlayerLife.Alive && _rac1Weapons.Equipped == Rac1WeaponId.Ryno) is { } barrel)
        {
            var target = FindArsenalTarget(EquipmentMuzzle(Rac1WeaponId.Ryno), 100, .1f, _rac1RynoTargets) ??
                FindArsenalTarget(EquipmentMuzzle(Rac1WeaponId.Ryno), 100, .1f);
            if (target is not null) _rac1RynoTargets.Add(target.Source.InstanceIndex);
            SpawnArsenalMissile(Rac1WeaponId.Ryno, target, barrel);
        }
        foreach (var missile in _rac1ArsenalMissiles.ToArray())
        {
            var from = Rac1LootPresentation.FromEffectNative(missile.Motion.Position);
            bool guided = missile.Weapon == Rac1WeaponId.VisibombGun;
            NVector3? target = missile.Target is { } victim && IsRac1WeaponAimTarget(victim)
                ? Rac1LootPresentation.ToEffectNative(victim.Root.GlobalPosition + Vector3.Up * .5f) : null;
            if (guided)
            {
                var steering = _player.Rac1RemoteSteering;
                var direction = Rac1LootPresentation.FromEffectNative(missile.Motion.Direction);
                var right = direction.Cross(Vector3.Up).Normalized();
                var desired = (direction + right * steering.X * .15f - Vector3.Up * steering.Y * .15f).Normalized();
                target = Rac1LootPresentation.ToEffectNative(from + desired * 30);
            }
            bool ended = missile.Motion.Step(target, guided ? Rac1ArsenalHostPolicy.GuidedTurnRate : 3.5f,
                guided ? 0 : 30, guided ? Rac1ArsenalHostPolicy.VisibombSpeed :
                    missile.Weapon == Rac1WeaponId.Ryno ? Rac1ArsenalRules.RynoMaximumSpeed : Rac1ArsenalRules.RocketMaximumSpeed);
            var to = Rac1LootPresentation.FromEffectNative(missile.Motion.Position);
            var hit = EquipmentContact(from, to, .15f);
            if (hit is not null || ended || guided && (_player.Rac1RemoteDetonate ||
                _rac1Weapons.Equipped != Rac1WeaponId.VisibombGun || !PlayerLife.Alive))
            {
                var position = hit?.Point ?? to;
                ApplyArsenalBlast(missile.Weapon, position, guided ? Rac1ArsenalHostPolicy.VisibombBlastRadius : Rac1ArsenalHostPolicy.RocketBlastRadius,
                    guided ? 6 : 3, 0x830000);
                SpawnRac1WeaponExplosion(missile.Weapon, position);
                FusionWeapons.PlayNativeSound(guided ? 172 : missile.Weapon == Rac1WeaponId.Ryno ? 457 : 153, 0, position);
                missile.View.QueueFree(); _rac1ArsenalMissiles.Remove(missile);
                if (guided) EndRac1GuidedCamera();
                continue;
            }
            missile.View.GlobalPosition = to; OrientArsenalView(missile.View, Rac1LootPresentation.FromEffectNative(missile.Motion.Direction));
            missile.View.Step();
            if (_rac1WeaponWorldTick % 3 == 0) SpawnEquipmentFlash(from, 25, new(.7f, .65f, .6f, .45f), .3f, 18);
            if (guided && _rac1MissileCamera is not null)
            {
                var forward = Rac1LootPresentation.FromEffectNative(missile.Motion.Direction);
                _rac1MissileCamera.GlobalPosition = to - forward * .7f + Vector3.Up * .15f;
                _rac1MissileCamera.LookAt(to + forward * 4);
            }
        }
        StepRac1ArsenalDeployables(); StepRac1ArsenalBeams(); StepRac1Walloper();
    }

    private static void OrientArsenalView(Node3D view, Vector3 direction)
    {
        view.Rotation = new(0, MathF.Atan2(direction.Z, -direction.X),
            -MathF.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length()));
    }

    private void ApplyArsenalBlast(Rac1WeaponId weapon, Vector3 position, float radius, double damage, uint flags)
    {
        foreach (var (node, bounds) in EquipmentGeometry.ToArray())
        {
            var closest = position.Clamp(bounds.Position, bounds.End);
            if (!Rac1World.Enemies.IsRac1GameplayActive(node) || position.DistanceTo(closest) > radius ||
                !BombHasLineOfSight(position + Vector3.Up * .1f, bounds.GetCenter())) continue;
            ApplyRac1AdditionalDamage(node, Rac1WeaponProfiles.DirectDamage(weapon,
                new(node.Source.NativeClassId, node.Source.InstanceIndex), damage, flags));
        }
    }

    private void BeginRac1GuidedCamera()
    {
        EndRac1GuidedCamera();
        _rac1MissileCamera = new Camera3D { Name = "VisibombCamera", Fov = 65, Near = .05f };
        _sceneResult!.Root.AddChild(_rac1MissileCamera); _rac1MissileCamera.MakeCurrent();
        _player!.Rac1RemoteControlActive = true;
    }

    private void EndRac1GuidedCamera()
    {
        if (_rac1MissileCamera is not null && IsInstanceValid(_rac1MissileCamera)) _rac1MissileCamera.QueueFree();
        _rac1MissileCamera = null;
        if (_player is not null && IsInstanceValid(_player))
        { _player.Rac1RemoteControlActive = false; if (IsInstanceValid(_player.Camera)) _player.Camera.MakeCurrent(); }
    }

    private void StepRac1ArsenalDeployables()
    {
        foreach (var decoy in _rac1Decoys.ToArray())
        {
            decoy.Age++;
            if (decoy.RetiringTicks >= 0)
            {
                decoy.View.Step();
                decoy.View.GlobalPosition = new(decoy.View.GlobalPosition.X,
                    decoy.GroundHeight + decoy.View.FootOffset * decoy.View.Scale.X, decoy.View.GlobalPosition.Z);
                if (++decoy.RetiringTicks >= 60)
                { decoy.View.QueueFree(); _rac1Decoys.Remove(decoy); }
                continue;
            }
            decoy.View.Scale = Vector3.One * Math.Min(1, decoy.View.Scale.X + Rac1ArsenalRules.DecoyGrowthPerTick);
            if (!decoy.Grounded)
            {
                var from = decoy.View.GlobalPosition;
                var next = from + decoy.Velocity / 60;
                decoy.Velocity += Vector3.Down * (18f / 60);
                if (EquipmentContact(from, next, .1f) is { } hit)
                {
                    decoy.GroundHeight = hit.Point.Y;
                    decoy.View.GlobalPosition = hit.Point + Vector3.Up * decoy.View.FootOffset;
                    decoy.Grounded = true; decoy.View.Play(1, false, ++_rac1ArsenalId, 2);
                }
                else decoy.View.GlobalPosition = next;
            }
            foreach (int sound in decoy.View.Step()) FusionWeapons.PlayNativeSound(203, sound, decoy.View.GlobalPosition);
            if (decoy.Grounded)
            {
                decoy.GroundTicks++;
                decoy.View.GlobalPosition = new(decoy.View.GlobalPosition.X, decoy.GroundHeight + decoy.View.FootOffset, decoy.View.GlobalPosition.Z);
            }
            if (decoy.GroundTicks >= Rac1ArsenalRules.DecoyLifetime || decoy.Hits >= 3)
            {
                decoy.RetiringTicks = 0;
                decoy.View.BlendTo(0, 60, ++_rac1ArsenalId); // 0x2c50d0..2c5108, fold over60 ticks.
            }
            else if (decoy.View.GlobalPosition.Y < -100)
            {
                SpawnEquipmentFlash(decoy.View.GlobalPosition + Vector3.Up * .6f, 25, new(.7f, .7f, .7f), 1, 20);
                decoy.View.QueueFree(); _rac1Decoys.Remove(decoy);
            }
        }
        foreach (var drone in _rac1Drones.ToArray())
        {
            drone.Age++; var from = drone.View.GlobalPosition;
            if (drone.Target is null || !IsRac1WeaponAimTarget(drone.Target))
                drone.Target = FindArsenalTarget(from, Rac1ArsenalHostPolicy.DroneInterceptRange);
            if (drone.Target is { } target)
            {
                var goal = target.Root.GlobalPosition + Vector3.Up * .6f;
                var next = from.MoveToward(goal, Rac1ArsenalHostPolicy.DroneSpeed / 60);
                var hit = EquipmentContact(from, next, .23f);
                if (hit is not null || next.DistanceTo(goal) < .35f)
                {
                    ApplyArsenalBlast(Rac1WeaponId.DroneDevice, hit?.Point ?? next, 1.5f, Rac1ArsenalRules.DroneDamage, 0x10000);
                    SpawnRac1WeaponExplosion(Rac1WeaponId.DroneDevice, hit?.Point ?? next);
                    FusionWeapons.PlayNativeSound(479, 1, hit?.Point ?? next);
                    drone.View.QueueFree(); _rac1Drones.Remove(drone); continue;
                }
                drone.View.GlobalPosition = next; OrientArsenalView(drone.View, next - from);
            }
            else
            {
                float phase = _rac1WeaponWorldTick / 45f + drone.Slot * MathF.Tau / Rac1ArsenalRules.DroneCapacity;
                var goal = _player!.GlobalPosition + new Vector3(MathF.Cos(phase) * Rac1ArsenalHostPolicy.DroneOrbitRadius,
                    1 + MathF.Sin(phase * 2) * .15f, MathF.Sin(phase) * Rac1ArsenalHostPolicy.DroneOrbitRadius);
                drone.View.GlobalPosition = from.MoveToward(goal, Rac1ArsenalHostPolicy.DroneSpeed / 60);
                OrientArsenalView(drone.View, goal - from);
            }
            drone.View.Step();
        }
        foreach (var pair in _rac1Chickens.ToArray())
        {
            var chicken = pair.Value;
            if (!Rac1World.Enemies.IsRac1GameplayActive(chicken.Enemy.Node) || chicken.Enemy.RewardEmitted)
            { chicken.View.QueueFree(); _rac1Chickens.Remove(pair.Key); continue; }
            if (chicken.Enemy.NeedsGroundPlacement)
            {
                if (!Rac1World.Enemies.TryGroundRac1Enemy(chicken.Enemy, upwardReach: 1, downwardReach: 1)) continue;
                chicken.Enemy.NeedsGroundPlacement = false;
            }
            chicken.Age++; var body = chicken.Enemy.Body;
            if (chicken.Age % 90 == 1) chicken.Direction = EquipmentForward.Rotated(Vector3.Up, _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, MathF.PI));
            bool supported = EquipmentGround(body.GlobalPosition + chicken.Direction * .5f, .4f, .8f) is not null;
            body.Velocity = (supported ? chicken.Direction : Vector3.Zero) * .8f + Vector3.Down * 2;
            body.MoveAndSlide(); chicken.Enemy.Node.Root.GlobalPosition = body.GlobalPosition;
            chicken.View.Play(1); OrientArsenalView(chicken.View, chicken.Direction);
            chicken.View.Rotation -= new Vector3(0, chicken.Enemy.Node.Root.Rotation.Y, 0); chicken.View.Step();
        }
    }

    private Vector3 ArsenalEnemyGoal(Rac1HostedEnemy enemy, Vector3 player)
    {
        var position = enemy.Node.Root.GlobalPosition;
        if (_rac1Taunts.GetValueOrDefault(enemy.Node.Source.InstanceIndex) > _rac1WeaponWorldTick) return player;
        var decoy = _rac1Decoys.Where(item => item.Grounded && item.RetiringTicks < 0 && item.Hits < 3 && position.DistanceSquaredTo(item.View.GlobalPosition) < 900 &&
            BombHasLineOfSight(position + Vector3.Up * .5f, item.View.GlobalPosition + Vector3.Up * .5f))
            .OrderBy(item => position.DistanceSquaredTo(item.View.GlobalPosition)).FirstOrDefault();
        return decoy?.View.GlobalPosition ?? player;
    }

    private bool ArsenalEnemyHitsDecoy(Rac1HostedEnemy enemy)
    {
        var decoy = _rac1Decoys.FirstOrDefault(item => item.Grounded && item.RetiringTicks < 0 && item.View.GlobalPosition.DistanceTo(enemy.Body.GlobalPosition) < 1.8f);
        if (decoy is null) return false;
        int attacker = enemy.Node.Source.InstanceIndex;
        // 0x2c4c9c..2c4cc4 filters the same attacker for30 ticks.
        if (attacker != decoy.LastAttacker || _rac1WeaponWorldTick - decoy.LastHitTick >= 30)
        {
            decoy.LastAttacker = attacker; decoy.LastHitTick = _rac1WeaponWorldTick;
            decoy.Hits++; FusionWeapons.PlayNativeSound(203, 0, decoy.View.GlobalPosition);
        }
        return true;
    }

    private void StepRac1ArsenalBeams()
    {
        foreach (var beam in _rac1ArsenalBeams) beam.Visible = false;
        foreach (int key in _rac1MorphExposure.Keys.ToArray())
            if (--_rac1MorphExposure[key] <= 0) _rac1MorphExposure.Remove(key);
        if (!_rac1ArsenalHeld) { _rac1TeslaSoundTargets.Clear(); return; }
        var weapon = _rac1Weapons.Equipped;
        if (weapon != Rac1WeaponId.TeslaClaw) _rac1TeslaSoundTargets.Clear();
        if (weapon is not (Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter)) return;
        if (weapon == Rac1WeaponId.TeslaClaw && _rac1Weapons.GetAmmo(19) <= 0) return;
        if (weapon == Rac1WeaponId.TeslaClaw) _rac1TeslaPresentation.Advance();
        var from = EquipmentMuzzle(weapon);
        if (weapon is Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay && _rac1WeaponWorldTick % 3 == 0)
            SpawnEquipmentFlash(from, 27, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1, .85f) : new(.4f, 1, .25f, .75f), .28f, 5);
        if (weapon == Rac1WeaponId.Taunter)
        {
            if (_rac1WeaponWorldTick % 8 == 0)
            {
                var ring = EquipmentParticle(27, new(.3f, .7f, 1, .6f), .6f);
                _sceneResult!.Root.AddChild(ring); ring.GlobalPosition = from + EquipmentForward * .5f;
                _rac1WeaponMotes.Add(new(ring, EquipmentForward * .25f));
            }
            foreach (var node in Rac1World.Enemies.Targets.Where(IsRac1WeaponAimTarget))
            {
                var delta = node.Root.GlobalPosition - _player!.GlobalPosition;
                if (delta.LengthSquared() < Rac1ArsenalRules.TaunterRange * Rac1ArsenalRules.TaunterRange &&
                    delta.Normalized().Dot(EquipmentForward) > MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle) &&
                    BombHasLineOfSight(from, node.Root.GlobalPosition + Vector3.Up * .4f)) _rac1Taunts[node.Source.InstanceIndex] = _rac1WeaponWorldTick + 60;
            }
            foreach (var mine in _rac1EquipmentShots.Where(shot => shot.Weapon == Rac1WeaponId.MineGlove))
                if (mine.Position.DistanceTo(from) < Rac1ArsenalRules.TaunterRange &&
                    (mine.Position - from).Normalized().Dot(EquipmentForward) > MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle)) mine.Taunted = true;
            // 0x2b6bc8 also hits one of the ordinary crate classes in its
            // 12-unit forward cone. The mine interaction is tracked separately.
            if (_rac1WeaponWorldTick % 30 == 0)
            {
                // Native class 505 is admitted by this cone, but its object identity is not yet confirmed.
                var crate = EquipmentTargets.Where(node => node.Source.NativeClassId is Rac1ClassIds.WoodenCrate or Rac1ClassIds.NanotechCrate or 505 or Rac1ClassIds.AmmoCrate)
                    .FirstOrDefault(node => node.Root.GlobalPosition.DistanceTo(from) < 12 &&
                        (node.Root.GlobalPosition - from).Normalized().Dot(EquipmentForward) > MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle) &&
                        BombHasLineOfSight(from, node.Root.GlobalPosition + Vector3.Up * .4f));
                if (crate is not null) ApplyRac1AdditionalDamage(crate,
                    Rac1WeaponProfiles.DirectDamage(weapon, new(crate.Source.NativeClassId, crate.Source.InstanceIndex), 1, 0x10000));
            }
            return;
        }
        var first = FindArsenalTarget(from, weapon == Rac1WeaponId.TeslaClaw ? Rac1ArsenalRules.TeslaRange : Rac1ArsenalHostPolicy.MorphRange, .5f);
        if (first is null)
        {
            _rac1TeslaSoundTargets.Clear();
            var end = from + EquipmentForward * (weapon == Rac1WeaponId.MorphORay ? 3 : .55f);
            var hit = EquipmentContact(from, end, .01f);
            DrawArsenalBeam(0, from, hit?.Point ?? end, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1) : new(.4f, 1, .25f));
            return;
        }
        var targets = new List<RuntimeWorldScene.DynamicObjectNode> { first };
        if (weapon == Rac1WeaponId.TeslaClaw)
        {
            var second = FindArsenalTarget(from, Rac1ArsenalRules.TeslaRange, .5f, [first.Source.InstanceIndex]);
            if (second is not null) targets.Add(second);
            _rac1TeslaSoundTargets.RemoveWhere(id => targets.All(target => target.Source.InstanceIndex != id));
            foreach (var target in targets)
                if (_rac1TeslaSoundTargets.Add(target.Source.InstanceIndex)) FusionWeapons.PlayNativeSound(177, 1, from); // 0x2b9870 lock acquisition.
        }
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i]; var to = target.Root.GlobalPosition + Vector3.Up * .5f;
            DrawArsenalBeam(i, from, to, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1) : new(.4f, 1, .25f));
            if (weapon == Rac1WeaponId.TeslaClaw)
            {
                if (_rac1WeaponWorldTick % 10 == 0) ApplyRac1AdditionalDamage(target,
                    Rac1WeaponProfiles.DirectDamage(weapon, new(target.Source.NativeClassId, target.Source.InstanceIndex), Rac1ArsenalRules.TeslaDamage, Rac1ArsenalRules.TeslaDamageFlags));
            }
            else
            {
                int progress = _rac1MorphExposure.GetValueOrDefault(target.Source.InstanceIndex) + 2;
                _rac1MorphExposure[target.Source.InstanceIndex] = progress;
                if (progress >= Rac1ArsenalHostPolicy.MorphExposureTicks) MorphArsenalEnemy(target);
            }
        }
    }

    private void DrawArsenalBeam(int index, Vector3 from, Vector3 to, Color color)
    {
        if (_rac1Weapons.Equipped == Rac1WeaponId.TeslaClaw && _rac1EquipmentAssets?.Effects is { } effects)
        {
            DrawRac1TeslaRibbons(index, from, to, effects);
            return;
        }
        while (_rac1ArsenalBeams.Count <= index)
        {
            var beam = new MeshInstance3D
            {
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                }
            };
            _sceneResult!.Root.AddChild(beam); _rac1ArsenalBeams.Add(beam);
        }
        _rac1ArsenalBeams[index].MaterialOverride = _rac1PlainBeamMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var mesh = _rac1ArsenalBeams[index].Mesh as ImmediateMesh ?? new ImmediateMesh();
        mesh.ClearSurfaces(); var side = (to - from).Cross(Vector3.Up).Normalized() * .045f;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        var points = new List<Vector3> { from };
        for (int part = 1; part < 10; part++) points.Add(from.Lerp(to, part / 10f) + new Vector3(
            _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, .13f), _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, .13f), _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, .13f)));
        points.Add(to);
        for (int part = 0; part + 1 < points.Count; part++)
            foreach (var p in new[] { points[part] - side, points[part] + side, points[part + 1] + side,
                points[part] - side, points[part + 1] + side, points[part + 1] - side })
            { mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(p); }
        mesh.SurfaceEnd(); _rac1ArsenalBeams[index].Mesh = mesh; _rac1ArsenalBeams[index].Visible = true;
    }

    private void DrawRac1TeslaRibbons(int index, Vector3 from, Vector3 to, Rac1EffectAssets effects)
    {
        _rac1TeslaEffectRandom ??= Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount)));
        var ribbons = _rac1TeslaPresentation.ProjectTargetLine(Rac1LootPresentation.ToEffectNative(from), Rac1LootPresentation.ToEffectNative(to), _rac1TeslaEffectRandom);
        Vector3 eye = GetViewport().GetCamera3D()?.GlobalPosition ?? from + Vector3.Up * 5;
        for (int layer = 0; layer < ribbons.Count; layer++)
        {
            int slot = index * 4 + layer;
            while (_rac1ArsenalBeams.Count <= slot)
            {
                var node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                _sceneResult!.Root.AddChild(node); _rac1ArsenalBeams.Add(node);
            }
            var ribbon = ribbons[layer];
            if (!_rac1TeslaMaterials.TryGetValue(ribbon.Texture, out var material))
            {
                var texture = effects.FxTextures[ribbon.Texture];
                material = new ShaderMaterial { Shader = new Shader { Code = """
                    shader_type spatial;
                    render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;
                    uniform sampler2D art : source_color, filter_linear, repeat_enable;
                    void fragment() {
                        vec4 texel = texture(art, UV);
                        ALBEDO = texel.rgb * COLOR.rgb;
                        ALPHA = texel.a * COLOR.a;
                    }
                    """ } };
                material.SetShaderParameter("art", ImageTexture.CreateFromImage(Image.CreateFromData(texture.Width,
                    texture.Height, false, Image.Format.Rgba8, texture.Rgba)));
                _rac1TeslaMaterials.Add(ribbon.Texture, material);
            }
            var beam = _rac1ArsenalBeams[slot]; beam.MaterialOverride = material;
            var mesh = beam.Mesh as ArrayMesh;
            if (mesh is null) { var previous = beam.Mesh; mesh = new ArrayMesh(); beam.Mesh = mesh; previous?.Dispose(); }
            var positions = new List<double>(); var uvs = new List<float>(); var colors = new List<float>();
            uint rgba = ribbon.Color;
            var tint = new Color((rgba & 255) / 128f, (rgba >> 8 & 255) / 128f, (rgba >> 16 & 255) / 128f, (rgba >> 24) / 128f);
            var points = ribbon.Points.Select(Rac1LootPresentation.FromEffectNative).ToArray();
            var left = new Vector3[points.Length]; var right = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
                Vector3 side = tangent.Cross(points[i] - eye);
                if (side.LengthSquared() < .000001f) side = tangent.Cross(Vector3.Right);
                if (side.LengthSquared() < .000001f) side = Vector3.Forward;
                side = side.Normalized() * ribbon.HalfWidth;
                left[i] = points[i] - side; right[i] = points[i] + side;
            }
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector3 la = left[i], ra = right[i], lb = left[i + 1], rb = right[i + 1];
                if (ribbon.EndExtension > 0)
                {
                    // 0x2bae40..2bafdc brings each blue segment's ends onto
                    // the nearer camera sphere, then extends it along its axis.
                    Vector3 pa = points[i], pb = points[i + 1];
                    float da = pa.DistanceTo(eye), db = pb.DistanceTo(eye);
                    if (da < db && db > .000001f) pb = eye + (pb - eye) * (da / db);
                    else if (da > .000001f) pa = eye + (pa - eye) * (db / da);
                    Vector3 extension = (pb - pa) * ribbon.EndExtension;
                    pa -= extension; pb += extension;
                    Vector3 side = (pb - pa).Cross(pa - eye).Normalized() * ribbon.HalfWidth;
                    la = pa - side; ra = pa + side; lb = pb - side; rb = pb + side;
                }
                float a = _rac1TeslaPresentation.TexturePhase, b = a + 1;
                Add(la, a, 0, i); Add(ra, a, 1, i); Add(rb, b, 1, i + 1);
                Add(la, a, 0, i); Add(rb, b, 1, i + 1); Add(lb, b, 0, i + 1);
            }
            EffectMeshSceneView.Upload(mesh, new(effects.FxTextures[ribbon.Texture].AssetKind, ribbon.Texture,
                positions.ToArray(), uvs.ToArray(), Enumerable.Range(0, positions.Count / 3).ToArray(), colors.ToArray()));
            beam.Visible = true;

            void Add(Vector3 p, float u, float v, int point)
            {
                var vertexColor = tint;
                if (point == 0 || point == points.Length - 1) vertexColor.A = 0;
                positions.AddRange([-p.X, p.Y, p.Z]); uvs.AddRange([u, v]);
                colors.AddRange([vertexColor.R, vertexColor.G, vertexColor.B, vertexColor.A]);
            }
        }
    }

    private void MorphArsenalEnemy(RuntimeWorldScene.DynamicObjectNode target)
    {
        if (_rac1Chickens.ContainsKey(target.Source.InstanceIndex) || !Rac1World.Enemies.Roster.TryGetValue(target.Source.InstanceIndex, out var enemy)) return;
        var view = CreateEquipmentWorldView(270); view.Reparent(target.Root, false); view.Position = Vector3.Up * view.FootOffset;
        view.Scale = Vector3.One / target.Root.Scale;
        foreach (var mesh in target.Root.GetChildren().OfType<MeshInstance3D>()) mesh.Visible = false;
        Rac1World.Enemies.SetRac1EnemyFlame(enemy, false); enemy.Reaction = null;
        var collider = enemy.Body.GetChild<CollisionShape3D>(0);
        var originalShape = collider.Shape; var originalPosition = collider.Position;
        collider.Shape = new CapsuleShape3D { Radius = .24f, Height = .65f }; collider.Position = Vector3.Up * .325f;
        _rac1Chickens.Add(target.Source.InstanceIndex, new(enemy, view, originalShape, originalPosition));
        _rac1MorphExposure.Remove(target.Source.InstanceIndex);
        SpawnEquipmentFlash(target.Root.GlobalPosition + Vector3.Up * .5f, 27, new(.4f, 1, .3f), 1, 12);
    }

    private void StepRac1Walloper()
    {
        _player!.Rac1EquipmentLungeVelocity = Vector3.Zero;
        _player.Rac1EquipmentActionActive = false;
        if (_rac1WalloperAge <= 0) return;
        int age = _rac1WalloperAge++;
        if (age >= Rac1ArsenalRules.WalloperDurationTicks || _rac1Weapons.Equipped != Rac1WeaponId.Walloper || !PlayerLife.Alive)
        { _rac1WalloperAge = 0; return; }
        if (age == 10) FusionWeapons.PlayNativeSound(0, 53, _player.GlobalPosition); // Ratchet seq103 cue2.5.
        if (age == 16) FusionWeapons.PlayNativeSound(0, 30, _player.GlobalPosition); // Ratchet seq103 cue4.
        _player.Rac1EquipmentActionActive = true;
        if (age > Rac1ArsenalRules.WalloperLungeFrom && age < Rac1ArsenalRules.WalloperLungeTo)
            _player.Rac1EquipmentLungeVelocity = EquipmentForward * Rac1ArsenalRules.WalloperLungeSpeed;
        if (age <= Rac1ArsenalRules.WalloperLungeFrom || age >= Rac1ArsenalRules.WalloperContactTo) return;
        var centre = _player!.GlobalPosition + EquipmentForward * .8f + Vector3.Up * .55f;
        foreach (var (node, bounds) in EquipmentGeometry)
        {
            if (_rac1WalloperVictims.Contains(node.Source.InstanceIndex) || centre.DistanceTo(centre.Clamp(bounds.Position, bounds.End)) > .8f ||
                !BombHasLineOfSight(centre, bounds.GetCenter())) continue;
            if (ApplyRac1AdditionalDamage(node, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Walloper,
                new(node.Source.NativeClassId, node.Source.InstanceIndex), 3, 0x30000)))
                _rac1WalloperVictims.Add(node.Source.InstanceIndex);
        }
        SpawnEquipmentFlash(EquipmentMuzzle(Rac1WeaponId.Walloper), 27, new(.5f, .7f, 1, .7f), .55f, 3);
    }
}
