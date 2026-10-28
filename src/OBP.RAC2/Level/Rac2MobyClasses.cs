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
