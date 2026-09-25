using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Exact authored native placement view retained in each UYA runtime Moby.
/// Positions/rotations remain PS2-native Z-up values from the 0x88 placement
/// record; no OBP/Godot basis conversion is applied here.
/// </summary>
public static class UyaMobyPlacement
{
    public const int NativeRecordBytes = 0x88;
    public const int NativePositionOffset = 0x40;
    public const int NativeRotationOffset = 0x4c;

    public static UyaMobyPlacementFacts ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac3")
            throw new ArgumentException("Source is not a UYA Moby.", nameof(source));

        byte[] bytes = source.NativePayloads?
            .SingleOrDefault(payload =>
                payload.Format == UyaMobyRuntimeSession.InstancePayloadFormat)?.Data
            ?? throw new InvalidDataException(
                $"UYA class {source.NativeClassId} instance {source.InstanceIndex} has no native placement payload.");

        if (bytes.Length != NativeRecordBytes)
            throw new InvalidDataException(
                $"UYA class {source.NativeClassId} instance {source.InstanceIndex} placement is 0x{bytes.Length:x} bytes, expected 0x{NativeRecordBytes:x}.");

        float F(int offset)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
            if (!float.IsFinite(value))
                throw new InvalidDataException(
                    $"UYA class {source.NativeClassId} instance {source.InstanceIndex} placement has a non-finite float at +0x{offset:x}.");
            return value;
        }

        return new UyaMobyPlacementFacts(
            NativeX: F(NativePositionOffset + 0x00),
            NativeY: F(NativePositionOffset + 0x04),
            NativeZ: F(NativePositionOffset + 0x08),
            RotationX: F(NativeRotationOffset + 0x00),
            RotationY: F(NativeRotationOffset + 0x04),
            RotationZ: F(NativeRotationOffset + 0x08));
    }

    public static UyaNativePoint ObpYUpToNative(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(x));
        return new UyaNativePoint(x, z, y);
    }
}

public sealed record UyaMobyPlacementFacts(
    float NativeX,
    float NativeY,
    float NativeZ,
    float RotationX,
    float RotationY,
    float RotationZ);

public readonly record struct UyaNativePoint(float X, float Y, float Z);
