using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Tesla's native target admission and damage callbacks. The existing
/// arsenal beam presenter owns the view, assets and effect RNG.
/// </summary>
internal sealed record Rac1TeslaPorts(
    Func<Vector3, float, float, IEnumerable<int>?, RuntimeWorldScene.DynamicObjectNode?> FindTarget,
    Func<ulong> WorldTick,
    Action<int, Vector3, Vector3> DrawBeam,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> Damage,
    Action<int, int, Vector3> Sound);

/// <summary>
/// World-scoped R&C1 Tesla Claw session: up to two unique source targets,
/// new-lock audio and ten-tick native damage cadence. Visual ribbon and ammo
/// handling stay with their existing owners.
/// </summary>
internal sealed class Rac1TeslaHost(Rac1TeslaPorts ports, Rac1WeaponContactHost contacts) : IDisposable
{
    private readonly HashSet<int> _locked = [];
    private bool _disposed;

    internal void Release() => _locked.Clear();

    internal void Tick(Vector3 from, Vector3 forward)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var first = ports.FindTarget(from, Rac1ArsenalRules.TeslaRange, .5f, null);
        if (first is null)
        {
            Release();
            var end = from + forward * .55f;
            var hit = contacts.Contact(from, end, .01f);
            ports.DrawBeam(0, from, hit?.Point ?? end);
            return;
        }

        var targets = new List<RuntimeWorldScene.DynamicObjectNode> { first };
        var second = ports.FindTarget(from, Rac1ArsenalRules.TeslaRange, .5f, [first.Source.InstanceIndex]);
        if (second is not null) targets.Add(second);

        _locked.RemoveWhere(id => targets.All(target => target.Source.InstanceIndex != id));
        foreach (var target in targets)
            if (_locked.Add(target.Source.InstanceIndex))
                ports.Sound(177, 1, from); // 0x2b9870 lock acquisition.

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            ports.DrawBeam(i, from, target.Root.GlobalPosition + Vector3.Up * .5f);
            if (ports.WorldTick() % 10 == 0)
                ports.Damage(target, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.TeslaClaw,
                    new(target.Source.NativeClassId, target.Source.InstanceIndex),
                    Rac1ArsenalRules.TeslaDamage, Rac1ArsenalRules.TeslaDamageFlags));
        }
    }

    internal void Reset() => _locked.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
