using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.PS2.Geometry;
using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.RAC2.Audio;
using OBP.RAC2.Geometry;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Presentation;

namespace OBP.RAC2;

/// <summary>
/// Native world assembly for a Going Commando retail level — the C# equivalent of
/// <c>reference-ts/tools/gc-world.mjs</c>. Opens the level WAD from an ISO,
/// decodes chunk-0 tfrags + octree collision, instances the tie / shrub / moby
/// classes (per-texture grouping + vertex welding, native Z-up → OBP Y-up), adds
/// moby instance markers, the sky shells and a death-height plane, and grows the
/// world bounds.
///
/// <para>
/// Output is a neutral <see cref="RuntimeWorld"/> — no GC or Godot types cross
/// this boundary. This is where GC-specific data conversion terminates.
/// </para>
public static partial class GcWorldImport
{
    private static RuntimeMaterialPresentation? PresentationFor(
        IReadOnlyList<RcMaterialState> materials,
        int[] stateIndices,
        bool?[]? alphaBlendEnabled,
        int face,
        bool classifyMobySurface = false)
    {
        if ((uint)face >= (uint)stateIndices.Length)
        {
            return null;
        }

        int stateIndex = stateIndices[face];
        if ((uint)stateIndex >= (uint)materials.Count)
        {
            return null;
        }

        bool? alphaBlend = alphaBlendEnabled is not null && (uint)face < (uint)alphaBlendEnabled.Length
            ? alphaBlendEnabled[face]
            : null;
        var presentation = Ps2MaterialPresentation.From(
            materials[stateIndex], alphaBlend, classifyMobySurface);
        return presentation.HasNativeEvidence ? presentation : null;
    }

    private static RuntimeMaterialPresentation?[] PresentationsFor(
        IReadOnlyList<RcMaterialState> materials,
        int[] stateIndices,
        bool?[]? alphaBlendEnabled,
        int faceCount,
        bool classifyMobySurface = false)
    {
        var result = new RuntimeMaterialPresentation?[faceCount];
        for (int face = 0; face < faceCount; face++)
        {
            result[face] = PresentationFor(
                materials, stateIndices, alphaBlendEnabled, face, classifyMobySurface);
        }

        return result;
    }

}
