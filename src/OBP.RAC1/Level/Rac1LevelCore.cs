using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.RAC1.Level;

/// <summary>R&C1 game-specific core index/container over shared PS2 codecs.</summary>
public static class Rac1LevelCore
{
    public const int HeaderSize = 0xbc;

    public readonly record struct ArrayRange(int Count, int Offset);

    public sealed record Header(
        int[] RawWords,
        int TfragsOffset,
        int TfragsEnd,
        int SkyOffset,
        int CollisionOffset,
        int CollisionEnd,
        int AssetsCompressedSize,
        int AssetsDecompressedSize,
        int TexturesBaseOffset,
        ArrayRange TfragTextures,
        ArrayRange MobyTextures,
        ArrayRange TieTextures,
        ArrayRange ShrubTextures,
        ArrayRange PartTextures,
        ArrayRange FxTextures);

    public sealed record Core(byte[] Index, byte[] Assets, byte[] GsRam, Header Header);

    public static Core Open(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level)
    {
        var levelData = Rac1DiscIndex.OpenRange(disc, level, 0);
        var directory = Rac1LevelData.ReadDirectory(levelData);
        var indexReader = Rac1LevelData.OpenRange(levelData, directory, Rac1LevelData.CoreIndexSlot);
        var dataReader = Rac1LevelData.OpenRange(levelData, directory, Rac1LevelData.CoreDataSlot);
        var gsReader = Rac1LevelData.OpenRange(levelData, directory, Rac1LevelData.GsRamSlot);

        var index = indexReader.Read(0, checked((int)indexReader.Length));
        if (index.Length < HeaderSize)
        {
            throw new InvalidDataException($"R&C1 level {level.LevelId} core index is shorter than 0xbc bytes.");
        }
        var header = ReadHeader(index);
        var wad = WadLz.ReadBlock(dataReader, 0);
        if (wad.CompressedSize != header.AssetsCompressedSize)
        {
            throw new InvalidDataException($"R&C1 core compressed size {wad.CompressedSize} != index {header.AssetsCompressedSize}.");
        }
        if (wad.Data.Length != header.AssetsDecompressedSize)
        {
            throw new InvalidDataException($"R&C1 core decompressed size {wad.Data.Length} != index {header.AssetsDecompressedSize}.");
        }
        var gsRam = gsReader.Read(0, checked((int)gsReader.Length));
        return new Core(index, wad.Data, gsRam, header);
    }

    public static Header ReadHeader(byte[] index)
    {
        if (index.Length < HeaderSize)
        {
            throw new InvalidDataException("R&C1 core index is shorter than 0xbc bytes.");
        }
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
        var raw = new int[HeaderSize / 4];
        for (int i = 0; i < raw.Length; i++) raw[i] = I32(i * 4);

        int tfrags = I32(0x08);
        int sky = I32(0x10);
        int collision = I32(0x14);
        int collisionEnd = I32(0x60);
        int compressed = I32(0x88);
        int decompressed = I32(0x8c);
        foreach (var (name, value) in new[]
        {
            ("tfrags", tfrags), ("sky", sky), ("collision", collision),
            ("collisionEnd", collisionEnd), ("compressed", compressed), ("decompressed", decompressed),
        })
        {
            if (value < 0) throw new InvalidDataException($"R&C1 core {name} is negative: {value}.");
        }

        var possibleEnds = new[] { I32(0x0c), sky, collision, collisionEnd, decompressed }
            .Where(v => v > tfrags).OrderBy(v => v).ToArray();
        if (possibleEnds.Length == 0)
        {
            throw new InvalidDataException($"R&C1 core has no boundary after tfrags offset {tfrags}.");
        }
        int tfragsEnd = possibleEnds[0];
        if (collisionEnd <= collision || collisionEnd > decompressed)
        {
            throw new InvalidDataException("R&C1 collision section boundaries are invalid.");
        }
        if (tfragsEnd > decompressed)
        {
            throw new InvalidDataException("R&C1 tfrag boundary exceeds decompressed core size.");
        }

        ArrayRange AR(int at)
        {
            int count = I32(at), offset = I32(at + 4);
            if (count < 0 || offset < 0 || (long)offset + (long)count * 16 > index.Length)
            {
                throw new InvalidDataException($"R&C1 core ArrayRange at 0x{at:x} is invalid ({count}, {offset}).");
            }
            return new ArrayRange(count, offset);
        }

        return new Header(raw, tfrags, tfragsEnd, sky, collision, collisionEnd, compressed, decompressed,
            collisionEnd, AR(0x30), AR(0x38), AR(0x40), AR(0x48), AR(0x50), AR(0x58));
    }
}
