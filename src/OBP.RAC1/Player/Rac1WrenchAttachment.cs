using System.Buffers.Binary;
using OBP.RAC1.Animation;
using OBP.RAC1.Geometry;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Item8 selects socket0. 0x209240 queries semantic joint0 through class+0x1c;
/// that chain terminates at Ratchet joint56. 0x20f6f8 applies that joint's native
/// matrix and position to the held Moby, without a calibrated root offset.
/// </summary>
public static class Rac1WrenchAttachment
{
    public const string SocketKey = "rac1:wrench-hand";
    public const int SemanticJoint = 0;
    public const int RatchetJoint = 56;

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
        int joint = classPayload[chain + 3 + chainLength];
        return joint;
    }

    public static PlayerAvatarAttachmentPose ToAttachment(Rac1MobyPose.JointPose joint) =>
        new(new(joint.Position[0], joint.Position[1], joint.Position[2]),
            new(joint.NativeLinear[0, 0], joint.NativeLinear[0, 1], joint.NativeLinear[0, 2]),
            new(joint.NativeLinear[1, 0], joint.NativeLinear[1, 1], joint.NativeLinear[1, 2]),
            new(joint.NativeLinear[2, 0], joint.NativeLinear[2, 1], joint.NativeLinear[2, 2]));

    private static void RequireRange(ReadOnlySpan<byte> source, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > source.Length - length)
            throw new InvalidDataException("Attachment metadata exceeds its class source.");
    }
}
