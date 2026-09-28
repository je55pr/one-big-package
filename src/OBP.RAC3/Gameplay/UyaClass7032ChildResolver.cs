using System.Buffers.Binary;
using OBP.RAC3.Level;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Recovered TABLE1 class-7032 state-1 child-resolution boundary.
/// Native 0x00333758 resolves the authored partner, invokes the class factory,
/// writes the returned Moby pool slot at PVar +0x50, links the partner at
/// Moby +0xB8, and advances the controller to native state 2.
/// </summary>
public static class UyaClass7032ChildResolver
{
    public const int NativeClassId = 7032;
    public const int Table1ChildClassId = 6886;
    public const int Table1PartnerClassId = 7031;

    public const int RequestedChildClassOffset = 0x40;
    public const int PartnerInstanceIndexOffset = 0x44;
    public const int AlternateInstanceIndexOffset = 0x4c;
    public const int RuntimeChildPoolSlotOffset = 0x50;
    public const int PartnerOptionalChildHookSelectorOffset = 0x90;
    public const int PartnerRegistryAdmissionSelectorOffset = 0x92;
    public const int ControllerPartnerPointerOffset = 0xb8;
    public const int ResolvedChildRegistryTag = 1;

    public const int StateZeroImmediateRouteWordOffset = 0x30;
    public const int StateOneNativeState = 1;
    public const int ResolvedNativeState = 2;

    public const int LiveMobySize = 0x100;
    public const int Table1ChildPVarSize = 0x560;
    public const int FactoryCopiedBlock10Offset = 0x10;
    public const int FactoryCopiedBlock10Size = 0x10;
    public const int FactoryCopiedBlock38Offset = 0x38;
    public const int FactoryCopiedBlock38Size = 0x08;
    public const int FactoryCopiedBlockF0Offset = 0xf0;
    public const int FactoryCopiedBlockF0Size = 0x10;
    public const int ChildControllerPointerOffset = 0xb8;
    public static UyaClass7032AuthoredChildResolver DecodeAuthored(
        ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < RuntimeChildPoolSlotOffset + sizeof(int))
            throw new InvalidDataException(
                "UYA class-7032 PVar is too short for the recovered child resolver.");

        return new UyaClass7032AuthoredChildResolver(
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar[RequestedChildClassOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar[PartnerInstanceIndexOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar[AlternateInstanceIndexOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(
                pvar[RuntimeChildPoolSlotOffset..]));
    }

    /// <summary>
    /// State zero branches directly to native state one when the signed dword at
    /// PVar +0x30 is non-positive. This is a control-flow fact, not a timer name.
    /// </summary>
    public static bool EntersStateOneImmediately(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < StateZeroImmediateRouteWordOffset + sizeof(int))
            throw new InvalidDataException(
                "UYA class-7032 PVar is too short for the recovered state-zero gate.");
        return BinaryPrimitives.ReadInt32LittleEndian(
            pvar[StateZeroImmediateRouteWordOffset..]) <= 0;
    }

    public static bool TryBuildTable1StateOneRequest(
        UyaGameplay.MobyInstance controller,
        IReadOnlyList<UyaGameplay.MobyInstance> population,
        out UyaClass7032ChildSpawnRequest? request)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(population);
        if (controller.OClass != NativeClassId)
            throw new ArgumentException(
                $"Expected UYA class {NativeClassId}, got {controller.OClass}.",
                nameof(controller));
        if (controller.PvarData is null)
            throw new InvalidDataException(
                $"UYA class-{NativeClassId} instance {controller.Index} has no PVar.");

        UyaClass7032AuthoredChildResolver authored =
            DecodeAuthored(controller.PvarData);

        if (authored.RequestedChildClassId == -1)
        {
            if (authored.PartnerInstanceIndex != -1)
                throw new InvalidDataException(
                    "Disabled TABLE1 class-7032 resolver has a partner index.");
            request = null;
            return false;
        }

        if (authored.RequestedChildClassId != Table1ChildClassId)
            throw new NotSupportedException(
                $"TABLE1 class-7032 child class {authored.RequestedChildClassId} is unrecovered.");
        if ((uint)authored.PartnerInstanceIndex >= (uint)population.Count)
            throw new InvalidDataException(
                $"TABLE1 class-7032 partner {authored.PartnerInstanceIndex} is outside the Moby population.");
        UyaGameplay.MobyInstance partner =
            population[authored.PartnerInstanceIndex];
        if (partner.OClass != Table1PartnerClassId)
            throw new InvalidDataException(
                $"TABLE1 class-7032 partner {partner.Index} is class {partner.OClass}, " +
                $"expected {Table1PartnerClassId}.");

        request = new UyaClass7032ChildSpawnRequest(
            controller.Index,
            authored.RequestedChildClassId,
            authored.PartnerInstanceIndex,
            partner.OClass);
        return true;
    }

    public static bool TryBuildTable1TagOneAdmission(
        ReadOnlySpan<byte> controllerPvar,
        ReadOnlySpan<byte> partnerPvar,
        int resolvedChildClassId,
        out UyaClass7032TagOneAdmission? admission)
    {
        UyaClass7032AuthoredChildResolver controller =
            DecodeAuthored(controllerPvar);

        if (controller.RuntimeChildPoolSlot < 0)
        {
            admission = null;
            return false;
        }

        if (resolvedChildClassId != Table1ChildClassId)
            throw new NotSupportedException(
                $"TABLE1 class-7032 resolved child class {resolvedChildClassId} is unrecovered.");
        if (partnerPvar.Length <= PartnerRegistryAdmissionSelectorOffset)
            throw new InvalidDataException(
                "UYA class-7031 partner PVar is too short for the recovered tag-1 admission gate.");

        byte selector = partnerPvar[PartnerRegistryAdmissionSelectorOffset];
        if (selector == 0)
        {
            admission = null;
            return false;
        }

        admission = new UyaClass7032TagOneAdmission(
            controller.RuntimeChildPoolSlot,
            resolvedChildClassId,
            selector,
            ResolvedChildRegistryTag);
        return true;
    }

    /// <summary>
    /// Replays the exact live-Moby seed copies performed by native 0x00335908
    /// after allocating a class-6886 child and before its class-local initializer.
    /// The meanings of the +0x38 and +0xF0 blocks remain intentionally unnamed.
    /// </summary>
    public static void ApplyTable1FactoryMobySeed(
        ReadOnlySpan<byte> controllerMoby,
        Span<byte> childMoby,
        uint controllerNativeAddress)
    {
        if (controllerMoby.Length < LiveMobySize || childMoby.Length < LiveMobySize)
            throw new InvalidDataException(
                "UYA class-7032 factory seed requires complete 0x100-byte live Mobies.");

        controllerMoby.Slice(FactoryCopiedBlock10Offset, FactoryCopiedBlock10Size)
            .CopyTo(childMoby[FactoryCopiedBlock10Offset..]);
        controllerMoby.Slice(FactoryCopiedBlock38Offset, FactoryCopiedBlock38Size)
            .CopyTo(childMoby[FactoryCopiedBlock38Offset..]);
        controllerMoby.Slice(FactoryCopiedBlockF0Offset, FactoryCopiedBlockF0Size)
            .CopyTo(childMoby[FactoryCopiedBlockF0Offset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            childMoby[ChildControllerPointerOffset..],
            controllerNativeAddress);
    }

    public static UyaClass7032ChildResolution ApplyStateOneResolution(
        Span<byte> mutablePvar,
        int childPoolSlot)
    {
        if (mutablePvar.Length < RuntimeChildPoolSlotOffset + sizeof(int))
            throw new InvalidDataException(
                "UYA class-7032 PVar is too short for child-slot resolution.");
        ArgumentOutOfRangeException.ThrowIfNegative(childPoolSlot);

        BinaryPrimitives.WriteInt32LittleEndian(
            mutablePvar[RuntimeChildPoolSlotOffset..],
            childPoolSlot);
        BinaryPrimitives.WriteInt32LittleEndian(
            mutablePvar[AlternateInstanceIndexOffset..],
            -1);

        return new UyaClass7032ChildResolution(
            childPoolSlot,
            ResolvedNativeState);
    }
}

public sealed record UyaClass7032AuthoredChildResolver(
    int RequestedChildClassId,
    int PartnerInstanceIndex,
    int AlternateInstanceIndex,
    int RuntimeChildPoolSlot);

public sealed record UyaClass7032ChildSpawnRequest(
    int ControllerInstanceIndex,
    int RequestedChildClassId,
    int PartnerInstanceIndex,
    int PartnerClassId);

public sealed record UyaClass7032ChildResolution(
    int ChildPoolSlot,
    int NativeStateAfterResolve);

public sealed record UyaClass7032TagOneAdmission(
    int ChildPoolSlot,
    int ChildNativeClassId,
    byte PartnerAdmissionSelector,
    int NativeRegistryTag);
