using System.Buffers.Binary;
using OBP.PS2.Vif;

namespace OBP.RAC1.Geometry;

/// <summary>
/// R&C1 Moby high-LOD bind/rest-pose geometry. The R&C1 generation keeps the
/// shared packet/strip grammar but uses an 0x20-byte 8*u32 vertex header.
/// Base-surface recovery intentionally does not apply skeleton transforms:
/// retail validation proves the stored positions already describe the bind pose.
/// </summary>
public static class Rac1Moby
{
    public const int ClassHeaderSize = 0x48;
    public const int PacketEntrySize = 0x10;
    public const int VertexHeaderSize = 0x20;
    private const int JointStride = 0x40;
    private const int CommonTransStride = 0x10;
    private const int VertexPipeline = 7;

    public sealed record Mesh(
        double[] Positions,
        float[] Uvs,
        int[] Indices,
        int[] TriangleMaterialSlots,
        float Scale,
        int HighLodPacketCount,
        double BoundingRadius,
        int JointCount)
    {
        /// <summary>Three joint indices per emitted vertex; empty for rigid classes.</summary>
        public int[] VertexJoints { get; init; } = [];

        /// <summary>Three normalized weights per emitted vertex; empty for rigid classes.</summary>
        public float[] VertexWeights { get; init; } = [];

        public int MatrixTransferCount { get; init; }
        public int TwoWayBlendVertexCount { get; init; }
        public int ThreeWayBlendVertexCount { get; init; }
        public int InFileVertexCount { get; init; }
    }

    /// <summary>
    /// One native 0x40 skeleton record plus its matching 0x10 common-transform
    /// record. The 4x4 matrix is preserved verbatim; no bind/animation meaning
    /// is assigned to it until the remaining retail transform archaeology lands.
    /// </summary>
    public sealed record SkeletonJoint(
        int Index,
        int ParentByteOffset,
        int ParentRecordIndex,
        float[] NativeMatrix,
        float CommonX,
        float CommonY,
        float CommonZ);

    private struct SkinAttr
    {
        public int Count;
        public int J0, J1, J2;
        public int W0, W1, W2;
    }

    private readonly record struct CachedVertex(double X, double Y, double Z, SkinAttr Skin);
    private sealed class Primitive
    {
        public int Material;
        public readonly List<int> Strip = [];
    }

    private sealed class PacketState
    {
        public readonly Dictionary<int, CachedVertex> VertexCache = [];
        public readonly SkinAttr?[] BlendCache = new SkinAttr?[64];
        public int ActiveTexture;
    }

    private sealed record Packet(
        double[] Positions,
        float[] Uvs,
        int[] Indices,
        int[] MaterialSlots,
        int[] VertexJoints,
        float[] VertexWeights,
        int ActiveTexture,
        int MatrixTransferCount,
        int TwoWayBlendVertexCount,
        int ThreeWayBlendVertexCount,
        int InFileVertexCount);

    private static int AsS8(byte value) => unchecked((sbyte)value);
    private static int Align(int value, int amount) => ((value + amount - 1) / amount) * amount;

    public static IReadOnlyList<SkeletonJoint> ReadSkeleton(byte[] bytes)
    {
        if (bytes.Length < ClassHeaderSize)
        {
            throw new InvalidDataException("R&C1 Moby class buffer shorter than the 0x48 header.");
        }
        int jointCount = bytes[0x08];
        if (jointCount == 0)
        {
            return [];
        }

        int skeletonOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x14));
        int commonTransOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x18));
        bool geometryBearing = bytes[0x04] > 0;
        if (skeletonOffset <= 0 || commonTransOffset <= 0 ||
            (long)skeletonOffset + (long)jointCount * JointStride > bytes.Length ||
            (long)commonTransOffset + (long)jointCount * CommonTransStride > bytes.Length)
        {
            if (!geometryBearing)
            {
                return [];
            }
            throw new InvalidDataException("R&C1 animated Moby has an out-of-range skeleton/common-transform table.");
        }

        var joints = new List<SkeletonJoint>(jointCount);
        for (int j = 0; j < jointCount; j++)
        {
            int s = skeletonOffset + j * JointStride;
            var matrix = new float[16];
            for (int i = 0; i < matrix.Length; i++)
            {
                matrix[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(s + i * 4));
                if (!float.IsFinite(matrix[i]))
                {
                    throw new InvalidDataException($"R&C1 Moby joint {j} contains a non-finite skeleton matrix value.");
                }
            }

            int c = commonTransOffset + j * CommonTransStride;
            float cx = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(c));
            float cy = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(c + 4));
            float cz = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(c + 8));
            int parentByteOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(c + 0x0c));
            if (!float.IsFinite(cx) || !float.IsFinite(cy) || !float.IsFinite(cz) ||
                parentByteOffset % JointStride != 0 || parentByteOffset / JointStride >= jointCount ||
                (parentByteOffset != 0 && parentByteOffset / JointStride >= j))
            {
                throw new InvalidDataException($"R&C1 Moby joint {j} has an invalid common-transform record.");
            }
            joints.Add(new SkeletonJoint(j, parentByteOffset, parentByteOffset / JointStride, matrix, cx, cy, cz));
        }
        return joints;
    }

    private static SkinAttr[] ReadPacketSkin(
        byte[] bytes, int vertexOffset, int vertexTableOffset, int matrixTransferCount,
        int twoWayCount, int threeWayCount, int inFileVertexCount, int jointCount,
        PacketState state, int packetIndex)
    {
        void CheckAddress(int address, string role)
        {
            if (address < 0 || address > 0xff || (address & 3) != 0)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has invalid {role} VU0 address {address}.");
            }
        }

        SkinAttr Rigid(int joint)
        {
            if (joint < 0 || joint >= jointCount)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} references joint {joint} outside {jointCount} joints.");
            }
            return new SkinAttr { Count = 1, J0 = joint, W0 = 256 };
        }

        void Set(int address, SkinAttr attr)
        {
            CheckAddress(address, "store");
            if (address != 0xf4)
            {
                state.BlendCache[address >> 2] = attr;
            }
        }

        SkinAttr Get(int address)
        {
            CheckAddress(address, "load");
            return state.BlendCache[address >> 2]
                ?? throw new InvalidDataException($"R&C1 Moby packet {packetIndex} reads uninitialized VU0 slot {address >> 2}.");
        }

        for (int t = 0; t < matrixTransferCount; t++)
        {
            int at = vertexOffset + VertexHeaderSize + t * 2;
            Set(bytes[at + 1], Rigid(bytes[at]));
        }

        int vertexBase = vertexOffset + vertexTableOffset;
        var attrs = new SkinAttr[inFileVertexCount];
        for (int v = 0; v < inFileVertexCount; v++)
        {
            int at = vertexBase + v * 0x10;
            int upperBits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)) >> 9;
            int B(int offset) => bytes[at + offset];
            SkinAttr attr;
            if (v < twoWayCount)
            {
                Set(B(6), Rigid(upperBits));
                var a = Get(B(2));
                var b = Get(B(3));
                if (a.Count != 1 || b.Count != 1 || B(4) + B(5) != 256)
                    throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has invalid two-way blend state at vertex {v}.");
                attr = new SkinAttr { Count = 2, J0 = a.J0, J1 = b.J0, W0 = B(4), W1 = B(5) };
                Set(B(7), attr);
            }
            else if (v < twoWayCount + threeWayCount)
            {
                var a = Get(B(2));
                var b = Get(B(3));
                var c = Get(upperBits * 2);
                if (a.Count != 1 || b.Count != 1 || c.Count != 1 || B(4) + B(5) + B(6) != 256)
                    throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has invalid three-way blend state at vertex {v}.");
                attr = new SkinAttr { Count = 3, J0 = a.J0, J1 = b.J0, J2 = c.J0, W0 = B(4), W1 = B(5), W2 = B(6) };
                Set(B(7), attr);
            }
            else
            {
                Set(B(3), Rigid(upperBits));
                attr = Get(B(2));
            }
            attrs[v] = attr;
        }
        return attrs;
    }

    public static Mesh ReadClass(byte[] bytes)
    {
        if (bytes.Length < ClassHeaderSize)
        {
            throw new InvalidDataException("R&C1 Moby class buffer shorter than the 0x48 header.");
        }
        int packetTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x00));
        int highLodPacketCount = bytes[0x04];
        int jointCount = bytes[0x08];
        float scale = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(0x24));
        double boundingRadius = System.Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(0x3c))) * (scale / 1024.0);
        if (!float.IsFinite(scale))
        {
            throw new InvalidDataException($"R&C1 Moby class has non-finite scale {scale}.");
        }
        if (highLodPacketCount == 0)
        {
            return new Mesh([], [], [], [], scale, 0, boundingRadius, jointCount);
        }
        if (packetTableOffset <= 0 || (long)packetTableOffset + (long)highLodPacketCount * PacketEntrySize > bytes.Length)
        {
            throw new InvalidDataException($"R&C1 Moby packet table {packetTableOffset}+{highLodPacketCount * PacketEntrySize} is out of range.");
        }

        var state = new PacketState();
        var positions = new List<double>();
        var uvs = new List<float>();
        var indices = new List<int>();
        var materialSlots = new List<int>();
        var vertexJoints = new List<int>();
        var vertexWeights = new List<float>();
        int matrixTransfers = 0, twoWay = 0, threeWay = 0, inFileVertices = 0;
        for (int packetIndex = 0; packetIndex < highLodPacketCount; packetIndex++)
        {
            var packet = DecodePacket(bytes, packetTableOffset + packetIndex * PacketEntrySize, scale, jointCount, state, packetIndex);
            int vertexBase = positions.Count / 3;
            positions.AddRange(packet.Positions);
            uvs.AddRange(packet.Uvs);
            vertexJoints.AddRange(packet.VertexJoints);
            vertexWeights.AddRange(packet.VertexWeights);
            foreach (int index in packet.Indices)
            {
                indices.Add(vertexBase + index);
            }
            materialSlots.AddRange(packet.MaterialSlots);
            matrixTransfers += packet.MatrixTransferCount;
            twoWay += packet.TwoWayBlendVertexCount;
            threeWay += packet.ThreeWayBlendVertexCount;
            inFileVertices += packet.InFileVertexCount;
            state.ActiveTexture = packet.ActiveTexture;
        }
        if (indices.Count / 3 != materialSlots.Count)
        {
            throw new InvalidDataException("R&C1 Moby triangle/material counts diverged during bind-pose decode.");
        }
        if (jointCount > 0 && (vertexJoints.Count != positions.Count || vertexWeights.Count != positions.Count))
        {
            throw new InvalidDataException("R&C1 Moby skin bindings diverged from emitted vertex count.");
        }

        return new Mesh(
            positions.ToArray(),
            uvs.ToArray(),
            indices.ToArray(),
            materialSlots.ToArray(),
            scale,
            highLodPacketCount,
            boundingRadius,
            jointCount)
        {
            VertexJoints = vertexJoints.ToArray(),
            VertexWeights = vertexWeights.ToArray(),
            MatrixTransferCount = matrixTransfers,
            TwoWayBlendVertexCount = twoWay,
            ThreeWayBlendVertexCount = threeWay,
            InFileVertexCount = inFileVertices,
        };
    }

    private static Packet DecodePacket(byte[] bytes, int entryOffset, float scale, int jointCount, PacketState state, int packetIndex)
    {
        uint vifListOffsetRaw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOffset));
        int vifListSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOffset + 0x04)) * 0x10;
        uint vertexOffsetRaw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOffset + 0x08));
        int vertexDataSize = bytes[entryOffset + 0x0c] * 0x10;
        int unknownD = bytes[entryOffset + 0x0d];
        int unknownE = bytes[entryOffset + 0x0e];
        int transferVertexCount = bytes[entryOffset + 0x0f];
        if (vifListOffsetRaw == 0 || vifListSize <= 0 || (long)vifListOffsetRaw + vifListSize > bytes.Length)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} VIF range {vifListOffsetRaw}+{vifListSize} is out of range.");
        }
        if (vertexOffsetRaw == 0 || (long)vertexOffsetRaw + VertexHeaderSize > bytes.Length)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} vertex header offset {vertexOffsetRaw} is out of range.");
        }

        int vifListOffset = checked((int)vifListOffsetRaw);
        int vertexOffset = checked((int)vertexOffsetRaw);
        var unpacks = Vif.FilterUnpacks(Vif.ReadCommandList(new ArraySegment<byte>(bytes, vifListOffset, vifListSize)));
        if (unpacks.Count is not (2 or 3))
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has {unpacks.Count} UNPACKs; expected 2 or 3.");
        }
        var stData = unpacks[0].Data;
        var indexData = unpacks[1].Data;
        if (indexData.Count < 4 || indexData[3] != 0)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has an invalid index UNPACK header.");
        }

        var secretIndices = new List<int> { AsS8(indexData[2]) };
        var packetTextures = new List<int>();
        if (unpacks.Count == 3)
        {
            var textureData = unpacks[2].Data;
            if (textureData.Count % 0x40 != 0)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} texture UNPACK is not 0x40-byte aligned.");
            }
            int textureCount = textureData.Count / 0x40;
            for (int i = 0; i < textureCount; i++)
            {
                secretIndices.Add(AsS8(textureData[i * 0x10 + 0x0c]));
                int slot = BinaryPrimitives.ReadInt32LittleEndian(textureData.AsSpan(i * 0x40 + 0x20));
                if (slot < -1 || slot >= 16)
                {
                    throw new InvalidDataException($"R&C1 Moby packet {packetIndex} uses invalid texture slot {slot}.");
                }
                packetTextures.Add(slot);
            }
        }

        int matrixTransferCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x00));
        int twoWayCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x04));
        int threeWayCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x08));
        int mainVertexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x0c));
        int duplicateVertexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x10));
        int headerTransferVertexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x14));
        int vertexTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x18));
        int vertexTailOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(vertexOffset + 0x1c));
        if (matrixTransferCount < 0 || twoWayCount < 0 || threeWayCount < 0 || mainVertexCount < 0 ||
            duplicateVertexCount < 0 || headerTransferVertexCount < 0 || vertexTableOffset < 0 || vertexTailOffset < 0)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has a vertex-header field above signed range.");
        }
        if (headerTransferVertexCount != transferVertexCount ||
            headerTransferVertexCount != twoWayCount + threeWayCount + mainVertexCount + duplicateVertexCount)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has conflicting transfer vertex counts.");
        }
        if (unknownD != (0x0f + transferVertexCount * 6) / 0x10 || unknownE != (3 + transferVertexCount) / 4)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} derived packet fields disagree with transfer count {transferVertexCount}.");
        }
        if (vertexTableOffset > vertexDataSize || vertexTailOffset < vertexTableOffset || vertexTailOffset > vertexDataSize)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} vertex offsets {vertexTableOffset}/{vertexTailOffset} exceed {vertexDataSize}.");
        }
        if (stData.Count != transferVertexCount * 4)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} ST count does not equal transfer vertex count.");
        }

        int inFileVertexCount = twoWayCount + threeWayCount + mainVertexCount;
        if ((long)vertexTableOffset + (long)inFileVertexCount * 0x10 > vertexTailOffset ||
            (vertexTailOffset - vertexTableOffset) % 0x10 != 0)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} in-file vertex array crosses its native tail boundary.");
        }
        int epilogueVertexCount = (vertexTailOffset - vertexTableOffset) / 0x10 - inFileVertexCount;
        if (epilogueVertexCount < 0 || epilogueVertexCount >= VertexPipeline)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has invalid epilogue vertex count {epilogueVertexCount}.");
        }

        int duplicateArrayOffset = Align(Align(vertexOffset + VertexHeaderSize + matrixTransferCount * 2, 4), 8);
        if ((long)duplicateArrayOffset + duplicateVertexCount * 2L > vertexOffset + vertexTableOffset)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} matrix/duplicate prelude crosses the vertex table.");
        }

        var vertices = new List<(double X, double Y, double Z, int NativeIndex)>(inFileVertexCount);
        double k = scale / 1024.0;
        for (int i = 0; i < inFileVertexCount; i++)
        {
            int at = vertexOffset + vertexTableOffset + i * 0x10;
            if (at < 0 || at + 0x10 > bytes.Length)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} vertex {i} is out of range.");
            }
            vertices.Add((
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at + 0x0a)) * k,
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at + 0x0c)) * k,
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at + 0x0e)) * k,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)) & 0x1ff));
        }
        for (int i = VertexPipeline; i < vertices.Count; i++)
        {
            var dst = vertices[i - VertexPipeline];
            vertices[i - VertexPipeline] = (dst.X, dst.Y, dst.Z, vertices[i].NativeIndex);
        }

        int epilogueCursor = vertexOffset + vertexTableOffset + inFileVertexCount * 0x10;
        epilogueCursor += System.Math.Max(VertexPipeline - inFileVertexCount, 0) * 0x10;
        for (int i = System.Math.Max(VertexPipeline - inFileVertexCount, 0); i < epilogueVertexCount; i++)
        {
            if (epilogueCursor + 0x10 > bytes.Length)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} epilogue is out of range.");
            }
            int destination = inFileVertexCount + i - VertexPipeline;
            if (destination < 0 || destination >= vertices.Count)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} epilogue destination {destination} is invalid.");
            }
            var dst = vertices[destination];
            int nativeIndex = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(epilogueCursor)) & 0x1ff;
            vertices[destination] = (dst.X, dst.Y, dst.Z, nativeIndex);
            epilogueCursor += 0x10;
        }

        int lastVertexOffset = epilogueCursor - 0x10;
        if (lastVertexOffset < vertexOffset || lastVertexOffset + 0x10 > bytes.Length)
        {
            throw new InvalidDataException($"R&C1 Moby packet {packetIndex} final pipeline vertex is out of range.");
        }
        for (int i = System.Math.Max(VertexPipeline - inFileVertexCount - epilogueVertexCount, 0); i < 6; i++)
        {
            int destination = inFileVertexCount + epilogueVertexCount + i - VertexPipeline;
            if (destination >= 0 && destination < vertices.Count)
            {
                var dst = vertices[destination];
                int nativeIndex = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(lastVertexOffset + 4 + i * 2)) & 0x1ff;
                vertices[destination] = (dst.X, dst.Y, dst.Z, nativeIndex);
            }
        }

        var duplicateIndices = new int[duplicateVertexCount];
        for (int i = 0; i < duplicateVertexCount; i++)
        {
            duplicateIndices[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(duplicateArrayOffset + i * 2)) >> 7;
        }

        SkinAttr[]? skinAttrs = jointCount > 0
            ? ReadPacketSkin(bytes, vertexOffset, vertexTableOffset, matrixTransferCount,
                twoWayCount, threeWayCount, inFileVertexCount, jointCount, state, packetIndex)
            : null;

        var positions = new List<double>();
        var uvs = new List<float>();
        var vertexJoints = new List<int>();
        var vertexWeights = new List<float>();

        void AppendSkin(SkinAttr skin)
        {
            if (jointCount == 0) return;
            int sum = skin.W0 + (skin.Count > 1 ? skin.W1 : 0) + (skin.Count > 2 ? skin.W2 : 0);
            if (skin.Count is < 1 or > 3 || sum <= 0)
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} produced an invalid skin binding.");
            vertexJoints.Add(skin.J0); vertexJoints.Add(skin.J1); vertexJoints.Add(skin.J2);
            vertexWeights.Add((float)skin.W0 / sum);
            vertexWeights.Add(skin.Count > 1 ? (float)skin.W1 / sum : 0f);
            vertexWeights.Add(skin.Count > 2 ? (float)skin.W2 / sum : 0f);
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var skin = skinAttrs?[i] ?? default;
            positions.Add(vertex.X);
            positions.Add(vertex.Y);
            positions.Add(vertex.Z);
            uvs.Add(BinaryPrimitives.ReadInt16LittleEndian(stData.AsSpan(i * 4)) / 4096f);
            uvs.Add(BinaryPrimitives.ReadInt16LittleEndian(stData.AsSpan(i * 4 + 2)) / 4096f);
            AppendSkin(skin);
            state.VertexCache[vertex.NativeIndex] = new CachedVertex(vertex.X, vertex.Y, vertex.Z, skin);
        }
        for (int i = 0; i < duplicateIndices.Length; i++)
        {
            if (!state.VertexCache.TryGetValue(duplicateIndices[i], out var source))
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} duplicate references uncached native vertex {duplicateIndices[i]}.");
            }
            positions.Add(source.X);
            positions.Add(source.Y);
            positions.Add(source.Z);
            int stIndex = vertices.Count + i;
            uvs.Add(BinaryPrimitives.ReadInt16LittleEndian(stData.AsSpan(stIndex * 4)) / 4096f);
            uvs.Add(BinaryPrimitives.ReadInt16LittleEndian(stData.AsSpan(stIndex * 4 + 2)) / 4096f);
            AppendSkin(source.Skin);
        }

        var primitives = new List<Primitive>();
        Primitive? primitive = null;
        int adGifIndex = 0;
        int activeTexture = state.ActiveTexture;
        var rawIndices = new List<int>(indexData.Count - 4);
        for (int i = 4; i < indexData.Count; i++) rawIndices.Add(AsS8(indexData[i]));
        for (int j = 0; j < rawIndices.Count; j++)
        {
            int index = rawIndices[j];
            if (index == 0)
            {
                if (adGifIndex >= secretIndices.Count)
                {
                    throw new InvalidDataException($"R&C1 Moby packet {packetIndex} exhausted its secret-index table.");
                }
                int secretIndex = secretIndices[adGifIndex];
                if (secretIndex == 0)
                {
                    if (primitive is null || primitive.Strip.Count < 3)
                    {
                        throw new InvalidDataException($"R&C1 Moby packet {packetIndex} terminated without an active strip.");
                    }
                    primitive.Strip.RemoveRange(primitive.Strip.Count - 3, 3);
                    break;
                }
                index = secretIndex - 0x80;
                if (adGifIndex >= packetTextures.Count)
                {
                    throw new InvalidDataException($"R&C1 Moby packet {packetIndex} texture switch {adGifIndex} has no texture record.");
                }
                activeTexture = packetTextures[adGifIndex];
                adGifIndex++;
            }

            if (index <= 0)
            {
                if (j + 1 < rawIndices.Count && rawIndices[j + 1] <= 0)
                {
                    primitive = new Primitive { Material = activeTexture };
                    primitives.Add(primitive);
                }
                else
                {
                    if (primitive is null || primitive.Strip.Count < 1)
                    {
                        throw new InvalidDataException($"R&C1 Moby packet {packetIndex} has an invalid strip restart.");
                    }
                    primitive.Strip.Add(primitive.Strip[^1]);
                }
            }
            if (primitive is null)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} index buffer emits a vertex before a strip.");
            }
            primitive.Strip.Add((index & 0x7f) - 1);
        }

        var indices = new List<int>();
        var materialSlots = new List<int>();
        int vertexCount = positions.Count / 3;
        foreach (var current in primitives)
        {
            if (current.Material < -1 || current.Material >= 16)
            {
                throw new InvalidDataException($"R&C1 Moby packet {packetIndex} emits invalid material slot {current.Material}.");
            }
            for (int i = 0; i + 2 < current.Strip.Count; i++)
            {
                int a = current.Strip[i], b = current.Strip[i + 1], c = current.Strip[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                {
                    throw new InvalidDataException($"R&C1 Moby packet {packetIndex} triangle index is outside {vertexCount} vertices.");
                }
                if (a == b || b == c || a == c) continue;
                if ((i & 1) == 0)
                {
                    indices.Add(a); indices.Add(b); indices.Add(c);
                }
                else
                {
                    indices.Add(b); indices.Add(a); indices.Add(c);
                }
                materialSlots.Add(current.Material);
            }
        }

        return new Packet(
            positions.ToArray(),
            uvs.ToArray(),
            indices.ToArray(),
            materialSlots.ToArray(),
            vertexJoints.ToArray(),
            vertexWeights.ToArray(),
            activeTexture,
            matrixTransferCount,
            twoWayCount,
            threeWayCount,
            inFileVertexCount);
    }
}
