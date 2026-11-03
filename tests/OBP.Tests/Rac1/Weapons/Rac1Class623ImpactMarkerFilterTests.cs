using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1Class623ImpactMarkerFilterTests
{
    [Fact]
    public void NativeMarkerClearRequiresExactOwnerAndUnmaskedFlags()
    {
        Assert.Equal(new Rac1Class623ImpactMarkerResult(0xff, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(0xff, true, 0));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, null, 0));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, false, 0));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, null));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, 0x00100000));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, 0x00200000));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, 0x00330000));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(0xff, true),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, 0));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(0xff, true),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, 0x00000033));
        Assert.Equal(new Rac1Class623ImpactMarkerResult(6, false),
            Rac1Class623ImpactMarkerFilter.Evaluate(6, true, 0, allowClearing: false));
    }

    [SkippableFact]
    public void RetailClass623MarkerFilterReadsOwnerFlagsAndClearsOnlyUnmaskedSlot()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay o, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(o.Read(address, 4));

        // Caller passes Moby pointer, a1=0x00330000, a2=0.
        Assert.Equal(0x3c050033u, Word(overlay, 0x003033c8));
        Assert.Equal(0x0c0a11b6u, Word(overlay, 0x003033cc));
        Assert.Equal(0x0000302du, Word(overlay, 0x003033d0));

        Assert.Equal(0x908200a4u, Word(overlay, 0x002846d8)); // marker ID
        Assert.Equal(0x240800ffu, Word(overlay, 0x002846dc)); // empty sentinel
        Assert.Equal(0x10480008u, Word(overlay, 0x002846e0)); // skip empty
        Assert.Equal(0x00031980u, Word(overlay, 0x002846ec)); // index * 0x40
        Assert.Equal(0x24428500u, Word(overlay, 0x002846f0)); // table +0x178500
        Assert.Equal(0x8c670034u, Word(overlay, 0x002846f8)); // marker owner
        Assert.Equal(0x50e40003u, Word(overlay, 0x002846fc)); // owner == this Moby
        Assert.Equal(0x8c620024u, Word(overlay, 0x00284700)); // slot flags
        Assert.Equal(0x00451024u, Word(overlay, 0x0028470c)); // flags & a1
        Assert.Equal(0x14400005u, Word(overlay, 0x00284710)); // if masked, retain
        Assert.Equal(0x50c00001u, Word(overlay, 0x00284718)); // a2==0
        Assert.Equal(0xa0e800a4u, Word(overlay, 0x0028471c)); // clear ID
    }
}
