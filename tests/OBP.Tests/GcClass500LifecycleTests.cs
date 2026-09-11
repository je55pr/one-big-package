using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class GcClass500LifecycleTests
{
    [Fact]
    public void ZeroC8ProjectsNativeBreakToNeutralDeactivation()
    {
        var source = Class500(pvarC8: 0);
        var initial = RuntimeEntityState.FromAuthored(source);

        var result = GcClass500Lifecycle.ApplyRecoveredBreak(source, initial);

        Assert.Equal(GcClass500Lifecycle.ActiveNativeState, result.NativeStateBefore);
        Assert.Equal(GcClass500Lifecycle.BreakTransitionNativeState, result.NativeBreakTransitionState);
        Assert.Null(result.NativeStateAfter);
        Assert.Equal(GcClass500PostBreakRoute.Deactivate, result.Route);
        Assert.Equal(RuntimeEntityPresence.Inactive, result.EntityState.Presentation.Presence);
        Assert.Equal(initial.Identity, result.EntityState.Identity);
    }

    [Fact]
    public void NonzeroC8KeepsEntityPresentWhileNativeStateSixStaysRac2Owned()
    {
        var source = Class500(pvarC8: 7);
        var initial = RuntimeEntityState.FromAuthored(source);

        var result = GcClass500Lifecycle.ApplyRecoveredBreak(source, initial);

        Assert.Equal(GcClass500PostBreakRoute.State6, result.Route);
        Assert.Equal(GcClass500Lifecycle.AlternateNativeState, result.NativeStateAfter);
        Assert.Equal(RuntimeEntityPresence.Active, result.EntityState.Presentation.Presence);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, result.EntityState.Presentation.AnimationRole);
    }

    [Fact]
    public void LifecycleRejectsWrongSourceOrMismatchedLiveIdentity()
    {
        var source = Class500(pvarC8: 0);
        var wrongGame = source with { SourceGame = "rac3" };
        Assert.Throws<ArgumentException>(() =>
            GcClass500Lifecycle.ApplyRecoveredBreak(wrongGame, RuntimeEntityState.FromAuthored(wrongGame)));

        var other = source with { InstanceIndex = 4, InteractionId = "level:1:moby:4" };
        Assert.Throws<InvalidOperationException>(() =>
            GcClass500Lifecycle.ApplyRecoveredBreak(source, RuntimeEntityState.FromAuthored(other)));
    }

    [Fact]
    public void MissingNativePayloadDoesNotInventLifecycleRoute()
    {
        var source = new RuntimeDynamicObject(
            "rac2", 500, 31, 73, "moby:500", "level:1:moby:31",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>());

        Assert.Throws<InvalidDataException>(() =>
            GcClass500Lifecycle.ApplyRecoveredBreak(source, RuntimeEntityState.FromAuthored(source)));
    }

    private static RuntimeDynamicObject Class500(byte pvarC8)
    {
        var raw = new byte[0x88];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), 73);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x14, 4), 13);
        var pvar = new byte[0x110];
        pvar[0xC8] = pvarC8;

        return new RuntimeDynamicObject(
            "rac2", 500, 31, 73, "moby:500", "level:1:moby:31",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload("rac2-moby-instance-0x88", raw),
             new RuntimeOpaquePayload("rac2-pvar", pvar)]);
    }
}
