using OBP.Core;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac3GameplayRuntimeTests
{
    [Fact]
    public void RuntimePreservesAuthoredIdentityAndCopiedPvar()
    {
        byte[] pvar = [1, 2, 3, 4];
        var source = Dynamic(500, 311, 352,
            new RuntimeOpaquePayload(Rac3MobyRuntimeSession.PVarPayloadFormat, pvar));
        var session = new Rac3MobyRuntimeSession();

        var instance = session.Register(source);
        pvar[0] = 9;

        Assert.Equal(new Rac3MobyRuntimeKey(500, 311), instance.Key);
        Assert.Equal(RuntimeEntityIdentity.From(source), instance.Identity);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, instance.PVar.ToArray());
        Assert.True(instance.IsActive);
        Assert.Same(source, session.Require(source).Source);
    }

    [Fact]
    public void TerminalizationOnlyProjectsNeutralLifetime()
    {
        var source = Dynamic(7032, 173, 27);
        var session = new Rac3MobyRuntimeSession();
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
        var session = new Rac3MobyRuntimeSession();
        session.Register(source);

        Assert.Throws<InvalidOperationException>(() => session.Register(source));
        var wrongGame = source with { SourceGame = ObpSourceGame.Rac2 };
        Assert.Throws<ArgumentException>(() => session.Register(wrongGame));
    }

    [Fact]
    public void DamageTransportKeepsUnknownNativeFieldsAbsent()
    {
        var session = new Rac3DamageTransportSession();
        var observed = new List<Rac3GameplayDamageDispatch>();
        session.Published += observed.Add;
        var target = Rac3GameplayEntityRef.Moby(new Rac3MobyRuntimeKey(500, 311));
        var damage = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Player,
            target);

        var dispatch = session.Publish(damage);

        Assert.Equal(1, session.Sequence);
        Assert.Single(observed);
        Assert.Same(damage, dispatch.Damage);
        Assert.Null(damage.NativeDamage);
        Assert.Null(damage.NativeDamageFlags);
        Assert.Null(damage.NativeMarker);
        Assert.Null(damage.NativeRecordKind);
        Assert.True(target.MatchesMoby(new Rac3MobyRuntimeKey(500, 311)));
    }

    [Fact]
    public void DamageTransportRetainsRecoveredOptionalValuesWithoutConsequences()
    {
        var damage = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Projectile(9000, 12),
            Rac3GameplayEntityRef.Player,
            nativeDamage: 2.5,
            nativeDamageFlags: 0x40,
            nativeMarker: 3,
            nativeRecordKind: 7);

        Assert.Equal(2.5, damage.NativeDamage);
        Assert.Equal(0x40u, damage.NativeDamageFlags);
        Assert.Equal(3d, damage.NativeMarker);
        Assert.Equal((byte)7, damage.NativeRecordKind);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac3GameplayDamageEvent(
                Rac3GameplayEntityRef.Player,
                Rac3GameplayEntityRef.Player,
                nativeDamage: double.NaN));
    }
    [Fact]
    public void UpdateDispatchUsesOnlyExactRegisteredNativeClass()
    {
        var source = Dynamic(500, 311, 352);
        var session = new Rac3MobyRuntimeSession();
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
        var session = new Rac3MobyRuntimeSession();
        var instance = session.Register(source);
        session.RegisterController(new EchoController(500));
        session.Terminalize(instance);

        Assert.Throws<InvalidOperationException>(() =>
            session.DispatchUpdate<string>(source, "step"));
    }

    private sealed class EchoController(int nativeClassId) : IRac3MobyClassController
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
            ObpSourceGame.Rac3,
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
