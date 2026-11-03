using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>
/// Shared synthetic class-749 builders. <see cref="Class749"/> writes the PVar fields the hostile reads at their
/// native offsets; callers pass only the field a test cares about. Synthetic data only; no retail content.
/// </summary>
internal static class Rac1Class749Fixtures
{
    internal static Rac1Class749TargetFacts Facts(
        double distance,
        double facingError,
        int? statusSentinel = 0,
        Rac1Class749WorldPoint currentPosition = default) =>
        new(distance, facingError, currentPosition, statusSentinel);

    internal static RuntimeDynamicObject Class749(
        int instanceIndex,
        float health,
        int pvarSize = Rac1Class749Hostile.PVarSize,
        Rac1Class749WorldPoint targetDestination = default,
        int statusSentinel = 0,
        Rac1Class749WorldPoint home = default)
    {
        var pvar = new byte[pvarSize];
        if (pvarSize >= Rac1Class749Hostile.HealthOffset + sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.HealthOffset, sizeof(int)),
                BitConverter.SingleToInt32Bits(health));
        }
        if (pvarSize >= Rac1Class749Hostile.TargetDestinationOffset + 3 * sizeof(float))
        {
            WriteSingle(pvar, Rac1Class749Hostile.TargetDestinationOffset, checked((float)targetDestination.X));
            WriteSingle(pvar, Rac1Class749Hostile.TargetDestinationOffset + sizeof(float), checked((float)targetDestination.Z));
            WriteSingle(pvar, Rac1Class749Hostile.TargetDestinationOffset + 2 * sizeof(float), checked((float)targetDestination.Y));
        }
        if (pvarSize >= Rac1Class749Hostile.StatusSentinelOffset + sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                pvar.AsSpan(Rac1Class749Hostile.StatusSentinelOffset, sizeof(int)),
                statusSentinel);
        }
        if (pvarSize >= Rac1Class749Hostile.HomePositionOffset + 3 * sizeof(float))
        {
            WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset, checked((float)home.X));
            WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset + sizeof(float), checked((float)home.Z));
            WriteSingle(pvar, Rac1Class749Hostile.HomePositionOffset + 2 * sizeof(float), checked((float)home.Y));
        }

        var transform = new double[16];
        transform[12] = home.X;
        transform[13] = home.Y;
        transform[14] = home.Z;
        transform[15] = 1d;
        return new RuntimeDynamicObject(
            ObpSourceGame.Rac1, Rac1Class749Hostile.NativeClassId, instanceIndex, null,
            $"moby:{Rac1Class749Hostile.NativeClassId}", $"moby:{instanceIndex}",
            new RuntimeObjectTransform(transform), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1Class749Hostile.PVarPayloadFormat, pvar)]);
    }

    internal static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(int)), BitConverter.SingleToInt32Bits(value));
}
