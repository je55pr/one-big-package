using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Borrowed, narrowly scoped host operations. The source sessions remain authoritative
/// for damage, rewards, and state; these callbacks must not outlive the world lease.
/// </summary>
internal sealed record Rac1BreakablePorts(
    Func<PlayerHost?> Player,
    Func<Rac1WorldModule, bool> Current,
    Func<int, int, int> ChooseFrames,
    Func<int, int> SelectCrateReward,
    Func<Vector3, Vector3, bool> LineOfSight,
    Action<Rac1GameplayDamageEvent> PlayerDamage,
    Action<RuntimeWorldScene.DynamicObjectNode, Rac1BoltCrateDamageAdmission> CompleteOrdinaryCrateDamage);

internal sealed record Rac1BreakableEntrySnapshot(
    int InstanceIndex, int NativeClassId, int? NativeState, RuntimeEntityPresence Presence, bool HasCollider);

internal sealed record Rac1BreakableSnapshot(
    ImmutableArray<Rac1BreakableEntrySnapshot> Explosives,
    ImmutableArray<Rac1BreakableEntrySnapshot> Novalis,
    int NovalisDestroyed);

/// <summary>
/// Per-R&C1-world breakable composition, explicitly independent of the loot presenter.
/// No borrowed contact or reward callback is permitted after retirement.
/// </summary>
internal sealed class Rac1BreakableHost : IDisposable
{
    private readonly Func<bool> _current;
    private readonly Rac1ExplosiveCrateAdapter _explosives;
    private readonly Rac1NovalisBreakableAdapter _novalis;
    private bool _disposed;

    internal Rac1BreakableHost(RuntimeWorld world, Rac1WorldContext context,
        Rac1WorldPresentation presentation, Rac1LootPresentation loot, Rac1EnemyHost enemies,
        Func<bool> current, Rac1BreakablePorts ports)
    {
        _current = current;
        _explosives = new(context, presentation, loot, enemies, ports);
        _novalis = new(world.LevelId, context, loot, ports);
    }

    private bool Current => !_disposed && _current();

    internal IEnumerable<RuntimeWorldScene.DynamicObjectNode> NovalisNodes =>
        Current ? _novalis.Nodes : [];

    internal bool IsNovalisDestroyed(int instanceIndex) =>
        !Current || _novalis.IsDestroyed(instanceIndex);

    internal bool IsNovalisProp(RuntimeWorldScene.DynamicObjectNode node) =>
        Current && _novalis.Contains(node);

    internal bool TryBreakNovalisProp(RuntimeWorldScene.DynamicObjectNode node,
        Rac1NovalisBreakableSession.ContactKind contact) =>
        Current && _novalis.TryBreak(node, contact);

    internal void ResolveNovalisBombBreakables(Vector3 origin, float radius)
    {
        if (Current) _novalis.ResolveBomb(origin, radius);
    }

    internal bool TryTriggerExplosiveDamage(RuntimeWorldScene.DynamicObjectNode target,
        Rac1GameplayDamageEvent damage) =>
        Current && _explosives.TryTriggerDamage(target, damage);

    internal bool TryTriggerExplosiveDamage(Rac1GameplayDamageEvent damage) =>
        Current && _explosives.TryTriggerDamage(damage);

    internal void ConfigureExplosives(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Rac1BreakableHost));
        _explosives.Configure(world, scene);
    }

    internal void ConfigureNovalis(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Rac1BreakableHost));
        _novalis.Configure(world, scene);
    }

    internal void TickExplosives(double delta)
    {
        if (Current) _explosives.Tick(delta);
    }

    // Explicit diagnostic-only entry points. Never exposed to ordinary gameplay or UI.
    internal bool TouchExplosiveForDiagnostics(int instanceIndex) =>
        Current && _explosives.TouchForDiagnostics(instanceIndex);

    internal bool BreakNovalisForDiagnostics(int instanceIndex,
        Rac1NovalisBreakableSession.ContactKind contact) =>
        Current && _novalis.BreakForDiagnostics(instanceIndex, contact);

    internal Rac1BreakableSnapshot Observe() => new(
        Current ? _explosives.Observe() : [],
        Current ? _novalis.Observe() : [],
        Current ? _novalis.DestroyedCount : 0);

    internal void Reset()
    {
        _explosives.Reset();
        _novalis.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
