using Godot;
using OBP.Godot;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;
using static Godot.GodotObject;
using static OBP.Game.Rac1EnemyCoordinates;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

internal sealed class Rac1BirdbotEnemyAdapter(Rac1EnemyHost host) : IRac1EnemyAdapter
{
    private readonly Rac1EnemyHost _host = host;
    internal Rac1NovalisBirdbotSession? Session { get; set; }
    // Temporary host projectile. Retail bird_shot_spawn_l1 is generated from
    // the disc by rac1-native-game, rather than committed as readable C++.
    // We preserve class459 ownership, but projectile mesh, ballistics and hit
    // capsule are not asserted to be retail parity.
    private sealed record HostedBirdbotShot(Node3D Root, int OwnerInstance,
        Vector3 Velocity, float Life);
    private readonly Dictionary<long, HostedBirdbotShot> _rac1BirdbotShots = [];
    private long _rac1NextBirdbotShotId;
    private long _rac1BirdbotShotCount;
    internal long ShotCount => _rac1BirdbotShotCount;
    internal bool HasShot(int owner) => _rac1BirdbotShots.Values.Any(shot => shot.OwnerInstance == owner);

    internal void Reset()
    {
        var shots = _rac1BirdbotShots.Values.ToArray();
        _rac1BirdbotShots.Clear();
        _rac1NextBirdbotShotId = 0;
        _rac1BirdbotShotCount = 0;
        OBP.Game.Diagnostics.DiagnosticCleanup.Run("R&C1 birdbot projectiles",
            shots.Select<HostedBirdbotShot, Action>(shot => () =>
            {
                if (IsInstanceValid(shot.Root) && !shot.Root.IsQueuedForDeletion()) shot.Root.QueueFree();
            }).ToArray());
    }

    public void Tick(Rac1HostedEnemy enemy)
    {
        if (!_host.Owns(enemy) || Session is null || _host.Player is null) return;
        var node = enemy.Node;
        Vector3 position = node.Root.GlobalPosition;
        Vector3 target = _host.Player.GlobalPosition;
        var probe = Session.Step(node.Source.InstanceIndex, new(
            ToEffectNative(position), ToEffectNative(target),
            !_host.Health.Probe().IsDead, enemy.Animation.Wrapped));
        Rac1EnemyHost.SelectRac1EnemySequence(enemy, probe.Sequence, probe.Mode == Rac1NovalisBirdbotSession.HostMode.Hit ? 3 : 8);
        if (probe.Destination is { } next)
            _host.MoveRac1Enemy(enemy, next, next);
        else if (probe.Mode is Rac1NovalisBirdbotSession.HostMode.Shooting or Rac1NovalisBirdbotSession.HostMode.Pursuing)
        {
            enemy.Motion!.Step(ToEffectNative(position), ToEffectNative(target),
                destination: null, enemy.Animation.Blending, enemy.Grounded);
            node.Root.Rotation = new(0, enemy.Motion!.Yaw, 0);
        }

        if (probe.Fire)
        {
            SpawnRac1BirdbotShot(enemy);
            // Original class459 one-shot is available from the Novalis SFX bank;
            // exact native firing-sound selector is not reconstructed.
            _host.Combat.Sound(Rac1NovalisBirdbotSession.ClassId, 0, position);
        }
    }

    private void SpawnRac1BirdbotShot(Rac1HostedEnemy enemy)
    {
        if (_host.Player is null || _host.Scene is null) return;
        Vector3 pos = enemy.Node.Root.GlobalPosition + Vector3.Up * .95f;
        Vector3 aim = _host.Player.GlobalPosition + Vector3.Up * .85f - pos;
        if (aim.LengthSquared() < .01f) return;
        var mesh = new MeshInstance3D
        {
            Name = $"BirdbotShot_i{enemy.Node.Source.InstanceIndex}",
            Mesh = new SphereMesh { Radius = .10f, Height = .2f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, .85f, .20f),
                EmissionEnabled = true,
                Emission = new Color(.9f, .5f, .05f)
            }
        };
        _host.Scene.Root.AddChild(mesh);
        mesh.GlobalPosition = pos;
        long id = ++_rac1NextBirdbotShotId;
        _rac1BirdbotShots.Add(id, new(mesh, enemy.Node.Source.InstanceIndex, aim.Normalized() * 10f, 2.5f));
        _rac1BirdbotShotCount++;
    }

    internal void TickProjectiles(double delta)
    {
        if (_host.Player is null || _rac1BirdbotShots.Count == 0) return;
        foreach (long id in _rac1BirdbotShots.Keys.ToArray())
        {
            var shot = _rac1BirdbotShots[id];
            if (!IsInstanceValid(shot.Root) || shot.Life <= 0)
            {
                RetireRac1BirdbotShot(id);
                continue;
            }
            Vector3 from = shot.Root.GlobalPosition;
            Vector3 to = from + shot.Velocity * (float)Math.Max(0, delta);
            var query = PhysicsRayQueryParameters3D.Create(from, to);
            if (_host.Roster.TryGetValue(shot.OwnerInstance, out var owner))
                query.Exclude = new global::Godot.Collections.Array<Rid> { owner.Body.GetRid() };
            var hit = _host.Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count != 0)
            {
                if (hit.TryGetValue("collider", out var collider) &&
                    collider.AsGodotObject() == _host.Player &&
                    !_host.Health.Probe().IsDead)
                {
                    _host.ApplyRac1EnemyPlayerDamage(new(
                        Rac1GameplayEntityRef.Moby(new(Rac1NovalisBirdbotSession.ClassId, shot.OwnerInstance)),
                        Rac1GameplayEntityRef.Player, 1));
                }
                RetireRac1BirdbotShot(id);
                continue;
            }
            shot.Root.GlobalPosition = to;
            _rac1BirdbotShots[id] = shot with { Life = shot.Life - (float)delta };
        }
    }

    private void RetireRac1BirdbotShot(long id)
    {
        if (!_rac1BirdbotShots.Remove(id, out var shot)) return;
        if (IsInstanceValid(shot.Root)) shot.Root.QueueFree();
    }
    public int ClassId => Rac1NovalisBirdbotSession.ClassId;
    public Rac1EnemyDamageAdmission Damage(Rac1GameplayDamageEvent damage)
    {
        var (probe, lethal) = _host.Context.MobyRuntime.DispatchDamage<(Rac1NovalisBirdbotSession.Probe Probe, bool Lethal)>(damage);
        return new(lethal, probe.Sequence);
    }

    internal void Register(RuntimeWorld world, RuntimeWorldScene.Result result, List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        if (Session is { } birdbots)
            foreach (var authored in (world.DynamicObjects ?? []).Where(item => item.NativeClassId == Rac1NovalisBirdbotSession.ClassId))
            {
                var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored)
                    ?? throw new InvalidDataException("Novalis Birdbot has no original visual.");
                var registered = birdbots.Register(authored, node.State);
                node.ApplyState(registered.Instance.EntityState);
                nodes.Add(node);
            }
    }

}
