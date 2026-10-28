using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using Xunit.Abstractions;

namespace OBP.Tests;

/// <summary>
/// Read-only cross-level census before enabling GC native-pose preview globally.
/// The ISO number is the LEVEL file index, not always the levelId.
/// No production code or golden hashes are modified by this test.
/// </summary>
public sealed class Rac2CrossLevelMobyEligibilityAuditTests(ITestOutputHelper log)
{
    [SkippableFact]
    public void AuditEveryRetailLevelBeforeBroadeningNativePosePreview()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_GC_ISO missing");
        using var reader = new FileRandomAccessReader(iso!);
        var fs = Iso9660Filesystem.Open(reader);
        int levels = 0, classes = 0, nativePose = 0, crossPacket = 0, fallback = 0, formatVariants = 0, failed = 0, noSeq = 0, totalErrors = 0;
        var failedRows = new List<string>();
        for (int file = 0; file <= 26; file++)
        {
            var wad = fs.OpenFile($"/G/LEVEL{file}.WAD");
            Assert.NotNull(wad);
            var hdr = Rac2LevelWad.ReadHeader(wad);
            var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, hdr, 0));
            var table = core.Header.MobyClasses;
            int count = 0, ready = 0, cache = 0, variant = 0, legacy = 0, errors = 0, none = 0;
            for (int k = 0; k < table.Count; k++)
            {
                int at = table.Offset + k * GcUyaMoby.ClassEntrySize;
                int off = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
                int id = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
                if (off <= 0 || off >= core.Assets.Length - 0x48) continue;
                int end = core.SectionBoundaries.FirstOrDefault(b => b > off, core.Assets.Length);
                if (end <= off) continue;
                count++;
                var bytes = core.Assets.AsSpan(off, end - off).ToArray();
                if (bytes[0x0b] != 0) { variant++; continue; }
                GcUyaMoby.Mesh result;
                try
                {
                    result = GcUyaMoby.ReadClass(bytes, recoverPersistentVertexCache: true);
                    cache++;
                }
                catch (Exception ex)
                {
                    errors++;
                    if (failedRows.Count < 65) failedRows.Add($"LEVEL{file} oClass={id} cache failure {ex.GetType().Name}: {ex.Message}");
                    try { result = GcUyaMoby.ReadClass(bytes); legacy++; }
                    catch { failed++; }
                    continue;
                }
                if (result.SkinningApplied && result.SkinStateFullyResolved && bytes[8] is > 0 and <= 112)
                {
                    try
                    {
                        var seq = GcUyaMoby.ReadSequences(bytes, bytes[8]);
                        var first = seq.FirstOrDefault(s => s.Index == 0);
                        if (first is { Frames.Count: > 0 } && first.Frames[0].ExtraChannelsResolved)
                        {
                            var posed = Ps2MobyNativePose.Pose(bytes, result, first.Frames[0]);
                            if (posed.Length != result.Positions.Length || posed.Any(x => !double.IsFinite(x)))
                                throw new InvalidDataException("Nonfinite or truncated native pose");
                            ready++;
                        }
                        else none++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        if (failedRows.Count < 65) failedRows.Add($"LEVEL{file} oClass={id} initial pose failure {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else none++;
            }
            totalErrors += errors;
            levels++; classes += count; nativePose += ready; crossPacket += cache; formatVariants += variant; fallback += legacy; noSeq += none;
            log.WriteLine($"LEVEL{file}: classes={count} GC-format cache={cache} legacyOnCacheFailure={legacy} alternativeFormat={variant} eligibleSeq0Poses={ready} ineligible={none} errors={errors}");
        }
        log.WriteLine($"TOTAL levels={levels} classes={classes} cache={crossPacket} altFormat={formatVariants} initialNativePose={nativePose} legacyCacheFallback={fallback} noNativePose={noSeq} unreadable={failed} errors={totalErrors}");
        foreach (var row in failedRows) log.WriteLine(row);
        Assert.Equal(27, levels);
        Assert.True(nativePose > 30);
        Assert.Equal(0, failed);
        Assert.Equal(0, totalErrors);
    }
}
