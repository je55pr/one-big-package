using System.Buffers.Binary;
using OBP.PS2.Graphics;

namespace OBP.PS2.Geometry;

/// <summary>
/// Going Commando / UYA <b>moby</b> class geometry — the dynamic objects (enemies,
/// crates, the vendor, gadget pickups, breakables). The most involved R&amp;C
/// geometry format. The established static renderer preserves its historical
/// bind-pose reconstruction for visual/census compatibility, while animation
/// metadata uses the separately retail-pinned VU0 matrix-slot state machine:
/// matrix slots persist across packets and skin-control upper bits belong to the
/// current vertex record. Only the low 9-bit strip vertex id is seven records
/// ahead. Translated originally from
/// <c>reference-ts/packages/gc-moby</c>. See <c>research/GC_MOBY.md</c>.
/// </summary>
public static partial class GcUyaMoby
{
    public const int ClassHeaderSize = 0x48;
    public const int ClassEntrySize = 0x20;
    private const int JointStride = 0x40;
    private const int CommonTransStride = 0x10;

    /// <summary>
    /// The low 9-bit strip vertex id is software-pipelined this many records
    /// ahead. Retail GC/UYA matrix-state censuses reject applying this offset to
    /// the upper skin-control bits; those are consumed from the current record.
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

        /// <summary>Stored skinned vertex positions before the native joint transform, in model units.</summary>
        public double[] SkinLocalPositions { get; init; } = [];

        /// <summary>True only when every VU0 matrix-slot read and blend source was retail-valid.</summary>
        public bool SkinStateFullyResolved { get; init; } = true;

        /// <summary>True when the mesh is initialized from a validated native regular key.</summary>
        public bool NativeInitialPoseApplied { get; init; }

        /// <summary>Native per-switch Moby material records, in decode order.</summary>
        public IReadOnlyList<RcMaterialState> Materials { get; init; } = [];

        /// <summary>One material-record index per emitted triangle; -1 means inherited state was unresolved.</summary>
        public int[] TriangleMaterialStateIndices { get; init; } = [];
    }

    /// <summary>
    /// One skeleton joint. <see cref="Parent"/> comes from
    /// <c>MobyTrans.parentByteOffset / 0x40</c>. The legacy B fields preserve the
    /// translation-only static reconstruction used by existing GC presentation;
    /// <see cref="LocalTranslation"/> separately preserves the native
    /// parent-relative <c>common_trans.vector</c> used by the retail-pinned
    /// multi-joint animation evaluator.
    /// </summary>
    public sealed record MobyJoint(int Parent, float Bx, float By, float Bz)
    {
        /// <summary>Native parent-relative joint translation from common_trans, in raw class units.</summary>
        public (float X, float Y, float Z) LocalTranslation { get; init; }
    }

    /// <summary>Native frame scale for one joint: 0x80 means inherited by children; otherwise post-scale.</summary>
    public readonly record struct MobyScaleOverride(int Joint, float X, float Y, float Z, bool Inherited);

    /// <summary>Native frame translation for one joint, in class model units (stored s16 / 1024).</summary>
    public readonly record struct MobyTranslationOverride(int Joint, float X, float Y, float Z);

    /// <summary>One native animation frame, including optional joint scale and translation records.</summary>
    public sealed record MobyFrame(float Speed, System.Numerics.Quaternion[] JointRotations)
    {
        /// <summary>Optional native per-joint scale channels. The default means unresolved or absent.</summary>
        public IReadOnlyList<MobyScaleOverride> ScaleOverrides { get; init; } = [];

        /// <summary>Optional native per-joint translation channels. The default means unresolved or absent.</summary>
        public IReadOnlyList<MobyTranslationOverride> TranslationOverrides { get; init; } = [];

        /// <summary>Whether the source frame's complete optional channel payload was validated and decoded.</summary>
        public bool ExtraChannelsResolved { get; init; }

        /// <summary>Signed native key time in eighths of a tick.</summary>
        public short TimeEighthTicks { get; init; }
    }

    /// <summary>
    /// A named motion — an ordered list of frames. <c>MobySequenceHeader</c>:
    /// <c>u8 frameCount @ 0x10</c>; frame offset table <c>u32[frameCount] @ 0x1c</c>
    /// (<c>&amp; 0x0fffffff</c> = offset relative to the class, <c>&amp; 0xf0000000</c>
    /// = flags). Frame body: <c>MobyFrameHeader</c> 0x10 then <c>s16[4]</c> per
    /// joint at <c>+0x10</c>, each channel <c>/ 32768</c>.
    /// </summary>
    public readonly record struct MobySphere(float X, float Y, float Z, float Radius);

    public sealed record MobySequence(int Index, IReadOnlyList<MobyFrame> Frames)
    {
        /// <summary>Native sequence bounding sphere in raw class units.</summary>
        public MobySphere? BoundingSphere { get; init; }

        /// <summary>Native rate override; zero uses each frame's rate.</summary>
        public float RateOverride { get; init; }

        /// <summary>Original sequence sound/loop flag byte.</summary>
        public byte LoopSound { get; init; }

        /// <summary>Number of native sequence triggers.</summary>
        public byte TriggerCount { get; init; }

        /// <summary>Class-relative offset of the optional gait data.</summary>
        public int TriggerDataOffset { get; init; }
    }

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
        public int MaterialStateIndex = -1;
        public readonly List<int> Strip = [];
    }

    /// <summary>Per-vertex bone binding: up to 3 (joint, weight) pairs; count 0 = rigid to model space.</summary>
    private struct SkinAttr
    {
        public int Count;
        public int J0, J1, J2;
        public int W0, W1, W2;
    }

    private sealed class SkinState
    {
        public SkinAttr?[] Slots { get; } = new SkinAttr?[64];
        public bool FullyResolved { get; set; } = true;
    }

    // The Moby VU1 vertex cache survives packet boundaries. Its duplicate
    // entries inherit geometry, skin and normal data from the previous packet,
    // but use ST values from the current packet's VIF unpack.
    private readonly record struct CachedVertex(
        double X, double Y, double Z,
        double LocalX, double LocalY, double LocalZ,
        (float X, float Y, float Z) Normal,
        (int J0, int J1, int J2, float W0, float W1, float W2) Binding);

    private static readonly double[] AffineId = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];

    private static (double X, double Y, double Z) AffineApply(double[] m, double x, double y, double z) => (
        m[0] * x + m[1] * y + m[2] * z + m[3],
        m[4] * x + m[5] * y + m[6] * z + m[7],
        m[8] * x + m[9] * y + m[10] * z + m[11]);

    /// <summary>
    /// Build the translation-only bind approximation retained by the established
    /// static renderer. This is a compatibility path, not the final native
    /// skeleton model: full-retail evidence shows many non-identity 3x3 bases.
    /// Animation uses preserved skin-local positions and <see cref="GcUyaMobyPose"/>
    /// instead of treating this shortcut as authoritative.
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
    public static List<MobyJoint> ReadJoints(byte[] buf, int jointCount)
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

            int s = skeletonOffset + j * JointStride;
            float bx = -BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(s + 48));
            float by = -BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(s + 52));
            float bz = -BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(s + 56));
            float lx = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(o));
            float ly = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(o + 4));
            float lz = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(o + 8));
            if (!float.IsFinite(bx) || !float.IsFinite(by) || !float.IsFinite(bz)) bx = by = bz = 0f;
            if (!float.IsFinite(lx) || !float.IsFinite(ly) || !float.IsFinite(lz)) lx = ly = lz = 0f;
            joints.Add(new MobyJoint(parent, bx, by, bz) { LocalTranslation = (lx, ly, lz) });
        }

        return joints;
    }

    /// <summary>
    /// Decode the full regular-frame trailing payload without interpreting it
    /// as a pose. Flagged/special or malformed frame layouts remain unresolved.
    /// All offsets/counts come from the original 0x10-byte frame header.
    /// </summary>
    private static bool TryReadFrameChannels(
        byte[] buf, int frameOffset, int jointCount,
        out MobyScaleOverride[] scales,
        out MobyTranslationOverride[] translations)
    {
        scales = [];
        translations = [];
        if (frameOffset < 0 || frameOffset > buf.Length - 0x10)
            return false;

        int qwords = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(frameOffset + 6));
        int quaternionBytes = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(frameOffset + 8));
        int scaleCount = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(frameOffset + 10));
        int translationOffset = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(frameOffset + 12));
        int translationCount = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(frameOffset + 14));
        if (quaternionBytes != jointCount * 8 ||
            translationOffset != quaternionBytes + scaleCount * 8 ||
            qwords != (translationOffset + translationCount * 8 + 15) / 16 ||
            (long)frameOffset + 0x10 + qwords * 16 > buf.Length)
            return false;

        var decodedScales = new MobyScaleOverride[scaleCount];
        for (int i = 0; i < scaleCount; i++)
        {
            int at = frameOffset + 0x10 + quaternionBytes + i * 8;
            int joint = buf[at + 6], flags = buf[at + 7];
            if (joint >= jointCount || (flags & 0x7f) != 0)
                return false;
            decodedScales[i] = new MobyScaleOverride(
                joint,
                BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(at)) / 4096f,
                BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(at + 2)) / 4096f,
                BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(at + 4)) / 4096f,
                (flags & 0x80) != 0);
        }

        var decodedTranslations = new MobyTranslationOverride[translationCount];
        for (int i = 0; i < translationCount; i++)
        {
            int at = frameOffset + 0x10 + translationOffset + i * 8;
            int joint = buf[at + 6], padding = buf[at + 7];
            if (joint >= jointCount || padding != 0)
                return false;
            decodedTranslations[i] = new MobyTranslationOverride(
                joint,
                BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(at)) / 1024f,
                BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(at + 2)) / 1024f,
                BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(at + 4)) / 1024f);
        }

        scales = decodedScales;
        translations = decodedTranslations;
        return true;
    }

    /// <summary>
    /// Read every <c>MobySequence</c> from the offset list at <c>class + 0x48</c>
    /// (<c>s32[sequenceCount]</c>, relative to the class). <c>sequenceCount</c> is
    /// <c>u8 @ 0x0c</c>. Frames whose data is out of range are skipped.
    /// </summary>
    public static List<MobySequence> ReadSequences(byte[] buf, int jointCount)
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

            var sphere = new MobySphere(
                BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(seqOffset)),
                BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(seqOffset + 4)),
                BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(seqOffset + 8)),
                System.Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(seqOffset + 12))));
            int frameCount = buf[seqOffset + 0x10];
            byte loopSound = buf[seqOffset + 0x11];
            byte triggerCount = buf[seqOffset + 0x12];
            int triggerDataOffset = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(seqOffset + 0x14));
            float rateOverride = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(seqOffset + 0x18));
            int frameTable = seqOffset + 0x1c;
            if (frameCount <= 0 || frameTable + frameCount * 4 > buf.Length)
            {
                sequences.Add(new MobySequence(s, [])
                {
                    BoundingSphere = sphere,
                    RateOverride = rateOverride,
                    LoopSound = loopSound,
                    TriggerCount = triggerCount,
                    TriggerDataOffset = triggerDataOffset,
                });
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

                MobyScaleOverride[] scales = [];
                MobyTranslationOverride[] translations = [];
                bool channelsResolved = (entry & 0xf0000000u) == 0 &&
                    TryReadFrameChannels(buf, frameOffset, jointCount, out scales, out translations);
                frames.Add(new MobyFrame(speed, rotations)
                {
                    ScaleOverrides = scales,
                    TranslationOverrides = translations,
                    ExtraChannelsResolved = channelsResolved,
                    TimeEighthTicks = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(frameOffset + 4)),
                });
            }

            sequences.Add(new MobySequence(s, frames)
            {
                BoundingSphere = sphere,
                RateOverride = rateOverride,
                LoopSound = loopSound,
                TriggerCount = triggerCount,
                TriggerDataOffset = triggerDataOffset,
            });
        }

        return sequences;
    }

    public static Mesh ReadClass(byte[] buf) => ReadClass(buf, false);

    public static Mesh ReadClass(byte[] buf, bool recoverPersistentVertexCache)
    {
        if (buf.Length < ClassHeaderSize)
        {
            throw new InvalidDataException("Moby class buffer shorter than the 0x48 header.");
        }

        float boundingRadius = System.Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x3c)))
            * (BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(0x24)) / 1024f);

        var skinned = Decode(buf, true, recoverPersistentVertexCache);
        if (!skinned.SkinningApplied)
        {
            return skinned;
        }

        var (pruned, removedFraction) = PruneSpikeTriangles(skinned);
        var folded = Decode(buf, false, recoverPersistentVertexCache);
        double se = MeshExtent(pruned);
        bool broken = se == 0
            || removedFraction > 0.03
            || se > System.Math.Max(40, System.Math.Max(boundingRadius * 5, MeshExtent(folded) * 4));

        // A fully reconstructed VU1 packet cache preserves the authored strip
        // indices; do not delete retail faces based on triangle edge length.
        // Pruning also leaves the per-triangle GS material-state indices out of
        // sync with the triangle arrays. Retain the established fallback for
        // genuinely unsupported skinning until the bind-pose work is complete.
        return broken ? folded with { Skinned = true } : recoverPersistentVertexCache ? skinned : pruned;
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

    /// <summary>
    /// Parse the shared GC/UYA Moby class table. The enclosing level/core
    /// container remains game-specific; callers provide the already-bounded
    /// index/assets views and table range.
    /// </summary>
    public readonly record struct ClassTable(int Count, int Offset);

    public static Dictionary<int, MobyClass> ReadClasses(
        byte[] index, byte[] assets, IReadOnlyList<int> sectionBoundaries, ClassTable table) =>
        ReadClasses(index, assets, sectionBoundaries, table, null, null);

    public static Dictionary<int, MobyClass> ReadClasses(
        byte[] index, byte[] assets, IReadOnlyList<int> sectionBoundaries, ClassTable table,
        Func<int, bool>? recoverPersistentVertexCache) =>
        ReadClasses(index, assets, sectionBoundaries, table, recoverPersistentVertexCache, null);

    public static Dictionary<int, MobyClass> ReadClasses(
        byte[] index,
        byte[] assets,
        IReadOnlyList<int> sectionBoundaries,
        ClassTable table,
        Func<int, bool>? recoverPersistentVertexCache,
        Func<int, bool>? previewNativeInitialPose)
    {
        var outp = new Dictionary<int, MobyClass>();

        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * ClassEntrySize;
            if (at < 0 || at + ClassEntrySize > index.Length)
            {
                break;
            }

            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at + 4));
            if (assetOffset <= 0 || assetOffset >= assets.Length)
            {
                continue;
            }

            var textures = new int[16];
            for (int t = 0; t < 16; t++)
            {
                textures[t] = index[at + 0x10 + t];
            }

            int end = sectionBoundaries.FirstOrDefault(b => b > assetOffset, assets.Length);
            bool nativePoseAdmitted = false;
            try
            {
                var classBuf = assets.AsSpan(assetOffset, end - assetOffset).ToArray();
                // R&C2 levels can contain R&C1-format class packets. The
                // native class header's format byte, not an Oozla-specific
                // class ID, decides whether the recovered GC/UYA VU1 cache
                // and trailing epilogue rules are safe to apply.
                bool compatiblePacketFormat = classBuf.Length > 0x0b &&
                    classBuf[0x0b] == 0;
                bool recoverCache = recoverPersistentVertexCache?.Invoke(oClass) == true &&
                    compatiblePacketFormat;
                Mesh mesh;
                bool strictRecoverySucceeded = recoverCache;
                try
                {
                    mesh = ReadClass(classBuf, recoverCache);
                }
                catch (InvalidDataException) when (recoverCache)
                {
                    // A valid GC-format marker alone does not guarantee every
                    // authored cache/epilogue variant is supported. Keep the
                    // previously available class on the established decoder
                    // when strict recovery rejects an unsupported packet.
                    strictRecoverySucceeded = false;
                    mesh = ReadClass(classBuf);
                }
                var triTexIds = mesh.TriangleMaterialSlots
                    .Select(slot => slot >= 0 && slot < 16 ? textures[slot] : -1)
                    .ToArray();
                int jointCount = classBuf[0x08];
                var joints = ReadJoints(classBuf, jointCount);
                var sequences = joints.Count > 0 ? ReadSequences(classBuf, jointCount) : new List<MobySequence>();
                if (strictRecoverySucceeded && jointCount is > 0 and <= 112 &&
                    previewNativeInitialPose?.Invoke(oClass) == true &&
                    mesh.SkinningApplied && mesh.SkinStateFullyResolved &&
                    sequences.FirstOrDefault(s => s.Index == 0) is { Frames.Count: > 0 } first &&
                    first.Frames[0].ExtraChannelsResolved)
                {
                    // Source-backed initial pose. Gameplay may select another
                    // sequence after the native instance is initialized.
                    nativePoseAdmitted = true;
                    var initialPositions = Ps2MobyNativePose.Pose(classBuf, mesh, first.Frames[0]);
                    var initialNormals = Ps2MobyNativePose.PoseNormals(
                        classBuf, mesh, first.Frames[0], initialPositions);
                    mesh = mesh with
                    {
                        Positions = initialPositions,
                        Normals = initialNormals,
                        NativeInitialPoseApplied = true
                    };
                }
                outp[oClass] = new MobyClass(
                    oClass, mesh, triTexIds,
                    triTexIds.Where(id => id >= 0).Distinct().OrderBy(id => id).ToList(),
                    joints, sequences);
            }
            catch when (!nativePoseAdmitted)
            {
                // Preserve the established GC permissive class-table behavior,
                // but fail visibly for an explicitly admitted native bind.
            }
        }

        return outp;
    }
}