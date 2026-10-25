using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private long _rac1BombThrowGeneration;
    private bool _rac1BombThrowActive;
    private int _rac1BombImpactCount;
    private ulong _rac1BombOcclusionFrame = ulong.MaxValue;
    private Rid _rac1BombOcclusionPlayer;
    private global::Godot.Collections.Array<Rid> _rac1BombOcclusionExclusions = [];
    // OBP query adapter: the native 1efc70 primitive/body representation remains
    // unresolved. Sweep a small sphere and use host enemy bounds for contact.
    private readonly SphereShape3D _rac1BombContactShape = new() { Radius = .12f };

    private void StepRac1BombWeaponPhysics()
    {
        if (PlayerPresentationAssets is null || _rac1BombGlove is null || _player is null || !PlayerAlive) return;
        bool fire = _rac1BombFireRequested && !_rac1DetectorEquipped &&
            !_player.Rac1WrenchFrame.Active && !_rac1WrenchThrow.Active;
        _rac1BombFireRequested = false;
        var probe = _rac1BombGlove.Step(fire);
        if (probe.Shot is { } shot)
        {
            _rac1BombThrowGeneration++;
            _rac1BombThrowActive = true;
            SpawnRac1BombProjectile(shot);
            PlayPlayerEquipmentSound(0, 26, _player.GlobalPosition); // 0x2c2.. hero throw, not the glove's equip cue.
            PersistRac1CampaignState("Bomb Glove ammo consumption");
            _rac1CombatStatus = $"Bomb Glove fired: ammo {shot.AmmoBefore}->{shot.AmmoAfter}";
            RefreshRac1HudState();
            GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
        }
        else if (fire && probe.UseAdmission is { Accepted: false } rejected)
            _rac1CombatStatus = $"Bomb Glove use rejected: {rejected.Rejection}";
    }

    private Rac1LocomotionAnimationInput SelectRac1BombThrowAnimation(
        Rac1PlayerAnimationPresentationController controller, Rac1LocomotionAnimationInput input)
    {
        if (!_rac1BombThrowActive) return input;
        if (!PlayerAlive || _player?.Rac1WrenchFrame.Active == true || input.Traversal is not null)
        {
            _rac1BombThrowActive = false;
            return input;
        }
        if (CurrentPlayerAvatarSourceSequence() == 44 &&
            controller.TraversalAuthoredAnimationTime >= Rac1BombProjectileSession.ThrowFinishTime)
        {
            _rac1BombThrowActive = false;
            return input with { GroundEntry = new(0, 0, _rac1BombThrowGeneration, BlendTicks: -1) };
        }
        return input with
        {
            GroundEntry = null,
            Traversal = new(0x23, 44, _rac1BombThrowGeneration,
                Rac1BombProjectileSession.ThrowEntryFrame, Rac1BombProjectileSession.ThrowBlendTicks)
        };
    }

    private void SpawnRac1BombProjectile(Rac1BombGloveShot shot)
    {
        if (_sceneResult is null || _player is null) return;
        float yaw = (float)_player.Rac1CurrentYaw;
        // Host feet-to-native body-origin mapping, shared with avatar alignment.
        NVector3 body = ToEffectNative(_player.GlobalPosition) + new NVector3(0, 0, (float)-(_playerAvatar?.BaseHeight ?? -.7));
        NVector3 origin = Rac1BombProjectileSession.LaunchOrigin(body, yaw);
        NVector3 forward = new(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        NVector3 target = origin + forward * 8.5f;
        // OBP target discovery only; launch/elevation/clamping are source-owned.
        // Prefer a nearby visible enemy in front, then a crate. The full native
        // reticle/target-history selector and strafe aiming remain separate work.
        var candidates = Rac1EnemyNodes.Concat(_rac1CrateNodes)
            .Where(node => IsInstanceValid(node.Root) && IsRac1GameplayActive(node) && HasRac1HostContactGeometry(node))
            .Select(node => (Node: node, Point: ToEffectNative(node.Root.GlobalPosition + Vector3.Up * .5f)));
        if (_actors is not null)
            candidates = RegisteredCombatGeometry().Select(target => (target.Node, ToEffectNative(target.Bounds.GetCenter())));
        var aim = candidates
            .Select(candidate => (candidate.Node, candidate.Point, Offset: candidate.Point - origin))
            .Where(candidate => candidate.Offset.LengthSquared() is > .25f and < 225f &&
                NVector3.Dot(NVector3.Normalize(candidate.Offset), forward) > .94f &&
                BombHasLineOfSight(FromEffectNative(origin), FromEffectNative(candidate.Point)))
            .OrderBy(candidate => _rac1Enemies.ContainsKey(candidate.Node.Source.InstanceIndex) ? 0 : 1)
            .ThenBy(candidate => candidate.Offset.LengthSquared()).FirstOrDefault();
        if (aim.Node is not null) target = aim.Point;
        var motion = new Rac1BombProjectileSession(origin, Rac1BombProjectileSession.LaunchVelocity(origin, target, yaw),
            new(Rac1EffectRandom.Angle(), Rac1EffectRandom.Angle(), Rac1EffectRandom.Angle()));
        var node = CreatePlayerEquipmentEffectNode(121, $"Rac1RetailBomb_{shot.Projectile.ProjectileId}_Class121");
        node.GlobalPosition = FromEffectNative(origin);
        SetBombOrientation(node, motion.Rotation);
        var hosted = new Rac1HostedProjectile(node, motion, _rac1BombThrowGeneration);
        _rac1Projectiles.Add(shot.Projectile.ProjectileId, hosted);
        // 2ad350..2ad390 checks the body-to-launch segment at launch height.
        // A clear muzzle endpoint alone would let a bomb spawn beyond a thin wall.
        var clearanceStart = FromEffectNative(new(body.X, body.Y, origin.Z));
        if (ProbeBombWorldContact(clearanceStart, FromEffectNative(origin)) is { } obstruction)
            ImpactRac1Bomb(hosted, obstruction);
    }

    private void StepRac1BombProjectilesPhysics()
    {
        if (_player is null || _rac1BombGlove is null) return;
        foreach (var pair in _rac1Projectiles.ToArray())
        {
            var projectile = pair.Value;
            var motion = projectile.Motion;
            if (!IsInstanceValid(projectile.Node)) { RetireBomb(pair.Key, projectile); continue; }
            if (motion.Flying)
            {
                Vector3 from = FromEffectNative(motion.Position);
                Vector3 to = FromEffectNative(motion.AdvanceFlight());
                Vector3? contact = ProbeBombWorldContact(from, to);
                float closest = contact is { } hit ? from.DistanceTo(hit) : float.PositiveInfinity;
                foreach (var target in Rac1EnemyNodes.Concat(_rac1CrateNodes))
                {
                    if (!IsInstanceValid(target.Root) || !IsRac1GameplayActive(target) || !HasRac1HostContactGeometry(target)) continue;
                    Vector3 centre = target.Root.GlobalPosition + Vector3.Up * .5f;
                    Vector3 segment = to - from;
                    float t = segment.LengthSquared() > 1e-8f ? Mathf.Clamp((centre - from).Dot(segment) / segment.LengthSquared(), 0, 1) : 0;
                    Vector3 point = from + segment * t;
                    if (point.DistanceTo(centre) <= .55f && from.DistanceTo(point) < closest)
                    {
                        contact = point; closest = from.DistanceTo(point);
                    }
                }
                if (contact is not null || motion.TickFlightCountdown())
                    ImpactRac1Bomb(projectile, contact ?? to);
                else
                {
                    projectile.Node.GlobalPosition = to;
                    SetBombOrientation(projectile.Node, motion.Rotation);
                }
            }
            if (motion.NativeState == 2)
            {
                ResolveRac1BombBlast(pair.Key, motion, projectile.Generation);
                motion.AdvanceBlast();
            }
            if (motion.Finished) RetireBomb(pair.Key, projectile);
        }
    }

    private Vector3? ProbeBombWorldContact(Vector3 from, Vector3 to)
    {
        if (_player is null) return null;
        var space = _player.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _rac1BombContactShape,
            Transform = new(Basis.Identity, from),
            Motion = to - from,
            Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() },
        };
        // CastMotion ignores an initial overlap; check it explicitly so firing
        // beside a wall cannot put a bomb through the far side.
        if (space.IntersectShape(query, 1).Count != 0) return from;
        var fractions = space.CastMotion(query);
        return fractions.Length >= 2 && fractions[0] < 1 ? from.Lerp(to, fractions[0]) : null;
    }

    private void ImpactRac1Bomb(Rac1HostedProjectile projectile, Vector3 position)
    {
        if (!projectile.Motion.Flying) return;
        projectile.Motion.Impact(ToEffectNative(position));
        projectile.Node.Visible = false;
        _rac1BombImpactCount++;
        // The sweep above supplies contact admission; resolve a terrain normal
        // once at impact for the native reflected spray. Enemy-body contact
        // and an expired flight retain the upward fallback.
        Vector3 velocity = FromEffectNative(projectile.Motion.Velocity);
        Vector3 direction = velocity.LengthSquared() > .000001f ? velocity.Normalized() : Vector3.Down;
        var ray = PhysicsRayQueryParameters3D.Create(position - direction * .4f, position + direction * .4f);
        ray.Exclude = Rac1DamageableRayExclusions();
        var hit = _player!.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        Vector3 normal = hit.TryGetValue("normal", out var result) && result.AsVector3().LengthSquared() > .5f
            ? result.AsVector3().Normalized() : Vector3.Up;
        Vector3 push = velocity.Bounce(normal);
        push = push.LengthSquared() > .000001f ? push.Normalized() * (2f / 60) : normal * (2f / 60);
        SpawnRac1BombExplosion(position, impactNormal: normal, reflectedPush: push);
    }

    private bool BombHasLineOfSight(Vector3 from, Vector3 to)
    {
        if (_player is null || from.DistanceSquaredTo(to) < .0001f) return true;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = Rac1DamageableRayExclusions();
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0;
    }

    private global::Godot.Collections.Array<Rid> Rac1DamageableRayExclusions()
    {
        var player = _player ?? throw new InvalidOperationException("World queries require a player.");
        // OBP occlusion policy: damageable bodies are the separately queried
        // victims, not walls. Testing against them made diagonal rays reject a
        // crate's own box, and let the first crate shield its entire stack.
        // Keep terrain/walls in the query, with no arbitrary endpoint allowance.
        // Share one exclusion list across every candidate in this physics tick.
        if (_rac1BombOcclusionFrame != Engine.GetPhysicsFrames() || _rac1BombOcclusionPlayer != player.GetRid())
        {
            _rac1BombOcclusionFrame = Engine.GetPhysicsFrames();
            _rac1BombOcclusionPlayer = player.GetRid();
            _rac1BombOcclusionExclusions = [player.GetRid()];
            foreach (var body in _rac1CrateBodies.Values.Concat<CollisionObject3D>(_rac1Enemies.Values.Select(enemy => enemy.Body)))
                if (IsInstanceValid(body)) _rac1BombOcclusionExclusions.Add(body.GetRid());
            foreach (var body in _actorContactBodies.Values)
                if (IsInstanceValid(body)) _rac1BombOcclusionExclusions.Add(body.GetRid());
        }
        return _rac1BombOcclusionExclusions;
    }

    private void ResolveRac1BombBlast(long id, Rac1BombProjectileSession motion, long generation)
    {
        Vector3 origin = FromEffectNative(motion.Position);
        ResolveRac1NovalisBombBreakables(origin, motion.BlastRadius);
        var facts = Rac1EnemyNodes.Concat(_rac1CrateNodes)
            .Where(node => IsInstanceValid(node.Root) && IsRac1GameplayActive(node) && HasRac1HostContactGeometry(node))
            .Where(node => origin.DistanceTo(node.Root.GlobalPosition + Vector3.Up * .5f) <= motion.BlastRadius + .35f &&
                BombHasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f))
            .OrderBy(node => node.Source.InstanceIndex)
            .Select(node => Rac1MobyContactFacts.FromRuntime(_rac1MobyRuntime.Require(node.Source), false)).ToArray();
        var resolution = _rac1BombGlove!.ResolveExplosionVolume(id, facts);
        ResolveRegisteredBlast(FusionWeaponSession.BombGlove, generation, id,
            origin, motion.BlastRadius, (float)Rac1BombGlove.NativeDamage, Rac1BombGlove.NativeDamageFlags);
        if (resolution.DamageResults.Count == 0) return;
        _rac1LastBombContactResolution = resolution;
        foreach (var damage in resolution.DamageEvents)
        {
            _rac1DamageTransport.Publish(damage);
            if (!_rac1MobyRuntime.CanDispatchDamage(damage) &&
                !Rac1AridiaEnemies.IsEnemyClass(damage.Target.NativeClassId)) continue;
            if (Rac1BoltCrate.IsCrateClass(damage.Target.NativeClassId))
            {
                var admission = _rac1MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage);
                CompleteRac1CrateDamage(_rac1CrateNodes.Single(node => node.Source.InstanceIndex == damage.Target.RuntimeId), admission);
            }
            else if (damage.Target.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
                TryTriggerRac1ExplosiveDamage(
                    _rac1ExplosiveNodes[(int)damage.Target.RuntimeId], damage);
            else if (_rac1Enemies.TryGetValue((int)damage.Target.RuntimeId, out var enemy))
                ApplyRac1EnemyWeaponDamage(enemy.Node, damage);
        }
        _rac1CombatStatus = $"Bomb Glove blast: {resolution.DamageEvents.Count} target(s)";
        GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
    }

    private void RetireBomb(long id, Rac1HostedProjectile projectile)
    {
        _rac1BombGlove?.CompleteProjectile(id);
        if (IsInstanceValid(projectile.Node)) projectile.Node.QueueFree();
        _rac1Projectiles.Remove(id);
    }

    private static void SetBombOrientation(Node3D node, NVector3 angles) =>
        node.Quaternion = FromEffectOrientation(System.Numerics.Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z));

    private sealed record Rac1HostedProjectile(Node3D Node, Rac1BombProjectileSession Motion, long Generation);
}
