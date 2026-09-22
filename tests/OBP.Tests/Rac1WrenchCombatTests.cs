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
    public void PlayerSelfIsExcludedFromHostAdmittedDamage()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: 123,
            IsPlayerSelf: true);

        Assert.Null(_controller.ResolveHostAdmittedDamage(target));
    }

    [Fact]
    public void Class500UsesSeparateCrateConsequencePath()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1BoltCrate.NativeClassId,
            IsPlayerSelf: false);

        Assert.Null(_controller.ResolveHostAdmittedDamage(target));
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
        Assert.Null(result.BoltCrateBreak);
    }

    [Fact]
    public void Class500HostAdmissionHandsPositiveDamageToRecoveredCrateConsumer()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var current = RuntimeEntityState.FromAuthored(source);
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1BoltCrate.NativeClassId,
            IsPlayerSelf: false);
        var session = new Rac1BoltCrateSession();

        var result = Assert.IsType<Rac1WrenchDamageResult>(
            _controller.ApplyClass500HostAdmittedContact(
                target,
                source,
                current,
                session,
                selectedTotal: 12));

        Assert.Equal(Rac1WrenchContactPath.HostPolicyAdmission, result.ContactPath);
        Assert.Equal(
            Rac1WrenchCombatController.RepresentativeDamage,
            result.NativeDamage);
        var crateBreak = Assert.IsType<Rac1BoltCrateBreakResult>(
            result.BoltCrateBreak);
        Assert.Equal(
            Rac1BoltCrate.ActiveNativeState,
            crateBreak.NativeStateBefore);
        Assert.Equal(
            RuntimeEntityPresence.Inactive,
            crateBreak.EntityState.Presentation.Presence);
        Assert.Equal(1, session.DestroyedCrateCount);
    }

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
