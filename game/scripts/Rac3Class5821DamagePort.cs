using System.Buffers.Binary;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// OBP's deliberate foreign-weapon-to-UYA-victim translation for the exact
/// recovered TABLE1 class-5821 lifetime branch. The native consumer makes the
/// consequence decision. No locomotion, death state or loot is synthesized.
/// </summary>
internal sealed class Rac3Class5821DamagePort
{
    private readonly RuntimeDynamicObject _source;
    private readonly Rac3MobyRuntimeSession _runtime = new();

    internal Rac3Class5821DamagePort(RuntimeDynamicObject source)
    {
        if (!Rac3Class5821Admission.TryRead(source, out _, out var reason))
            throw new InvalidDataException("UYA class-5821 TABLE1 damage context unavailable: " + reason);
        _source = source;
        _runtime.Register(source);
        _ = new Rac3Class5821DamageSession(_runtime);
    }

    internal float Lifetime => BinaryPrimitives.ReadSingleLittleEndian(
        _runtime.Require(_source).PVar.Span.Slice(Rac3Class5821Actor.RuntimeLifetimeOffset, sizeof(float)));

    internal bool HasNativePresence => _runtime.Require(_source).IsActive;

    internal ActorHitResult Apply(WeaponDamageIntent intent)
    {
        if (intent.Target.SourceGame != ObpSourceGame.Rac3 ||
            intent.Target.NativeClassId != _source.NativeClassId ||
            intent.Target.InstanceIndex != _source.InstanceIndex ||
            !float.IsFinite(intent.SourceDamage) || intent.SourceDamage <= 0f)
            return new(false, false, "Unsupported UYA class-5821 target or source damage.");

        // Foreign weapon flags never become native UYA flags. The recovered
        // TABLE1 victim mask and record kind are explicit OBP adapter policy.
        var damage = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(new(_source.NativeClassId, _source.InstanceIndex)),
            nativeDamage: intent.SourceDamage,
            nativeDamageFlags: Rac3Class5821Actor.QueriedDamageMask,
            nativeRecordKind: 0);
        if (!_runtime.TryDispatchDamage<Rac3Class5821DamageResult>(damage, out var result) || result is null)
            return new(false, false, "Native UYA class-5821 victim profile rejected the damage.");

        // A depleted lifetime is not proof of a native terminal Moby action.
        return new(true, false);
    }
}
