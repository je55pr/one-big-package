using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Borrowed scene/player presentation and actor-combat operations. Native
/// projectile motion and gameplay damage stay with their R&C1 authorities.
/// All callbacks are invoked only inside the owning world lease.
/// </summary>
internal sealed record Rac1BombProjectilePorts(
    Func<PlayerHost?> Player,
    Func<bool> SceneReady,
    Func<float> AvatarBaseHeight,
    Func<float> NextAngle,
    Func<bool> ActorActive,
    Func<IEnumerable<(RuntimeWorldScene.DynamicObjectNode Node, Aabb Bounds)>> ActorGeometry,
    Func<int, string, Node3D> CreateVisual,
    Action<Node3D, NVector3> Orient,
    Action<Vector3, Vector3, Vector3> ImpactFx,
    Action<long, long, Vector3, float> ActorBlast,
    Action<RuntimeWorldScene.DynamicObjectNode, Rac1BoltCrateDamageAdmission> CompleteCrate);

/// <summary>
/// One R&C1 world lease owns Bomb Glove projectile flight, impacts and hit
/// dispatch. Scene-only projectile views remain in Rac1WorldPresentation for
/// unchanged HUD, capture and hostile reaction projections.
/// </summary>
internal sealed class Rac1BombProjectileHost(
    Rac1WorldContext _context, Rac1WorldPresentation _presentation,
    Rac1WeaponHost _weapons, Rac1EnemyHost _enemies,
    Rac1BreakableHost _breakables, Rac1BombProjectilePorts ports) : IDisposable
{
    private int _impactCount;
    private bool _disposed;

    internal int ImpactCount => _disposed ? 0 : _impactCount;

    internal void ResetImpactCount()
    {
        if (!_disposed) _impactCount = 0;
    }

    internal void RetireAll()
    {
        if (_disposed) return;
        foreach (var (id, projectile) in _presentation.Projectiles.ToArray())
            Retire(id, projectile);
    }

    internal void Reset()
    {
        if (_disposed) return;
        RetireAll();
        _impactCount = 0;
    }

    internal void Spawn(Rac1BombGloveShot shot)
    {
        if (_disposed || !ports.SceneReady() || ports.Player() is not { } player) return;
        float yaw = (float)player.Rac1CurrentYaw;
        // Host feet-to-native body-origin mapping, shared with avatar alignment.
        NVector3 body = Rac1LootPresentation.ToEffectNative(player.GlobalPosition) + new NVector3(0, 0, (float)-ports.AvatarBaseHeight());
        NVector3 origin = Rac1BombProjectileSession.LaunchOrigin(body, yaw);
        NVector3 forward = new(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        NVector3 target = origin + forward * 8.5f;
        // OBP target discovery only; launch/elevation/clamping are source-owned.
        // Prefer a nearby visible enemy in front, then a crate. The full native
        // reticle/target-history selector and strafe aiming remain separate work.
        var candidates = _enemies.Targets.Concat(_presentation.CrateNodes)
            .Where(node => GodotObject.IsInstanceValid(node.Root) && _enemies.IsRac1GameplayActive(node) && Rac1EnemyHost.HasRac1HostContactGeometry(node))
            .Select(node => (Node: node, Point: Rac1LootPresentation.ToEffectNative(node.Root.GlobalPosition + Vector3.Up * .5f)));
        if (ports.ActorActive())
            candidates = ports.ActorGeometry().Select(target => (target.Node, Rac1LootPresentation.ToEffectNative(target.Bounds.GetCenter())));
        var aim = candidates
            .Select(candidate => (candidate.Node, candidate.Point, Offset: candidate.Point - origin))
            .Where(candidate => candidate.Offset.LengthSquared() is > .25f and < 225f &&
                NVector3.Dot(NVector3.Normalize(candidate.Offset), forward) > .94f &&
                _weapons.Contacts.HasLineOfSight(Rac1LootPresentation.FromEffectNative(origin), Rac1LootPresentation.FromEffectNative(candidate.Point)))
            .OrderBy(candidate => _enemies.Roster.ContainsKey(candidate.Node.Source.InstanceIndex) ? 0 : 1)
            .ThenBy(candidate => candidate.Offset.LengthSquared()).FirstOrDefault();
        if (aim.Node is not null) target = aim.Point;
        var motion = new Rac1BombProjectileSession(origin, Rac1BombProjectileSession.LaunchVelocity(origin, target, yaw),
            new(ports.NextAngle(), ports.NextAngle(), ports.NextAngle()));
        var node = ports.CreateVisual(121, $"Rac1RetailBomb_{shot.Projectile.ProjectileId}_Class121");
        node.GlobalPosition = Rac1LootPresentation.FromEffectNative(origin);
        ports.Orient(node, motion.Rotation);
        var hosted = new Rac1HostedProjectile(node, motion, _weapons.ThrownAnimation.Generation);
        _presentation.Projectiles.Add(shot.Projectile.ProjectileId, hosted);
        // 2ad350..2ad390 checks the body-to-launch segment at launch height.
        // A clear muzzle endpoint alone would let a bomb spawn beyond a thin wall.
        var clearanceStart = Rac1LootPresentation.FromEffectNative(new(body.X, body.Y, origin.Z));
        if (_weapons.Contacts.ProbeBombWorldContact(clearanceStart, Rac1LootPresentation.FromEffectNative(origin)) is { } obstruction)
            Impact(hosted, obstruction);
    }

    internal void StepPhysics()
    {
        if (_disposed || ports.Player() is null || _context.BombGlove is null) return;
        foreach (var pair in _presentation.Projectiles.ToArray())
        {
            var projectile = pair.Value;
            var motion = projectile.Motion;
            if (!GodotObject.IsInstanceValid(projectile.Node)) { Retire(pair.Key, projectile); continue; }
            if (motion.Flying)
            {
                Vector3 from = Rac1LootPresentation.FromEffectNative(motion.Position);
                Vector3 to = Rac1LootPresentation.FromEffectNative(motion.AdvanceFlight());
                Vector3? contact = _weapons.Contacts.ProbeBombWorldContact(from, to);
                float closest = contact is { } hit ? from.DistanceTo(hit) : float.PositiveInfinity;
                foreach (var target in _enemies.Targets.Concat(_presentation.CrateNodes))
                {
                    if (!GodotObject.IsInstanceValid(target.Root) || !_enemies.IsRac1GameplayActive(target) || !Rac1EnemyHost.HasRac1HostContactGeometry(target)) continue;
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
                    Impact(projectile, contact ?? to);
                else
                {
                    projectile.Node.GlobalPosition = to;
                    ports.Orient(projectile.Node, motion.Rotation);
                }
            }
            if (motion.NativeState == 2)
            {
                ResolveBlast(pair.Key, motion, projectile.Generation);
                motion.AdvanceBlast();
            }
            if (motion.Finished) Retire(pair.Key, projectile);
        }
    }

    private void Impact(Rac1HostedProjectile projectile, Vector3 position)
    {
        if (!projectile.Motion.Flying) return;
        projectile.Motion.Impact(Rac1LootPresentation.ToEffectNative(position));
        projectile.Node.Visible = false;
        _impactCount++;
        // The sweep above supplies contact admission; resolve a terrain normal
        // once at impact for the native reflected spray. Enemy-body contact
        // and an expired flight retain the upward fallback.
        Vector3 velocity = Rac1LootPresentation.FromEffectNative(projectile.Motion.Velocity);
        Vector3 direction = velocity.LengthSquared() > .000001f ? velocity.Normalized() : Vector3.Down;
        var ray = PhysicsRayQueryParameters3D.Create(position - direction * .4f, position + direction * .4f);
        ray.Exclude = _weapons.Contacts.RayExclusions();
        var hit = ports.Player()!.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        Vector3 normal = hit.TryGetValue("normal", out var result) && result.AsVector3().LengthSquared() > .5f
            ? result.AsVector3().Normalized() : Vector3.Up;
        Vector3 push = velocity.Bounce(normal);
        push = push.LengthSquared() > .000001f ? push.Normalized() * (2f / 60) : normal * (2f / 60);
        ports.ImpactFx(position, normal, push);
    }

    private void ResolveBlast(long id, Rac1BombProjectileSession motion, long generation)
    {
        Vector3 origin = Rac1LootPresentation.FromEffectNative(motion.Position);
        _breakables.ResolveNovalisBombBreakables(origin, motion.BlastRadius);
        var facts = _enemies.Targets.Concat(_presentation.CrateNodes)
            .Where(node => GodotObject.IsInstanceValid(node.Root) && _enemies.IsRac1GameplayActive(node) && Rac1EnemyHost.HasRac1HostContactGeometry(node))
            .Where(node => origin.DistanceTo(node.Root.GlobalPosition + Vector3.Up * .5f) <= motion.BlastRadius + .35f &&
                _weapons.Contacts.HasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f))
            .OrderBy(node => node.Source.InstanceIndex)
            .Select(node => Rac1MobyContactFacts.FromRuntime(_context.MobyRuntime.Require(node.Source), false)).ToArray();
        var resolution = _context.BombGlove!.ResolveExplosionVolume(id, facts);
        ports.ActorBlast(generation, id, origin, motion.BlastRadius);
        if (resolution.DamageResults.Count == 0) return;
        _context.LastBombContactResolution = resolution;
        foreach (var damage in resolution.DamageEvents)
        {
            _context.DamageTransport.Publish(damage);
            if (!_context.MobyRuntime.CanDispatchDamage(damage) &&
                !Rac1AridiaEnemies.IsEnemyClass(damage.Target.NativeClassId)) continue;
            if (Rac1BoltCrate.IsCrateClass(damage.Target.NativeClassId))
            {
                var admission = _context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage);
                ports.CompleteCrate(_presentation.CrateNodes.Single(node => node.Source.InstanceIndex == damage.Target.RuntimeId), admission);
            }
            else if (damage.Target.NativeClassId == Rac1ExplosiveCrateSession.ClassId)
                _breakables.TryTriggerExplosiveDamage(damage);
            else if (_enemies.Roster.TryGetValue((int)damage.Target.RuntimeId, out var enemy))
                _enemies.ApplyRac1EnemyWeaponDamage(enemy.Node, damage);
        }
        _context.CombatStatus = $"Bomb Glove blast: {resolution.DamageEvents.Count} target(s)";
        GD.Print($"[rac1-gameplay] {_context.CombatStatus}");
    }

    internal void Retire(long id, Rac1HostedProjectile projectile)
    {
        if (_disposed || !_presentation.Projectiles.TryGetValue(id, out var current) ||
            !ReferenceEquals(current, projectile)) return;
        _context.BombGlove?.CompleteProjectile(id);
        if (GodotObject.IsInstanceValid(projectile.Node)) projectile.Node.QueueFree();
        _presentation.Projectiles.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
