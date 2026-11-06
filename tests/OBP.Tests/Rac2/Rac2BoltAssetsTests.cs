using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2BoltAssetsTests
{
    [SkippableFact]
    public void OozlaPickupsUseOriginalModelsTexturesAndConstructorScales()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        var assets = Rac2BoltAssets.Read(reader, 1);
        Assert.Equal(Enumerable.Range(13, 7), assets.Models.Keys.Order());
        int[] triangles = [52, 48, 96, 192, 162, 174, 204];
        foreach (int id in Enumerable.Range(13, 7))
        {
            Assert.Equal(triangles[id - 13], assets.Models[id].Surfaces.Sum(surface => surface.Indices.Length / 3));
            Assert.All(assets.Models[id].Surfaces, surface =>
                Assert.Contains(assets.Textures, texture => texture.TextureId == surface.TextureId));
        }
        var wad = Iso9660Filesystem.Open(reader).OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        // Oozla's own constructor selects classes 13..19; these are not R&C1 aliases.
        uint[] scaleLoads = [0x378378, 0x37838C, 0x3783A0, 0x3783B4, 0x3783C8, 0x3783DC, 0x3783E8];
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(0xC781FBC0u + (uint)i * 4, BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(scaleLoads[i], 4)));
            Assert.Equal(Rac2BoltAssets.VisualScale, BinaryPrimitives.ReadSingleLittleEndian(overlay.ReadVirtual(0x1AEBB0u + (uint)i * 4, 4)));
        }
    }
}
