using System.Buffers.Binary;
using OBP.PS2.Graphics;

namespace OBP.PS2.Geometry;

public static partial class GcUyaMoby
{

    /// <summary>Cross-packet VU state and full-class output ownership.</summary>
    private sealed class DecodeState
    {
        public readonly byte[] Buffer;
        public readonly bool AllowSkinning;
        public readonly bool RecoverPersistentVertexCache;
        public readonly int PacketTableOffset;
        public readonly int HighLodCount;
        public readonly int JointCount;
        public readonly float Scale;
        public readonly double K;
        public readonly double SkinCap;
        public readonly double[][]? GlobalBind;
        public readonly SkinState SkinState = new();
        public readonly Dictionary<int, CachedVertex> VertexCache = new();

        public readonly List<double> Positions = [];
        public readonly List<double> SkinLocalPositions = [];
        public readonly List<float> Uvs = [];
        public readonly List<float> Normals = [];
        public readonly List<int> VertexJoints = [];
        public readonly List<float> VertexWeights = [];
        public readonly List<int> Indices = [];
        public readonly List<int> TriangleMaterialSlots = [];
        public readonly List<int> TriangleMaterialStateIndices = [];
        public readonly List<RcMaterialState> Materials = [];

        public int Material;
        public int MaterialStateIndex = -1;
        public bool Skinned { get; private set; }
        public bool SkinningApplied { get; private set; }

        public void MarkSkinned() => Skinned = true;
        public void MarkSkinningApplied() => SkinningApplied = true;

        public DecodeState(byte[] buf, bool allowSkinning, bool recoverPersistentVertexCache)
        {
            Buffer = buf;
            AllowSkinning = allowSkinning;
            RecoverPersistentVertexCache = recoverPersistentVertexCache;
            PacketTableOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x00));
            HighLodCount = buf[0x04];
            JointCount = buf[0x08];
            Scale = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x24));
            K = Scale / 1024.0;
            double boundingRadius =
                System.Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x3c))) * K;
            SkinCap = System.Math.Max(2000, boundingRadius * 8);
            GlobalBind = allowSkinning && JointCount > 0 ? ReadGlobalBind(buf, JointCount) : null;
        }

        public void InvalidateSkippedPacket()
        {
            if (!RecoverPersistentVertexCache) return;
            // Missing packet writes break VU1 cache continuity. Never allow
            // later duplicates to reuse stale slots or admit incomplete skin.
            VertexCache.Clear();
            SkinState.FullyResolved = false;
        }

        public Mesh ToMesh() => new(
            Positions.ToArray(),
            Uvs.ToArray(),
            Indices.ToArray(),
            TriangleMaterialSlots.ToArray(),
            Scale,
            Skinned,
            SkinningApplied)
        {
            Normals = Normals.ToArray(),
            VertexJoints = VertexJoints.ToArray(),
            VertexWeights = VertexWeights.ToArray(),
            SkinLocalPositions = SkinLocalPositions.ToArray(),
            // Rigid packets have no blended weights but still resolve joints.
            // A substituted joint-zero binding must not be classified as native.
            SkinStateFullyResolved = !AllowSkinning || JointCount == 0 ||
                (GlobalBind is not null && SkinState.FullyResolved),
            Materials = Materials,
            TriangleMaterialStateIndices = TriangleMaterialStateIndices.ToArray(),
        };
    }

    /// <summary>Traverse packets in authored order, delegating three distinct stages.</summary>
    private static Mesh Decode(byte[] buf, bool allowSkinning, bool recoverPersistentVertexCache)
    {
        var state = new DecodeState(buf, allowSkinning, recoverPersistentVertexCache);
        for (int pkt = 0; pkt < state.HighLodCount; pkt++)
        {
            int eo = state.PacketTableOffset + pkt * 0x10;
            if (eo < 0 || eo + 0x10 > buf.Length)
            {
                state.InvalidateSkippedPacket();
                break;
            }

            var packet = ReadPacket(state, eo);
            if (packet is null) continue;
            var vertices = ReconstructVertices(state, packet);
            AppendPacketGeometry(state, packet, vertices);
        }

        return state.ToMesh();
    }

}
