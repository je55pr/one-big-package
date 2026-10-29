using OBP.RAC1.Animation;
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
        => Rac1MobyAttachment.ReadJoint(classPayload, semanticJoint);

    public static PlayerAvatarAttachmentPose ToAttachment(Rac1MobyPose.JointPose joint) =>
        new(new(joint.Position[0], joint.Position[1], joint.Position[2]),
            new(joint.NativeLinear[0, 0], joint.NativeLinear[0, 1], joint.NativeLinear[0, 2]),
            new(joint.NativeLinear[1, 0], joint.NativeLinear[1, 1], joint.NativeLinear[1, 2]),
            new(joint.NativeLinear[2, 0], joint.NativeLinear[2, 1], joint.NativeLinear[2, 2]));

}
