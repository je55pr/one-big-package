using System.Text.Json;
using OBP.IO;
using OBP.PS2;
using OBP.RAC1;
using OBP.RAC1.Research;

namespace OBP.Tests;

public sealed class Rac1AuthoredMobyCensusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static Rac1AuthoredMobyCensus.Census ReadCheckedIn()
    {
        string path = Path.Combine(
            RepoPaths.Root, "research", "generated", "rac1-authored-moby-census.json");
        return JsonSerializer.Deserialize<Rac1AuthoredMobyCensus.Census>(
            File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Checked-in R&C1 authored-Moby census is empty.");
    }

    [Fact]
    public void CheckedInCensusPinsStaticAcceptanceBoundary()
    {
        var census = ReadCheckedIn();
        Assert.Equal(1, census.SchemaVersion);
        Assert.Equal("rac1-ntscu-original", census.Authority.BuildId);
        Assert.Equal("SCUS-97199", census.Authority.Serial);

        var totals = census.Totals;
        Assert.Equal(19, totals.Levels);
        Assert.Equal(3_641, totals.ClassTableOccurrences);
        Assert.Equal(1_350, totals.DistinctClasses);
        Assert.Equal(813, totals.InstantiatedUniqueClasses);
        Assert.Equal(537, totals.NeverPlacedUniqueClasses);
        Assert.Equal(16_232, totals.Instances);
        Assert.Equal(2_968, totals.PayloadOccurrences);
        Assert.Equal(2_930, totals.ModelOccurrences);
        Assert.Equal(2_949, totals.SourceSequenceOccurrences);
        Assert.Equal(1_445, totals.JointBearingOccurrences);
        Assert.Equal(1_194, totals.UniquePayloadClasses);
        Assert.Equal(1_192, totals.UniqueModelClasses);
        Assert.Equal(1_193, totals.UniqueSourceSequenceClasses);
        Assert.Equal(571, totals.UniqueJointBearingClasses);
        Assert.Equal(14_279, totals.PVarPlacements);
        Assert.Equal(1_953, totals.NoPVarPlacements);
        Assert.Equal(15_359, totals.PlacementsWithModel);
        Assert.Equal(873, totals.PlacementsWithoutModel);

        int[] expectedPVarSizes =
        [
            16, 32, 48, 64, 80, 96, 112, 128, 144, 160, 176, 192, 208, 224, 240,
            256, 272, 288, 304, 320, 336, 352, 384, 400, 416, 432, 448, 464, 480,
            512, 528, 544, 560, 576, 592, 608, 624, 640, 656, 672, 688, 704, 720,
            736, 752, 768, 784, 880, 912, 928, 1008, 1088, 1104, 1120, 1296, 1584,
            1616, 3872, 9024, 77344,
        ];
        Assert.Equal(expectedPVarSizes, totals.PVarByteSizes);

        int[][] expectedLevels =
        [
            [125, 96, 94, 95, 54, 21, 296],
            [212,169,167,168, 83, 84, 983],
            [187,156,154,155, 87, 60, 775],
            [208,174,172,173,107, 61,1029],
            [191,160,158,159, 99, 52, 536],
            [180,147,145,146, 74, 62,1431],
            [200,165,163,164, 64, 62, 766],
            [217,181,179,180, 71, 84, 649],
            [219,181,179,180, 90, 71, 847],
            [189,159,157,158, 71, 55, 908],
            [196,158,156,157, 72, 63, 872],
            [182,150,148,149, 70, 57, 720],
            [173,138,136,137, 69, 59, 695],
            [220,183,181,182, 85, 84, 970],
            [189,148,146,147, 73, 71, 551],
            [191,152,150,151, 70, 58, 910],
            [190,155,153,154, 72, 76,1569],
            [187,149,147,148, 65, 66, 734],
            [185,147,145,146, 69, 58, 991],
        ];

        Assert.Equal(expectedLevels.Length, census.Levels.Length);
        for (int level = 0; level < expectedLevels.Length; level++)
        {
            var actual = census.Levels[level];
            int[] expected = expectedLevels[level];
            Assert.Equal(level, actual.Level);
            Assert.Equal(expected[0], actual.TableRows);
            Assert.Equal(expected[1], actual.PayloadOccurrences);
            Assert.Equal(expected[2], actual.ModelOccurrences);
            Assert.Equal(expected[3], actual.SourceSequenceOccurrences);
            Assert.Equal(expected[4], actual.JointBearingOccurrences);
            Assert.Equal(expected[5], actual.InstantiatedClassIds);
            Assert.Equal(expected[6], actual.Instances);
        }

        Assert.Equal(1_350, census.Classes.Length);
        Assert.Equal(1_350, census.Classes.Select(row => row.OClass).Distinct().Count());
        Assert.Equal(3_641, census.Classes.Sum(row => row.LevelTableOccurrences));
        Assert.Equal(16_232, census.Classes.Sum(row => row.TotalInstances));
    }

    [Fact]
    public void CheckedInCensusPreservesSpecialStructuralBoundaries()
    {
        var census = ReadCheckedIn();
        Rac1AuthoredMobyCensus.ClassRow Class(int id) =>
            Assert.Single(census.Classes, row => row.OClass == id);

        var ratchet = Class(0);
        Assert.Equal(19, ratchet.LevelTableOccurrences);
        Assert.Equal(19, ratchet.TotalInstances);
        Assert.Equal([63], ratchet.HighLodPacketCounts);
        Assert.Equal([111], ratchet.JointCounts);
        Assert.Equal([134], ratchet.OrdinarySequenceSlotCounts);
        Assert.Equal([0], ratchet.OrdinarySequencePopulatedCounts);
        Assert.Equal(Enumerable.Range(0, 19), ratchet.RatchetSequenceTableLevels);
        Assert.Equal(3, ratchet.PVarPlacements);
        Assert.Equal(16, ratchet.NoPVarPlacements);
        Assert.Equal([32], ratchet.PVarByteSizes);
        Assert.All(ratchet.Levels, row =>
        {
            Assert.True(row.RatchetSequenceTableAvailable);
            Assert.Equal(256, row.RatchetSequenceSlotCount);
            Assert.True(row.RatchetSequencePopulatedCount > 0);
        });

        foreach (int id in new[] { 1, 2 })
        {
            var row = Class(id);
            Assert.Equal(0, row.TotalInstances);
            Assert.Equal(19, row.PayloadLevelOccurrences);
            Assert.Equal(0, row.ModelLevelOccurrences);
            Assert.Equal(19, row.SourceSequenceLevelOccurrences);
            Assert.Equal([0], row.HighLodPacketCounts);
            Assert.Equal([20], row.JointCounts);
            Assert.Equal([23], row.OrdinarySequenceSlotCounts);
            Assert.Equal([23], row.OrdinarySequencePopulatedCounts);
        }

        var class12 = Class(12);
        Assert.Equal(0, class12.TotalInstances);
        Assert.Equal(19, class12.PayloadLevelOccurrences);
        Assert.Equal(19, class12.ModelLevelOccurrences);
        Assert.Equal(0, class12.SourceSequenceLevelOccurrences);
        Assert.Equal([22], class12.HighLodPacketCounts);
        Assert.Equal([92], class12.JointCounts);
        Assert.Equal([10], class12.OrdinarySequenceSlotCounts);
        Assert.Equal([0], class12.OrdinarySequencePopulatedCounts);

        Assert.Equal(
            [12],
            census.Classes
                .Where(row => row.AssetPayloadAvailable && !row.SourceSequenceAvailable)
                .Select(row => row.OClass)
                .ToArray());

        var instantiated = census.Classes.Where(row => row.TotalInstances > 0).ToArray();
        Assert.Equal(616, instantiated.Count(row => row.PVarPlacements == row.TotalInstances));
        Assert.Equal(196, instantiated.Count(row => row.NoPVarPlacements == row.TotalInstances));
        Assert.Equal([0], instantiated
            .Where(row => row.PVarPlacements > 0 && row.NoPVarPlacements > 0)
            .Select(row => row.OClass).ToArray());

        string csvPath = Path.Combine(
            RepoPaths.Root, "research", "generated", "rac1-authored-moby-census.csv");
        string[] lines = File.ReadAllLines(csvPath);
        Assert.Equal(3_642, lines.Length);
        Assert.Equal(
            "level,tableIndex,oClass,instances,pvarPlacements,noPvarPlacements,pvarByteSizes," +
            "assetPayloadAvailable,highLodModelAvailable,highLodPacketCount,jointCount," +
            "ordinarySequenceSlotCount,ordinarySequencePopulatedCount,sourceSequenceAvailable," +
            "ratchetSequenceTableAvailable,ratchetSequenceSlotCount,ratchetSequencePopulatedCount",
            lines[0]);
    }

    [SkippableFact]
    public void AuthorizedRetailReproductionMatchesCheckedInOutputsExactly()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        Assert.Equal(Rac1Authority.PrimaryIsoSizeBytes, reader.Length);
        Assert.Equal(
            Rac1Authority.Primary.Serial,
            Ps2Boot.ReadBootInfo(reader).Serial,
            ignoreCase: true);

        var census = Rac1AuthoredMobyCensus.Build(reader);
        string expectedJson = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "research", "generated", "rac1-authored-moby-census.json"));
        string expectedCsv = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "research", "generated", "rac1-authored-moby-census.csv"));
        Assert.Equal(expectedJson, Rac1AuthoredMobyCensus.ToJson(census));
        Assert.Equal(expectedCsv, Rac1AuthoredMobyCensus.ToCsv(census));
    }
}
