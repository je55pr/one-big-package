using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WrenchCombatTests
{
    private readonly Rac1WrenchCombatController _controller = new();

    [Theory]
    [InlineData(16.5, false)]
    [InlineData(17.0, true)]
    [InlineData(23.0, true)]
    [InlineData(23.5, false)]
    public void OrdinaryFirstSwingContactWindowIsInclusive(double age, bool expected)
    {
        Assert.Equal(expected, _controller.IsContactActive(
            Rac1WrenchCombatController.OrdinaryActionId,
            Rac1WrenchCombatController.OrdinaryProfileId,
            age));
    }

    [Fact]
    public void UnsupportedActionOrProfileDoesNotOpenContactWindow()
    {
        Assert.False(_controller.IsContactActive(0x14, 0, 20));
        Assert.False(_controller.IsContactActive(0x13, 1, 20));
    }

    [Theory]
    [InlineData("rac1", Rac1BoltCrate.NativeClassId, true)]
    [InlineData("rac1", Rac1Class749Hostile.NativeClassId, true)]
    [InlineData("rac1", 1781, false)]
    [InlineData("rac2", Rac1BoltCrate.NativeClassId, false)]
    [InlineData("rac3", Rac1Class749Hostile.NativeClassId, false)]
    public void Goal1LiveTargetAdmissionIsRac1Specific(string sourceGame, int nativeClassId, bool expected)
    {
        var source = new RuntimeDynamicObject(
            sourceGame, nativeClassId, 7, null, $"moby:{nativeClassId}", "moby:7",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>());

        var target = _controller.AdmitGoal1RuntimeTarget(source);
        if (!expected)
        {
            Assert.Null(target);
            return;
        }

        var admitted = Assert.IsType<Rac1WrenchContactTarget>(target);
        Assert.Equal(nativeClassId, admitted.NativeClassId);
        Assert.Equal(Rac1WrenchCombatController.DamageableMobyFlag, admitted.MobyFlags);
        Assert.False(admitted.IsPlayerSelf);
    }

    [Fact]
    public void Class500ToolTipSphereUsesRecoveredInsetAndRadius()
    {
        var sphere = Assert.IsType<Rac1WrenchSphere>(_controller.GetClass500ToolTipSphere(
            0x13,
            0,
            20,
            new Rac1WrenchPoint(0, 0, 0),
            new Rac1WrenchPoint(1, 0, 0)));

        Assert.Equal(0.915d, sphere.Center.X, 12);
        Assert.Equal(0d, sphere.Center.Y, 12);
        Assert.Equal(0d, sphere.Center.Z, 12);
        Assert.Equal(0.35d, sphere.Radius, 12);
    }

    [Fact]
    public void PlayerSelfIsExcluded()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: 123,
            MobyFlags: Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: true);

        Assert.Null(_controller.ResolveForwardDirectRecord(0x13, 0, 20, target));
    }

    [Fact]
    public void VictimRequiresDamageabilityFlag()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: 123,
            MobyFlags: 0,
            IsPlayerSelf: false);

        Assert.Null(_controller.ResolveForwardDirectRecord(0x13, 0, 20, target));
    }

    [Fact]
    public void Class500IsExcludedFromForwardDirectRecordPath()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1BoltCrate.NativeClassId,
            MobyFlags: Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: false);

        Assert.Null(_controller.ResolveForwardDirectRecord(0x13, 0, 20, target));
    }

    [Fact]
    public void ForwardDirectRecordUsesRecoveredDamageAndFlags()
    {
        var target = new Rac1WrenchContactTarget(
            NativeClassId: 123,
            MobyFlags: Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: false);

        var result = Assert.IsType<Rac1WrenchDamageResult>(
            _controller.ResolveForwardDirectRecord(0x13, 0, 20, target));

        Assert.Equal(Rac1WrenchContactPath.ForwardDirectRecord, result.ContactPath);
        Assert.Equal(1d, result.NativeDamage);
        Assert.Equal(0x00010000u, result.NativeDamageFlags);
        Assert.Null(result.BoltCrateBreak);
    }

    [Fact]
    public void Class500ToolTipContactHandsDamageToBoltCrateSession()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var current = RuntimeEntityState.FromAuthored(source);
        var target = new Rac1WrenchContactTarget(
            NativeClassId: Rac1BoltCrate.NativeClassId,
            MobyFlags: Rac1WrenchCombatController.DamageableMobyFlag,
            IsPlayerSelf: false);
        var session = new Rac1BoltCrateSession();

        var result = Assert.IsType<Rac1WrenchDamageResult>(
            _controller.ApplyClass500ToolTipContact(
                0x13, 0, 20, target, source, current, session, selectedTotal: 12));

        Assert.Equal(Rac1WrenchContactPath.ToolTipSphere, result.ContactPath);
        Assert.Equal(1d, result.NativeDamage);
        Assert.Equal(0x00010000u, result.NativeDamageFlags);
        var crateBreak = Assert.IsType<Rac1BoltCrateBreakResult>(result.BoltCrateBreak);
        Assert.Equal(Rac1BoltCrate.ActiveNativeState, crateBreak.NativeStateBefore);
        Assert.Equal(RuntimeEntityPresence.Inactive, crateBreak.EntityState.Presentation.Presence);
        Assert.Equal(1, session.DestroyedCrateCount);
    }

    private static RuntimeDynamicObject Class500(int uid, int rewardCentre)
    {
        var raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x0c, 2), checked((ushort)uid));
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), rewardCentre);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x18, 4), 500);
        var pvar = new byte[0x100];

        return new RuntimeDynamicObject(
            "rac1", 500, 89, uid, "moby:500", "moby:89",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1BoltCrate.InstancePayloadFormat, raw),
             new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
