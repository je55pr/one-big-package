using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1PlacedBoltTests
{
    private static Rac1PickupRandom Midpoint() => new(() => 16384);

    [Fact]
    public void PlacementStartsRestingAtTheFloorInsteadOfBeingThrown()
    {
        var source = Source(13, new(5, 7, 2));
        var authored = Rac1PlacedBolt.ReadAuthored(source)!;
        Assert.Equal(new Vector3(5, 7, 2.2f), authored.ProbeStart);
        Assert.Equal(new Vector3(5, 7, -1), authored.ProbeEnd);
        var motion = Rac1BoltMotionSession.FromPlaced(authored, Midpoint(), new(new(5, 7, 0), Vector3.UnitZ));
        Assert.Equal(3, motion.NativeState);
        Assert.False(motion.NeedsWorldContact);
        Assert.Equal(Vector3.Zero, motion.Velocity);
        Assert.Equal(.65f, motion.VisualScale);
        Assert.InRange(motion.Position.Z, .12915f, .12917f);
        var first = motion.Orientation;
        for (int i = 0; i < 600; i++)
        {
            motion.Step(new(100, 100, 0), new(100, 100, 1));
            Assert.Equal(3, motion.NativeState);
            Assert.InRange(Vector3.Distance(motion.Position, new(5, 7, 0)), .32f, .35f);
        }
        Assert.NotEqual(first, motion.Orientation);
        Assert.False(motion.Collected);
    }

    [Fact]
    public void PreserveHeightFlagIgnoresTheFloorHitAndKeepsFloatingAuthoredBolts()
    {
        var authored = Rac1PlacedBolt.ReadAuthored(Source(13, new(1, 2, 9), preserveHeight: true))!;
        var motion = Rac1BoltMotionSession.FromPlaced(authored, Midpoint(), new(new(1, 2, 0), Vector3.UnitZ));
        Assert.InRange(motion.Position.Z, 9.12915f, 9.12917f);
        motion.Step(new(100, 100, 0), Vector3.Zero);
        Assert.Equal(3, motion.NativeState);
        Assert.InRange(motion.Position.Z, 9.12915f, 9.12917f);
    }

    [Fact]
    public void SlopePoseFollowsTheContactNormalAndThrownWrenchCanStartDistantHoming()
    {
        Vector3 normal = Vector3.Normalize(new Vector3(.4f, .2f, 1));
        var motion = Rac1BoltMotionSession.FromPlaced(Rac1PlacedBolt.ReadAuthored(Source(13, new(8, 0, 2)))!,
            Midpoint(), new(new(8, 0, 0), normal));
        Assert.InRange(Vector3.Dot(motion.Position - new Vector3(8, 0, 0), normal), .12915f, .12917f);
        motion.Step(Vector3.Zero, new(0, 0, .7f));
        Assert.Equal(3, motion.NativeState);
        motion.Step(Vector3.Zero, new(0, 0, .7f), thrownWrenchContact: true);
        Assert.Equal(4, motion.NativeState);
        Assert.False(motion.Collected);
        Assert.True(motion.Velocity.LengthSquared() > 0);
        for (int i = 0; i < 240 && !motion.Collected; i++) motion.Step(Vector3.Zero, new(0, 0, .7f));
        Assert.True(motion.Collected);
    }

    [Theory]
    [InlineData(13, 1, .65f)]
    [InlineData(14, 5, .7f)]
    [InlineData(15, 20, .75f)]
    [InlineData(16, 50, .6f)]
    public void AuthoredCollectionUsesClassValueAndTerminalizesTheOriginalEntityOnce(int nativeClass, int value, float scale)
    {
        var source = Source(nativeClass, new(1, 2, 3));
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1PlacedBoltSession(runtime);
        var registration = session.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.True(registration.PickupId < 0);
        Assert.Same(registration, session.Register(source, RuntimeEntityState.FromAuthored(source)));
        Assert.Null(source.NativeUid); // Class-local UID meaning does not rewrite neutral identity.
        Assert.False(session.TryCollect(1, out _)); // A crate emission cannot alias the authored id.
        Assert.True(session.TryCollect(registration.PickupId, out var collected));
        Assert.Equal(value, collected!.Value);
        Assert.Equal(76, collected.AuthoredUid);
        Assert.Equal(RuntimeEntityPresence.Inactive, collected.EntityState.Presentation.Presence);
        Assert.Equal(1, session.CollectedCount);
        Assert.Equal(value, session.CollectedBolts);
        Assert.True(runtime.Require(source).State.IsTerminalized);
        Assert.Null(runtime.Require(source).State.NativeTerminalState);
        Assert.Throws<InvalidOperationException>(() => session.TryCollect(registration.PickupId, out _));
        Assert.False(session.Register(source, RuntimeEntityState.FromAuthored(source)).Instance.IsActive);
        Assert.Equal(scale, Rac1BoltMotionSession.FromPlaced(registration.Authored, Midpoint()).VisualScale);
    }

    [Fact]
    public void AuthoredAndCratePickupCollectorsCoexistWithoutSharingUidBits()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var persistence = new Rac1MobyPersistenceSession(0);
        var crates = new Rac1BoltCrateSession(runtime, persistence);
        var placed = new Rac1PlacedBoltSession(runtime);
        var source = Source(13, Vector3.Zero);
        var registration = placed.Register(source, RuntimeEntityState.FromAuthored(source));
        placed.TryCollect(registration.PickupId, out var collected);
        Assert.Equal(1, collected!.Value);
        Assert.Equal(0, crates.DestroyedCrateCount);
        Assert.Equal(0, crates.CollectedBolts);
        Assert.Equal(0, persistence.TrackedUidCount);
    }

    [Fact]
    public void UnrelatedClassesAndTruncatedPlacedStateDoNotGainPickupBehavior()
    {
        var source = Source(13, Vector3.Zero);
        Assert.Null(Rac1PlacedBolt.ReadAuthored(source with { NativeClassId = 71 }));
        var truncated = source with
        {
            NativePayloads = [new(Rac1BoltCrate.InstancePayloadFormat, new byte[0x78]),
            new(Rac1BoltCrate.PVarPayloadFormat, new byte[0x54])]
        };
        Assert.Throws<InvalidDataException>(() => Rac1PlacedBolt.ReadAuthored(truncated));
    }

    private static RuntimeDynamicObject Source(int nativeClass, Vector3 nativePosition, bool preserveHeight = false)
    {
        byte[] packed = new byte[0x78], pvar = new byte[0x70];
        BinaryPrimitives.WriteInt16LittleEndian(packed.AsSpan(0xc), 76);
        pvar[0x54] = preserveHeight ? (byte)1 : (byte)0;
        double[] transform = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, nativePosition.X, nativePosition.Z, nativePosition.Y, 1];
        return new(ObpSourceGame.Rac1, nativeClass, 2, null, $"moby:{nativeClass}", "moby:2", new(transform), [],
            [new(Rac1BoltCrate.InstancePayloadFormat, packed), new(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
