using OBP.PS2.Presentation;
using OBP.RAC1.Level;
using OBP.Runtime.Presentation;

namespace OBP.RAC1;

/// <summary>R&C1 class mapping for the shared PS2 layered-foliage evidence test.</summary>
internal static class Rac1FoliageCutoutPolicy
{
    internal static HashSet<int> FindLayeredCutoutTextures(
        Rac1StaticClasses.ShrubClass shrub,
        IReadOnlyDictionary<int, AlphaProfile> alpha) =>
        RcFoliageCutoutPolicy.FindFoliageCutoutTextures(
            shrub.Mesh.Positions,
            shrub.Mesh.Indices,
            shrub.TriangleTextureIds,
            shrub.Mesh.TriangleMaterialStateIndices,
            shrub.Mesh.TriangleAlphaBlendEnabled,
            shrub.Mesh.Materials,
            alpha);
}
