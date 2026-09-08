using System.Buffers.Binary;
using OBP.PS2.Vif;
using OBP.RAC2.Level;

namespace OBP.RAC2.Geometry;

/// <summary>
/// Going Commando / UYA <b>moby</b> class geometry — the dynamic objects (enemies,
/// crates, the vendor, gadget pickups, breakables). The most involved R&amp;C
/// geometry format. Recovers the high-LOD mesh at bind pose: each packet's vertex
/// table holds positions (<c>s16 x,y,z</c>) and, for animated classes, per-vertex
/// bone bindings decoded through a simulation of the PS2 VU0 matrix-slot machine.
/// A skinned vertex is stored in its bone's local space, so the bind pose is
/// <c>pos_model = Σ w·globalBind[joint]·pos_local</c> with
/// <c>globalBind[j] = translate(-skeleton[j] row 3)</c> (every GC bind-pose joint
/// rotation is identity). The joint index is pipelined <c>VERTEX_PIPELINE</c>
/// entries ahead of the vertex it binds. Translated from
/// <c>reference-ts/packages/gc-moby</c>. See <c>research/GC_MOBY.md</c>.
/// </summary>
public static class GcMoby
{
    public const int ClassHeaderSize = 0x48;
    public const int ClassEntrySize = 0x20;
    private const int JointStride = 0x40;
    private const int CommonTransStride = 0x10;

    /// <summary>
    /// The PS2 vertex loop is software-pipelined: the <c>lowHalfword</c> of each
    /// <c>MobyVertex</c> (vertex index bits 0..8, spr joint bits 9..15) is staged
    /// this many entries ahead of the vertex it describes.
    /// </summary>
    private const int VertexPipeline = 7;

    public sealed record Mesh(
        double[] Positions,
        float[] Uvs,
        int[] Indices,
        int[] TriangleMaterialSlots,
        float Scale,
        bool Skinned,
        bool SkinningApplied)
    {
        /// <summary>
        /// Per-vertex unit normal in the class's local frame (native Z-up), from
        /// the <c>MobyVertex</c> spherical angles at 0x08 / 0x09. Mobies carry no
        /// baked vertex colour — they are lit from these normals. Flat XYZ,
        /// length == <see cref="Positions"/>.
        /// </summary>
        public float[] Normals { get; init; } = [];

        /// <summary>
        /// Per-vertex bone binding: 3 joint indices per output vertex (unused
        /// slots = 0), length == <c>Positions.Length</c>. Only meaningful when
        /// <see cref="SkinningApplied"/>; the rest pose is already baked into
        /// <see cref="Positions"/>, so <c>MobyAnimation</c> re-poses from these.
        /// </summary>
        public int[] VertexJoints { get; init; } = [];

        /// <summary>Per-vertex bone weights, normalised to sum 1, 3 per vertex, length == <see cref="Positions"/>.</summary>
        public float[] VertexWeights { get; init; } = [];
    }

    /// <summary>
    /// One skeleton joint. <see cref="Parent"/> is the parent joint index (a
    /// self-reference marks a root), from <c>MobyTrans.parentByteOffset / 0x40</c>
    /// (<c>commonTransOffset</c>, 0x10 stride). <see cref="Bx"/>/<see cref="By"/>/
    /// <see cref="Bz"/> is the joint's bind-pose <b>global</b> translation in raw
    /// class units (multiply by <c>scale / 1024</c> for model units) — the exact
    /// value the rest-pose decoder subtracts (<c>-skeletonRow3</c>), so re-posing
    /// from it reproduces <see cref="Mesh.Positions"/> at identity. GC bind-pose
    /// joint rotations are identity.
    /// </summary>
    public sealed record MobyJoint(int Parent, float Bx, float By, float Bz);

    /// <summary>One animation frame: a local rotation quaternion per joint (<c>x,y,z,w</c>, unit length).</summary>
    public sealed record MobyFrame(float Speed, System.Numerics.Quaternion[] JointRotations);

    /// <summary>
    /// A named motion — an ordered list of frames. <c>MobySequenceHeader</c>:
    /// <c>u8 frameCount @ 0x10</c>; frame offset table <c>u32[frameCount] @ 0x1c</c>
    /// (<c>&amp; 0x0fffffff</c> = offset relative to the class, <c>&amp; 0xf0000000</c>
    /// = flags). Frame body: <c>MobyFrameHeader</c> 0x10 then <c>s16[4]</c> per
    /// joint at <c>+0x10</c>, each channel <c>/ 32768</c>.
    /// </summary>
    public sealed record MobySequence(int Index, IReadOnlyList<MobyFrame> Frames);

    public sealed record MobyClass(
        int OClass,
        Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<int> TextureIds,
        IReadOnlyList<MobyJoint> Joints,
        IReadOnlyList<MobySequence> Sequences);

    private sealed class MPrim
    {
        public int Material;
        public readonly List<int> Strip = [];
    }

    /// <summary>Per-vertex bone binding: up to 3 (joint, weight) pairs; count 0 = rigid to model space.</summary>
    private struct SkinAttr
    {
        public int Count;
        public int J0, J1, J2;
        public int W0, W1, W2;
    }

    private static readonly double[] AffineId = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];

    private static (double X, double Y, double Z) AffineApply(double[] m, double x, double y, double z) => (
        m[0] * x + m[1] * y + m[2] * z + m[3],
        m[4] * x + m[5] * y + m[6] * z + m[7],
        m[8] * x + m[9] * y + m[10] * z + m[11]);

    /// <summary>
    /// Build one global bind matrix per joint from the moby skeleton. Returns
    /// <c>null</c> when the skeleton is absent, out of range, or numerically
    /// unusable. Row 3 of each 0x40 skeleton entry is <c>-T_global</c>; every GC
    /// bind-pose joint rotation is identity, so the global bind is
    /// <c>translate(-row3)</c>.
    /// </summary>
    private static double[][]? ReadGlobalBind(byte[] buf, int jointCount)
    {
        int skeletonOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x14));
        int commonTransOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x18));
        if (jointCount is <= 0 or > 256)
        {
            return null;
        }

        if (skeletonOffset <= 0 || commonTransOffset <= 0)
        {
            return null;
        }

        if (skeletonOffset + jointCount * JointStride > buf.Length)
        {
            return null;
        }

        if (commonTransOffset + jointCount * CommonTransStride > buf.Length)
        {
            return null;
        }

        var global = new double[jointCount][];
        for (int j = 0; j < jointCount; j++)
        {
            int o = skeletonOffset + j * JointStride;
            float gx = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(o + 48));
            float gy = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(o + 52));
            float gz = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(o + 56));
            if (!float.IsFinite(gx) || !float.IsFinite(gy) || !float.IsFinite(gz))
            {
                return null;
            }

            global[j] = [1, 0, 0, -gx, 0, 1, 0, -gy, 0, 0, 1, -gz];
        }

        return global;
    }

    /// <summary>
    /// Read the moby skeleton hierarchy from <c>commonTransOffset</c>
    /// (<c>MobyTrans[jointCount]</c>, 0x10 stride). Returns an empty list when the
    /// table is absent or out of range.
    /// </summary>
    internal static List<MobyJoint> ReadJoints(byte[] buf, int jointCount)
    {
        var joints = new List<MobyJoint>();
        if (jointCount is <= 0 or > 256)
        {
            return joints;
        }

        int commonTransOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x18));
        int skeletonOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x14));
        if (commonTransOffset <= 0 || commonTransOffset + jointCount * CommonTransStride > buf.Length)
        {
            return joints;
        }

        if (skeletonOffset <= 0 || skeletonOffset + jointCount * JointStride > buf.Length)
        {
            return joints;
        }

        for (int j = 0; j < jointCount; j++)
        {
            int o = commonTransOffset + j * CommonTransStride;
            int parent = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o + 0x0c)) / JointStride;
            if (parent < 0 || parent >= jointCount)
            {
                parent = 0;
            }

            // Bind global translation = -skeletonRow3, matching ReadGlobalBind /
            // the rest-pose decoder exactly (so identity frames reproduce it).
            int s = skeletonOffset + j * JointStride;
            float bx = -BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(s + 48));
            float by = -BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(s + 52));
            float bz = -BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(s + 56));
            if (!float.IsFinite(bx) || !float.IsFinite(by) || !float.IsFinite(bz))
            {
                bx = by = bz = 0f;
            }

            joints.Add(new MobyJoint(parent, bx, by, bz));
        }

        return joints;
    }

    /// <summary>
    /// Read every <c>MobySequence</c> from the offset list at <c>class + 0x48</c>
    /// (<c>s32[sequenceCount]</c>, relative to the class). <c>sequenceCount</c> is
    /// <c>u8 @ 0x0c</c>. Frames whose data is out of range are skipped.
    /// </summary>
    internal static List<MobySequence> ReadSequences(byte[] buf, int jointCount)
    {
        var sequences = new List<MobySequence>();
        int sequenceCount = buf[0x0c];
        if (sequenceCount is <= 0 or > 64 || jointCount is <= 0 or > 256)
        {
            return sequences;
        }

        int listOffset = ClassHeaderSize;
        if (listOffset + sequenceCount * 4 > buf.Length)
        {
            return sequences;
        }

        for (int s = 0; s < sequenceCount; s++)
        {
            int seqOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(listOffset + s * 4));
            if (seqOffset <= 0 || seqOffset + 0x1c > buf.Length)
            {
                continue;
            }

            int frameCount = buf[seqOffset + 0x10];
            int frameTable = seqOffset + 0x1c;
            if (frameCount <= 0 || frameTable + frameCount * 4 > buf.Length)
            {
                sequences.Add(new MobySequence(s, []));
                continue;
            }

            var frames = new List<MobyFrame>(frameCount);
            for (int f = 0; f < frameCount; f++)
            {
                uint entry = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(frameTable + f * 4));
                int frameOffset = (int)(entry & 0x0fffffff);
                int dataOffset = frameOffset + 0x10;
                if (frameOffset <= 0 || dataOffset + jointCount * 8 > buf.Length)
                {
                    continue;
                }

                float speed = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(frameOffset));
                var rotations = new System.Numerics.Quaternion[jointCount];
                for (int j = 0; j < jointCount; j++)
                {
                    int o = dataOffset + j * 8;
                    rotations[j] = new System.Numerics.Quaternion(
                        BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o)) / 32768f,
                        BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 2)) / 32768f,
                        BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 4)) / 32768f,
                        BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 6)) / 32768f);
                }

                frames.Add(new MobyFrame(speed, rotations));
            }

            sequences.Add(new MobySequence(s, frames));
        }

        return sequences;
    }

    /// <summary>
    /// Simulate the VU0 matrix-slot machine for one packet and return the bone
    /// binding of each in-file vertex (<c>twoWay</c> first, then <c>threeWay</c>,
    /// then <c>main</c>).
    /// </summary>
    private static SkinAttr[] ReadPacketSkin(
        byte[] buf, int vh, int vbase, int matrixTransferCount, int twoWay, int threeWay, int count)
    {
        var blend = new SkinAttr?[64];

        void Set(int addr, SkinAttr a)
        {
            if (addr != 0xf4)
            {
                blend[(addr & 0xff) >> 2] = a;
            }
        }

        SkinAttr Get(int addr) =>
            blend[(addr & 0xff) >> 2] ?? new SkinAttr { Count = 1, J0 = 0, J1 = 0, J2 = 0, W0 = 255, W1 = 0, W2 = 0 };

        for (int t = 0; t < matrixTransferCount; t++)
        {
            int o = vh + 0x10 + t * 2;
            if (o + 2 > buf.Length)
            {
                break;
            }

            Set(buf[o + 1], new SkinAttr { Count = 1, J0 = buf[o], W0 = 255 });
        }

        var attrs = new SkinAttr[count];
        for (int v = 0; v < count; v++)
        {
            int o = vbase + v * 0x10;
            int sprOffset = vbase + System.Math.Min(count - 1, v + VertexPipeline) * 0x10;
            int sprJoint = (BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(sprOffset)) & 0xfe00) >> 9;
            int B(int k) => buf[o + k];

            SkinAttr a;
            if (v < twoWay)
            {
                Set(B(6), new SkinAttr { Count = 1, J0 = sprJoint, W0 = 255 });
                var s1 = Get(B(2));
                var s2 = Get(B(3));
                a = new SkinAttr { Count = 2, J0 = s1.J0, J1 = s2.J0, J2 = 0, W0 = B(4), W1 = B(5), W2 = 0 };
                Set(B(7), a);
            }
            else if (v < twoWay + threeWay)
            {
                var s1 = Get(B(2));
                var s2 = Get(B(3));
                var s3 = Get((sprJoint * 2) & 0xff);
                a = new SkinAttr { Count = 3, J0 = s1.J0, J1 = s2.J0, J2 = s3.J0, W0 = B(4), W1 = B(5), W2 = B(6) };
                Set(B(7), a);
            }
            else
            {
                Set(B(3), new SkinAttr { Count = 1, J0 = sprJoint, W0 = 255 });
                a = Get(B(2));
            }

            attrs[v] = a;
        }

        return attrs;
    }

    public static Mesh ReadClass(byte[] buf)
    {
        if (buf.Length < ClassHeaderSize)
        {
            throw new InvalidDataException("Moby class buffer shorter than the 0x48 header.");
        }

        float boundingRadius = System.Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x3c)))
            * (BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x24)) / 1024f);

        var skinned = Decode(buf, true);
        if (!skinned.SkinningApplied)
        {
            return skinned;
        }

        var (pruned, removedFraction) = PruneSpikeTriangles(skinned);
        var folded = Decode(buf, false);
        double se = MeshExtent(pruned);
        bool broken = se == 0
            || removedFraction > 0.03
            || se > System.Math.Max(40, System.Math.Max(boundingRadius * 5, MeshExtent(folded) * 4));

        return broken ? folded with { Skinned = true } : pruned;
    }

    private static double MeshExtent(Mesh m)
    {
        if (m.Positions.Length == 0)
        {
            return 0;
        }

        double mn = double.PositiveInfinity, mx = double.NegativeInfinity;
        foreach (double c in m.Positions)
        {
            if (c < mn) mn = c;
            if (c > mx) mx = c;
        }

        return mx - mn;
    }

    private static (Mesh Mesh, double RemovedFraction) PruneSpikeTriangles(Mesh m)
    {
        var p = m.Positions;
        var ix = m.Indices;
        int triCount = ix.Length / 3;
        if (triCount == 0)
        {
            return (m, 0);
        }

        var longest = new double[triCount];
        var edges = new double[triCount];
        for (int t = 0; t < triCount; t++)
        {
            double e = 0;
            for (int s = 0; s < 3; s++)
            {
                int a = ix[t * 3 + s] * 3;
                int b = ix[t * 3 + (s + 1) % 3] * 3;
                double d = System.Math.Sqrt(
                    (p[a] - p[b]) * (p[a] - p[b]) +
                    (p[a + 1] - p[b + 1]) * (p[a + 1] - p[b + 1]) +
                    (p[a + 2] - p[b + 2]) * (p[a + 2] - p[b + 2]));
                if (d > e) e = d;
            }

            longest[t] = e;
            edges[t] = e;
        }

        Array.Sort(edges);
        double median = edges[edges.Length >> 1];
        double limit = System.Math.Max(1.0, median * 10);

        var keep = new List<int>();
        var slots = new List<int>();
        int removed = 0;
        for (int t = 0; t < triCount; t++)
        {
            if (longest[t] > limit)
            {
                removed++;
                continue;
            }

            keep.Add(ix[t * 3]);
            keep.Add(ix[t * 3 + 1]);
            keep.Add(ix[t * 3 + 2]);
            slots.Add(m.TriangleMaterialSlots[t]);
        }

        if (removed == 0)
        {
            return (m, 0);
        }

        return (m with { Indices = keep.ToArray(), TriangleMaterialSlots = slots.ToArray() }, (double)removed / triCount);
    }

    private static Mesh Decode(byte[] buf, bool allowSkinning)
    {
        int packetTableOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(0x00));
        int highLodCount = buf[0x04];
        int jointCount = buf[0x08];
        float scale = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x24));
        double k = scale / 1024.0;
        double boundingRadius = System.Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x3c))) * k;
        double skinCap = System.Math.Max(2000, boundingRadius * 8);

        var globalBind = allowSkinning && jointCount > 0 ? ReadGlobalBind(buf, jointCount) : null;

        var positions = new List<double>();
        var uvs = new List<float>();
        var normalsOut = new List<float>();
        var vertexJointsOut = new List<int>();
        var vertexWeightsOut = new List<float>();
        var indices = new List<int>();
        var triangleMaterialSlots = new List<int>();
        bool skinned = false;
        bool skinningApplied = false;

        // The PS2 GS texture register persists across the whole draw: a moby
        // packet only emits an AD-GIF when the texture changes, so a packet /
        // strip with none inherits the last texture set by an earlier packet.
        // One running value across every packet (init 0, matching Wrench's
        // recover_packets) — see research/GC_MOBY.md.
        int material = 0;

        for (int pkt = 0; pkt < highLodCount; pkt++)
        {
            int eo = packetTableOffset + pkt * 0x10;
            if (eo < 0 || eo + 0x10 > buf.Length)
            {
                break;
            }

            long vifListOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eo + 0x00));
            int vifListSize = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(eo + 0x04)) * 0x10;
            long vertexOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eo + 0x08));

            if (vifListOffset + vifListSize > buf.Length || vertexOffset + 0x10 > buf.Length)
            {
                continue;
            }

            var unpacks = Vif.FilterUnpacks(Vif.ReadCommandList(
                new ArraySegment<byte>(buf, (int)vifListOffset, vifListSize)));
            if (unpacks.Count < 2)
            {
                continue;
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
                continue;
            }

            var secretIndices = new List<int> { (sbyte)idxData[2] };
            var idxBuf = new List<int>();
            for (int i = 4; i < idxData.Count; i++)
            {
                idxBuf.Add((sbyte)idxData[i]);
            }

            var textures = new List<int>();
            if (unpacks.Count >= 3)
            {
                var td = unpacks[2].Data;
                for (int i = 0; i * 0x40 + 0x40 <= td.Count; i++)
                {
                    secretIndices.Add((sbyte)td[i * 0x10 + 0x0c]);
                    textures.Add(BinaryPrimitives.ReadInt32LittleEndian(td.AsSpan(i * 0x40 + 0x20)));
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
                skinned = true;
            }

            int inFileCount = twoWay + threeWay + mainCount;
            int vbase = vh + vertexTableOffset;
            if (vbase + inFileCount * 0x10 > buf.Length || inFileCount > 4096)
            {
                continue;
            }

            bool applySkin = globalBind is not null;
            var skinAttrs = applySkin
                ? ReadPacketSkin(buf, vh, vbase, matrixTransferCount, twoWay, threeWay, inFileCount)
                : null;

            var rawX = new int[inFileCount];
            var rawY = new int[inFileCount];
            var rawZ = new int[inFileCount];
            var rawIdx = new int[inFileCount];
            var norm = new (float X, float Y, float Z)[inFileCount];
            for (int v = 0; v < inFileCount; v++)
            {
                int o = vbase + v * 0x10;
                rawIdx[v] = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o)) & 0x1ff;
                rawX[v] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 0x0a));
                rawY[v] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 0x0c));
                rawZ[v] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 0x0e));
                // Normal: spherical (azimuth @0x08, elevation @0x09), unit,
                // byte * PI/128. Not pipelined (like the position).
                double az = buf[o + 0x08] * (System.Math.PI / 128.0);
                double el = buf[o + 0x09] * (System.Math.PI / 128.0);
                norm[v] = (
                    (float)(System.Math.Sin(az) * System.Math.Cos(el)),
                    (float)(System.Math.Cos(az) * System.Math.Cos(el)),
                    (float)System.Math.Sin(el));
            }

            for (int i = VertexPipeline; i < inFileCount; i++)
            {
                rawIdx[i - VertexPipeline] = rawIdx[i];
            }

            var posX = new List<double>();
            var posY = new List<double>();
            var posZ = new List<double>();
            bool packetSkinBlewUp = false;
            for (int v = 0; v < inFileCount; v++)
            {
                SkinAttr? attr = skinAttrs?[v];
                if (globalBind is null || attr is not { } at || at.Count == 0)
                {
                    posX.Add(rawX[v] * k);
                    posY.Add(rawY[v] * k);
                    posZ.Add(rawZ[v] * k);
                    continue;
                }

                double sx = 0, sy = 0, sz = 0, tw = 0;
                Span<int> js = [at.J0, at.J1, at.J2];
                Span<int> ws = [at.W0, at.W1, at.W2];
                for (int j = 0; j < at.Count; j++)
                {
                    int w = ws[j];
                    if (w == 0)
                    {
                        continue;
                    }

                    int ji = js[j];
                    var m = ji >= 0 && ji < globalBind.Length ? globalBind[ji] : AffineId;
                    var (qx, qy, qz) = AffineApply(m, rawX[v], rawY[v], rawZ[v]);
                    sx += w * qx;
                    sy += w * qy;
                    sz += w * qz;
                    tw += w;
                }

                if (tw == 0)
                {
                    var m0 = at.J0 >= 0 && at.J0 < globalBind.Length ? globalBind[at.J0] : AffineId;
                    var (qx, qy, qz) = AffineApply(m0, rawX[v], rawY[v], rawZ[v]);
                    sx = qx;
                    sy = qy;
                    sz = qz;
                    tw = 1;
                }

                double fx = sx / tw * k, fy = sy / tw * k, fz = sz / tw * k;
                if (!double.IsFinite(fx) || !double.IsFinite(fy) || !double.IsFinite(fz) ||
                    System.Math.Abs(fx) > skinCap || System.Math.Abs(fy) > skinCap || System.Math.Abs(fz) > skinCap)
                {
                    packetSkinBlewUp = true;
                    break;
                }

                posX.Add(fx);
                posY.Add(fy);
                posZ.Add(fz);
            }

            if (packetSkinBlewUp)
            {
                posX.Clear();
                posY.Clear();
                posZ.Clear();
                for (int v = 0; v < inFileCount; v++)
                {
                    posX.Add(rawX[v] * k);
                    posY.Add(rawY[v] * k);
                    posZ.Add(rawZ[v] * k);
                }
            }
            else if (applySkin && twoWay + threeWay > 0)
            {
                skinningApplied = true;
            }

            int arrayOfs = vh + 0x10 + matrixTransferCount * 2;
            if (arrayOfs % 4 != 0) arrayOfs += 2;
            if (arrayOfs % 8 != 0) arrayOfs += 4;
            var dupes = new List<int>();
            for (int d = 0; d < dupCount; d++)
            {
                int o = arrayOfs + d * 2;
                if (o + 2 > buf.Length)
                {
                    break;
                }

                dupes.Add(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o)) >> 7);
            }

            var listX = new List<double>();
            var listY = new List<double>();
            var listZ = new List<double>();
            var listS = new List<float>();
            var listT = new List<float>();
            var listN = new List<(float X, float Y, float Z)>();
            var listJW = new List<(int J0, int J1, int J2, float W0, float W1, float W2)>();
            var cache = new Dictionary<int, int>();

            static (int, int, int, float, float, float) Bind(SkinAttr? attr)
            {
                if (attr is not { } a || a.Count == 0)
                {
                    return (0, 0, 0, 1f, 0f, 0f);
                }

                float w0 = a.W0, w1 = a.Count > 1 ? a.W1 : 0f, w2 = a.Count > 2 ? a.W2 : 0f;
                float sum = w0 + w1 + w2;
                if (sum <= 0f)
                {
                    return (a.J0, 0, 0, 1f, 0f, 0f);
                }

                return (a.J0, a.J1, a.J2, w0 / sum, w1 / sum, w2 / sum);
            }

            for (int v = 0; v < inFileCount; v++)
            {
                int pos = listX.Count;
                listX.Add(posX[v]);
                listY.Add(posY[v]);
                listZ.Add(posZ[v]);
                listN.Add(norm[v]);
                listJW.Add(Bind(skinAttrs?[v]));
                var st = v < sts.Count ? sts[v] : (0, 0);
                listS.Add(st.Item1 / 4096f);
                listT.Add(st.Item2 / 4096f);
                cache[rawIdx[v]] = pos;
            }

            for (int di = 0; di < dupes.Count; di++)
            {
                if (!cache.TryGetValue(dupes[di], out int from))
                {
                    continue;
                }

                listX.Add(listX[from]);
                listY.Add(listY[from]);
                listZ.Add(listZ[from]);
                listN.Add(listN[from]);
                listJW.Add(listJW[from]);
                var st = inFileCount + di < sts.Count ? sts[inFileCount + di] : (0, 0);
                listS.Add(st.Item1 / 4096f);
                listT.Add(st.Item2 / 4096f);
            }

            var prims = new List<MPrim>();
            MPrim? prim = null;
            int adGif = 0;
            for (int j = 0; j < idxBuf.Count; j++)
            {
                int index = idxBuf[j];
                if (index == 0)
                {
                    int secret = adGif < secretIndices.Count ? secretIndices[adGif] : 0;
                    if (secret == 0)
                    {
                        if (prim is { Strip.Count: >= 3 })
                        {
                            prim.Strip.RemoveRange(prim.Strip.Count - 3, 3);
                        }

                        break;
                    }

                    index = secret - 0x80;
                    material = adGif < textures.Count ? textures[adGif] : material;
                    adGif++;
                }

                if (index <= 0)
                {
                    if (j + 1 < idxBuf.Count && idxBuf[j + 1] <= 0)
                    {
                        prim = new MPrim { Material = material };
                        prims.Add(prim);
                    }
                    else if (prim is { Strip.Count: >= 1 })
                    {
                        prim.Strip.Add(prim.Strip[^1]);
                    }
                }

                if (prim is null)
                {
                    continue;
                }

                prim.Strip.Add((index & 0x7f) - 1);
            }

            int vbaseOut = positions.Count / 3;
            for (int v = 0; v < listX.Count; v++)
            {
                positions.Add(listX[v]);
                positions.Add(listY[v]);
                positions.Add(listZ[v]);
                uvs.Add(listS[v]);
                uvs.Add(listT[v]);
                normalsOut.Add(listN[v].X);
                normalsOut.Add(listN[v].Y);
                normalsOut.Add(listN[v].Z);
                var jw = listJW[v];
                vertexJointsOut.Add(jw.J0);
                vertexJointsOut.Add(jw.J1);
                vertexJointsOut.Add(jw.J2);
                vertexWeightsOut.Add(jw.W0);
                vertexWeightsOut.Add(jw.W1);
                vertexWeightsOut.Add(jw.W2);
            }

            foreach (var pr in prims)
            {
                for (int i = 0; i + 3 <= pr.Strip.Count; i++)
                {
                    int a = pr.Strip[i], b = pr.Strip[i + 1], c = pr.Strip[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= listX.Count || b >= listX.Count || c >= listX.Count)
                    {
                        continue;
                    }

                    if (a == b || b == c || a == c)
                    {
                        continue;
                    }

                    if (i % 2 == 0)
                    {
                        indices.Add(vbaseOut + a);
                        indices.Add(vbaseOut + b);
                        indices.Add(vbaseOut + c);
                    }
                    else
                    {
                        indices.Add(vbaseOut + b);
                        indices.Add(vbaseOut + a);
                        indices.Add(vbaseOut + c);
                    }

                    triangleMaterialSlots.Add(pr.Material);
                }
            }
        }

        return new Mesh(
            positions.ToArray(),
            uvs.ToArray(),
            indices.ToArray(),
            triangleMaterialSlots.ToArray(),
            scale,
            skinned,
            skinningApplied)
        {
            Normals = normalsOut.ToArray(),
            VertexJoints = vertexJointsOut.ToArray(),
            VertexWeights = vertexWeightsOut.ToArray(),
        };
    }

    /// <summary>
    /// Parse moby classes from <c>LevelCoreHeader.mobyClasses</c>
    /// (<c>MobyClassEntry</c> 0x20: offset, oClass, u32, u32, u8 textures[16]).
    /// </summary>
    public static Dictionary<int, MobyClass> ReadClasses(GcLevelCore.Core core)
    {
        var table = core.Header.MobyClasses;
        var boundaries = core.SectionBoundaries;
        var outp = new Dictionary<int, MobyClass>();

        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * ClassEntrySize;
            if (at < 0 || at + ClassEntrySize > core.Index.Length)
            {
                break;
            }

            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (assetOffset <= 0 || assetOffset >= core.Assets.Length)
            {
                continue;
            }

            var textures = new int[16];
            for (int t = 0; t < 16; t++)
            {
                textures[t] = core.Index[at + 0x10 + t];
            }

            int end = boundaries.FirstOrDefault(b => b > assetOffset, core.Assets.Length);
            try
            {
                var classBuf = core.Assets.AsSpan(assetOffset, end - assetOffset).ToArray();
                var mesh = ReadClass(classBuf);
                var triTexIds = mesh.TriangleMaterialSlots
                    .Select(slot => slot >= 0 && slot < 16 ? textures[slot] : -1)
                    .ToArray();
                int jointCount = classBuf[0x08];
                var joints = ReadJoints(classBuf, jointCount);
                var sequences = joints.Count > 0 ? ReadSequences(classBuf, jointCount) : new List<MobySequence>();
                outp[oClass] = new MobyClass(
                    oClass, mesh, triTexIds,
                    triTexIds.Where(id => id >= 0).Distinct().OrderBy(id => id).ToList(),
                    joints, sequences);
            }
            catch
            {
                // skip a class that fails to parse
            }
        }

        return outp;
    }
}
