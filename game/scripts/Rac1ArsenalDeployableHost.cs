using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// World-specific adapters for the recovered R&C1 decoy and drone behaviours.
/// Source damage, target admission, native audio and projectile effects are
/// delegated to the established combat and presentation owners.
/// </summary>
internal sealed record Rac1ArsenalDeployablePorts(
    Func<int, Rac1EquipmentView> CreateView,
    Func<Rac1WeaponId, Vector3> Muzzle,
    Func<Vector3> Forward,
    Func<PlayerHost?> Player,
    Func<ulong> WorldTick,
    Func<Vector3, float, float, RuntimeWorldScene.DynamicObjectNode?> FindTarget,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> IsAimTarget,
    Func<Vector3, Vector3, float, (Vector3 Point, RuntimeWorldScene.DynamicObjectNode? Target)?> Contact,
    Func<Vector3, Vector3, bool> HasLineOfSight,
    Action<Vector3, int, Color, float, int> Flash,
    Action<Rac1WeaponId, Vector3, float, double, uint> Blast,
    Action<Rac1WeaponId, Vector3> Explosion,
    Action<int, int, Vector3> Sound);

/// <summary>
/// One world lease for deployed Decoy Glove models and Drone Device attackers.
/// Does not own native inventory, actor damage/loot or world enemy sessions.
/// Retired models are freed with the same queues and frame ordering as the
/// original arsenal host; no generic projectile behaviour is substituted.
/// </summary>
internal sealed class Rac1ArsenalDeployableHost(Rac1ArsenalDeployablePorts ports, Func<long> nextId,
    Rac1TaunterHost taunter) : IDisposable
{
    private sealed record Decoy(Rac1EquipmentView View)
    {
        internal Vector3 Velocity;
        internal int Age;
        internal int Hits;
        internal bool Grounded;
        internal float GroundHeight;
        internal int GroundTicks;
        internal int RetiringTicks = -1;
        internal int LastAttacker = -1;
        internal ulong LastHitTick;
    }

    private sealed record Drone(Rac1EquipmentView View, int Slot)
    {
        internal RuntimeWorldScene.DynamicObjectNode? Target;
        internal int Age;
    }

    private readonly List<Decoy> _decoys = [];
    private readonly List<Drone> _drones = [];
    private bool _disposed;

    internal bool CanFireDecoy => !_disposed && _decoys.Count < Rac1ArsenalHostPolicy.DecoyCapacity;
    internal bool CanFireDrone => !_disposed && _drones.Count < Rac1ArsenalRules.DroneCapacity;
    internal int DroneCount => _drones.Count;

    // Witnesses read live state after being created; avoid freezing Grounded
    // and RetiringTicks at diagnostic fixture construction time.
    internal IReadOnlyList<T> ProjectDecoys<T>(Func<Func<(Rac1EquipmentView View, bool Grounded, int RetiringTicks)>, Action, T> project) =>
        _disposed ? [] : _decoys.Select(decoy => project(
            () => (decoy.View, decoy.Grounded, decoy.RetiringTicks),
            () => decoy.GroundTicks = Rac1ArsenalRules.DecoyLifetime - 1)).ToArray();

    internal void FireDecoy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var player = ports.Player() ?? throw new InvalidOperationException("The Decoy Glove requires a player.");
        ports.Sound(0, 26, player.GlobalPosition);
        var view = ports.CreateView(203); view.Play(0);
        view.Scale = Vector3.One * Rac1ArsenalRules.DecoyInitialScale;
        view.GlobalPosition = ports.Muzzle(Rac1WeaponId.DecoyGlove);
        _decoys.Add(new(view) { Velocity = ports.Forward() * 5 + Vector3.Up * 3 });
    }

    internal void FireDrones()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var player = ports.Player() ?? throw new InvalidOperationException("The Drone Device requires a player.");
        while (_drones.Count < Rac1ArsenalRules.DroneCapacity)
        {
            int slot = Enumerable.Range(0, Rac1ArsenalRules.DroneCapacity)
                .First(index => _drones.All(drone => drone.Slot != index));
            var view = ports.CreateView(479); view.Play(0);
            view.GlobalPosition = ports.Muzzle(Rac1WeaponId.DroneDevice);
            _drones.Add(new(view, slot));
        }
        ports.Sound(483, 0, player.GlobalPosition);
    }

    internal void Tick()
    {
        if (_disposed || ports.Player() is not { } player) return;
        foreach (var decoy in _decoys.ToArray())
        {
            decoy.Age++;
            if (decoy.RetiringTicks >= 0)
            {
                decoy.View.Step();
                decoy.View.GlobalPosition = new(decoy.View.GlobalPosition.X,
                    decoy.GroundHeight + decoy.View.FootOffset * decoy.View.Scale.X, decoy.View.GlobalPosition.Z);
                if (++decoy.RetiringTicks >= 60)
                { decoy.View.QueueFree(); _decoys.Remove(decoy); }
                continue;
            }
            decoy.View.Scale = Vector3.One * Math.Min(1, decoy.View.Scale.X + Rac1ArsenalRules.DecoyGrowthPerTick);
            if (!decoy.Grounded)
            {
                var from = decoy.View.GlobalPosition;
                var next = from + decoy.Velocity / 60;
                decoy.Velocity += Vector3.Down * (18f / 60);
                if (ports.Contact(from, next, .1f) is { } hit)
                {
                    decoy.GroundHeight = hit.Point.Y;
                    decoy.View.GlobalPosition = hit.Point + Vector3.Up * decoy.View.FootOffset;
                    decoy.Grounded = true; decoy.View.Play(1, false, nextId(), 2);
                }
                else decoy.View.GlobalPosition = next;
            }
            foreach (int sound in decoy.View.Step()) ports.Sound(203, sound, decoy.View.GlobalPosition);
            if (decoy.Grounded)
            {
                decoy.GroundTicks++;
                decoy.View.GlobalPosition = new(decoy.View.GlobalPosition.X,
                    decoy.GroundHeight + decoy.View.FootOffset, decoy.View.GlobalPosition.Z);
            }
            if (decoy.GroundTicks >= Rac1ArsenalRules.DecoyLifetime || decoy.Hits >= 3)
            {
                decoy.RetiringTicks = 0;
                decoy.View.BlendTo(0, 60, nextId()); // 0x2c50d0..2c5108, fold over 60 ticks.
            }
            else if (decoy.View.GlobalPosition.Y < -100)
            {
                ports.Flash(decoy.View.GlobalPosition + Vector3.Up * .6f, 25, new(.7f, .7f, .7f), 1, 20);
                decoy.View.QueueFree(); _decoys.Remove(decoy);
            }
        }
        foreach (var drone in _drones.ToArray())
        {
            drone.Age++; var from = drone.View.GlobalPosition;
            if (drone.Target is null || !ports.IsAimTarget(drone.Target))
                drone.Target = ports.FindTarget(from, Rac1ArsenalHostPolicy.DroneInterceptRange, -.1f);
            if (drone.Target is { } target)
            {
                var goal = target.Root.GlobalPosition + Vector3.Up * .6f;
                var next = from.MoveToward(goal, Rac1ArsenalHostPolicy.DroneSpeed / 60);
                var hit = ports.Contact(from, next, .23f);
                if (hit is not null || next.DistanceTo(goal) < .35f)
                {
                    ports.Blast(Rac1WeaponId.DroneDevice, hit?.Point ?? next, 1.5f, Rac1ArsenalRules.DroneDamage, 0x10000);
                    ports.Explosion(Rac1WeaponId.DroneDevice, hit?.Point ?? next);
                    ports.Sound(479, 1, hit?.Point ?? next);
                    drone.View.QueueFree(); _drones.Remove(drone); continue;
                }
                drone.View.GlobalPosition = next; Rac1ArsenalViewPose.Orient(drone.View, next - from);
            }
            else
            {
                float phase = ports.WorldTick() / 45f + drone.Slot * MathF.Tau / Rac1ArsenalRules.DroneCapacity;
                var goal = player.GlobalPosition + new Vector3(MathF.Cos(phase) * Rac1ArsenalHostPolicy.DroneOrbitRadius,
                    1 + MathF.Sin(phase * 2) * .15f, MathF.Sin(phase) * Rac1ArsenalHostPolicy.DroneOrbitRadius);
                drone.View.GlobalPosition = from.MoveToward(goal, Rac1ArsenalHostPolicy.DroneSpeed / 60);
                Rac1ArsenalViewPose.Orient(drone.View, goal - from);
            }
            drone.View.Step();
        }
    }

    internal Vector3 EnemyGoal(Rac1HostedEnemy enemy, Vector3 player)
    {
        var position = enemy.Node.Root.GlobalPosition;
        if (taunter.IsTaunted(enemy.Node.Source.InstanceIndex)) return player;
        var decoy = _decoys.Where(item => item.Grounded && item.RetiringTicks < 0 && item.Hits < 3 &&
            position.DistanceSquaredTo(item.View.GlobalPosition) < 900 &&
            ports.HasLineOfSight(position + Vector3.Up * .5f, item.View.GlobalPosition + Vector3.Up * .5f))
            .OrderBy(item => position.DistanceSquaredTo(item.View.GlobalPosition)).FirstOrDefault();
        return decoy?.View.GlobalPosition ?? player;
    }

    internal bool EnemyHitsDecoy(Rac1HostedEnemy enemy)
    {
        var decoy = _decoys.FirstOrDefault(item => item.Grounded && item.RetiringTicks < 0 &&
            item.View.GlobalPosition.DistanceTo(enemy.Body.GlobalPosition) < 1.8f);
        if (decoy is null) return false;
        int attacker = enemy.Node.Source.InstanceIndex;
        // 0x2c4c9c..2c4cc4 filters the same attacker for 30 ticks.
        if (attacker != decoy.LastAttacker || ports.WorldTick() - decoy.LastHitTick >= 30)
        {
            decoy.LastAttacker = attacker; decoy.LastHitTick = ports.WorldTick();
            decoy.Hits++; ports.Sound(203, 0, decoy.View.GlobalPosition);
        }
        return true;
    }

    // Preserve native enemy-movement proximity admission: grounded alone,
    // even for a retiring decoy, is sufficient for this query.
    internal bool IsDecoyNearby(Rac1HostedEnemy enemy) =>
        _decoys.Any(decoy => decoy.Grounded &&
            decoy.View.GlobalPosition.DistanceTo(enemy.Body.GlobalPosition) < 1.8f);

    internal void RetireDrones()
    {
        foreach (var drone in _drones)
            if (GodotObject.IsInstanceValid(drone.View)) drone.View.QueueFree();
        _drones.Clear();
    }

    internal void Reset()
    {
        foreach (var decoy in _decoys)
            if (GodotObject.IsInstanceValid(decoy.View)) decoy.View.QueueFree();
        _decoys.Clear();
        RetireDrones();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
