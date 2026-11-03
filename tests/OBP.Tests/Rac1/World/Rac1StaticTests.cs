using OBP.IO;
using OBP.RAC1.Level;

namespace OBP.Tests;

/// <summary>Retail equivalence for R&C1 static class libraries and placements.</summary>
public sealed class Rac1StaticTests
{
    [SkippableFact]
    public void Level0_StaticClassesAndPlacementsMatchReferenceEvidence()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);
        var level = catalogue.Levels.Single(l => l.LevelId == 0);
        var core = Rac1LevelCore.Open(reader, level);

        var classes = Rac1StaticClasses.Read(core);
        Assert.Equal(63, classes.Ties.Count);
        Assert.Equal(40_672, classes.Ties.Values.Sum(c => c.Mesh.Indices.Length / 3));
        Assert.Equal(33, classes.Shrubs.Count);
        Assert.Equal(12_244, classes.Shrubs.Values.Sum(c => c.Mesh.Indices.Length / 3));

        Assert.All(classes.Ties.Values, cls =>
        {
            Assert.Equal(cls.Mesh.Indices.Length / 3, cls.TriangleTextureIds.Length);
            Assert.All(cls.TriangleTextureIds, id => Assert.InRange(id, 0, core.Header.TieTextures.Count - 1));
        });
        Assert.All(classes.Shrubs.Values, cls =>
        {
            Assert.Equal(cls.Mesh.Indices.Length / 3, cls.TriangleTextureIds.Length);
            Assert.All(cls.TriangleTextureIds, id => Assert.InRange(id, 0, core.Header.ShrubTextures.Count - 1));
        });

        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var placements = Rac1Instances.Parse(gameplay);
        Assert.Equal(1_114, placements.TieInstances.Count);
        Assert.Equal(1_697, placements.ShrubInstances.Count);
        Assert.All(placements.TieInstances, instance => Assert.Contains(instance.OClass, classes.Ties.Keys));
        Assert.All(placements.ShrubInstances, instance => Assert.Contains(instance.OClass, classes.Shrubs.Keys));
    }
}
