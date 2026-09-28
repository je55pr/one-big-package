using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class UyaClass7032RegistryTests
{
    [SkippableFact]
    public void RetailTable1Class7032AuthoredRegistryLinksMatchRecoveredInitializer()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        UyaLevelCore.OpenedLevel opened = UyaLevelCore.Open(reader, 1);
        UyaGameplay.Gameplay gameplay = UyaGameplay.Read(opened.GameplayReader);

        UyaGameplay.MobyInstance[] controllers = gameplay.MobyInstances
            .Where(instance => instance.OClass == 7032)
            .OrderBy(instance => instance.Index)
            .ToArray();
        Assert.Equal(27, controllers.Length);

        int enabled = 0;
        int disabled = 0;
        foreach (UyaGameplay.MobyInstance controller in controllers)
        {
            Assert.NotNull(controller.PvarData);
            UyaClass7032AuthoredChildResolver authored =
                UyaClass7032ChildResolver.DecodeAuthored(controller.PvarData!);

            Assert.Equal(-1, authored.AlternateInstanceIndex);
            Assert.Equal(-1, authored.RuntimeChildPoolSlot);

            if (!UyaClass7032ChildResolver.TryBuildTable1StateOneRequest(
                    controller,
                    gameplay.MobyInstances,
                    out UyaClass7032ChildSpawnRequest? request))
            {
                Assert.Equal(-1, authored.RequestedChildClassId);
                Assert.Equal(-1, authored.PartnerInstanceIndex);
                Assert.False(
                    UyaClass7032ChildResolver.EntersStateOneImmediately(
                        controller.PvarData!));
                Assert.Null(request);
                disabled++;
                continue;
            }

            Assert.True(
                UyaClass7032ChildResolver.EntersStateOneImmediately(
                    controller.PvarData!));
            Assert.NotNull(request);
            Assert.Equal(6886, request.RequestedChildClassId);
            Assert.Equal(7031, request.PartnerClassId);
            Assert.Equal(controller.Index, request.ControllerInstanceIndex);
            UyaGameplay.MobyInstance partner = gameplay.MobyInstances[request.PartnerInstanceIndex];
            Assert.NotNull(partner.PvarData);
            Assert.True(
                partner.PvarData!.Length >
                UyaClass7032ChildResolver.PartnerRegistryAdmissionSelectorOffset);
            Assert.Equal(
                0,
                partner.PvarData[UyaClass7032ChildResolver.PartnerOptionalChildHookSelectorOffset]);
            Assert.Equal(
                1,
                partner.PvarData[UyaClass7032ChildResolver.PartnerRegistryAdmissionSelectorOffset]);
            enabled++;
        }

        Assert.Equal(17, enabled);
        Assert.Equal(10, disabled);
    }

    [Fact]
    public void TagOneAdmissionRequiresResolved6886ChildAndPartnerGate()
    {
        byte[] controller = new byte[0x80];
        BinaryPrimitives.WriteInt32LittleEndian(
            controller.AsSpan(UyaClass7032ChildResolver.RuntimeChildPoolSlotOffset),
            668);
        byte[] partner = new byte[0xA0];

        Assert.False(
            UyaClass7032ChildResolver.TryBuildTable1TagOneAdmission(
                controller,
                partner,
                UyaClass7032ChildResolver.Table1ChildClassId,
                out UyaClass7032TagOneAdmission? closed));
        Assert.Null(closed);

        partner[UyaClass7032ChildResolver.PartnerRegistryAdmissionSelectorOffset] = 3;
        Assert.True(
            UyaClass7032ChildResolver.TryBuildTable1TagOneAdmission(
                controller,
                partner,
                UyaClass7032ChildResolver.Table1ChildClassId,
                out UyaClass7032TagOneAdmission? admitted));
        Assert.NotNull(admitted);
        Assert.Equal(668, admitted.ChildPoolSlot);
        Assert.Equal(6886, admitted.ChildNativeClassId);
        Assert.Equal((byte)3, admitted.PartnerAdmissionSelector);
        Assert.Equal(1, admitted.NativeRegistryTag);
    }

    [Fact]
    public void TagOneAdmissionFailsClosedWithoutResolvedChild()
    {
        byte[] controller = new byte[0x80];
        BinaryPrimitives.WriteInt32LittleEndian(
            controller.AsSpan(UyaClass7032ChildResolver.RuntimeChildPoolSlotOffset),
            -1);
        byte[] partner = new byte[0xA0];
        partner[UyaClass7032ChildResolver.PartnerRegistryAdmissionSelectorOffset] = 1;

        Assert.False(
            UyaClass7032ChildResolver.TryBuildTable1TagOneAdmission(
                controller,
                partner,
                UyaClass7032ChildResolver.Table1ChildClassId,
                out UyaClass7032TagOneAdmission? admission));
        Assert.Null(admission);
    }

    [Fact]
    public void TagOneAdmissionRejectsUnknownResolvedChildAndShortPartnerPvar()
    {
        byte[] controller = new byte[0x80];
        BinaryPrimitives.WriteInt32LittleEndian(
            controller.AsSpan(UyaClass7032ChildResolver.RuntimeChildPoolSlotOffset),
            668);
        byte[] partner = new byte[0xA0];

        Assert.Throws<NotSupportedException>(() =>
            UyaClass7032ChildResolver.TryBuildTable1TagOneAdmission(
                controller,
                partner,
                resolvedChildClassId: 6885,
                out _));
        Assert.Throws<InvalidDataException>(() =>
            UyaClass7032ChildResolver.TryBuildTable1TagOneAdmission(
                controller,
                new byte[UyaClass7032ChildResolver.PartnerRegistryAdmissionSelectorOffset],
                UyaClass7032ChildResolver.Table1ChildClassId,
                out _));
    }

    [Fact]
    public void FactorySeedCopiesRecoveredControllerBlocksAndBackLink()
    {
        byte[] controller = Enumerable.Range(0, UyaClass7032ChildResolver.LiveMobySize)
            .Select(value => (byte)value)
            .ToArray();
        byte[] child = Enumerable.Repeat((byte)0xCD, UyaClass7032ChildResolver.LiveMobySize)
            .ToArray();

        UyaClass7032ChildResolver.ApplyTable1FactoryMobySeed(
            controller,
            child,
            controllerNativeAddress: 0x01D3CE00u);

        Assert.Equal(0x560, UyaClass7032ChildResolver.Table1ChildPVarSize);
        Assert.Equal(
            controller.AsSpan(0x10, 0x10).ToArray(),
            child.AsSpan(0x10, 0x10).ToArray());
        Assert.Equal(
            controller.AsSpan(0x38, 0x08).ToArray(),
            child.AsSpan(0x38, 0x08).ToArray());
        Assert.Equal(
            controller.AsSpan(0xF0, 0x10).ToArray(),
            child.AsSpan(0xF0, 0x10).ToArray());
        Assert.Equal(0x01D3CE00u, BinaryPrimitives.ReadUInt32LittleEndian(child.AsSpan(0xB8)));
        Assert.Equal(0xCD, child[0x20]);
        Assert.Equal(0xCD, child[0x40]);
    }

    [Fact]
    public void StateOneResolutionRecordsFactoryChildSlotAndAdvancesToStateTwo()
    {
        byte[] pvar = new byte[0x80];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(UyaClass7032ChildResolver.AlternateInstanceIndexOffset),
            37);
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(UyaClass7032ChildResolver.RuntimeChildPoolSlotOffset),
            -1);

        UyaClass7032ChildResolution result =
            UyaClass7032ChildResolver.ApplyStateOneResolution(pvar, 668);

        Assert.Equal(668, result.ChildPoolSlot);
        Assert.Equal(2, result.NativeStateAfterResolve);
        Assert.Equal(
            -1,
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(UyaClass7032ChildResolver.AlternateInstanceIndexOffset)));
        Assert.Equal(
            668,
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(UyaClass7032ChildResolver.RuntimeChildPoolSlotOffset)));
    }

    [Fact]
    public void ResolverFailsClosedOnShortPvarAndNegativeChildSlot()
    {
        Assert.Throws<InvalidDataException>(
            () => UyaClass7032ChildResolver.DecodeAuthored(new byte[0x50]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UyaClass7032ChildResolver.ApplyStateOneResolution(
                new byte[0x80],
                -1));
    }
}
