using System.Buffers.Binary;
using OBP.PS2.Geometry;

namespace OBP.RAC2.Level;

/// <summary>
/// Going Commando level-core facade over the shared GC/UYA Moby codec.
/// The class packet decoder lives in OBP.PS2; GC still owns its core container.
/// </summary>
public static class Rac2MobyClasses
{
    // The class header selects the GC packet format. Source-backed initial
    // poses and the recovered VU1 cache are supported across RAC2.
    // Legacy mode remains available for diagnosis and regression comparisons.
    public static bool NativePoseEnabledForLevel(int level) =>
        level >= 0 && Environment.GetEnvironmentVariable("OBP_RAC2_LEGACY_MOBY") != "1";

    /// <summary>Extract the bounded asset bytes for a native Moby class.</summary>
    public static byte[] ClassBytes(Rac2LevelCore.Core core, int oClass)
    {
        var table = core.Header.MobyClasses;
        if (table.Count < 0 || table.Offset < 0 ||
            (long)table.Offset + (long)table.Count * GcUyaMoby.ClassEntrySize > core.Index.Length)
            throw new InvalidDataException("Invalid native Moby class table bounds.");

        int entry = Enumerable.Range(0, table.Count)
            .Select(i => table.Offset + i * GcUyaMoby.ClassEntrySize)
            .Single(at => BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4)) == oClass);
        int start = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(entry));
        if (start <= 0 || start >= core.Assets.Length)
            throw new InvalidDataException($"Invalid native class {oClass} asset offset.");
        int end = core.SectionBoundaries.FirstOrDefault(boundary => boundary > start, core.Assets.Length);
        if (end <= start || end > core.Assets.Length)
            throw new InvalidDataException($"Invalid native class {oClass} asset bounds.");
        return core.Assets.AsSpan(start, end - start).ToArray();
    }

    public static Dictionary<int, GcUyaMoby.MobyClass> Read(
        Rac2LevelCore.Core core, int level = -1, bool? nativePoseOverride = null)
    {
        var table = core.Header.MobyClasses;
        bool nativePose = level >= 0 && (nativePoseOverride ?? NativePoseEnabledForLevel(level));
        return GcUyaMoby.ReadClasses(
            core.Index,
            core.Assets,
            core.SectionBoundaries,
            new GcUyaMoby.ClassTable(table.Count, table.Offset),
            nativePose ? _ => true : null,
            previewNativeInitialPose: nativePose ? _ => true : null);
    }
}
