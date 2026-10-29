using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.PS2.Graphics;
using OBP.PS2.Textures;

namespace OBP.RAC3.Level;

/// <summary>Retail-corroborated UYA level textures and static TIE/shrub class geometry.</summary>
public static class Rac3Assets
{
    public enum TextureTable { Tfrag, Moby, Tie, Shrub }
    public sealed record StaticClass(int OClass, double[] Positions, float[] Uvs, int[] Indices, int[] TriangleTextureIds)
    {
        public IReadOnlyList<RcMaterialState> Materials { get; init; } = [];
        public int[] TriangleMaterialStateIndices { get; init; } = [];
        public bool?[] TriangleAlphaBlendEnabled { get; init; } = [];
    }
    public sealed record MobyVisualClass(
        int OClass,
        GcUyaMoby.Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<GcUyaMoby.MobyJoint> Joints,
        IReadOnlyList<GcUyaMoby.MobySequence> Sequences)
    {
        // Temporary, bounded retail source needed to evaluate the same native
        // animation frames as the first-key pose (not carried into RuntimeWorld).
        public byte[]? NativePoseSource { get; init; }
    }
    public sealed record MobyClassSet(
        int DeclaredCount,
        IReadOnlyDictionary<int, MobyVisualClass> Decoded,
        IReadOnlySet<int> ZeroLocalCoreClasses);

    public static IReadOnlyList<RcLevelTextureTable.Texture> ReadTextures(Rac3LevelCore.Core core, TextureTable table)
    {
        Rac3LevelCore.ArrayRange r = table switch
        {
            TextureTable.Tfrag => core.Header.TfragTextures,
            TextureTable.Moby => core.Header.MobyTextures,
            TextureTable.Tie => core.Header.TieTextures,
            TextureTable.Shrub => core.Header.ShrubTextures,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return RcLevelTextureTable.Read(core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(r.Count, r.Offset), new RcLevelTextureTable.Options(1024, Strict: true));
    }

    // Retain a diagnostic escape hatch while the native first-frame UYA pose
    // is being compared with retail presentation across multiple planets.
    public static bool NativeMobyPoseEnabled =>
        Environment.GetEnvironmentVariable("OBP_RAC3_LEGACY_MOBY") != "1";

    public static MobyClassSet ReadMobyClasses(Rac3LevelCore.Core core, int textureCount) =>
        ReadMobyClasses(core, textureCount, NativeMobyPoseEnabled);

    public static MobyClassSet ReadMobyClasses(
        Rac3LevelCore.Core core, int textureCount, bool nativePoseOverride)
    {
        bool nativePose = nativePoseOverride;
        var table = core.Header.MobyClasses;
        if (table.Count < 0 || table.Offset < 0 ||
            (long)table.Offset + (long)table.Count * GcUyaMoby.ClassEntrySize > core.Index.Length)
        {
            throw new InvalidDataException("UYA Moby class table lies outside the core index.");
        }

        var decoded = new Dictionary<int, MobyVisualClass>();
        var zeroLocalCore = new HashSet<int>();
        var seen = new HashSet<int>();
        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * GcUyaMoby.ClassEntrySize;
            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (!seen.Add(oClass))
                throw new InvalidDataException($"UYA Moby class table repeats oClass {oClass}.");
            if (assetOffset == 0)
            {
                zeroLocalCore.Add(oClass);
                continue;
            }
            if (assetOffset < 0 || assetOffset + GcUyaMoby.ClassHeaderSize > core.Assets.Length)
                throw new InvalidDataException($"UYA Moby class {oClass} has invalid asset offset 0x{assetOffset:x}.");

            int end = core.SectionBoundaries.FirstOrDefault(b => b > assetOffset, core.Assets.Length);
            if (end <= assetOffset)
                throw new InvalidDataException($"UYA Moby class {oClass} has no bounded asset extent.");
            byte[] classBytes = core.Assets.AsSpan(assetOffset, end - assetOffset).ToArray();
            // The same persistent VU1 vertex slots and pipelined strip epilogue
            // found in GC also occur in UYA. Only the compatible packet format
            // can opt in; unsupported class packets retain the legacy reader.
            bool recoverCache = nativePose && classBytes[0x0b] == 0;
            GcUyaMoby.Mesh mesh;
            try
            {
                mesh = GcUyaMoby.ReadClass(classBytes, recoverCache);
            }
            catch (InvalidDataException) when (recoverCache)
            {
                recoverCache = false;
                mesh = GcUyaMoby.ReadClass(classBytes);
            }
            if (recoverCache && mesh.Skinned && !mesh.SkinningApplied)
            {
                // Some UYA packet variants decode but cannot bind their
                // recovered vertices (retail table 11/class 7158). Never drop
                // an actor that the existing skinning path could present.
                var legacy = GcUyaMoby.ReadClass(classBytes);
                if (!legacy.Skinned || legacy.SkinningApplied)
                {
                    mesh = legacy;
                    recoverCache = false;
                }
            }
            if (mesh.Positions.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException($"UYA Moby class {oClass} contains non-finite geometry.");
            if (mesh.TriangleMaterialSlots.Length != mesh.Indices.Length / 3)
                throw new InvalidDataException($"UYA Moby class {oClass} has inconsistent triangle materials.");

            byte[] textureIds = core.Index.AsSpan(at + 0x10, 16).ToArray();
            var mapped = new int[mesh.TriangleMaterialSlots.Length];
            for (int face = 0; face < mapped.Length; face++)
            {
                int slot = mesh.TriangleMaterialSlots[face];
                if (slot < 0)
                {
                    mapped[face] = -1;
                    continue;
                }
                if (slot >= textureIds.Length)
                    throw new InvalidDataException($"UYA Moby class {oClass} triangle {face} references invalid material slot {slot}.");
                int textureId = textureIds[slot];
                if (textureId != 0xff && (textureId < 0 || textureId >= textureCount))
                    throw new InvalidDataException($"UYA Moby class {oClass} references texture {textureId} outside 0..{textureCount - 1}.");
                mapped[face] = textureId;
            }
            int jointCount = classBytes[0x08];
            var joints = GcUyaMoby.ReadJoints(classBytes, jointCount);
            var sequences = joints.Count > 0 ? GcUyaMoby.ReadSequences(classBytes, jointCount) : [];
            if (recoverCache && jointCount is > 0 and <= 112 &&
                mesh.SkinningApplied && mesh.SkinStateFullyResolved &&
                sequences.FirstOrDefault(s => s.Index == 0) is { Frames.Count: > 0 } first &&
                first.Frames[0].ExtraChannelsResolved)
            {
                try
                {
                    // An ordinary native actor starts from the source's first
                    // regular key. Legacy bind-only skinning can place limbs
                    // far outside the authored model before animation starts.
                    var posed = Ps2MobyNativePose.Pose(classBytes, mesh, first.Frames[0]);
                    var normals = Ps2MobyNativePose.PoseNormals(classBytes, mesh, first.Frames[0], posed);
                    mesh = mesh with
                    {
                        Positions = posed,
                        Normals = normals,
                        NativeInitialPoseApplied = true,
                    };
                }
                catch (InvalidDataException)
                {
                    // Unsupported native channel or skeleton: keep the
                    // recovered packet topology without inventing a pose.
                }
            }
            decoded.Add(oClass, new MobyVisualClass(oClass, mesh, mapped, joints, sequences)
            {
                NativePoseSource = mesh.NativeInitialPoseApplied ? classBytes : null,
            });
        }

        if (decoded.Count + zeroLocalCore.Count != table.Count)
            throw new InvalidDataException($"UYA Moby table declares {table.Count} classes but classified {decoded.Count + zeroLocalCore.Count}.");
        return new MobyClassSet(table.Count, decoded, zeroLocalCore);
    }
    public static IReadOnlyDictionary<int, StaticClass> ReadTieClasses(Rac3LevelCore.Core core)
        => ReadClasses(core, core.Header.TieClasses, RcTie.ClassEntrySize, 0x80,
            bytes =>
            {
                var m = RcTie.ReadClass(bytes, RcTie.Rac2Layout);
                return (
                    m.Positions, m.Uvs, m.Indices, m.TriangleMaterialSlots,
                    m.Materials, m.TriangleMaterialSlots, new bool?[m.TriangleMaterialSlots.Length]);
            }, "TIE");

    public static IReadOnlyDictionary<int, StaticClass> ReadShrubClasses(Rac3LevelCore.Core core)
        => ReadClasses(core, core.Header.ShrubClasses, RcShrub.ClassEntrySize, 0x40,
            bytes =>
            {
                var m = RcShrub.ReadClass(bytes);
                return (
                    m.Positions, m.Uvs, m.Indices, m.TriangleMaterialSlots,
                    m.Materials, m.TriangleMaterialStateIndices, m.TriangleAlphaBlendEnabled);
            }, "shrub");

    private static IReadOnlyDictionary<int, StaticClass> ReadClasses(
        Rac3LevelCore.Core core, Rac3LevelCore.ArrayRange table, int stride, int minimumHeader,
        Func<byte[], (
            double[] P,
            float[] U,
            int[] I,
            int[] Slots,
            IReadOnlyList<RcMaterialState> Materials,
            int[] StateIndices,
            bool?[] AlphaBlend)> decode, string label)
    {
        if (table.Count < 0 || table.Offset < 0 || (long)table.Offset + (long)table.Count * stride > core.Index.Length)
            throw new InvalidDataException($"UYA {label} class table lies outside the core index.");
        var result = new Dictionary<int, StaticClass>();
        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * stride;
            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (assetOffset <= 0 || assetOffset + minimumHeader > core.Assets.Length)
                throw new InvalidDataException($"UYA {label} class {oClass} has invalid asset offset 0x{assetOffset:x}.");
            if (result.ContainsKey(oClass)) throw new InvalidDataException($"UYA {label} class table repeats oClass {oClass}.");
            int end = core.SectionBoundaries.FirstOrDefault(b => b > assetOffset, core.Assets.Length);
            if (end <= assetOffset) throw new InvalidDataException($"UYA {label} class {oClass} has no bounded asset extent.");
            var (p, u, ind, slots, materials, stateIndices, alphaBlend) =
                decode(core.Assets.AsSpan(assetOffset, end - assetOffset).ToArray());
            if (slots.Length != ind.Length / 3 || stateIndices.Length != slots.Length || alphaBlend.Length != slots.Length)
                throw new InvalidDataException($"UYA {label} class {oClass} has inconsistent triangle materials.");
            byte[] textureIds = core.Index.AsSpan(at + 0x10, 16).ToArray();
            int[] mapped = slots.Select(slot => slot >= 0 && slot < textureIds.Length ? textureIds[slot] : -1).ToArray();
            if (p.Any(v => !double.IsFinite(v))) throw new InvalidDataException($"UYA {label} class {oClass} contains non-finite geometry.");
            result.Add(oClass, new StaticClass(oClass, p, u, ind, mapped)
            {
                Materials = materials,
                TriangleMaterialStateIndices = stateIndices,
                TriangleAlphaBlendEnabled = alphaBlend,
            });
        }
        if (result.Count != table.Count) throw new InvalidDataException($"UYA {label} declared {table.Count} classes but decoded {result.Count}.");
        return result;
    }
}
