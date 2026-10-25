using OBP.Core;
using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Exact authored native placement view retained in each UYA runtime Moby.
/// Positions/rotations remain PS2-native Z-up values from the 0x88 placement
/// record; no OBP/Godot basis conversion is applied here.
/// </summary>
public static class Rac3MobyPlacement
{
    public const int NativeRecordBytes = 0x88;
    public const int NativePositionOffset = 0x40;
    public const int NativeRotationOffset = 0x4c;

    public static Rac3MobyPlacementFacts ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != ObpSourceGame.Rac3)
            throw new ArgumentException("Source is not a UYA Moby.", nameof(source));

        byte[] bytes = source.NativePayloads?
            .SingleOrDefault(payload =>
                payload.Format == Rac3MobyRuntimeSession.InstancePayloadFormat)?.Data
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

        return new Rac3MobyPlacementFacts(
            NativeX: F(NativePositionOffset + 0x00),
            NativeY: F(NativePositionOffset + 0x04),
            NativeZ: F(NativePositionOffset + 0x08),
            RotationX: F(NativeRotationOffset + 0x00),
            RotationY: F(NativeRotationOffset + 0x04),
            RotationZ: F(NativeRotationOffset + 0x08));
    }

    public static Rac3NativePoint ObpYUpToNative(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(x));
        return new Rac3NativePoint(x, z, y);
    }
}

public sealed record Rac3MobyPlacementFacts(
    float NativeX,
    float NativeY,
    float NativeZ,
    float RotationX,
    float RotationY,
    float RotationZ);

public readonly record struct Rac3NativePoint(float X, float Y, float Z);
