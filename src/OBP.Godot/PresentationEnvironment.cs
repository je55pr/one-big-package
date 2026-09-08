using Godot;
using OBP.Runtime;
using OBP.Runtime.Presentation;
using GodotEnvironment = Godot.Environment;

namespace OBP.Godot;

/// <summary>
/// Translates the engine-independent <see cref="PresentationState"/> /
/// <see cref="EnvResolved"/> onto a Godot <see cref="GodotEnvironment"/>: the
/// clear colour, scene ambient, tone-map + exposure, the gentle colour grade,
/// and depth fog. All of the decision-making is in
/// <see cref="WorldPresentation"/> (unit-tested); this is purely the Godot-facing
/// side. Exact PS2 GS blend / fog is not reproduced.
/// </summary>
public static class PresentationEnvironment
{
    /// <summary>Linear <see cref="Rgb"/> → Godot <see cref="Color"/>.</summary>
    public static Color ToColor(Rgb c) => new((float)c.R, (float)c.G, (float)c.B);

    /// <summary>Load-time: build the whole environment from a world's atmosphere.</summary>
    public static void Configure(GodotEnvironment env, RuntimeWorld world) =>
        Apply(env, WorldPresentation.Resolve(world.Environment, world.Bounds));

    /// <summary>Load-time: apply an already-resolved presentation state.</summary>
    public static void Apply(GodotEnvironment env, PresentationState state)
    {
        env.BackgroundMode = GodotEnvironment.BGMode.Color;
        env.BackgroundColor = ToColor(state.Background);
        env.AmbientLightSource = GodotEnvironment.AmbientSource.Color;
        env.AmbientLightColor = ToColor(state.Ambient);
        env.AmbientLightEnergy = (float)state.AmbientEnergy;

        env.TonemapMode = state.ToneMap.Mode switch
        {
            ToneMapMode.Agx => GodotEnvironment.ToneMapper.Agx,
            ToneMapMode.Filmic => GodotEnvironment.ToneMapper.Filmic,
            ToneMapMode.Reinhard => GodotEnvironment.ToneMapper.Reinhardt,
            _ => GodotEnvironment.ToneMapper.Linear,
        };
        env.TonemapExposure = (float)state.ToneMap.Exposure;
        env.TonemapWhite = (float)state.ToneMap.White;

        if (state.Grade.IsNeutral)
        {
            env.AdjustmentEnabled = false;
        }
        else
        {
            env.AdjustmentEnabled = true;
            env.AdjustmentBrightness = (float)state.Grade.Brightness;
            env.AdjustmentContrast = (float)state.Grade.Contrast;
            env.AdjustmentSaturation = (float)state.Grade.Saturation;
        }

        ApplyFog(env, state.Fog, setCurve: true);
    }

    /// <summary>
    /// Per-frame: apply a resolved region — lift the scene ambient toward white
    /// (so the unshaded world keeps its decoded colour) and, only where the
    /// region defines fog, override the depth fog. The load-time ease-in curve is
    /// left untouched.
    /// </summary>
    public static void ApplyResolvedRegion(GodotEnvironment env, EnvResolved r, double worldDiagonal)
    {
        env.AmbientLightColor = new Color(
            0.5f + (0.5f * (float)r.Ambient.R),
            0.5f + (0.5f * (float)r.Ambient.G),
            0.5f + (0.5f * (float)r.Ambient.B));

        var fog = WorldPresentation.FogFromResolved(r, worldDiagonal);
        if (fog.Enabled)
        {
            ApplyFog(env, fog, setCurve: false);
        }
    }

    /// <summary>
    /// Translate a resolved <see cref="FogState"/> onto a Godot environment.
    /// <paramref name="setCurve"/> is false on the per-frame path so a live
    /// region change never re-writes the load-time ease-in curve.
    /// </summary>
    public static void ApplyFog(GodotEnvironment env, FogState fog, bool setCurve)
    {
        if (!fog.Enabled)
        {
            env.FogEnabled = false;
            return;
        }

        env.FogEnabled = true;
        env.FogMode = GodotEnvironment.FogModeEnum.Depth;
        env.FogLightColor = ToColor(fog.Colour);
        env.FogDepthBegin = (float)fog.Begin;
        env.FogDepthEnd = (float)fog.End;
        env.FogDensity = (float)fog.Density;
        env.FogSkyAffect = 0.0f;
        if (setCurve)
        {
            env.FogDepthCurve = (float)fog.Curve;
        }
    }
}
