using System.Buffers.Binary;
using OBP.RAC1.Geometry;

namespace OBP.RAC1.Animation;

/// <summary>Class+0x1c semantic joint chains used by 0x253fa0 and its world-space query wrappers.</summary>
public static class Rac1MobyAttachment
{
    /// <summary>Class chain's second list, consumed by 0x24f028 for joint animation layers.</summary>
    public static IReadOnlyList<int> ReadAnimationLayerJoints(ReadOnlySpan<byte> classPayload, int semanticJoint)
    {
        _ = ReadJoint(classPayload, semanticJoint);
        int table = BinaryPrimitives.ReadInt32LittleEndian(classPayload[0x1c..]);
        int chain = BinaryPrimitives.ReadInt32LittleEndian(classPayload[(table + 4 + semanticJoint * 4)..]);
        int count = classPayload[chain + 2];
        int start = chain + 4 + classPayload[chain];
        RequireRange(classPayload, start, count);
        var joints = classPayload.Slice(start, count).ToArray().Select(joint => (int)joint).ToArray();
        int jointCount = classPayload[8];
        if (joints.Distinct().Count() != count || joints.Any(joint => joint >= jointCount))
            throw new InvalidDataException("Animation layer references invalid joints.");
        return joints;
    }

    public static int ReadJoint(ReadOnlySpan<byte> classPayload, int semanticJoint)
    {
        if (classPayload.Length < Rac1Moby.ClassHeaderSize || semanticJoint < 0)
            throw new InvalidDataException("Invalid R&C1 attachment joint source.");
        int table = BinaryPrimitives.ReadInt32LittleEndian(classPayload[0x1c..]);
        RequireRange(classPayload, table, 4);
        int count = BinaryPrimitives.ReadInt32LittleEndian(classPayload[table..]);
        if (semanticJoint >= count || count is < 1 or > 256)
            throw new InvalidDataException("Attachment semantic joint is outside its table.");
        RequireRange(classPayload, table + 4, count * 4);
        int chain = BinaryPrimitives.ReadInt32LittleEndian(classPayload[(table + 4 + semanticJoint * 4)..]);
        RequireRange(classPayload, chain, 4);
        int chainLength = BinaryPrimitives.ReadUInt16LittleEndian(classPayload[chain..]);
        if (chainLength is < 1 or > 128)
            throw new InvalidDataException("Attachment joint chain has an invalid length.");
        RequireRange(classPayload, chain + 4, chainLength);
        for (int i = 0; i < chainLength; i++)
            if (classPayload[chain + 4 + i] >= classPayload[8])
                throw new InvalidDataException("Attachment chain references a missing skeleton joint.");
        return classPayload[chain + 3 + chainLength];
    }

    private static void RequireRange(ReadOnlySpan<byte> source, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > source.Length - length)
            throw new InvalidDataException("Attachment metadata exceeds its class source.");
    }
}
