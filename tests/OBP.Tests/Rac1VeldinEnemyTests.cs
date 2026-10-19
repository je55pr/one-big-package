using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1VeldinEnemyTests
{
    private static readonly Vector3[] Region = [new(-2, -2, 0), new(2, -2, 0), new(2, 2, 0), new(-2, 2, 0)];

    [Fact]
    public void RobotInitializationMovesFromEditorPlacementToEntranceStartWithoutMutatingAuthoredTransform()
    {
        double[] authored = [0, 0, -2, 0, 0, 3, 0, 0, 4, 0, 0, 0, 40, 50, 60, 1];
        var source = Robot(189, region: 0, path: 1) with { Transform = new(authored) };
        var session = new Rac1Class1440Session(new(), [Region, [new(10, 20, 30), new(11, 21, 31)]]);
        var probe = session.Register(source, RuntimeEntityState.FromAuthored(source));
        var initialized = probe.Instance.EntityState.Presentation.Transform.Matrix;
        Assert.Equal(authored[..12], initialized[..12]);
        Assert.Equal([10d, 30d, 20d, 1d], initialized[12..]);
        Assert.Equal([40d, 50d, 60d, 1d], source.Transform.Matrix[12..]);
        Assert.NotSame(source.Transform.Matrix, initialized);
        Assert.Equal(1, probe.NativeState);

        var stationary = Robot(197) with { Transform = new(authored) };
        var noEntrance = session.Register(stationary, RuntimeEntityState.FromAuthored(stationary));
        Assert.Same(authored, noEntrance.Instance.EntityState.Presentation.Transform.Matrix);
        Assert.Equal(3, noEntrance.NativeState);
    }

    [Fact]
    public void RobotWaypointArrivalIncludesHeightRatherThanOnlyPlanarDistance()
    {
        var source = Robot(189, region: 0, path: 1);
        var session = new Rac1Class1440Session(new(), [Region, [Vector3.Zero, new(5, 0, 2)]]);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        session.Step(189, new(Vector3.Zero, Vector3.Zero));
        Assert.Equal(2, session.Step(189, new(new(5, 0, 0), Vector3.Zero, StepLength: .1f)).NativeState);
        Assert.Equal(3, session.Step(189, new(new(5, 0, 2), Vector3.Zero, StepLength: .1f)).NativeState);
    }

    [Fact]
    public void PlayerIntakeSharesRecoveredProtectionAcrossBothEnemyFamilies()
    {
        var player = new Rac1RatchetNanotechSession();
        var robot = Rac1DamageRuntime.FromClass1440Contact(new(1440, 189));
        var frog = Rac1DamageRuntime.FromClass749Attack(new(749, 143), new(34, 1));
        Assert.Equal(3, player.ApplyDamage(robot).Nanotech);
        Assert.Equal(77, player.Probe().EnemyHitProtectionTicks);
        for (int tick = 0; tick < 76; tick++)
        {
            player.UpdateNative();
            Assert.Equal(3, player.ApplyDamage(frog).Nanotech);
        }
        player.UpdateNative();
        Assert.Equal(2, player.ApplyDamage(frog).Nanotech);
    }

    [Fact]
    public void RobotEntranceUsesAuthoredPolygonAndWaypointsBeforeAttacking()
    {
        var source = Robot(189, region: 0, path: 1);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1Class1440Session(runtime, [Region, [Vector3.Zero, new(5, 0, 0), new(10, 0, 0)]]);
        Assert.Equal(2, session.Register(source, RuntimeEntityState.FromAuthored(source)).Health);
        Assert.Equal(1, session.Step(189, new(Vector3.Zero, new(3, 0, 0))).NativeState);
        Assert.Equal(2, session.Step(189, new(Vector3.Zero, Vector3.Zero)).NativeState);
        var travel = session.Step(189, new(Vector3.Zero, Vector3.Zero));
        Assert.Equal(9, travel.Sequence);
        Assert.Equal(new Vector3(5, 0, 0), travel.Destination);
        Assert.Equal(10, travel.DistanceToPathEnd);
        session.Step(189, new(new(5, 0, 0), Vector3.Zero, StepLength: .1f));
        Assert.Equal(new Vector3(10, 0, 0), session.Step(189, new(new(5, 0, 0), Vector3.Zero)).Destination);
        Assert.Equal(3, session.Step(189, new(new(10, 0, 0), Vector3.Zero, StepLength: .1f)).NativeState);
        Assert.Equal(4, session.Step(189, new(new(10, 0, 0), Vector3.Zero)).NativeState);
    }

    [Fact]
    public void RobotAttackHasThreeSharedSlotsAndFourFireCycles()
    {
        var session = new Rac1Class1440Session(new(), []);
        for (int i = 189; i < 193; i++) { var source = Robot(i); session.Register(source, RuntimeEntityState.FromAuthored(source)); }
        var facts = new Rac1Class1440Session.Facts(Vector3.Zero, Vector3.UnitX);
        for (int i = 189; i < 192; i++) Assert.Equal(4, session.Step(i, facts).NativeState);
        Assert.Equal(3, session.Step(192, facts).NativeState);
        Assert.Equal(4, session.Step(189, facts with { AnimationFinished = true, AnimationBlending = true }).NativeState);
        Assert.Equal(5, session.Step(189, facts with { AnimationFinished = true }).NativeState);
        for (int i = 0; i < 3; i++) Assert.Equal(5, session.Step(189, facts with { AnimationFinished = true }).NativeState);
        var complete = session.Step(189, facts with { AnimationFinished = true });
        Assert.Equal(6, complete.NativeState);
        Assert.Equal(12, complete.Sequence);
        Assert.False(complete.HasAttackSlot);
        Assert.Equal(4, session.Step(192, facts).NativeState);
    }

    [Fact]
    public void RobotAcquisitionUsesInclusivePlanarDistanceInsteadOfHeightOrSquaredRange()
    {
        var session = new Rac1Class1440Session(new(), []);
        foreach (int instance in new[] { 196, 197 })
        {
            var source = Robot(instance);
            session.Register(source, RuntimeEntityState.FromAuthored(source));
        }
        Assert.Equal(4, session.Step(196, new(Vector3.Zero, new(24, 0, 100))).NativeState);
        Assert.Equal(3, session.Step(197, new(Vector3.Zero, new(MathF.BitIncrement(24), 0, 0))).NativeState);
    }

    [Fact]
    public void RobotContactNeedsFinishedBlendAndAuthoredPositionPastOne()
    {
        var source = Robot(197);
        var session = new Rac1Class1440Session(new(), []);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        var facts = new Rac1Class1440Session.Facts(Vector3.Zero, Vector3.UnitX, PlayerContact: true);
        session.Step(197, facts);
        session.Step(197, facts with { AnimationFinished = true });
        Assert.False(session.Step(197, facts with { AnimationPosition = 1 }).EmitContactDamage);
        Assert.False(session.Step(197, facts with { AnimationPosition = 2, AnimationBlending = true }).EmitContactDamage);
        var hit = session.Step(197, facts with { AnimationPosition = 1.5f });
        Assert.True(hit.EmitContactDamage);
        Assert.Equal(6, hit.NativeState);
        Assert.Equal(7, hit.Sequence);
        Assert.False(hit.HasAttackSlot);
        var player = new Rac1RatchetNanotechSession();
        Assert.Equal(3, player.ApplyDamage(Rac1DamageRuntime.FromClass1440Contact(new(1440, 197))).Nanotech);
    }

    [Fact]
    public void AuthoredLinkedTargetWinsWhenCloserAndMobyContactDoesNotSelectPlayerRecovery()
    {
        var source = Robot(197, linked: 154);
        var session = new Rac1Class1440Session(new(), []);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        var frog = new Rac1Class1440Session.Target(new(749, 154), new(8, 0, 0));
        var facts = new Rac1Class1440Session.Facts(Vector3.Zero, new(100, 0, 0), LinkedTarget: frog);
        var windup = session.Step(197, facts);
        Assert.Equal(4, windup.NativeState);
        Assert.Equal(frog.Position, windup.LookAt);
        session.Step(197, facts with { AnimationFinished = true });
        var target = Rac1GameplayEntityRef.Moby(new(749, 154));
        var contact = session.Step(197, facts with { AnimationPosition = 2, ContactTarget = target });
        Assert.Equal(target, contact.DamageTarget);
        Assert.Equal(5, contact.NativeState);
        Assert.True(contact.HasAttackSlot);
        Assert.True(Rac1EnemyDamage.IsClass1440Contact(Rac1DamageRuntime.FromClass1440Contact(new(1440, 197), target)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RobotDamageUsesScriptHealthAndWaitsForReactionBeforeRemoval(bool bomb)
    {
        var source = Robot(197);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1Class1440Session(runtime, []);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        var damage = bomb ? Rac1DamageRuntime.FromBombGlove(new(1, 1440, 197, Rac1BombGlove.NativeDamage, Rac1BombGlove.NativeDamageFlags)) :
            Rac1DamageRuntime.FromWrench(new(1440, 197), new(Rac1WrenchContactPath.AnimatedSweepHostGeometry, 1, Rac1WrenchAttackProfile.NativeDamageFlags));
        var result = runtime.DispatchDamage<Rac1Class1440Session.DamageResult>(damage);
        if (!bomb)
        {
            Assert.False(result.Lethal); Assert.Equal(1, result.Probe.Health); Assert.Equal(7, result.Probe.NativeState);
            Assert.False(runtime.CanDispatchDamage(damage));
            for (int tick = 0; tick < 60; tick++) session.Step(197, new(Vector3.Zero, new(100, 0, 0), AnimationFinished: true));
            Assert.True(runtime.CanDispatchDamage(damage));
            result = runtime.DispatchDamage<Rac1Class1440Session.DamageResult>(damage);
        }
        Assert.True(result.Lethal); Assert.Equal(0, result.Probe.Health); Assert.Equal(11, result.Probe.Sequence);
        Assert.True(runtime.Require(source).IsActive);
        Assert.False(runtime.CanDispatchDamage(damage));
        session.Step(197, new(Vector3.Zero, Vector3.UnitX, AnimationFinished: true, Grounded: false));
        Assert.True(runtime.Require(source).IsActive);
        session.Step(197, new(Vector3.Zero, Vector3.UnitX, AnimationFinished: true));
        Assert.False(runtime.Require(source).IsActive);
    }

    [Fact]
    public void EmittedBoltPoolDoesNotReuseIdentitiesOrCreditArrivalsTwice()
    {
        var pool = new Rac1BoltPickupSession();
        var first = pool.Emit(7); var second = pool.Emit(22);
        Assert.Equal(first.Count + second.Count, first.Concat(second).Select(pickup => pickup.PickupId).Distinct().Count());
        Assert.Equal(29, first.Concat(second).Sum(pickup => pool.Collect(pickup.PickupId)));
        Assert.Equal(0, pool.OutstandingCount);
        Assert.Throws<InvalidOperationException>(() => pool.Collect(first[0].PickupId));
    }

    [Fact]
    public void SteeringAcceleratesPerNativeTickAndTurnsAcrossTheAngleSeam()
    {
        var motion = new Rac1EnemyMotion(749, 0);
        Vector3 position = Vector3.Zero;
        Vector3 first = motion.Step(position, new(20, 0, 0), new(20, 0, 0), false, true);
        Assert.Equal(8f / 3600, first.X, 6);
        for (int i = 0; i < 90; i++) position += motion.Step(position, new(20, 0, 0), new(20, 0, 0), false, true);
        Assert.InRange(motion.StepLength, 4f / 60 - 1e-6f, 4f / 60 + 1e-6f);
        Assert.InRange(position.X, 4.9, 5.2);
        var angular = new Rac1MobySteering(MathF.PI - .01f);
        float yaw = angular.Step(-MathF.PI + .1f, .01f, .01f, .2f, true);
        Assert.InRange(Rac1MobySteering.Wrap(yaw - (MathF.PI - .01f)), 0, .02f);
        Assert.Equal(Vector3.Zero, motion.Step(position, new(20, 0, 0), new(20, 0, 0), true, true));
    }

    [Fact]
    public void ReactionsUseClassOwnedLaunchGravityDragAndContactProjection()
    {
        var frog = new Rac1MobyReaction(749, true, 0);
        Assert.Equal(new Vector3(13f / 60, 0, 14f / 60), frog.Velocity);
        var step = frog.Step();
        Assert.Equal(13f / 60 - 14f / 3600, step.X, 6);
        Assert.Equal(14f / 60 - 40f / 3600, step.Z, 6);
        var hurt = new Rac1MobyReaction(1440, false, 0);
        var lethal = new Rac1MobyReaction(1440, true, 0);
        Assert.InRange(hurt.Velocity.X / lethal.Velocity.X, .34999, .35001);
        Assert.Equal(.5f, hurt.Velocity.Z / lethal.Velocity.Z);
        for (int tick = 0; tick < 40; tick++) hurt.Step();
        Assert.True(hurt.Velocity.Z < 0);
        hurt.AcceptHostContact(Vector3.UnitZ);
        Assert.Equal(0, hurt.Velocity.Z);
    }

    [Fact]
    public void SequenceClockHonorsSelectorBlendVariableRatesCuesAndIndependentInstances()
    {
        var clip = new Rac1MobyAnimationAssets.Clip(5, "attack", [.5f, .25f], [0, 1], 7,
            [new(0, 16, 3)], [Vector3.Zero, Vector3.One]);
        var asset = new Rac1MobyAnimationAssets.Asset(new([]), new Dictionary<int, Rac1MobyAnimationAssets.Clip> { [5] = clip }, 0, 1);
        var a = new Rac1MobySequenceClock(asset, 5); var b = new Rac1MobySequenceClock(asset, 5);
        a.Select(5, 2);
        Assert.Equal([7], a.Tick());
        Assert.True(a.Blending); Assert.Equal(0, a.Position);
        Assert.Empty(a.Tick()); Assert.False(a.Blending);
        Assert.Empty(a.Tick()); Assert.Equal(.5f, a.Position);
        Assert.Equal([3], a.Tick()); Assert.Equal(1, a.Position);
        Assert.Equal(0, b.Position);
        for (int tick = 0; tick < 4; tick++) a.Tick();
        Assert.True(a.Wrapped); Assert.Equal(0, a.Position);
    }

    internal static RuntimeDynamicObject Robot(int instance, int region = -1, int path = -1, int linked = -1)
    {
        var pvar = new byte[Rac1Class1440Session.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x20), BitConverter.SingleToInt32Bits(1));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1b4), region);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1b8), path);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1bc), linked);
        return new("rac1", 1440, instance, null, "moby:1440", $"moby:{instance}", new(new double[16]), [],
            [new(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
