using OBP.IO;
using OBP.PS2.Graphics;
using OBP.PS2.Iso;
using OBP.RAC1.Level;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed class MaterialRetailEvidenceTests
{
    private static string Summarize(IEnumerable<RcMaterialState> materials)
    {
        var all = materials.ToArray();
        int Hint(RcBlendModeHint hint) => all.Count(m => m.AlphaBlend?.Hint == hint);
        return $"m={all.Length};36={all.Count(m => m.Extra1Address == 0x36)};" +
            $"42={all.Count(m => m.Extra1Address == 0x42)};" +
            $"u={Hint(RcBlendModeHint.Unknown)};" +
            $"s={Hint(RcBlendModeHint.SourceAlpha)};" +
            $"a={Hint(RcBlendModeHint.AdditiveSourceAlpha)};" +
            $"f={Hint(RcBlendModeHint.FixedAlpha)};" +
            $"af={Hint(RcBlendModeHint.AdditiveFixedAlpha)}";
    }

    [SkippableFact]
    public void Rac1Level1_TieMaterialCensusIsPayloadFreeAndPinned()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 1);
        var classes = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level));

        string summary = Summarize(classes.Ties.Values.SelectMany(c => c.Mesh.Materials));

        Assert.Equal("m=593;36=593;42=0;u=0;s=0;a=0;f=0;af=0", summary);
    }

    [SkippableFact]
    public void GcLevel1_TieMaterialCensusIsPayloadFreeAndPinned()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD") ?? throw new FileNotFoundException("/G/LEVEL1.WAD");
        var header = GcLevelWad.ReadHeader(wad);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var classes = GcTie.ReadClasses(core);

        string summary = Summarize(classes.Values.SelectMany(c => c.Mesh.Materials));

        Assert.Equal("m=373;36=0;42=373;u=0;s=373;a=0;f=0;af=0", summary);
    }

    [SkippableFact]
    public void UyaRow1_TieMaterialCensusIsPayloadFreeAndPinned()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var core = UyaLevelCore.Open(reader, 1).Core;
        var classes = UyaAssets.ReadTieClasses(core);

        string summary = Summarize(classes.Values.SelectMany(c => c.Materials));

        Assert.Equal("m=417;36=0;42=417;u=26;s=389;a=0;f=0;af=2", summary);
    }
}
