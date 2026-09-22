using System.Globalization;
using System.Text;
using System.Text.Json;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Level;

namespace OBP.RAC1.Research;

/// <summary>
/// Payload-free authored-Moby census for the pinned R&C1 NTSC-U authority.
/// Structural facts are projected only from the production retail codecs.
/// </summary>
public static class Rac1AuthoredMobyCensus
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record AuthorityRow(string BuildId, string Serial, string Sha256);
    public sealed record LevelRow(
        int Level, int TableRows, int PayloadOccurrences, int ModelOccurrences,
        int SourceSequenceOccurrences, int JointBearingOccurrences,
        int InstantiatedClassIds, int Instances, int PVarPlacements, int NoPVarPlacements);

    public sealed record ClassOccurrence(
        int Level, int TableIndex, int Instances, int PVarPlacements, int NoPVarPlacements,
        int[] PVarByteSizes, bool AssetPayloadAvailable, bool HighLodModelAvailable,
        int HighLodPacketCount, int JointCount, int OrdinarySequenceSlotCount,
        int OrdinarySequencePopulatedCount, bool SourceSequenceAvailable,
        bool RatchetSequenceTableAvailable, int RatchetSequenceSlotCount,
        int RatchetSequencePopulatedCount);

    public sealed record ClassRow(
        int OClass, int LevelTableOccurrences, int[] LevelTableIds, int TotalInstances,
        int PVarPlacements, int NoPVarPlacements, int[] PVarByteSizes,
        bool AssetPayloadAvailable, int PayloadLevelOccurrences,
        bool HighLodModelAvailable, int ModelLevelOccurrences,
        bool SourceSequenceAvailable, int SourceSequenceLevelOccurrences,
        int[] HighLodPacketCounts, int[] JointCounts, int[] OrdinarySequenceSlotCounts,
        int[] OrdinarySequencePopulatedCounts, int[] RatchetSequenceTableLevels,
        ClassOccurrence[] Levels);

    public sealed record TotalsRow(
        int Levels, int ClassTableOccurrences, int DistinctClasses,
        int InstantiatedUniqueClasses, int NeverPlacedUniqueClasses, int Instances,
        int PayloadOccurrences, int ModelOccurrences, int SourceSequenceOccurrences,
        int JointBearingOccurrences, int UniquePayloadClasses, int UniqueModelClasses,
        int UniqueSourceSequenceClasses, int UniqueJointBearingClasses,
        int PVarPlacements, int NoPVarPlacements, int[] PVarByteSizes,
        int PlacementsWithModel, int PlacementsWithoutModel);

    public sealed record Census(
        int SchemaVersion, AuthorityRow Authority, LevelRow[] Levels,
        ClassRow[] Classes, TotalsRow Totals);

    public static Census Build(IRandomAccessReader disc)
    {
        var levels = Rac1DiscIndex.Read(disc).Levels.OrderBy(level => level.LevelId).ToArray();
        if (levels.Length != Rac1DiscIndex.LevelTableCount ||
            !levels.Select(level => level.LevelId)
                .SequenceEqual(Enumerable.Range(0, Rac1DiscIndex.LevelTableCount)))
        {
            throw new InvalidDataException(
                "R&C1 authored-Moby census requires the complete native level set 0..18.");
        }

        var levelRows = new List<LevelRow>(levels.Length);
        var byClass = new SortedDictionary<int, List<ClassOccurrence>>();
        int placementsWithModel = 0;
        int placementsWithoutModel = 0;

        foreach (var level in levels)
        {
            var core = Rac1LevelCore.Open(disc, level);
            var table = Rac1StaticClasses.ReadMobyTable(core);
            var decoded = Rac1StaticClasses.Read(core).Mobies;
            var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level));

            var duplicate = table.GroupBy(entry => entry.OClass)
                .FirstOrDefault(group => group.Count() != 1);
            if (duplicate is not null)
            {
                throw new InvalidDataException(
                    $"R&C1 level {level.LevelId} repeats authored Moby class {duplicate.Key}.");
            }

            var tableIds = table.Select(entry => entry.OClass).ToHashSet();
            var missingPlacementClass = gameplay.MobyInstances
                .FirstOrDefault(instance => !tableIds.Contains(instance.OClass));
            if (missingPlacementClass is not null)
            {
                throw new InvalidDataException(
                    $"R&C1 level {level.LevelId} placement {missingPlacementClass.Index} " +
                    $"class {missingPlacementClass.OClass} is absent from the Moby table.");
            }

            var instancesByClass = gameplay.MobyInstances
                .GroupBy(instance => instance.OClass)
                .ToDictionary(group => group.Key, group => group.ToArray());

            int ratchetSlots = 0;
            int ratchetPopulated = 0;
            if (core.Header.RatchetSequencesOffset > 0 &&
                decoded.TryGetValue(0, out var ratchetClass))
            {
                var ratchetSequences = Rac1MobyAnimation.ReadRatchetSequences(
                    core.Assets, core.Index, core.Header.RatchetSequencesOffset,
                    ratchetClass.JointCount);
                ratchetSlots = ratchetSequences.Count;
                ratchetPopulated = ratchetSequences.Count(slot => slot.Value is not null);
            }

            var levelOccurrences = new List<ClassOccurrence>(table.Count);
            foreach (var entry in table)
            {
                decoded.TryGetValue(entry.OClass, out var cls);
                if (entry.HasAssetPayload != (cls is not null))
                {
                    throw new InvalidDataException(
                        $"R&C1 level {level.LevelId} class {entry.OClass} " +
                        "payload/table decode availability diverged.");
                }

                instancesByClass.TryGetValue(entry.OClass, out var instances);
                instances ??= [];
                int pvarPlacements = instances.Count(instance => instance.PVar is not null);
                int noPvarPlacements = instances.Length - pvarPlacements;
                int[] pvarSizes = instances
                    .Where(instance => instance.PVar is not null)
                    .Select(instance => instance.PVar!.Length)
                    .Distinct()
                    .OrderBy(size => size)
                    .ToArray();

                int highLodPackets = cls?.Mesh.HighLodPacketCount ?? 0;
                int ordinarySlots = cls?.Sequences.Count ?? 0;
                int ordinaryPopulated =
                    cls?.Sequences.Count(slot => slot.Value is not null) ?? 0;
                bool ratchetTableAvailable = entry.OClass == 0 && ratchetSlots > 0;
                int dedicatedSlots = ratchetTableAvailable ? ratchetSlots : 0;
                int dedicatedPopulated = ratchetTableAvailable ? ratchetPopulated : 0;
                bool sourceSequenceAvailable =
                    ordinaryPopulated > 0 || dedicatedPopulated > 0;

                var occurrence = new ClassOccurrence(
                    level.LevelId, entry.Index, instances.Length,
                    pvarPlacements, noPvarPlacements, pvarSizes,
                    entry.HasAssetPayload, highLodPackets > 0, highLodPackets,
                    cls?.JointCount ?? 0, ordinarySlots, ordinaryPopulated,
                    sourceSequenceAvailable, ratchetTableAvailable,
                    dedicatedSlots, dedicatedPopulated);
                levelOccurrences.Add(occurrence);

                if (!byClass.TryGetValue(entry.OClass, out var list))
                {
                    list = [];
                    byClass.Add(entry.OClass, list);
                }
                list.Add(occurrence);

                if (highLodPackets > 0)
                    placementsWithModel += instances.Length;
                else
                    placementsWithoutModel += instances.Length;
            }

            levelRows.Add(new LevelRow(
                level.LevelId,
                table.Count,
                levelOccurrences.Count(row => row.AssetPayloadAvailable),
                levelOccurrences.Count(row => row.HighLodModelAvailable),
                levelOccurrences.Count(row => row.SourceSequenceAvailable),
                levelOccurrences.Count(row => row.JointCount > 0),
                instancesByClass.Count,
                gameplay.MobyInstances.Count,
                gameplay.MobyInstances.Count(instance => instance.PVar is not null),
                gameplay.MobyInstances.Count(instance => instance.PVar is null)));
        }

        ClassRow[] classRows = byClass.Select(pair =>
        {
            ClassOccurrence[] occurrences = pair.Value
                .OrderBy(row => row.Level)
                .ThenBy(row => row.TableIndex)
                .ToArray();
            return new ClassRow(
                pair.Key,
                occurrences.Length,
                occurrences.Select(row => row.Level).ToArray(),
                occurrences.Sum(row => row.Instances),
                occurrences.Sum(row => row.PVarPlacements),
                occurrences.Sum(row => row.NoPVarPlacements),
                occurrences.SelectMany(row => row.PVarByteSizes)
                    .Distinct().OrderBy(size => size).ToArray(),
                occurrences.Any(row => row.AssetPayloadAvailable),
                occurrences.Count(row => row.AssetPayloadAvailable),
                occurrences.Any(row => row.HighLodModelAvailable),
                occurrences.Count(row => row.HighLodModelAvailable),
                occurrences.Any(row => row.SourceSequenceAvailable),
                occurrences.Count(row => row.SourceSequenceAvailable),
                occurrences.Select(row => row.HighLodPacketCount)
                    .Distinct().OrderBy(value => value).ToArray(),
                occurrences.Select(row => row.JointCount)
                    .Distinct().OrderBy(value => value).ToArray(),
                occurrences.Select(row => row.OrdinarySequenceSlotCount)
                    .Distinct().OrderBy(value => value).ToArray(),
                occurrences.Select(row => row.OrdinarySequencePopulatedCount)
                    .Distinct().OrderBy(value => value).ToArray(),
                occurrences.Where(row => row.RatchetSequenceTableAvailable)
                    .Select(row => row.Level).ToArray(),
                occurrences);
        }).ToArray();

        var totals = new TotalsRow(
            levelRows.Count,
            levelRows.Sum(row => row.TableRows),
            classRows.Length,
            classRows.Count(row => row.TotalInstances > 0),
            classRows.Count(row => row.TotalInstances == 0),
            levelRows.Sum(row => row.Instances),
            levelRows.Sum(row => row.PayloadOccurrences),
            levelRows.Sum(row => row.ModelOccurrences),
            levelRows.Sum(row => row.SourceSequenceOccurrences),
            levelRows.Sum(row => row.JointBearingOccurrences),
            classRows.Count(row => row.AssetPayloadAvailable),
            classRows.Count(row => row.HighLodModelAvailable),
            classRows.Count(row => row.SourceSequenceAvailable),
            classRows.Count(row => row.JointCounts.Any(count => count > 0)),
            levelRows.Sum(row => row.PVarPlacements),
            levelRows.Sum(row => row.NoPVarPlacements),
            classRows.SelectMany(row => row.PVarByteSizes)
                .Distinct().OrderBy(size => size).ToArray(),
            placementsWithModel,
            placementsWithoutModel);

        return new Census(
            SchemaVersion,
            new AuthorityRow(
                Rac1Authority.Primary.BuildId,
                Rac1Authority.Primary.Serial!,
                Rac1Authority.Primary.Sha256!),
            levelRows.ToArray(),
            classRows,
            totals);
    }

    public static string ToJson(Census census)
    {
        var output = new StringBuilder();
        output.Append("{\n")
            .Append("  \"schemaVersion\": ").Append(census.SchemaVersion).Append(",\n")
            .Append("  \"authority\": ")
            .Append(JsonSerializer.Serialize(census.Authority, JsonOptions)).Append(",\n")
            .Append("  \"levels\": [\n");
        for (int i = 0; i < census.Levels.Length; i++)
        {
            output.Append("    ")
                .Append(JsonSerializer.Serialize(census.Levels[i], JsonOptions))
                .Append(i + 1 == census.Levels.Length ? '\n' : ",\n");
        }
        output.Append("  ],\n  \"classes\": [\n");
        for (int i = 0; i < census.Classes.Length; i++)
        {
            output.Append("    ")
                .Append(JsonSerializer.Serialize(census.Classes[i], JsonOptions))
                .Append(i + 1 == census.Classes.Length ? '\n' : ",\n");
        }
        output.Append("  ],\n  \"totals\": ")
            .Append(JsonSerializer.Serialize(census.Totals, JsonOptions))
            .Append("\n}\n");
        return output.ToString();
    }

    public static string ToCsv(Census census)
    {
        var output = new StringBuilder();
        output.Append(
            "level,tableIndex,oClass,instances,pvarPlacements,noPvarPlacements,pvarByteSizes," +
            "assetPayloadAvailable,highLodModelAvailable,highLodPacketCount,jointCount," +
            "ordinarySequenceSlotCount,ordinarySequencePopulatedCount,sourceSequenceAvailable," +
            "ratchetSequenceTableAvailable,ratchetSequenceSlotCount,ratchetSequencePopulatedCount")
            .Append('\n');

        foreach (var pair in census.Classes
                     .SelectMany(cls => cls.Levels.Select(row => (Class: cls, Row: row)))
                     .OrderBy(pair => pair.Row.Level)
                     .ThenBy(pair => pair.Row.TableIndex))
        {
            var row = pair.Row;
            output.Append(row.Level.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.TableIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(pair.Class.OClass.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Instances.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PVarPlacements.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NoPVarPlacements.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(string.Join('|', row.PVarByteSizes)).Append(',')
                .Append(row.AssetPayloadAvailable ? "true" : "false").Append(',')
                .Append(row.HighLodModelAvailable ? "true" : "false").Append(',')
                .Append(row.HighLodPacketCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.JointCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OrdinarySequenceSlotCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OrdinarySequencePopulatedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.SourceSequenceAvailable ? "true" : "false").Append(',')
                .Append(row.RatchetSequenceTableAvailable ? "true" : "false").Append(',')
                .Append(row.RatchetSequenceSlotCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RatchetSequencePopulatedCount.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return output.ToString();
    }
}
