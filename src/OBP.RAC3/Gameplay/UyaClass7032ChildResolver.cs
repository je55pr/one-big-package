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
    public const int ControllerPartnerPointerOffset = 0xb8;

    public const int StateOneNativeState = 1;
    public const int ResolvedNativeState = 2;
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
