using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WrenchCombatTests
{
    private readonly Rac1WrenchCombatController _controller = new();

    [Fact]
    public void OrdinaryWrenchUseHasOwnAdmissionWithoutRangedCadenceOrAmmo()
    {
        var rejected = _controller.AdmitOrdinaryUse(weaponEquipped: false);
        Assert.False(rejected.Accepted);
        Assert.Equal(Rac1WeaponUseRejection.NotEquipped, rejected.Rejection);

        var accepted = _controller.AdmitOrdinaryUse(weaponEquipped: true);
        Assert.True(accepted.Accepted);
        Assert.Equal(Rac1WeaponId.Wrench, accepted.WeaponId);
        Assert.Null(accepted.AmmoBefore);
        Assert.Null(accepted.AmmoAfter);
        Assert.Equal(
            Rac1PlayerActionDomain.Wrench,
            accepted.NativePlayerActionState);
        Assert.Equal(
            Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
            accepted.NativePlayerSequenceId);
    }

    [Fact]
    public void OrdinaryFirstSwingIdentityDoesNotInventContactTiming()
    {
        Assert.True(Rac1WrenchCombatController.IsOrdinaryFirstSwing(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId));
        Assert.False(Rac1WrenchCombatController.IsOrdinaryFirstSwing(0x14, 0));
        Assert.False(Rac1WrenchCombatController.IsOrdinaryFirstSwing(0x13, 1));
    }

    [Theory]
    [InlineData(1.111041784286499, 0.29766845703125, 0.6013565063476562)]
    [InlineData(0.3777317404747009, 0.6141510009765625, 0.243621826171875)]
    public void OrdinaryFirstSwingFacingMatchesRetailLunge(
        double nativeYaw,
        double observedDeltaX,
        double observedDeltaY)
    {
        var facing = _controller.ResolveFirstSwingFacing(nativeYaw);
        double observedLength = Math.Sqrt(
            (observedDeltaX * observedDeltaX) +
            (observedDeltaY * observedDeltaY));

        Assert.InRange(
            Math.Abs(facing.X - (observedDeltaX / observedLength)),
            0d,
            0.00013d);
        Assert.InRange(
            Math.Abs(facing.Y - (observedDeltaY / observedLength)),
            0d,
            0.00013d);
        Assert.Equal(0d, facing.Z);
        Assert.Equal(
            1d,
            Math.Sqrt((facing.X * facing.X) + (facing.Y * facing.Y)),
            12);
    }

    [Fact]
    public void FirstSwingMotion_ReplaysRecoveredTargetRiseAndDecay()
    {
        var motion = new Rac1WrenchMotionSession();
        motion.Begin(0d);

        Rac1WrenchDirection step = default;
        for (int i = 0; i < 8; i++)
            step = motion.Step();

        Assert.Equal(Rac1WrenchMotionSession.FirstSwingTargetStep, motion.Magnitude, 12);
        Assert.Equal(Rac1WrenchMotionSession.FirstSwingTargetStep, step.X, 12);
        Assert.Equal(0d, step.Y, 12);

        for (int i = 8; i < Rac1WrenchMotionSession.FirstSwingTargetTicks; i++)
            motion.Step();
        Assert.Equal(Rac1WrenchMotionSession.FirstSwingTargetStep, motion.Magnitude, 12);

        var firstDecay = motion.Step();
        Assert.Equal(
            Rac1WrenchMotionSession.FirstSwingTargetStep - Rac1WrenchMotionSession.DecayPerTick,
            motion.Magnitude,
            12);
        Assert.Equal(motion.Magnitude, firstDecay.X, 12);

        while (motion.Active)
            motion.Step();
        Assert.Equal(0d, motion.Magnitude, 12);
        Assert.Equal(28, motion.Tick);
    }

    [Fact]
    public void FirstSwingMotion_LocksAttackFacingYaw()
    {
        const double yaw = 1.111041784286499d;
        var motion = new Rac1WrenchMotionSession();
        motion.Begin(yaw);

        Rac1WrenchDirection step = motion.Step();
        Assert.Equal(Math.Cos(yaw) * Rac1WrenchMotionSession.RisePerTick, step.X, 12);
        Assert.Equal(Math.Sin(yaw) * Rac1WrenchMotionSession.RisePerTick, step.Y, 12);
        Assert.Equal(yaw, motion.NativeYaw, 12);
    }

    [Fact]
    public void FirstSwingFacingRejectsNonFiniteYaw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _controller.ResolveFirstSwingFacing(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _controller.ResolveFirstSwingFacing(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _controller.ResolveFirstSwingFacing(double.NegativeInfinity));
    }

    [Theory]
    [InlineData("rac1", Rac1BoltCrate.NativeClassId, true)]
    [InlineData("rac1", Rac1Class749Hostile.NativeClassId, true)]
    [InlineData("rac1", 1781, false)]
    [InlineData("rac2", Rac1BoltCrate.NativeClassId, false)]
    [InlineData("rac3", Rac1Class749Hostile.NativeClassId, false)]
    public void Goal1LiveTargetAdmissionIsIntegrationScoped(
        string sourceGame,
        int nativeClassId,
        bool expected)
    {
        var source = new RuntimeDynamicObject(
            sourceGame,
            nativeClassId,
            7,
            null,
            $"moby:{nativeClassId}",
            "moby:7",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>());

        var target = _controller.AdmitGoal1RuntimeTarget(source);
        if (!expected)
        {
            Assert.Null(target);
            return;
        }

        var admitted = Assert.IsType<Rac1WrenchContactTarget>(target);
        Assert.Equal(nativeClassId, admitted.NativeClassId);
        Assert.False(admitted.IsPlayerSelf);
    }

    [Theory]
    [InlineData(0.5, 0.0, 0.0, true)]
    [InlineData(1.8, 50.0, 0.0, true)]
    [InlineData(3.0, 0.0, 0.0, true)]
    [InlineData(3.4, 0.0, 0.0, false)]
    [InlineData(1.5, 0.0, 1.7, false)]
    [InlineData(-0.4, 0.0, 0.0, false)]
    public void HostPolicyAdmitsObviousVisualStrikesWithoutClaimingRetailGeometry(
        double x,
        double y,
        double z,
        bool expected)
    {
        bool admitted = Rac1WrenchHostContactPolicy.Admits(
            new Rac1WrenchHostPoint(0, 0, 0),
            new Rac1WrenchHostDirection(1, 0, 0),
            new Rac1WrenchHostPoint(x, y, z));

        Assert.Equal(expected, admitted);
    }

    [Fact]
    public void HostPolicyNormalizesFacingAndRejectsInvalidGeometry()
    {
        Assert.True(Rac1WrenchHostContactPolicy.Admits(
            new Rac1WrenchHostPoint(0, 0, 0),
            new Rac1WrenchHostDirection(4, 0, 0),
            new Rac1WrenchHostPoint(2.5, 0.7, 0)));

        Assert.Throws<ArgumentException>(
            () => Rac1WrenchHostContactPolicy.Admits(
                new Rac1WrenchHostPoint(0, 0, 0),
                new Rac1WrenchHostDirection(0, 0, 0),
                new Rac1WrenchHostPoint(1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Rac1WrenchHostContactPolicy.Admits(
                new Rac1WrenchHostPoint(0, 0, 0),
                new Rac1WrenchHostDirection(1, 0, 0),
                new Rac1WrenchHostPoint(1, 0, 0),
                targetRadius: -0.1));
    }

    [Fact]
    public void NearestHostAdmittedTargetWinsAcrossRecoveredClasses()
    {
        var crate = Target(Rac1BoltCrate.NativeClassId, 89);
        var hostile = Target(Rac1Class749Hostile.NativeClassId, 149);
        var ignored = Target(1781, 7);
        var root = new Rac1WrenchHostPoint(0d, 0d, 0d);
        var forward = new Rac1WrenchHostDirection(1d, 0d, 0d);

        var selected = _controller.SelectNearestGoal1HostTarget(
            root,
            forward,
            [
                new Rac1WrenchHostCandidate(
                    crate,
                    new Rac1WrenchHostPoint(2.4d, 0d, 0d)),
                new Rac1WrenchHostCandidate(
                    hostile,
                    new Rac1WrenchHostPoint(1.2d, 0d, 0d)),
                new Rac1WrenchHostCandidate(
                    ignored,
                    new Rac1WrenchHostPoint(0.5d, 0d, 0d)),
            ]);

        Assert.Same(hostile, selected);
    }

    [Fact]
    public void NearestHostTargetSelectionHasNoCrateFirstBias()
    {
        var crate = Target(Rac1BoltCrate.NativeClassId, 89);
        var hostile = Target(Rac1Class749Hostile.NativeClassId, 149);
        var root = new Rac1WrenchHostPoint(0d, 0d, 0d);
        var forward = new Rac1WrenchHostDirection(1d, 0d, 0d);

        var crateNearest = _controller.SelectNearestGoal1HostTarget(
            root,
            forward,
            [
                new Rac1WrenchHostCandidate(
                    hostile,
                    new Rac1WrenchHostPoint(2.3d, 0d, 0d)),
                new Rac1WrenchHostCandidate(
                    crate,
                    new Rac1WrenchHostPoint(1.1d, 0d, 0d)),
            ]);

        Assert.Same(crate, crateNearest);
    }

    [Fact]
    public void PlayerSelfIsExcludedFromHostAdmittedDamage()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: 123,
            IsPlayerSelf: true);

        Assert.Null(_controller.ResolveHostAdmittedDamage(target));
    }

    [Fact]
    public void Class500UsesSharedDamageTransportBeforeSeparateCrateConsequence()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1BoltCrate.NativeClassId,
            IsPlayerSelf: false);

        var transport = Assert.IsType<Rac1WrenchDamageResult>(
            _controller.ResolveHostAdmittedDamage(target));

        Assert.Equal(Rac1WrenchContactPath.HostPolicyAdmission, transport.ContactPath);
        Assert.Equal(Rac1WrenchCombatController.RepresentativeDamage, transport.NativeDamage);
        Assert.Equal(
            Rac1WrenchCombatController.RepresentativeDamageFlags,
            transport.NativeDamageFlags);
    }

    [Fact]
    public void HostAdmittedStimulusKeepsRepresentativeDamageExplicit()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1Class749Hostile.NativeClassId,
            IsPlayerSelf: false);

        var result = Assert.IsType<Rac1WrenchDamageResult>(
            _controller.ResolveHostAdmittedDamage(target));

        Assert.Equal(Rac1WrenchContactPath.HostPolicyAdmission, result.ContactPath);
        Assert.Equal(Rac1WrenchCombatController.RepresentativeDamage, result.NativeDamage);
        Assert.Equal(
            Rac1WrenchCombatController.RepresentativeDamageFlags,
            result.NativeDamageFlags);
    }

    [Fact]
    public void Class500HostAdmissionRoutesThroughSharedRuntimeBeforeRewardCompletion()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var current = RuntimeEntityState.FromAuthored(source);
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1BoltCrate.NativeClassId,
            IsPlayerSelf: false);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1BoltCrateSession(
            runtime,
            new Rac1MobyPersistenceSession(levelId: 0));
        var registered = session.Register(source, current);
        var transport = Assert.IsType<Rac1WrenchDamageResult>(
            _controller.ResolveHostAdmittedDamage(target));
        var damageEvent = Rac1DamageRuntime.FromWrench(
            registered.Key,
            transport);

        var admission = runtime.DispatchDamage<Rac1BoltCrateDamageAdmission>(
            damageEvent);

        Assert.Equal(registered.Key, admission.Target);
        Assert.Equal(
            Rac1WrenchCombatController.RepresentativeDamage,
            admission.NativeDamage);
        Assert.True(registered.IsActive);
        Assert.Equal(0, session.DestroyedCrateCount);

        var crateBreak = session.CompleteDamage(admission, selectedTotal: 12);
        Assert.Equal(
            Rac1BoltCrate.ActiveNativeState,
            crateBreak.NativeStateBefore);
        Assert.Equal(
            RuntimeEntityPresence.Inactive,
            crateBreak.EntityState.Presentation.Presence);
        Assert.Equal(1, session.DestroyedCrateCount);
    }

    private static RuntimeDynamicObject Target(int nativeClassId, int instanceIndex) =>
        new(
            "rac1",
            nativeClassId,
            instanceIndex,
            null,
            $"moby:{nativeClassId}",
            $"moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>());

    private static RuntimeDynamicObject Class500(int uid, int rewardCentre)
    {
        var raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(
            raw.AsSpan(0x0c, 2),
            checked((ushort)uid));
        BinaryPrimitives.WriteInt32LittleEndian(
            raw.AsSpan(0x10, 4),
            rewardCentre);
        BinaryPrimitives.WriteInt32LittleEndian(
            raw.AsSpan(0x18, 4),
            500);
        var pvar = new byte[0x100];

        return new RuntimeDynamicObject(
            "rac1",
            500,
            89,
            uid,
            "moby:500",
            "moby:89",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload(
                    Rac1BoltCrate.InstancePayloadFormat,
                    raw),
                new RuntimeOpaquePayload(
                    Rac1BoltCrate.PVarPayloadFormat,
                    pvar),
            ]);
    }
}
