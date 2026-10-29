using System.Diagnostics.CodeAnalysis;
using OBP.Core;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Source-owned, Godot-free UYA class-500 admission and damage/lifecycle state
/// for one authored world. No actor from a replaced world can use this owner.
/// </summary>
internal sealed class Rac3Class500WorldRuntime : IDisposable
{
    private readonly Rac3MobyRuntimeSession _native = new();
    private readonly Rac3DamageTransportSession _transport = new();
    private readonly HashSet<RuntimeDynamicObject> _admitted = new(ReferenceEqualityComparer.Instance);
    private readonly List<RuntimeDynamicObject> _admittedOrder = [];
    private bool _disposed;

    internal Rac3Class500WorldRuntime(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.BuildId != Rac3Authority.Primary.BuildId)
            throw new ArgumentException("Only the supported R&C3 source authority can create this native owner.", nameof(world));

        _native.RegisterWorld(world);
        _ = new Rac3Class500DestructibleSession(_native);
        _ = new Rac3Class5821DamageSession(_native);

        foreach (var source in world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
        {
            if (source.NativeClassId != Rac3Class500Destructible.NativeClassId) continue;
            AuthoredCount++;
            try
            {
                if (Rac3Class500Destructible.PostBreakRoute(
                    Rac3Class500Destructible.ReadAuthored(source)) != Rac3Class500PostBreakRoute.Deactivate)
                {
                    RejectedCount++;
                    continue;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                RejectedCount++;
                continue;
            }

            _admitted.Add(source);
            _admittedOrder.Add(source);
        }
    }

    internal int AuthoredCount { get; private set; }
    internal int RejectedCount { get; private set; }
    internal int AdmittedCount => _admittedOrder.Count;
    internal IReadOnlyList<RuntimeDynamicObject> AdmittedSources => _admittedOrder;
    internal bool Active => !_disposed;

    internal bool IsActive(RuntimeDynamicObject source) =>
        !_disposed && _admitted.Contains(source) && _native.Require(source).IsActive;

    /// <summary>
    /// Dispatch through the original native consumer. The parity-only direct
    /// action publishes a transport event before checking admission, whereas
    /// the shared weapon actor adapter historically did not publish one.
    /// </summary>
    internal bool TryBreak(RuntimeDynamicObject source, double damageHp, uint damageFlags,
        bool publish, [NotNullWhen(true)] out Rac3Class500BreakResult? result, out long sequence)
    {
        result = null;
        sequence = 0;
        if (!IsActive(source)) return false;

        var damage = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(new Rac3MobyRuntimeKey(source.NativeClassId, source.InstanceIndex)),
            damageHp, damageFlags);
        if (publish) sequence = _transport.Publish(damage).Sequence;
        return _native.TryDispatchDamage(damage, out result) && result is not null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _admitted.Clear();
        _admittedOrder.Clear();
    }
}
