using System.Buffers.Binary;
using OBP.Core;
using OBP.PS2.Presentation;
using OBP.PS2.Geometry;
using OBP.RAC2.Level;
using OBP.Runtime.Player;

namespace OBP.RAC2.Player;

/// <summary>Oozla's original player and arm carriers; Lancer selectors at 0x2b0020 and descriptor row 30.</summary>
public static class Rac2RatchetSkeletalSource
{
    public static PlayerEquipmentAnimationSource ReadLancer(Rac2LevelCore.Core core)
    {
        var decoded = Ps2RatchetSkeletalDecoder.Read(core.Index, core.Assets, core.SectionBoundaries,
            core.Header.RatchetSeqsOffset, Payload, ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, "GC");
        var skeleton = decoded.Skeleton;
        var mesh = GcUyaMoby.ReadClass(decoded.BodyClass);
        var avatar = new PlayerSkeletalAsset(new(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, "ratchet", "rac2:ratchet:moby-class-0"),
            skeleton, mesh.Positions.Length == 0 ? null : new(mesh.Positions, mesh.VertexJoints, mesh.VertexWeights), decoded.Body);
        // Native row30: hand matrix0, one arm, full56 / moving-1 / crouch62,
        // sustained ownership; the Lancer-specific entry blend is five updates.
        return new(avatar, decoded.Arms, 1, skeleton.Attachments[0].Joint, 56, -1, 62, true, 5)
        { BlendSteps = new(.25f, .07f, .25f), ArmBankUpperExclusive = 23 };

        byte[] Payload(int id)
        {
            var table = core.Header.MobyClasses;
            for (int i = 0; i < table.Count; i++)
            {
                int row = table.Offset + i * 0x20;
                if (I32(core.Index, row + 4) != id) continue;
                int at = I32(core.Index, row), end = core.SectionBoundaries.FirstOrDefault(b => b > at, core.Assets.Length);
                if (at <= 0 || at >= end || end > core.Assets.Length) throw new InvalidDataException("Invalid GC player carrier.");
                return core.Assets[at..end];
            }
            throw new InvalidDataException($"GC player carrier {id} is absent.");
        }
    }

    private static int I32(byte[] b, int at)
    {
        if (at < 0 || at > b.Length - 4) throw new InvalidDataException("GC skeletal directory exceeds its source.");
        return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    }
}
