using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private void TickRac1BombGlove(double delta)
    {
        if (_rac1BombGlove is null || _rac1Nanotech.Probe().IsDead) return;
        _rac1BombTickAccumulator += Math.Max(0d, delta) * Rac1NativeTicksPerSecond;
        while (_rac1BombTickAccumulator >= 1d)
        {
            bool fire = _rac1BombFireRequested;
            var probe = _rac1BombGlove.Step(fire);
            if (fire) _rac1BombFireRequested = false;
            _rac1BombTickAccumulator -= 1d;
            if (probe.Shot is { } shot)
            {
                SpawnRac1BombProjectile(shot);
                PersistRac1CampaignState("Bomb Glove ammo consumption");
                _rac1CombatStatus =
                    $"Bomb Glove fired: ammo {shot.AmmoBefore}->{shot.AmmoAfter}; sequence {shot.Admission.NativePlayerSequenceId}";
                RefreshRac1HudState();
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
            }
            else if (fire && probe.UseAdmission is { Accepted: false } rejected)
            {
                _rac1CombatStatus = $"Bomb Glove use rejected: {rejected.Rejection}";
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
            }
        }
    }

    private void SpawnRac1BombProjectile(Rac1BombGloveShot shot)
    {
        if (_sceneResult is null || _player is null) return;

        // The corrected item-10 path has a recovered dedicated launch/step frame,
        // but its full launch-origin construction is not yet promoted. Keep only
        // the direct native-yaw direction mapping and retain the existing visible
        // muzzle offset as an explicit host presentation fallback.
        double nativeYaw = _player.Rac1CurrentYaw;
        Vector3 direction = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
            Math.Cos(nativeYaw),
            Math.Sin(nativeYaw));
        if (direction.LengthSquared() <= 1e-5f) return;
        direction = direction.Normalized();

        var node = new Node3D { Name = $"rac1_bomb_projectile_{shot.Projectile.ProjectileId}" };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.48f, 0.08f),
            },
        });
        _sceneResult.Root.AddChild(node);
        node.GlobalPosition = _player.GlobalPosition + Vector3.Up * 1.1f + direction * 0.8f;
        _rac1Projectiles.Add(shot.Projectile.ProjectileId, new Rac1HostedProjectile(node, direction));
    }

    private void TickRac1Projectiles(double delta)
    {
        if (_rac1BombGlove is null || _rac1Projectiles.Count == 0) return;
        foreach (var pair in _rac1Projectiles.ToArray())
        {
            var projectile = pair.Value;
            if (!IsInstanceValid(projectile.Node))
            {
                _rac1Projectiles.Remove(pair.Key);
                continue;
            }

            Vector3 start = projectile.Node.GlobalPosition;
            Vector3 end = start + projectile.Direction * Rac1BombPresentationSpeed * (float)Math.Max(0d, delta);
            projectile.Node.GlobalPosition = end;
            projectile.Age += (float)Math.Max(0d, delta);

            bool impacted = false;
            Vector3 segment = end - start;
            var contacts = _rac1HostileNodes.Values
                .Where(hostile =>
                    IsInstanceValid(hostile.Root) &&
                    IsRac1GameplayActive(hostile) &&
                    HasRac1HostContactGeometry(hostile))
                .Select(hostile =>
                {
                    Vector3 target = hostile.Root.GlobalPosition + Vector3.Up * 0.5f;
                    float t = segment.LengthSquared() <= 1e-6f
                        ? 0f
                        : Mathf.Clamp((target - start).Dot(segment) / segment.LengthSquared(), 0f, 1f);
                    float separation = target.DistanceTo(start + segment * t);
                    return new { Hostile = hostile, T = t, Separation = separation };
                })
                .Where(candidate => candidate.Separation <= Rac1BombHostileContactRadius)
                .OrderBy(candidate => candidate.T)
                .ThenBy(candidate => candidate.Hostile.Source.InstanceIndex)
                .ToArray();

            var contactFacts = contacts
                .Select(contact =>
                {
                    var hostile = contact.Hostile;
                    var runtime = _rac1MobyRuntime.Require(hostile.Source);
                    return Rac1MobyContactFacts.FromRuntime(
                        runtime,
                        isSourceMoby: false);
                })
                .ToArray();
            var contactResolution = _rac1BombGlove.ResolveGoal1ContactVolume(
                pair.Key,
                contactFacts);
            if (contactResolution.ProjectileCompleted)
                _rac1LastBombContactResolution = contactResolution;

            if (contactResolution.ProjectileCompleted)
            {
                int damageEventCount = contactResolution.DamageEvents.Count;
                int admittedConsequenceCount = 0;
                foreach (var damageEvent in contactResolution.DamageEvents)
                {
                    _rac1DamageTransport.Publish(damageEvent);
                    if (_rac1MobyRuntime.CanDispatchDamage(damageEvent))
                        admittedConsequenceCount++;
                }

                _rac1CombatStatus =
                    $"Bomb Glove contact: {damageEventCount} transported damage event(s), " +
                    $"{admittedConsequenceCount} recovered class-local consequence(s) admitted";
                GD.Print($"[rac1-gameplay] {_rac1CombatStatus}");
                impacted = true;
            }

            if (impacted || projectile.Age >= Rac1BombPresentationLifetime)
            {
                if (!impacted) _rac1BombGlove.CompleteProjectile(pair.Key);
                projectile.Node.QueueFree();
                _rac1Projectiles.Remove(pair.Key);
            }
        }
    }

    private sealed class Rac1HostedProjectile(Node3D node, Vector3 direction)
    {
        public Node3D Node { get; } = node;
        public Vector3 Direction { get; } = direction;
        public float Age { get; set; }
    }
}
