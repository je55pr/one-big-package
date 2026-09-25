using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class UyaGameplayRuntimeTests
{
    [Fact]
    public void RuntimePreservesAuthoredIdentityAndCopiedPvar()
    {
        byte[] pvar = [1, 2, 3, 4];
        var source = Dynamic(500, 311, 352,
            new RuntimeOpaquePayload(UyaMobyRuntimeSession.PVarPayloadFormat, pvar));
        var session = new UyaMobyRuntimeSession();

        var instance = session.Register(source);
        pvar[0] = 9;

        Assert.Equal(new UyaMobyRuntimeKey(500, 311), instance.Key);
        Assert.Equal(RuntimeEntityIdentity.From(source), instance.Identity);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, instance.PVar.ToArray());
        Assert.True(instance.IsActive);
        Assert.Same(source, session.Require(source).Source);
    }

    [Fact]
    public void TerminalizationOnlyProjectsNeutralLifetime()
    {
        var source = Dynamic(7032, 173, 27);
        var session = new UyaMobyRuntimeSession();
        var instance = session.Register(source);

        var terminal = session.Terminalize(instance);

        Assert.True(terminal.IsTerminalized);
        Assert.False(instance.IsActive);
        Assert.Equal(
            RuntimeEntityPresence.Inactive,
            terminal.EntityState.Presentation.Presence);
        Assert.Equal(RuntimeEntityIdentity.From(source), terminal.EntityState.Identity);
    }
    [Fact]
    public void RuntimeRejectsCrossGameAndDuplicateIdentity()
    {
        var source = Dynamic(500, 311, 352);
        var session = new UyaMobyRuntimeSession();
        session.Register(source);

        Assert.Throws<InvalidOperationException>(() => session.Register(source));
        var wrongGame = source with { SourceGame = "rac2" };
        Assert.Throws<ArgumentException>(() => session.Register(wrongGame));
    }

    [Fact]
    public void DamageTransportKeepsUnknownNativeFieldsAbsent()
    {
        var session = new UyaDamageTransportSession();
        var observed = new List<UyaGameplayDamageDispatch>();
        session.Published += observed.Add;
        var target = UyaGameplayEntityRef.Moby(new UyaMobyRuntimeKey(500, 311));
        var damage = new UyaGameplayDamageEvent(
            UyaGameplayEntityRef.Player,
            target);

        var dispatch = session.Publish(damage);

        Assert.Equal(1, session.Sequence);
        Assert.Single(observed);
        Assert.Same(damage, dispatch.Damage);
        Assert.Null(damage.NativeDamage);
        Assert.Null(damage.NativeDamageFlags);
        Assert.Null(damage.NativeMarker);
        Assert.Null(damage.NativeRecordKind);
        Assert.True(target.MatchesMoby(new UyaMobyRuntimeKey(500, 311)));
    }

    [Fact]
    public void DamageTransportRetainsRecoveredOptionalValuesWithoutConsequences()
    {
        var damage = new UyaGameplayDamageEvent(
            UyaGameplayEntityRef.Projectile(9000, 12),
            UyaGameplayEntityRef.Player,
            nativeDamage: 2.5,
            nativeDamageFlags: 0x40,
            nativeMarker: 3,
            nativeRecordKind: 7);

        Assert.Equal(2.5, damage.NativeDamage);
        Assert.Equal(0x40u, damage.NativeDamageFlags);
        Assert.Equal(3d, damage.NativeMarker);
        Assert.Equal((byte)7, damage.NativeRecordKind);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UyaGameplayDamageEvent(
                UyaGameplayEntityRef.Player,
                UyaGameplayEntityRef.Player,
                nativeDamage: double.NaN));
    }
    [Fact]
    public void UpdateDispatchUsesOnlyExactRegisteredNativeClass()
    {
        var source = Dynamic(500, 311, 352);
        var session = new UyaMobyRuntimeSession();
        session.Register(source);
        session.RegisterController(new EchoController(500));

        Assert.Equal("500:311:step", session.DispatchUpdate<string>(source, "step"));

        var unknown = Dynamic(501, 317, 586);
        session.Register(unknown);
        Assert.Throws<NotSupportedException>(() =>
            session.DispatchUpdate<string>(unknown, "step"));
    }

    [Fact]
    public void TerminalizedMobyCannotReceiveClassUpdate()
    {
        var source = Dynamic(500, 311, 352);
        var session = new UyaMobyRuntimeSession();
        var instance = session.Register(source);
        session.RegisterController(new EchoController(500));
        session.Terminalize(instance);

        Assert.Throws<InvalidOperationException>(() =>
            session.DispatchUpdate<string>(source, "step"));
    }

    private sealed class EchoController(int nativeClassId) : IUyaMobyClassController
    {
        public int NativeClassId { get; } = nativeClassId;

        public object Update(RuntimeDynamicObject source, object facts) =>
            $"{source.NativeClassId}:{source.InstanceIndex}:{facts}";
    }

    private static RuntimeDynamicObject Dynamic(
        int nativeClassId,
        int instanceIndex,
        int? nativeUid,
        params RuntimeOpaquePayload[] payloads) =>
        new(
            "rac3",
            nativeClassId,
            instanceIndex,
            nativeUid,
            $"moby:{nativeClassId}",
            $"table:1:moby:{instanceIndex}",
            new RuntimeObjectTransform(
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            ]),
            Array.Empty<RuntimeObjectMesh>(),
            payloads);
}
