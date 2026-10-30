using System.Buffers.Binary;
using OBP.PS2.Graphics;
using OBP.PS2.Vif;

namespace OBP.PS2.Geometry;

public static partial class GcUyaMoby
{

    /// <summary>Packet-local VIF, material switches and vertex table metadata.</summary>
    private sealed record PacketData(
        int EntryOffset, int HeaderOffset, int VertexBase, int MatrixTransferCount,
        int TwoWay, int ThreeWay, int VertexCount, int DuplicateCount, int VertexTableOffset,
        List<(int S, int T)> Sts, List<int> StripIndices, List<int> SecretIndices,
        List<(int TextureId, int MaterialStateIndex)> Textures);

    /// <summary>
    /// A skipped packet invalidates strict cache/skin continuity, without
    /// resetting material inheritance from earlier packets.
    /// </summary>
    private static PacketData? ReadPacket(DecodeState state, int eo)
    {
        byte[] buf = state.Buffer;
        var materials = state.Materials;
        long vifListOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eo + 0x00));
        int vifListSize = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(eo + 0x04)) * 0x10;
        long vertexOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eo + 0x08));

        if (vifListOffset + vifListSize > buf.Length || vertexOffset + 0x10 > buf.Length)
        {
            state.InvalidateSkippedPacket();
            return null;
        }

        var unpacks = Vif.FilterUnpacks(Vif.ReadCommandList(
            new ArraySegment<byte>(buf, (int)vifListOffset, vifListSize)));
        if (unpacks.Count < 2)
        {
            state.InvalidateSkippedPacket();
            return null;
        }

        var stData = unpacks[0].Data;
        var sts = new List<(int S, int T)>();
        for (int i = 0; i + 4 <= stData.Count; i += 4)
        {
            sts.Add((
                BinaryPrimitives.ReadInt16LittleEndian(stData.AsSpan(i)),
                BinaryPrimitives.ReadInt16LittleEndian(stData.AsSpan(i + 2))));
        }

        var idxData = unpacks[1].Data;
        if (idxData.Count < 4)
        {
            state.InvalidateSkippedPacket();
            return null;
        }

        var secretIndices = new List<int> { (sbyte)idxData[2] };
        var idxBuf = new List<int>();
        for (int i = 4; i < idxData.Count; i++)
        {
            idxBuf.Add((sbyte)idxData[i]);
        }

        var textures = new List<(int TextureId, int MaterialStateIndex)>();
        if (unpacks.Count >= 3)
        {
            var td = unpacks[2].Data;
            for (int i = 0; i * 0x40 + 0x40 <= td.Count; i++)
            {
                secretIndices.Add((sbyte)td[i * 0x10 + 0x0c]);
                var decodedMaterial = RcMaterialState.ReadMoby(td.AsSpan(i * 0x40, 0x40));
                int stateIndex = materials.Count;
                materials.Add(decodedMaterial);
                textures.Add((decodedMaterial.TextureId, stateIndex));
            }
        }

        int vh = (int)vertexOffset;
        int matrixTransferCount = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(vh + 0x00));
        int twoWay = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(vh + 0x02));
        int threeWay = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(vh + 0x04));
        int mainCount = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(vh + 0x06));
        int dupCount = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(vh + 0x08));
        int vertexTableOffset = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(vh + 0x0c));
        if (twoWay + threeWay > 0)
        {
            state.Skinned = true;
        }

        int inFileCount = twoWay + threeWay + mainCount;
        int vbase = vh + vertexTableOffset;
        if (vbase + inFileCount * 0x10 > buf.Length || inFileCount > 4096)
        {
            state.InvalidateSkippedPacket();
            return null;
        }


        return new PacketData(eo, vh, vbase, matrixTransferCount, twoWay, threeWay,
            inFileCount, dupCount, vertexTableOffset, sts, idxBuf, secretIndices, textures);
    }

}
