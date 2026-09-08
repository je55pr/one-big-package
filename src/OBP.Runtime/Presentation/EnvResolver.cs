using OBP.Core.Math;

namespace OBP.Runtime.Presentation;

/// <summary>
/// Resolves the Going Commando "hero" lighting + per-region fog at a world point
/// so the host can light the player and pick the right atmosphere as the view
/// moves: the nearest environment sample sets the base ambient colour /
/// directional light / fog, and any env-transition volume the point sits inside
/// blends those toward its far-side state (a doorway). See
/// <c>research/GC_LIGHTING.md</c>.
///
/// <para>Everything here is OBP space (Y-up). The Godot host mirrors X on the
/// query point before calling in (the world geometry is X-mirrored — see
/// <c>OBP.Godot.RuntimeWorldScene</c>) and mirrors the resolved travel direction
/// back on the way out.</para>
/// </summary>
public static class EnvResolver
{
    private static readonly Vec3 DefaultTravel = new(0.3, -0.8, 0.4);
    private static readonly Rgb DefaultHeroColour = new(0.6, 0.6, 0.6);
    private static readonly Rgb DefaultAmbient = new(0.4, 0.4, 0.4);

    public static EnvResolved Evaluate(RuntimeLighting lighting, Vec3 p)
    {
        // --- base: nearest env sample -----------------------------------------
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

        Rgb heroColour = nearest is { } n0 ? Rgb.From(n0.HeroColour) : DefaultAmbient;
        int heroIdx = nearest?.HeroLightIndex ?? -1;

        // Fog: the spawn region often defines none, so fall back to the nearest
        // sample that does.
        RuntimeFog? fog = nearest?.Fog;
        if (fog is null)
        {
            double fogBest = double.PositiveInfinity;
            foreach (var s in lighting.EnvSamples)
            {
                if (s.Fog is not { } f)
                {
                    continue;
                }

                double d = Dist2(s.Position, p);
                if (d < fogBest)
                {
                    fogBest = d;
                    fog = f;
                }
            }
        }

        // --- env transitions the point is inside ----------------------------
        foreach (var t in lighting.EnvTransitions)
        {
            var (cx, cy, cz, r) = t.BoundingSphere;
            double dc = System.Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy)) + ((p.Z - cz) * (p.Z - cz)));
            if (dc > r)
            {
                continue;
            }

            // Local box coords: the stored inverse matrix is native Z-up, so feed
            // it the y/z-swapped point. Blend along local X, box assumed [-1, 1].
            var m = t.InverseMatrixZUp;
            double lx = (m[0] * p.X) + (m[4] * p.Z) + (m[8] * p.Y) + m[12];
            double blend = System.Math.Clamp(0.5 + (lx * 0.5), 0.0, 1.0);

            if (t.BlendHero)
            {
                heroColour = Rgb.From(t.A.HeroColour).Lerp(Rgb.From(t.B.HeroColour), blend);
                heroIdx = blend < 0.5 ? t.A.HeroLightIndex : t.B.HeroLightIndex;
            }

            if (t.BlendFog)
            {
                var fa = t.A.Fog;
                var fb = t.B.Fog;
                fog = new RuntimeFog(
                    (Lerp(fa.Colour.R, fb.Colour.R, blend), Lerp(fa.Colour.G, fb.Colour.G, blend), Lerp(fa.Colour.B, fb.Colour.B, blend)),
                    Lerp(fa.NearDistance, fb.NearDistance, blend),
                    Lerp(fa.FarDistance, fb.FarDistance, blend),
                    Lerp(fa.NearIntensity, fb.NearIntensity, blend),
                    Lerp(fa.FarIntensity, fb.FarIntensity, blend));
            }
        }

        // --- resolve the hero directional light ----------------------------
        bool hasHero = heroIdx >= 0 && heroIdx < lighting.DirLights.Count;
        var dir = hasHero ? lighting.DirLights[heroIdx] : null;

        var travel = dir is { } dl
            ? new Vec3(dl.DirectionA.X, dl.DirectionA.Y, dl.DirectionA.Z)
            : DefaultTravel;
        if (LengthSquared(travel) < 1e-4)
        {
            travel = DefaultTravel;
        }

        Rgb heroLightColour = dir is { } d2 ? Rgb.From(d2.ColourA) : DefaultHeroColour;

        return new EnvResolved(
            heroColour,
            hasHero,
            Normalized(travel),
            heroLightColour,
            fog is not null,
            fog is { } ff ? Rgb.From(ff.Colour) : new Rgb(0.5, 0.5, 0.5),
            fog?.NearDistance ?? 0,
            fog?.FarDistance ?? 0,
            fog is { } fi ? System.Math.Clamp(fi.FarIntensity / 255.0, 0.0, 1.0) : 1.0);
    }

    private static double Dist2((double X, double Y, double Z) a, Vec3 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static double LengthSquared(Vec3 v) => (v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z);

    private static Vec3 Normalized(Vec3 v)
    {
        double len = System.Math.Sqrt(LengthSquared(v));
        return len > 1e-9 ? new Vec3(v.X / len, v.Y / len, v.Z / len) : v;
    }
}
