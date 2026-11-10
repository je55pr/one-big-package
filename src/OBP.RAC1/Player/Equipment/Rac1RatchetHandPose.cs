using System.Buffers.Binary;
using OBP.RAC1.Level;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>Local finger channels copied by 0x2092c8 into a held glove's encoded pose.</summary>
public sealed record Rac1RatchetHandPose(IReadOnlyList<PlayerJointChannels> Joints, float CoordinateScale)
{
    public static IReadOnlyList<int> ReadJointMap(Rac1ExecutableOverlay overlay)
    {
        // The header names the attachment joint; the following full-joint IDs terminate with -1.
        if (BinaryPrimitives.ReadInt32LittleEndian(overlay.Read(0x17a5c0, 4)) != 54)
            throw new InvalidDataException("R&C1 glove pose attachment differs from retail.");
        var joints = new List<int>();
        for (int i = 0; i <= 16; i++)
        {
            int joint = BinaryPrimitives.ReadInt32LittleEndian(overlay.Read(0x17a5c4 + i * 4, 4));
            if (joint == -1 && joints.Count == 16) return joints;
            if (joint is < 0 or >= 111 || joints.Contains(joint))
                throw new InvalidDataException("R&C1 glove pose joint list is invalid.");
            joints.Add(joint);
        }
        throw new InvalidDataException("R&C1 glove pose joint list is not terminated.");
    }
}
