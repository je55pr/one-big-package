using System.Buffers.Binary;
using OBP.Core;
using OBP.PS2.Presentation;
using OBP.PS2.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime.Player;

namespace OBP.RAC3.Player;

/// <summary>Veldin's original player and arm carriers; Cannon selectors at 0x4e6a08 and definition 43.</summary>
public static class Rac3RatchetSkeletalSource
{
    public static PlayerEquipmentAnimationSource ReadShockCannon(Rac3LevelCore.Core core)
    {
        var decoded = Ps2RatchetSkeletalDecoder.Read(core.Index, core.Assets, core.SectionBoundaries,
            core.Header.RatchetSeqsOffset, Payload, ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "UYA");
        var skeleton = decoded.Skeleton;
        var mesh = GcUyaMoby.ReadClass(decoded.BodyClass);
        var avatar = new PlayerSkeletalAsset(new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "ratchet", "rac3:ratchet:moby-class-0"),
            skeleton, mesh.Positions.Length == 0 ? null : new(mesh.Positions, mesh.VertexJoints, mesh.VertexWeights), decoded.Body);
        // Native row43: hand matrix0, both arms, full49 / moving-1 / crouch65,
        // one-cycle ownership; the Cannon entry blend is two updates.
        return new(avatar, decoded.Arms, 2, skeleton.Attachments[0].Joint, 49, -1, 65, false, 2)
        { BlendSteps = new(.25f, .07f, .25f), ArmBankUpperExclusive = 23 };

        byte[] Payload(int id)
        {
            var table = core.Header.MobyClasses;
            for (int i = 0; i < table.Count; i++)
            {
                int row = table.Offset + i * 0x20;
                if (I32(core.Index, row + 4) != id) continue;
                int at = I32(core.Index, row), end = core.SectionBoundaries.FirstOrDefault(b => b > at, core.Assets.Length);
                if (at <= 0 || at >= end || end > core.Assets.Length) throw new InvalidDataException("Invalid UYA player carrier.");
                return core.Assets[at..end];
            }
            throw new InvalidDataException($"UYA player carrier {id} is absent.");
        }
    }

    private static int I32(byte[] b, int at)
    {
        if (at < 0 || at > b.Length - 4) throw new InvalidDataException("UYA skeletal directory exceeds its source.");
        return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    }
}
