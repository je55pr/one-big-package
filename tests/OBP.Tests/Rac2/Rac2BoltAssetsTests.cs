using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2BoltAssetsTests
{
    [Fact]
    public void UnsupportedDenominationCannotChooseAPlaceholderModel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltAssets.ClassForDenomination(2));

    [SkippableFact]
    public void OozlaPickupsUseOriginalModelsTexturesAndConstructorScales()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        var assets = Rac2BoltAssets.Read(reader, 1);
        Assert.Equal(Enumerable.Range(13, 7), assets.Models.Keys.Order());
        int[] triangles = [52, 48, 96, 192, 162, 174, 204];
        int[][] textureIds = [[24, 23], [0], [0], [0], [25], [25], [25]];
        foreach (int id in Enumerable.Range(13, 7))
        {
            Assert.Equal(triangles[id - 13], assets.Models[id].Surfaces.Sum(surface => surface.Indices.Length / 3));
            Assert.Equal(textureIds[id - 13], assets.Models[id].Surfaces.Select(surface => surface.TextureId));
            Assert.All(assets.Models[id].Surfaces, surface =>
                Assert.Contains(assets.Textures, texture => texture.TextureId == surface.TextureId));
        }
        // Payload-free fingerprints of the Oozla NTSC-U Moby texture table.
        (int Id, string Hash)[] expectedTextures =
        [
            (0, "AC4EB53E8608B337921069496D7E0763E2C4FCD4F05CCE4C584BCD6870B1EB5D"),
            (23, "76C3B3017261FAEA873E612F3BE5AB2A172F51BF5AB5C49C1B65FFF7F38A0C70"),
            (24, "FB5727C250A081F005BFA5A17BF99A0DAF44C5A5F2424A2918317D1A4F3A6617"),
            (25, "7CCEF95D73447246D582170D021461386B546100858EDAA80779C32769922BD0"),
        ];
        Assert.Equal(expectedTextures.Select(texture => texture.Id), assets.Textures.Select(texture => texture.TextureId).Order());
        foreach (var (id, hash) in expectedTextures)
        {
            var texture = assets.Textures.Single(texture => texture.TextureId == id);
            Assert.Equal(("moby", 32, 32), (texture.AssetKind, texture.Width, texture.Height));
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(texture.Rgba)));
        }
        var wad = Iso9660Filesystem.Open(reader).OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        uint ReadWord(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(address, 4));
        // Follow the retail denomination comparisons to each allocator call.
        // This checks the association against the original control flow rather
        // than duplicating the production lookup table as test expectations.
        uint[] comparisons = [0x378314, 0x378324, 0x378304, 0x378348, 0x378338, 0x37835C];
        foreach (uint address in comparisons)
        {
            uint load = ReadWord(address), branch = ReadWord(address + 4);
            Assert.Equal(0x24020000u, load & 0xFFFF0000);
            Assert.Equal(0x12020000u, branch & 0xFFFF0000);
            int displacement = unchecked((short)branch);
            uint target = checked((uint)((long)address + 8 + displacement * 4));
            Assert.Equal(0x0C0C17BAu, ReadWord(target));
            uint selector = ReadWord(target + 4);
            Assert.Equal(0x24040000u, selector & 0xFFFF0000);
            Assert.Equal((int)(selector & 0xFFFF), Rac2BoltAssets.ClassForDenomination((int)(load & 0xFFFF)));
        }
        Assert.Equal((int)(ReadWord(0x37836C) & 0xFFFF), Rac2BoltAssets.ClassForDenomination(1));
        // Oozla's own constructor selects classes 13..19; these are not R&C1 aliases.
        uint[] scaleLoads = [0x378378, 0x37838C, 0x3783A0, 0x3783B4, 0x3783C8, 0x3783DC, 0x3783E8];
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(0xC781FBC0u + (uint)i * 4, BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(scaleLoads[i], 4)));
            Assert.Equal(Rac2BoltAssets.VisualScale, BinaryPrimitives.ReadSingleLittleEndian(overlay.ReadVirtual(0x1AEBB0u + (uint)i * 4, 4)));
        }
    }
}
