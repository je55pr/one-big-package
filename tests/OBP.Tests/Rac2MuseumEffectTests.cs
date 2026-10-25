using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;

namespace OBP.Tests;

public sealed class Rac2MuseumEffectTests
{
    [SkippableFact]
    public void MuseumNativeControlsResolveToTypedParticleParameters()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_GC_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var fs = Iso9660Filesystem.Open(disc);
        var wad = fs.OpenFile("/G/LEVEL21.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        Assert.Equal(30, header.LevelId);
        var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        Assert.Equal("b46e44c26195efd160b1bc62910cf22e1b1fe3eb46944f5926a9f2b7879e45b6",
            Convert.ToHexString(SHA256.HashData(overlay.Raw)).ToLowerInvariant());
        Assert.Equal("2f1fdf8c6e35505036c009e6e10036d244ad0cae289724ac3dd36e0f5afa6aab",
            Convert.ToHexString(SHA256.HashData(overlay.ReadVirtual(0x4187d4, 0x224))).ToLowerInvariant());
        Assert.Equal("b30021ddfca51e589fcea0d76526d8b00a1759cc78b407af5799392a9d68e15a",
            Convert.ToHexString(SHA256.HashData(overlay.ReadVirtual(0x418a4c, 0x108))).ToLowerInvariant());
        var parameters = Rac2MuseumEffectControls.ReadParticleEditor(overlay);
        Assert.Equal(17, parameters.Count);
        var x = parameters.Single(p => p.Name == "Velocity X");
        Assert.Equal(0, x.PVarOffset);
        Assert.Equal(Rac2MuseumEffectControls.Storage.Float32, x.Kind);
        Assert.Equal(-1000, x.Minimum); Assert.Equal(1000, x.Maximum);
        var life = parameters.Single(p => p.Name == "Lifespan");
        Assert.Equal(0x10, life.PVarOffset);
        Assert.Equal(Rac2MuseumEffectControls.Storage.Int32, life.Kind);
        Assert.Equal(1, life.Minimum);
        var opacity = parameters.Single(p => p.Name == "Init Opacity");
        Assert.Equal(0x24, opacity.PVarOffset); Assert.Equal(127, opacity.Maximum);
        Assert.Equal(0x34, parameters.Single(p => p.Name == "Gravity").PVarOffset);
        Assert.Equal(103, parameters.Single(p => p.Name == "Texture 1").Maximum);
        Assert.Equal(20, parameters.Single(p => p.Name == "Texture 2").Maximum);
        Assert.Equal(2, parameters.Count(p => p.Kind == Rac2MuseumEffectControls.Storage.Int16));
        // Editor creation is backed by the retail particle constructor; this
        // is not a shader-only preview with unrelated settings.
        Assert.Equal(0x0c0cfa3eu, BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(0x418b04, 4)));
    }
}
