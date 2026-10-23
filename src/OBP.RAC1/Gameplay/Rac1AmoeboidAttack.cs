using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native class-572/865/866 lunge at animation time 15.5 calls the common
/// nearby effect with damage descriptor 0x10001 and 1.0f damage.
/// </summary>
public static class Rac1AmoeboidAttack
{
    public const uint NativeFlags = 0x00010001;
    public const int NativeDamage = 1;
    public static Rac1GameplayDamageEvent ToPlayer(int cls, int instanceIndex) =>
        ToTarget(cls, instanceIndex, Rac1GameplayEntityRef.Player);

    /// <summary>
    /// Preserve the original lunge descriptor and the actual victim identity.
    /// This transports a class-623 hit but does NOT establish its admission or
    /// consequence: that requires the target's separately recovered receiver.
    /// </summary>
    public static Rac1GameplayDamageEvent ToTarget(int cls, int instanceIndex,
        Rac1GameplayEntityRef target)
    {
        if (!Rac1AmoeboidAuthored.IsFamily(cls) || instanceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cls));
        if (target != Rac1GameplayEntityRef.Player &&
            (target.Kind != Rac1GameplayEntityKind.Moby ||
             target.NativeClassId != Rac1AmoeboidTargetSelection.TargetClassId ||
             target.RuntimeId < 0))
            throw new ArgumentException("Unsupported Amoeboid lunge victim.", nameof(target));
        var source = Rac1GameplayEntityRef.Moby(new(cls, instanceIndex));
        return new(source, target, NativeDamage, NativeFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(source));
    }

    public static bool IsNativeAttack(Rac1GameplayDamageEvent damage) =>
        damage.Source.Kind == Rac1GameplayEntityKind.Moby &&
        Rac1AmoeboidAuthored.IsFamily(damage.Source.NativeClassId) &&
        (damage.Target == Rac1GameplayEntityRef.Player ||
         (damage.Target.Kind == Rac1GameplayEntityKind.Moby &&
          damage.Target.NativeClassId == Rac1AmoeboidTargetSelection.TargetClassId &&
          damage.Target.RuntimeId >= 0)) &&
        damage.NativeDamage == NativeDamage &&
        damage.NativeDamageFlags == NativeFlags &&
        damage.Owner?.RuntimeEntity == damage.Source &&
        damage.NativeMarker is null;

    public static bool IsNativePlayerAttack(Rac1GameplayDamageEvent damage) =>
        damage.Target == Rac1GameplayEntityRef.Player && IsNativeAttack(damage);
}

/// <summary>
/// Conservative host attribution guard, not a claimed native target-pointer solver.
/// A lunge spanning several animation ticks cannot silently change its victim
/// from an authored class-623 Moby to Ratchet (or vice versa).
/// </summary>
public sealed class Rac1AmoeboidLungeTargetLatch
{
    private readonly Dictionary<int, Rac1GameplayEntityRef> _targets = [];

    public Rac1GameplayEntityRef? Current(int instanceIndex) =>
        _targets.TryGetValue(instanceIndex, out var target) ? target : null;

    public bool AcceptsContact(int instanceIndex, Rac1GameplayEntityRef candidate) =>
        !_targets.TryGetValue(instanceIndex, out var target) || target == candidate;

    public Rac1GameplayEntityRef? Observe(int instanceIndex, int nativeState,
        Rac1GameplayEntityRef candidate)
    {
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (nativeState != Rac1AmoeboidFamilySession.LungeState)
        {
            _targets.Remove(instanceIndex);
            return null;
        }
        _targets.TryAdd(instanceIndex, candidate);
        return _targets[instanceIndex];
    }

    public void Clear() => _targets.Clear();
}
