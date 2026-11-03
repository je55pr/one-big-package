using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1CheckpointRetailTests
{
    private static readonly int[] GateClassFamily =
    [
        0x213, 0x214, 0x215, 0x217, 0x218, 0x219,
    ];

    [SkippableFact]
    public void ResetSnapshotGateClassFamilyCollapsesToOneLevel13AuthoredMoby()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var matches = new List<(int LevelId, Rac1Instances.MobyInstance Moby)>();
        foreach (var level in Rac1DiscIndex.Read(reader).Levels.OrderBy(level => level.LevelId))
        {
            byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            foreach (var moby in Rac1Instances.Parse(gameplay).MobyInstances)
            {
                if (GateClassFamily.Contains(moby.OClass))
                    matches.Add((level.LevelId, moby));
            }
        }

        var match = Assert.Single(matches);
        Assert.Equal(13, match.LevelId);
        Assert.Equal(801, match.Moby.Index);
        Assert.Equal(0x215, match.Moby.OClass);
        Assert.Equal(467.16425f, match.Moby.Position.X, 5);
        Assert.Equal(584.96655f, match.Moby.Position.Y, 5);
        Assert.Equal(316.71402f, match.Moby.Position.Z, 5);
        Assert.Equal(736, match.Moby.PVarIndex);
        Assert.NotNull(match.Moby.PVar);
        Assert.Equal(16, match.Moby.PVar!.Length);
    }
}
