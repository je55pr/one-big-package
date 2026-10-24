using System.Buffers.Binary;
using System.Numerics;

namespace OBP.PS2.Animation;

/// <summary>
/// The trilogy sequence container and GC/UYA packed joint channels. Packed
/// decoding is checked against GC 0x308b20 and UYA 0x4397f8, not an exporter.
/// Player sequence offsets are relative to their own bounded sequence chunk;
/// ordinary class-local frame offsets are relative to the class payload.
/// </summary>
public static class Ps2SkeletalAnimation
{
    public readonly record struct Rotation(short X, short Y, short Z, short W)
    {
        public Quaternion Quaternion => new(X / 32768f, Y / 32768f, Z / 32768f, W / 32768f);
    }

    public readonly record struct VectorChannel(short X, short Y, short Z, ushort JointTag)
    {
        public int Joint => JointTag & 0x7fff;
        public Vector3 Vector => new(X, Y, Z);
    }

    public sealed record Frame(float TransitionRate, short TimelineUnits,
        IReadOnlyList<Rotation> Rotations, IReadOnlyList<VectorChannel> Scales,
        IReadOnlyList<VectorChannel> Translations);

    public sealed record Sequence(int Id, float ConstantTransitionRate,
        IReadOnlyList<Frame> Frames, IReadOnlyList<uint> SoundCues);

    public static Sequence ReadSequence(ReadOnlySpan<byte> bytes, int offset, int joints, int id,
        bool sequenceRelativeFrames, int? vectorJointCount = null)
    {
        if (joints is < 1 or > 256) throw new InvalidDataException("Invalid skeletal joint count.");
        Require(bytes, offset, 0x1c);
        int count = bytes[offset + 0x10], sounds = bytes[offset + 0x12];
        if (count == 0) throw new InvalidDataException("Empty skeletal sequence.");
        Require(bytes, offset + 0x1c, (count + sounds) * 4);
        int descriptor = checked(offset + 0x1c + count * 4 + bytes[offset + 0x13] * 4);
        var frameOffsets = new int[count];
        var flags = new uint[count];
        for (int frame = 0; frame < count; frame++)
        {
            uint entry = U32(bytes, offset + 0x1c + frame * 4);
            frameOffsets[frame] = checked((int)(entry & 0x0fffffff) + (sequenceRelativeFrames ? offset : 0));
            flags[frame] = entry >> 28;
            Require(bytes, frameOffsets[frame], 4);
        }
        if (flags.Any(flag => flag != flags[0]) || flags[0] is not (0 or 15))
            throw new NotSupportedException("Unknown or mixed skeletal frame encoding.");
        if (flags[0] == 15)
        {
            Require(bytes, descriptor, 6 + joints * 6);
            int channelCounts = descriptor + 4 + joints * 6;
            Require(bytes, descriptor, 6 + joints * 6 + (bytes[channelCounts] + bytes[channelCounts + 1]) * 8);
        }
        var frames = new Frame[count];
        for (int frame = 0; frame < count; frame++)
        {
            int at = frameOffsets[frame], end = bytes.Length;
            foreach (int candidate in frameOffsets) if (candidate > at) end = Math.Min(end, candidate);
            var bounded = bytes[..end];
            frames[frame] = flags[frame] == 0 ? Regular(bounded, at, joints, vectorJointCount ?? joints) : Packed(bounded, descriptor, at, joints);
        }
        float rate = F32(bytes, offset + 0x18);
        if (!float.IsFinite(rate) || rate < 0) throw new InvalidDataException("Invalid sequence transition rate.");
        var cues = new uint[sounds];
        for (int i = 0; i < sounds; i++) cues[i] = U32(bytes, offset + 0x1c + (count + i) * 4);
        return new(id, rate, frames, cues);
    }

    private static Frame Regular(ReadOnlySpan<byte> bytes, int at, int joints, int vectorJoints)
    {
        Require(bytes, at, 0x10);
        int scales = U16(bytes, at + 0xa), translations = U16(bytes, at + 0xe);
        if (U16(bytes, at + 8) != joints * 8 || scales > joints || translations > joints)
            throw new InvalidDataException("Skeletal frame channels do not match their skeleton.");
        Require(bytes, at + 0x10, (joints + scales + translations) * 8);
        float rate = F32(bytes, at);
        if (!float.IsFinite(rate) || rate < 0) throw new InvalidDataException("Invalid frame transition rate.");
        var rotations = new Rotation[joints];
        int cursor = at + 0x10;
        for (int j = 0; j < joints; j++, cursor += 8)
            rotations[j] = new(S16(bytes, cursor), S16(bytes, cursor + 2), S16(bytes, cursor + 4), S16(bytes, cursor + 6));
        var scale = Vectors(bytes, ref cursor, scales, vectorJoints, true);
        var translation = Vectors(bytes, ref cursor, translations, vectorJoints, false);
        return new(rate, S16(bytes, at + 4), rotations, scale, translation);
    }

    private static Frame Packed(ReadOnlySpan<byte> bytes, int descriptor, int at, int joints)
    {
        uint layout = U32(bytes, descriptor);
        int tenBit = at + 4, tenShift = 0;
        int literals = checked(at + 4 + (int)(layout & 0x3ff));
        int deltas = checked(at + 4 + (int)((layout >> 10) & 0x7ff));
        int signs = checked(at + 4 + (int)(layout >> 21));
        int tenEnd = literals, literalEnd = deltas;
        if (literals < tenBit || deltas < literals || signs < deltas)
            throw new InvalidDataException("Packed joint partitions overlap.");
        Require(bytes, signs, (joints + 7) / 8);
        var rotations = new Rotation[joints];
        Span<short> xyz = stackalloc short[3];
        for (int joint = 0; joint < joints; joint++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int value = S16(bytes, descriptor + 4 + joint * 6 + axis * 2);
                switch (value & 3)
                {
                    case 1:
                        if (deltas >= signs) throw new InvalidDataException("Packed byte delta exceeds its partition.");
                        value += unchecked((sbyte)bytes[deltas++]) * 4;
                        break;
                    case 2:
                        if (tenBit > tenEnd - 4) throw new InvalidDataException("Packed ten-bit delta exceeds its partition.");
                        int bits = (int)(U32(bytes, tenBit) >> tenShift) & 0x3ff;
                        value += ((bits & 0x200) == 0 ? bits : bits - 1024) * 4;
                        tenShift += 10;
                        if (tenShift == 30) { tenBit += 4; tenShift = 0; }
                        break;
                    case 3:
                        if (literals > literalEnd - 2) throw new InvalidDataException("Packed literal exceeds its partition.");
                        value = S16(bytes, literals); literals += 2;
                        break;
                }
                xyz[axis] = unchecked((short)value);
            }
            double x = xyz[0] / 32768d, y = xyz[1] / 32768d, z = xyz[2] / 32768d;
            int w = Math.Min(32767, (int)(Math.Sqrt(Math.Max(0, 1 - x * x - y * y - z * z)) * 32768));
            if ((bytes[signs + joint / 8] & (1 << (joint % 8))) != 0) w = -w;
            rotations[joint] = new(xyz[0], xyz[1], xyz[2], (short)w);
        }
        int auxiliary = signs + (joints + 7) / 8;
        int baseChannels = descriptor + 4 + joints * 6;
        int scales = bytes[baseChannels], translations = bytes[baseChannels + 1];
        int baseCursor = baseChannels + 2;
        var scale = PackedVectors(bytes, ref baseCursor, ref auxiliary, scales, joints, true);
        var translation = PackedVectors(bytes, ref baseCursor, ref auxiliary, translations, joints, false);
        int divisor = U16(bytes, at);
        return new(divisor == 0 ? 0 : 1f / divisor, S16(bytes, at + 2), rotations, scale, translation);
    }

    private static VectorChannel[] PackedVectors(ReadOnlySpan<byte> bytes, ref int basis, ref int cursor,
        int count, int joints, bool scale)
    {
        if (count > joints) throw new InvalidDataException("Too many packed vector channels.");
        var result = new VectorChannel[count];
        Span<short> xyz = stackalloc short[3];
        for (int i = 0; i < count; i++, basis += 8)
        {
            Require(bytes, cursor, 1);
            int flags = bytes[cursor++];
            for (int k = 0; k < 3; k++, flags >>= 2)
            {
                int value = S16(bytes, basis + k * 2);
                if ((flags & 3) == 1)
                {
                    Require(bytes, cursor, 1);
                    value += unchecked((sbyte)bytes[cursor++]) * 4;
                }
                else if ((flags & 3) == 2) { value = S16(bytes, cursor); cursor += 2; }
                xyz[k] = unchecked((short)value);
            }
            result[i] = new(xyz[0], xyz[1], xyz[2], U16(bytes, basis + 6));
        }
        ValidateVectors(result, joints, scale);
        return result;
    }

    private static VectorChannel[] Vectors(ReadOnlySpan<byte> bytes, ref int cursor, int count, int joints, bool scale)
    {
        var result = new VectorChannel[count];
        for (int i = 0; i < count; i++, cursor += 8)
            result[i] = new(S16(bytes, cursor), S16(bytes, cursor + 2), S16(bytes, cursor + 4), U16(bytes, cursor + 6));
        ValidateVectors(result, joints, scale);
        return result;
    }

    private static void ValidateVectors(VectorChannel[] vectors, int joints, bool scale)
    {
        if (vectors.Select(v => v.Joint).Distinct().Count() != vectors.Length ||
            vectors.Any(v => (scale ? v.Joint : v.JointTag) >= joints))
            throw new InvalidDataException("Vector channel references a missing or repeated joint.");
    }

    private static void Require(ReadOnlySpan<byte> bytes, int at, int size)
    {
        if (at < 0 || size < 0 || at > bytes.Length - size)
            throw new InvalidDataException("Skeletal animation range exceeds its source.");
    }
    private static short S16(ReadOnlySpan<byte> b, int at) { Require(b, at, 2); return BinaryPrimitives.ReadInt16LittleEndian(b[at..]); }
    private static ushort U16(ReadOnlySpan<byte> b, int at) { Require(b, at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(b[at..]); }
    private static uint U32(ReadOnlySpan<byte> b, int at) { Require(b, at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(b[at..]); }
    private static float F32(ReadOnlySpan<byte> b, int at) => BitConverter.Int32BitsToSingle(unchecked((int)U32(b, at)));
}
