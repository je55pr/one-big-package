using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Existing host boundaries borrowed by the R&C1 missile family. Damage,
/// effect presentation and native inventory remain under their current owners.
/// The missile motion/session, unique target salvo and guided focus are world
/// scoped and are never persisted as campaign equipment.
/// </summary>
internal sealed record Rac1ArsenalMissilePorts(
    Func<Rac1WeaponId, int, Vector3> Muzzle,
    Func<Vector3> Forward,
    Func<int, Rac1EquipmentView> CreateView,
    Func<Vector3, float, float, IEnumerable<int>?, RuntimeWorldScene.DynamicObjectNode?> FindTarget,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> IsAimTarget,
    Func<Vector3, Vector3, float, (Vector3 Point, RuntimeWorldScene.DynamicObjectNode? Target)?> Contact,
    Func<PlayerHost?> Player,
    Func<bool> Alive,
    Func<Rac1WeaponId> Equipped,
    Func<ulong> WorldTick,
    Action<Vector3, int, Color, float, int> Flash,
    Action<Rac1WeaponId, Vector3, float, double, uint> Blast,
    Action<Rac1WeaponId, Vector3> Explosion,
    Action<int, int, Vector3> Sound,
    Func<Node3D> SceneRoot);

internal sealed class Rac1ArsenalMissileHost(Rac1ArsenalMissilePorts ports, Rac1GuidedWeaponCamera camera, Func<long> nextId) : IDisposable
{
    private sealed record Missile(Rac1WeaponId Weapon, long Id, Rac1EquipmentView View,
        Rac1ArsenalProjectile Motion, RuntimeWorldScene.DynamicObjectNode? Target);

    private readonly List<Missile> _missiles = [];
    private readonly HashSet<int> _rynoTargets = [];
    private Rac1RynoSalvoSession _ryno = new();
    private bool _disposed;

    internal int SalvoRemaining => _ryno.Remaining;
    internal bool GuidedActive => _missiles.Any(missile => missile.Weapon == Rac1WeaponId.VisibombGun);
    internal bool CanBeginRyno => !_disposed && _ryno.Remaining == 0;

    internal IReadOnlyList<T> Project<T>(Func<Rac1WeaponId, long, Rac1EquipmentView, T> projection) =>
        _disposed ? [] : _missiles.Select(missile => projection(missile.Weapon, missile.Id, missile.View)).ToArray();

    internal void Reset()
    {
        if (_disposed) return;
        camera.End();
        foreach (var missile in _missiles)
            if (GodotObject.IsInstanceValid(missile.View)) missile.View.QueueFree();
        _missiles.Clear();
        _rynoTargets.Clear();
        _ryno = new();
    }

    internal void Fire(Rac1WeaponId weapon)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (weapon)
        {
            case Rac1WeaponId.Devastator:
                Spawn(weapon, ports.FindTarget(ports.Muzzle(weapon, 0), 70, .85f, null));
                break;
            case Rac1WeaponId.VisibombGun:
                Spawn(weapon, null);
                var player = ports.Player() ?? throw new InvalidOperationException("Guided missiles require a player.");
                camera.Begin(ports.SceneRoot(), player);
                break;
            case Rac1WeaponId.Ryno:
                _ryno.Begin();
                _rynoTargets.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(weapon), "Not an R&C1 missile family weapon.");
        }
    }

    private void Spawn(Rac1WeaponId weapon, RuntimeWorldScene.DynamicObjectNode? target, int barrel = 0)
    {
        var origin = ports.Muzzle(weapon, barrel);
        Vector3 direction = target is null ? ports.Forward() : (target.Root.GlobalPosition + Vector3.Up * .5f - origin).Normalized();
        int cls = weapon switch { Rac1WeaponId.VisibombGun => 172, Rac1WeaponId.Ryno => 457, _ => 153 };
        var view = ports.CreateView(cls); view.Play(0); view.GlobalPosition = origin;
        if (weapon == Rac1WeaponId.Ryno) view.Scale = Vector3.One * Rac1ArsenalRules.RynoRocketScale;
        var motion = new Rac1ArsenalProjectile(Rac1LootPresentation.ToEffectNative(origin), Rac1LootPresentation.ToEffectNative(direction),
            weapon switch { Rac1WeaponId.VisibombGun => Rac1ArsenalHostPolicy.VisibombSpeed, Rac1WeaponId.Ryno => Rac1ArsenalRules.RynoInitialSpeed, _ => Rac1ArsenalRules.RocketSpeed },
            weapon switch { Rac1WeaponId.VisibombGun => Rac1ArsenalRules.VisibombLifetime, Rac1WeaponId.Ryno => Rac1ArsenalRules.RynoRocketLifetime, _ => Rac1ArsenalRules.RocketLifetime });
        _missiles.Add(new(weapon, nextId(), view, motion, target));
        ports.Flash(origin, 27, new(1, .7f, .25f), .55f, 6);
        ports.Sound(weapon == Rac1WeaponId.Ryno ? 457 : Rac1WeaponProfiles.Get(weapon).ModelClass,
            weapon == Rac1WeaponId.Ryno ? 1 : 0, origin);
    }

    internal void Tick()
    {
        if (_disposed || ports.Player() is not { } player) return;
        if (_ryno.Step(ports.Alive() && ports.Equipped() == Rac1WeaponId.Ryno) is { } barrel)
        {
            var target = ports.FindTarget(ports.Muzzle(Rac1WeaponId.Ryno, 0), 100, .1f, _rynoTargets) ??
                ports.FindTarget(ports.Muzzle(Rac1WeaponId.Ryno, 0), 100, .1f, null);
            if (target is not null) _rynoTargets.Add(target.Source.InstanceIndex);
            Spawn(Rac1WeaponId.Ryno, target, barrel);
        }
        foreach (var missile in _missiles.ToArray())
        {
            var from = Rac1LootPresentation.FromEffectNative(missile.Motion.Position);
            bool guided = missile.Weapon == Rac1WeaponId.VisibombGun;
            NVector3? target = missile.Target is { } victim && ports.IsAimTarget(victim)
                ? Rac1LootPresentation.ToEffectNative(victim.Root.GlobalPosition + Vector3.Up * .5f) : null;
            if (guided)
            {
                var steering = player.Rac1RemoteSteering;
                var direction = Rac1LootPresentation.FromEffectNative(missile.Motion.Direction);
                var right = direction.Cross(Vector3.Up).Normalized();
                var desired = (direction + right * steering.X * .15f - Vector3.Up * steering.Y * .15f).Normalized();
                target = Rac1LootPresentation.ToEffectNative(from + desired * 30);
            }
            bool ended = missile.Motion.Step(target, guided ? Rac1ArsenalHostPolicy.GuidedTurnRate : 3.5f,
                guided ? 0 : 30, guided ? Rac1ArsenalHostPolicy.VisibombSpeed :
                    missile.Weapon == Rac1WeaponId.Ryno ? Rac1ArsenalRules.RynoMaximumSpeed : Rac1ArsenalRules.RocketMaximumSpeed);
            var to = Rac1LootPresentation.FromEffectNative(missile.Motion.Position);
            var hit = ports.Contact(from, to, .15f);
            if (hit is not null || ended || guided && (player.Rac1RemoteDetonate ||
                ports.Equipped() != Rac1WeaponId.VisibombGun || !ports.Alive()))
            {
                var position = hit?.Point ?? to;
                ports.Blast(missile.Weapon, position,
                    guided ? Rac1ArsenalHostPolicy.VisibombBlastRadius : Rac1ArsenalHostPolicy.RocketBlastRadius,
                    guided ? 6 : 3, 0x830000);
                ports.Explosion(missile.Weapon, position);
                ports.Sound(guided ? 172 : missile.Weapon == Rac1WeaponId.Ryno ? 457 : 153, 0, position);
                missile.View.QueueFree(); _missiles.Remove(missile);
                if (guided) camera.End();
                continue;
            }
            missile.View.GlobalPosition = to;
            Rac1ArsenalViewPose.Orient(missile.View, Rac1LootPresentation.FromEffectNative(missile.Motion.Direction));
            missile.View.Step();
            if (ports.WorldTick() % 3 == 0) ports.Flash(from, 25, new(.7f, .65f, .6f, .45f), .3f, 18);
            if (guided) camera.Step(to, Rac1LootPresentation.FromEffectNative(missile.Motion.Direction));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
