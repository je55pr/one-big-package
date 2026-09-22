using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Compression;
using OBP.PS2.Textures;
using OBP.RAC1.Animation;
using OBP.RAC1.Geometry;
using OBP.RAC1.Level;

namespace OBP.RAC1.Player;

/// <summary>
/// Retail R&amp;C1 Wrench presentation asset. Native item 8 selects global Moby
/// class 71; the class-table entry intentionally has no level-local payload, so
/// retail loads its compressed class WAD from the level core's global-class area.
/// </summary>
public static class Rac1WrenchAsset
{
    public const int NativeItemId = 8;
    public const int NativeClassId = 71;
    public const int CanonicalLevelId = 0;
    public const int NeutralNativeSequenceId = 1;
    public const int OwnerMobyOffset = 0xb8;
    public const int CompressedSourceSize = 0x5628;
    public const string CompressedSourceSha256 =
        "6923ebb76c882fd58f9a04220a722a3f9e18c720109e28aaac4aecd08a633306";

    public sealed record Surface(int TextureId, float[] Uvs, int[] Indices);
    public sealed record Texture(int TextureId, int Width, int Height, byte[] Rgba);

    public sealed record Asset(
        Rac1Moby.Mesh Mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> Skeleton,
        IReadOnlyList<Rac1MobyAnimation.SequenceSlot> Sequences,
        IReadOnlyList<Surface> Surfaces,
        IReadOnlyList<Texture> Textures,
        int CompressedSourceOffset)
    {
        public int VertexCount => Mesh.Positions.Length / 3;
        public int TriangleCount => Mesh.Indices.Length / 3;
    }

    public static Asset Load(string sourcePath)
    {
        using var reader = new FileRandomAccessReader(sourcePath);
        var level = Rac1DiscIndex.Read(reader).Levels
            .SingleOrDefault(level => level.LevelId == CanonicalLevelId)
            ?? throw new InvalidDataException(
                $"R&C1 canonical Wrench carrier level {CanonicalLevelId} is absent.");
        var core = Rac1LevelCore.Open(reader, level);
        return Decode(core);
    }

    internal static Asset Decode(Rac1LevelCore.Core core)
    {
        var (sourceOffset, compressed) = ReadCompressedSource(core);
        byte[] payload = WadLz.Decompress(compressed).Data;
        var mesh = Rac1Moby.ReadClass(payload);

        var skeleton = Rac1Moby.ReadSkeleton(payload);
        var sequences = Rac1MobyAnimation.ReadSequences(payload);
        ValidateDecodedClass(mesh, skeleton, sequences);

        byte[] textureMap = ReadClassTextureMap(core.Index);
        var bySurface = new Dictionary<int, List<int>>();
        for (int face = 0; face < mesh.TriangleMaterialSlots.Length; face++)
        {
            int slot = mesh.TriangleMaterialSlots[face];
            if ((uint)slot >= (uint)textureMap.Length || textureMap[slot] == 0xff)
                throw new InvalidDataException(
                    $"R&C1 Wrench face {face} references unmapped texture slot {slot}.");
            int textureId = textureMap[slot];
            if (!bySurface.TryGetValue(textureId, out var indices))
                bySurface[textureId] = indices = [];
            indices.Add(mesh.Indices[face * 3]);
            indices.Add(mesh.Indices[face * 3 + 1]);
            indices.Add(mesh.Indices[face * 3 + 2]);
        }

        var surfaces = bySurface
            .OrderBy(pair => pair.Key)
            .Select(pair => new Surface(pair.Key, mesh.Uvs, pair.Value.ToArray()))
            .ToArray();

        var decodedTextures = RcLevelTextureTable.Read(
            core.Index,
            core.Assets,
            core.GsRam,
            core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(
                core.Header.MobyTextures.Count,
                core.Header.MobyTextures.Offset))
            .ToDictionary(texture => texture.Index);
        var textures = surfaces
            .Select(surface => surface.TextureId)
            .Distinct()
            .Order()
            .Select(id =>
            {
                if (!decodedTextures.TryGetValue(id, out var texture))
                    throw new InvalidDataException(
                        $"R&C1 Wrench references missing Moby texture {id}.");
                return new Texture(id, texture.Width, texture.Height, texture.Rgba);
            })
            .ToArray();

        return new Asset(mesh, skeleton, sequences, surfaces, textures, sourceOffset);
    }

    private static (int Offset, byte[] Compressed) ReadCompressedSource(Rac1LevelCore.Core core)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x80));
        int tableOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(0x84));
        const int entrySize = 0x10;
        if (count < 0 || count > 256 || tableOffset < Rac1LevelCore.HeaderSize ||
            (long)tableOffset + (long)count * entrySize > core.Index.Length)
        {
            throw new InvalidDataException(
                $"R&C1 gadget directory {count}@0x{tableOffset:x} is invalid.");
        }

        int foundOffset = -1;
        int foundSize = -1;
        for (int i = 0; i < count; i++)
        {
            int at = tableOffset + i * entrySize;
            int sourceOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int classId = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            int compressedSize = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 8));
            if (classId != NativeClassId) continue;
            if (foundOffset >= 0)
                throw new InvalidDataException("Duplicate R&C1 Wrench class-71 gadget entry.");
            foundOffset = sourceOffset;
            foundSize = compressedSize;
        }

        if (foundOffset < 0)
            throw new InvalidDataException("R&C1 Wrench class-71 gadget entry is absent.");
        if (foundSize != CompressedSourceSize || foundOffset < 0 ||
            (long)foundOffset + foundSize > core.Assets.Length)
        {
            throw new InvalidDataException(
                $"R&C1 Wrench gadget source {foundSize}@0x{foundOffset:x} is invalid.");
        }

        byte[] compressed = core.Assets.AsSpan(foundOffset, foundSize).ToArray();
        var header = WadLz.ReadHeader(compressed);
        if (header.CompressedSize != foundSize)
            throw new InvalidDataException(
                $"R&C1 Wrench WAD declares 0x{header.CompressedSize:x} bytes, expected 0x{foundSize:x}.");

        byte[] expectedHash = Convert.FromHexString(CompressedSourceSha256);
        byte[] actualHash = SHA256.HashData(compressed);
        if (!actualHash.AsSpan().SequenceEqual(expectedHash))
            throw new InvalidDataException(
                "R&C1 Wrench compressed-source SHA-256 no longer matches the fixed retail authority.");

        return (foundOffset, compressed);
    }

    private static byte[] ReadClassTextureMap(byte[] index)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(0x18));
        int offset = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(0x1c));

        int found = -1;
        for (int i = 0; i < count; i++)
        {
            int at = checked(offset + i * Rac1StaticClasses.MobyEntrySize);
            int classId = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at + 4));
            if (classId != NativeClassId) continue;
            if (found >= 0)
                throw new InvalidDataException("Duplicate R&C1 Wrench class-71 table entry.");
            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
            if (assetOffset != 0)
                throw new InvalidDataException(
                    $"R&C1 Wrench class 71 unexpectedly has level-local asset offset 0x{assetOffset:x}.");
            found = at;
        }
        if (found < 0)
            throw new InvalidDataException("R&C1 Wrench class-71 table entry is absent.");
        return index.AsSpan(found + 0x10, 16).ToArray();
    }

    private static void ValidateDecodedClass(
        Rac1Moby.Mesh mesh,
        IReadOnlyList<Rac1Moby.SkeletonJoint> skeleton,
        IReadOnlyList<Rac1MobyAnimation.SequenceSlot> sequences)
    {
        if (mesh.HighLodPacketCount != 9 ||
            mesh.JointCount != 12 ||
            mesh.Positions.Length / 3 != 837 ||
            mesh.Indices.Length / 3 != 922 ||
            skeleton.Count != 12 ||
            sequences.Count != 20 ||
            sequences[NeutralNativeSequenceId].Value?.Frames.Count != 1)
        {
            throw new InvalidDataException(
                "R&C1 retail Wrench class-71 shape no longer matches the pinned authority.");
        }
    }
}
