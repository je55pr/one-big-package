using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Presentation;
using GodotEnvironment = global::Godot.Environment;

namespace OBP.Game;

/// <summary>
/// The underwater look of Novalis: while the camera is under a water surface the original swaps the fog for a
/// short tinted one and fades a translucent colour over the 3D view (before the HUD). The underwater flag is
/// latched like the original's camera check (0x1ecdf0): it is only re-evaluated while a water surface lies within
/// 0.75 of the camera, so it holds through flooded tunnels that have no surface plane.
/// </summary>
internal sealed class Rac1NovalisUnderwaterMechanism(
    Rac1AuthoredMechanismPorts ports, Rac1NovalisWaterMechanism water, Rac1NovalisUnderwaterLook look) : Rac1AuthoredMechanism
{
    private const float ProbeReach = .75f;       // segment from 0.75 above to 0.75 below the camera
    private const float SubmergedMargin = .04f;  // flag = camera below surface + 0.04

    private sealed record SavedFog(bool Enabled, GodotEnvironment.FogModeEnum Mode, Color Colour,
        float Begin, float End, float Density, float Curve);

    private CanvasLayer? _layer;
    private ColorRect? _fade;
    private GodotEnvironment? _environment;
    private SavedFog? _saved;
    private bool _underwater;
    private float? _lastSurface;

    internal bool Underwater => _underwater;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (world.Game != OBP.Core.ObpSourceGame.Rac1 || world.LevelId != Rac1NovalisScrollingWater.NativeLevelId) return;
        if (ports.SourcePath() is { } path)
        {
            try
            {
                using var reader = new FileRandomAccessReader(path);
                var level = Rac1DiscIndex.Read(reader).Levels.Single(candidate => candidate.LevelId == world.LevelId);
                look.Initialize(Rac1ExecutableOverlay.Read(reader, level));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException or OverflowException)
            {
                GD.PushError($"[rac1-novalis-underwater] retail underwater values unavailable; using the recovered defaults: {exception.Message}");
            }
        }
        // Layer 0 draws over the 3D view and under the HUD canvas layers.
        _layer = new CanvasLayer { Name = "Rac1NovalisUnderwaterFade", Layer = 0 };
        _fade = new ColorRect { Name = "Fade", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_fade);
        scene.Root.AddChild(_layer);
    }

    internal override void Tick(double delta, Rac1AuthoredTickPhase phase, PlayerHost? player)
    {
        if (phase != Rac1AuthoredTickPhase.Visual || _layer is null || _fade is null) return;
        var camera = _layer.GetViewport().GetCamera3D();
        if (camera is null) return;

        UpdateFlag(camera.GlobalPosition, player);
        if (_underwater) Apply(camera);
        else Restore();
    }

    private void UpdateFlag(Vector3 camera, PlayerHost? player)
    {
        float? surface = water.Sample(camera);
        if (surface is float height && Math.Abs(camera.Y - height) <= ProbeReach)
        {
            _underwater = camera.Y < height + SubmergedMargin;
            _lastSurface = height;
        }
        else if (_underwater && player is not null && player.Rac1SwimState is null &&
                 (surface is not float plane || camera.Y >= plane + SubmergedMargin))
        {
            // Not swimming and not under a surface any more (respawn, a jump out of a tunnel): the latch ends.
            _underwater = false;
        }
    }

    private void Apply(Camera3D camera)
    {
        var env = camera.Environment ?? camera.GetWorld3D()?.Environment;
        if (env is null) return;
        if (_environment != env)
        {
            Restore();
            _environment = env;
        }
        _saved ??= new(env.FogEnabled, env.FogMode, env.FogLightColor, env.FogDepthBegin, env.FogDepthEnd,
            env.FogDensity, env.FogDepthCurve);
        var colour = look.FogColour;
        // The same mapping the level's own fog uses: density is the share of the far fog intensity lost.
        var fog = new FogState(true, new Rgb(colour.Red / 255d, colour.Green / 255d, colour.Blue / 255d),
            look.FogNear, look.FogFar, Math.Clamp(1d - look.FogFarIntensity / 255d, 0d, 1d), WorldPresentation.FogCurve);
        PresentationEnvironment.ApplyFog(env, fog, setCurve: true);

        var fade = look.Overlay;
        _fade!.Color = new Color(fade.Red / 255f, fade.Green / 255f, fade.Blue / 255f, fade.Alpha / 128f);
        _fade.Visible = true;
    }

    private void Restore()
    {
        if (_fade is not null) _fade.Visible = false;
        if (_saved is { } saved && _environment is not null && GodotObject.IsInstanceValid(_environment))
        {
            _environment.FogEnabled = saved.Enabled;
            _environment.FogMode = saved.Mode;
            _environment.FogLightColor = saved.Colour;
            _environment.FogDepthBegin = saved.Begin;
            _environment.FogDepthEnd = saved.End;
            _environment.FogDensity = saved.Density;
            _environment.FogDepthCurve = saved.Curve;
        }
        _saved = null;
    }

    public override void Dispose()
    {
        Restore();
        _environment = null;
        if (_layer is not null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
        (_layer, _fade) = (null, null);
        _underwater = false;
    }
}
