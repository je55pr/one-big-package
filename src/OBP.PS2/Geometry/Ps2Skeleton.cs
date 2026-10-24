using System.Buffers.Binary;
using System.Numerics;

namespace OBP.PS2.Geometry;

/// <summary>Shared native class bind/common records and semantic joint chains.</summary>
public static class Ps2Skeleton
{
    public sealed record Joint(int Parent, Vector3 Translation, Matrix4x4 InverseBind);
    public sealed record Chain(int Target, IReadOnlyList<int> Ancestors, IReadOnlyList<int> LayerJoints);
    public sealed record Source(float CoordinateScale, IReadOnlyList<Joint> Joints, IReadOnlyList<Chain> Chains);

    public static Source Read(ReadOnlySpan<byte> bytes)
    {
        Require(bytes, 0, 0x48);
        int count = bytes[8], bind = I32(bytes, 0x14), common = I32(bytes, 0x18), table = I32(bytes, 0x1c);
        float scale = F32(bytes, 0x24) / 1024;
        if (count == 0 || !float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("Invalid native skeleton header.");
        Require(bytes, bind, count * 64); Require(bytes, common, count * 16);
        var joints = new Joint[count];
        for (int j = 0; j < count; j++)
        {
            int at = bind + j * 64, t = common + j * 16;
            int parentOffset = U16(bytes, t + 12), parent = parentOffset / 64;
            if (parentOffset % 64 != 0 || parent >= count)
                throw new InvalidDataException("Invalid native parent offset.");
            var translation = new Vector3(F32(bytes, t), F32(bytes, t + 4), F32(bytes, t + 8)) * scale;
            var inverse = new Matrix4x4(F32(bytes, at), F32(bytes, at + 4), F32(bytes, at + 8), 0,
                F32(bytes, at + 16), F32(bytes, at + 20), F32(bytes, at + 24), 0,
                F32(bytes, at + 32), F32(bytes, at + 36), F32(bytes, at + 40), 0,
                F32(bytes, at + 48) * scale, F32(bytes, at + 52) * scale, F32(bytes, at + 56) * scale, 1);
            joints[j] = new(parent == j ? -1 : parent, translation, inverse);
        }
        int chainCount = I32(bytes, table);
        if (chainCount is < 1 or > 256) throw new InvalidDataException("Invalid native joint-chain count.");
        Require(bytes, table + 4, chainCount * 4);
        var chains = new Chain[chainCount];
        for (int i = 0; i < chainCount; i++)
        {
            int at = I32(bytes, table + 4 + i * 4);
            Require(bytes, at, 4);
            int length = U16(bytes, at), layerCount = bytes[at + 2];
            if (length is < 1 or > 128) throw new InvalidDataException("Invalid native joint chain.");
            Require(bytes, at + 4, length + layerCount);
            var ancestors = bytes.Slice(at + 4, length).ToArray().Select(v => (int)v).ToArray();
            var layer = bytes.Slice(at + 4 + length, layerCount).ToArray().Select(v => (int)v).ToArray();
            if (ancestors.Concat(layer).Any(j => j >= count) || layer.Distinct().Count() != layerCount)
                throw new InvalidDataException("Native chain references an invalid joint.");
            chains[i] = new(ancestors[^1], ancestors, layer);
        }
        return new(scale, joints, chains);
    }

    /// <summary>Animation-only classes 1/2 scatter their local channels onto the full player skeleton.</summary>
    public static int[] ReadAnimationJointMap(ReadOnlySpan<byte> bytes, int targetJointCount)
    {
        Require(bytes, 0, 0x48);
        int count = bytes[8], table = I32(bytes, 0x1c);
        Require(bytes, table, count + 5);
        if (count == 0 || bytes[table + 4 + count] != 0xff)
            throw new InvalidDataException("Invalid animation-only joint map.");
        var result = bytes.Slice(table + 4, count).ToArray().Select(j => (int)j).ToArray();
        if (result.Any(j => j >= targetJointCount) || result.Distinct().Count() != count)
            throw new InvalidDataException("Animation joint map escapes or repeats its target skeleton.");
        return result;
    }

    private static void Require(ReadOnlySpan<byte> b, int at, int size)
    {
        if (at < 0 || size < 0 || at > b.Length - size) throw new InvalidDataException("Skeleton range exceeds its source.");
    }
    private static int I32(ReadOnlySpan<byte> b, int at) { Require(b, at, 4); return BinaryPrimitives.ReadInt32LittleEndian(b[at..]); }
    private static int U16(ReadOnlySpan<byte> b, int at) { Require(b, at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(b[at..]); }
    private static float F32(ReadOnlySpan<byte> b, int at)
    {
        float value = BitConverter.Int32BitsToSingle(I32(b, at));
        if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite skeleton component.");
        return value;
    }
}
