using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

// Mine position is observed during the same arsenal tick. Only the existing
// projectile owner can change the shot's native Taunted flag.
internal sealed record Rac1TaunterMine(Vector3 Position, Action MarkTaunted);

/// <summary>
/// Source-world observations and presentation/attack actions borrowed by the
/// world-scoped Taunter. No root instance, native inventory, or projectile
/// collection is retained beyond a synchronous tick.
/// </summary>
internal sealed record Rac1TaunterPorts(
    Func<ulong> WorldTick,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> EnemyTargets,
    Func<IEnumerable<Rac1TaunterMine>> Mines,
    Action<Vector3, Vector3> SpawnRing,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> Damage);

/// <summary>
/// R&C1 Taunter enemy attraction, mine activation and crate-cone admission.
/// Shares the world contact host rather than duplicating target/occlusion rules.
/// The 60-tick attraction expiry and 8/30-tick effects preserve native timing.
/// </summary>
internal sealed class Rac1TaunterHost(Rac1TaunterPorts ports, Rac1WeaponContactHost contacts) : IDisposable
{
    private readonly Dictionary<int, ulong> _taunts = [];
    private bool _disposed;

    internal bool IsTaunted(int instanceIndex) =>
        !_disposed && _taunts.GetValueOrDefault(instanceIndex) > ports.WorldTick();

    internal void Tick(Vector3 from, Vector3 forward, Vector3 playerPosition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong tick = ports.WorldTick();
        if (tick % 8 == 0) ports.SpawnRing(from, forward);

        foreach (var node in ports.EnemyTargets().Where(contacts.IsAimTarget))
        {
            var delta = node.Root.GlobalPosition - playerPosition;
            if (delta.LengthSquared() < Rac1ArsenalRules.TaunterRange * Rac1ArsenalRules.TaunterRange &&
                delta.Normalized().Dot(forward) > MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle) &&
                contacts.HasLineOfSight(from, node.Root.GlobalPosition + Vector3.Up * .4f))
                _taunts[node.Source.InstanceIndex] = tick + 60;
        }

        foreach (var mine in ports.Mines())
            if (mine.Position.DistanceTo(from) < Rac1ArsenalRules.TaunterRange &&
                (mine.Position - from).Normalized().Dot(forward) > MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle))
                mine.MarkTaunted();

        // 0x2b6bc8 also hits one of the ordinary crate classes in its
        // 12-unit forward cone. The mine interaction is tracked separately.
        if (tick % 30 == 0)
        {
            // Native class 505 is admitted by this cone, but its object identity is not yet confirmed.
            var crate = contacts.Targets.Where(node => node.Source.NativeClassId is
                    Rac1ClassIds.WoodenCrate or Rac1ClassIds.NanotechCrate or 505 or Rac1ClassIds.AmmoCrate)
                .FirstOrDefault(node => node.Root.GlobalPosition.DistanceTo(from) < 12 &&
                    (node.Root.GlobalPosition - from).Normalized().Dot(forward) > MathF.Cos(Rac1ArsenalRules.TaunterHalfAngle) &&
                    contacts.HasLineOfSight(from, node.Root.GlobalPosition + Vector3.Up * .4f));
            if (crate is not null)
                ports.Damage(crate, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Taunter,
                    new(crate.Source.NativeClassId, crate.Source.InstanceIndex), 1, 0x10000));
        }
    }

    internal void Reset() => _taunts.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
