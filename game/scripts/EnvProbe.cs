using Godot;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Resolves the Going Commando "hero" lighting + per-region fog at a world point,
/// so the host can light the debug player and pick the right atmosphere as it
/// moves: the nearest environment sample point sets the base ambient colour /
/// directional light / fog, and any env-transition volume the point sits inside
/// blends those toward its far-side state (a doorway). See research/GC_LIGHTING.md.
///
/// Everything here is in OBP space (Y-up); the caller converts the Godot camera
/// position (which is X-mirrored — see <see cref="OBP.Godot.RuntimeWorldScene"/>)
/// on the way in, and mirrors the resolved light direction on the way out.
/// </summary>
public static class EnvProbe
{
    public readonly record struct Resolved(
        Color Ambient,
        bool HasHeroLight,
        Vector3 HeroTravelDir, // direction the key light travels, OBP space
        Color HeroColour,
        bool HasFog,
        Color FogColour,
        float FogNear,
        float FogFar,
        float FogFarVisibility);

    public static Resolved Evaluate(RuntimeLighting lighting, Vector3 p)
    {
        // --- base: nearest env sample -------------------------------------------------
        RuntimeEnvSample? nearest = null;
        double best = double.PositiveInfinity;
        foreach (var s in lighting.EnvSamples)
        {
            double d = Dist2(s.Position, p);
            if (d < best)
            {
                best = d;
                nearest = s;
            }
        }

        var heroCol = nearest is { } n0 ? V(n0.HeroColour) : new Vector3(0.4f, 0.4f, 0.4f);
        int heroIdx = nearest?.HeroLightIndex ?? -1;

        // Fog: the spawn region often defines none, so fall back to the nearest sample that does.
        RuntimeFog? fog = nearest?.Fog;
        if (fog is null)
        {
            double fb = double.PositiveInfinity;
            foreach (var s in lighting.EnvSamples)
            {
                if (s.Fog is not { } f)
                {
                    continue;
                }

                double d = Dist2(s.Position, p);
                if (d < fb)
                {
                    fb = d;
                    fog = f;
                }
            }
        }

        // --- env transitions the point is inside ------------------------------------
        foreach (var t in lighting.EnvTransitions)
        {
            var (cx, cy, cz, r) = t.BoundingSphere;
            double dc = System.Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy) + (p.Z - cz) * (p.Z - cz));
            if (dc > r)
            {
                continue;
            }

            // local box coords: the stored inverse matrix is native Z-up, so feed
            // it the y/z-swapped point. Blend along local X, box assumed [-1, 1].
            var m = t.InverseMatrixZUp;
            double lx = m[0] * p.X + m[4] * p.Z + m[8] * p.Y + m[12];
            double blend = System.Math.Clamp(0.5 + lx * 0.5, 0.0, 1.0);

            if (t.BlendHero)
            {
                heroCol = V(t.A.HeroColour).Lerp(V(t.B.HeroColour), (float)blend);
                heroIdx = blend < 0.5 ? t.A.HeroLightIndex : t.B.HeroLightIndex;
            }

            if (t.BlendFog)
            {
                var fa = t.A.Fog;
                var fb2 = t.B.Fog;
                fog = new RuntimeFog(
                    (Lerp(fa.Colour.R, fb2.Colour.R, blend), Lerp(fa.Colour.G, fb2.Colour.G, blend), Lerp(fa.Colour.B, fb2.Colour.B, blend)),
                    Lerp(fa.NearDistance, fb2.NearDistance, blend),
                    Lerp(fa.FarDistance, fb2.FarDistance, blend),
                    Lerp(fa.NearIntensity, fb2.NearIntensity, blend),
                    Lerp(fa.FarIntensity, fb2.FarIntensity, blend));
            }
        }

        // --- resolve the hero directional light -----------------------------------
        bool hasHero = heroIdx >= 0 && heroIdx < lighting.DirLights.Count;
        var dir = hasHero ? lighting.DirLights[heroIdx] : null;
        var travel = dir is { } dl
            ? new Vector3((float)dl.DirectionA.X, (float)dl.DirectionA.Y, (float)dl.DirectionA.Z)
            : new Vector3(0.3f, -0.8f, 0.4f);
        if (travel.LengthSquared() < 1e-4f)
        {
            travel = new Vector3(0.3f, -0.8f, 0.4f);
        }

        var heroLightCol = dir is { } d2
            ? new Color((float)d2.ColourA.R, (float)d2.ColourA.G, (float)d2.ColourA.B)
            : new Color(0.6f, 0.6f, 0.6f);

        return new Resolved(
            new Color(heroCol.X, heroCol.Y, heroCol.Z),
            hasHero,
            travel.Normalized(),
            heroLightCol,
            fog is not null,
            fog is { } ff ? new Color((float)ff.Colour.R, (float)ff.Colour.G, (float)ff.Colour.B) : Colors.Gray,
            (float)(fog?.NearDistance ?? 0),
            (float)(fog?.FarDistance ?? 0),
            fog is { } fi ? System.Math.Clamp((float)fi.FarIntensity / 255f, 0f, 1f) : 1f);
    }

    private static double Dist2((double X, double Y, double Z) a, Vector3 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static Vector3 V((double R, double G, double B) c) => new((float)c.R, (float)c.G, (float)c.B);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
