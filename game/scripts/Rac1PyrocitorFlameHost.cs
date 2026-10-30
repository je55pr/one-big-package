using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Borrowed Pyrocitor presentation, player geometry, and damage authority.
/// The recovered particle session and persistent equipment state stay outside
/// this world-scoped contact adapter.
/// </summary>
internal sealed record Rac1PyrocitorFlamePorts(
    Func<Rac1ParticleSession?> EnsureParticles,
    Func<Rac1ParticleSession?> CurrentParticles,
    Func<Rac1WeaponId, Vector3> Muzzle,
    Func<Vector3> Forward,
    Func<PlayerHost?> Player,
    Func<ulong> WorldTick,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> IsActive,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> ApplyDamage);

/// <summary>
/// World-lease contact tracking for the R&C1 Pyrocitor. The native particle
/// pool owns emission/expiry and secondary motion; this adapter owns only
/// selected flame handles and victim repeat admission, not the damage rules.
/// </summary>
internal sealed class Rac1PyrocitorFlameHost(
    Rac1WeaponContactHost contacts, Rac1PyrocitorFlamePorts ports) : IDisposable
{
    private sealed record TrackedFlame(Rac1ParticleSession.Particle Motion)
    {
        internal HashSet<Rac1MobyRuntimeKey> Victims { get; } = [];
    }

    private readonly List<TrackedFlame> _flames = [];
    private readonly Dictionary<Rac1MobyRuntimeKey, ulong> _victimHitTicks = [];
    private bool _disposed;

    internal void Emit(bool emitting)
    {
        if (_disposed || !emitting || ports.EnsureParticles() is not { } particles) return;
        var player = ports.Player();
        if (player is null) return;
        Vector3 nozzle = ports.Muzzle(Rac1WeaponId.Pyrocitor);
        float reach = Rac1WeaponProfiles.PyrocitorReach;
        Vector3 forward = ports.Forward();
        var ray = PhysicsRayQueryParameters3D.Create(nozzle, nozzle + forward * (reach + .75f));
        ray.Exclude = contacts.RayExclusions();
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.TryGetValue("position", out var contact))
            reach = Math.Max(0, nozzle.DistanceTo(contact.AsVector3()) - .75f);
        var created = particles.Pyrocitor(
            Rac1LootPresentation.ToEffectNative(nozzle),
            Rac1LootPresentation.ToEffectNative(forward), reach);
        // 0x2b8438 owns fifteen tracked slots. Only emitter index2 on every
        // fourth update is kept for travelling damage-volume queries.
        if ((ports.WorldTick() & 3) == 0 && created.Count > 2)
        {
            if (_flames.Count == 15) _flames.RemoveAt(0);
            _flames.Add(new(created[2]));
        }
    }

    internal void StepContacts()
    {
        if (_disposed || ports.CurrentParticles() is not { } particles) return;
        foreach (var flame in _flames.ToArray())
        {
            var p = flame.Motion;
            if (!particles.IsLive(p) || p.Remaining < 2) { _flames.Remove(flame); continue; }
            particles.FlameSecondary(p);
            Vector3 point = Rac1LootPresentation.FromEffectNative(p.Position);
            float radius = p.Size * .5f;
            foreach (var (victim, bounds) in contacts.Geometry)
            {
                var key = new Rac1MobyRuntimeKey(victim.Source.NativeClassId, victim.Source.InstanceIndex);
                if (!ports.IsActive(victim) || flame.Victims.Contains(key) ||
                    _victimHitTicks.GetValueOrDefault(key) > ports.WorldTick()) continue;
                if (!bounds.Grow(radius).HasPoint(point) ||
                    !contacts.HasLineOfSight(point, victim.Root.GlobalPosition + Vector3.Up * .4f)) continue;
                if (ports.ApplyDamage(victim, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Pyrocitor, key,
                    Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags)))
                {
                    // Victim repetition is bounded independently of native
                    // particle life, and only advances after accepted damage.
                    flame.Victims.Add(key);
                    _victimHitTicks[key] = ports.WorldTick() + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks;
                }
            }
        }
    }

    internal void ClearCooldowns() => _victimHitTicks.Clear();

    internal void ClearTrackedFlames() => _flames.Clear();

    internal void Reset()
    {
        if (_disposed) return;
        ClearCooldowns();
        ClearTrackedFlames();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
