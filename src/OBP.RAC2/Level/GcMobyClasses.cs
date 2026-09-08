using OBP.PS2.Geometry;

namespace OBP.RAC2.Level;

/// <summary>
/// Going Commando level-core facade over the shared GC/UYA Moby codec.
/// The class packet decoder lives in OBP.PS2; GC still owns its core container.
/// </summary>
public static class GcMobyClasses
{
    public static Dictionary<int, GcUyaMoby.MobyClass> Read(GcLevelCore.Core core)
    {
        var table = core.Header.MobyClasses;
        return GcUyaMoby.ReadClasses(
            core.Index,
            core.Assets,
            core.SectionBoundaries,
            new GcUyaMoby.ClassTable(table.Count, table.Offset));
    }
}
