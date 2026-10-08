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
    private static RuntimeSpawn? BuildAuthoredPlayerStart(GcInstances.Gameplay gameplay)
    {
        var starts = gameplay.MobyInstances.Where(instance => instance.OClass == 0).ToArray();
        if (starts.Length != 1)
        {
            return null;
        }

        var start = starts[0];
        return new RuntimeSpawn(
            start.Position.X,
            start.Position.Z,
            start.Position.Y,
            start.Rotation.Z);
    }

    /// <summary>
    /// Build the per-frame geometry for a small, bounded set of animated moby
    /// instances (single-joint classes with a real <see cref="GcUyaMoby.MobySequence"/>),
    /// kept out of the merged static soup so the host can play them over time.
    /// Also reports which instance indices were consumed so the static path can
    /// skip them.
    /// </summary>
    private static RuntimeLighting BuildLighting(GcInstances.Gameplay g)
    {
        static (double, double, double) C((float R, float G, float B) c) => (c.R, c.G, c.B);
        static (double, double, double) V((float X, float Y, float Z) v) => (v.X, v.Z, v.Y); // Z-up -> Y-up

        static RuntimeFog Fog(GcInstances.EnvFog f) =>
            new(C(f.Colour), f.NearDistance, f.FarDistance, f.NearIntensity, f.FarIntensity);

        static RuntimeFog StateFog(GcInstances.EnvState s) =>
            new(C(s.FogColour), s.FogNearDistance, s.FogFarDistance, s.FogNearIntensity, s.FogFarIntensity);

        var dir = g.DirLights
            .Select(d => new RuntimeDirLight(C(d.ColourA), V(d.DirectionA), C(d.ColourB), V(d.DirectionB)))
            .ToArray();

        var samples = g.EnvSamples
            .Select(s => new RuntimeEnvSample(V(s.Position), s.HeroLightIndex, C(s.HeroColour), s.Fog is { } f ? Fog(f) : null))
            .ToArray();

        var transitions = g.EnvTransitions
            .Select(t => new RuntimeEnvTransition(
                t.InverseMatrix.Select(x => (double)x).ToArray(),
                (t.BoundingSphere.X, t.BoundingSphere.Z, t.BoundingSphere.Y, t.BoundingSphere.R),
                t.EnableHero, t.EnableFog,
                new RuntimeEnvState(C(t.StateA.HeroColour), t.StateA.HeroLightIndex, StateFog(t.StateA)),
                new RuntimeEnvState(C(t.StateB.HeroColour), t.StateB.HeroLightIndex, StateFog(t.StateB))))
            .ToArray();

        return new RuntimeLighting(dir, samples, transitions);
    }

}
