using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

/// <summary>
/// <c>--shots &lt;file&gt;</c>: load one world, then run every named
/// <see cref="ShotSpec"/> in the <see cref="ShotList"/> — set the framing, apply
/// the shot's overlay layers, settle, and write
/// <c>&lt;out&gt;/&lt;world&gt;-&lt;shot&gt;.png</c> plus a JSON sidecar — in a single
/// process, then quit. The world token is <c>--shots-world</c>, the list's own
/// <c>world</c> field, or <c>--planet</c>; a token with a <c>:</c>
/// (<c>rac1:LEVEL0</c>, <c>rac3:TABLE1</c>) routes through the neutral provider
/// path, otherwise it is a GC planet name / id.
/// </summary>
public partial class OBPGame
{
    private async System.Threading.Tasks.Task RunShotsAsync(string listPath)
    {
        ShotList list;
        try
        {
            list = ShotList.Parse(System.IO.File.ReadAllText(listPath));
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[shots] could not read shot list '{listPath}': {ex.Message}");
            GetTree().Quit(2);
            return;
        }

        RegisterCommandLineSources();

        string? token = _args.ShotsWorld ?? list.World ?? _args.Planet;
        string slug;

        if (token is { } t && t.Contains(':'))
        {
            OpenDestinationFromBootstrap(t);
            slug = t.Replace(':', '-').ToLowerInvariant();
        }
        else
        {
            int levelId = token is null ? _args.GcLevel : GcPlanetCatalogue.Resolve(token) ?? _args.GcLevel;
            _isoPath = _args.GcIso;
            if (_isoPath is null || !IdentifyDisc(_isoPath))
            {
                GD.PrintErr("[shots] a GC planet token needs --gc-iso");
                GetTree().Quit(2);
                return;
            }

            EnterWorld(levelId);
            slug = (GcPlanetCatalogue.Find(levelId)?.Planet ?? $"level{levelId}").ToLowerInvariant().Replace(' ', '-');
        }

        if (_mode != Mode.World || _world is not { } world)
        {
            GD.PrintErr($"[shots] world '{token ?? "?"}' did not load");
            GetTree().Quit(1);
            return;
        }
        Engine.MaxFps = 60;
        int failures = 0;

        for (int i = 0; i < list.Shots.Count; i++)
        {
            var shot = list.Shots[i];
            GD.Print($"[shots] {i + 1}/{list.Shots.Count}  {shot.Name}  camera={shot.Camera} overlay={shot.Overlay ?? "-"}");

            ApplyShotCamera(shot, world);
            ApplyOverlaySpec(shot.Overlay);
            UpdateWorldHud();

            string outDir = _args.ShotsOut ?? System.IO.Path.Combine("captures", "shots");
            string outPath = System.IO.Path.Combine(outDir, $"{slug}-{shot.Name}.png");
            var result = await CaptureHarness.CaptureAsync(this, outPath, shot.SettleFrames, () =>
            {
                var meta = CaptureMetadata();
                meta["capture"] = $"shot:{shot.Name}";
                meta["shot"] = new Dictionary<string, object?>
                {
                    ["name"] = shot.Name,
                    ["camera"] = shot.Camera.ToString(),
                    ["overlay"] = shot.Overlay,
                    ["settleFrames"] = shot.SettleFrames,
                };
                return meta;
            });
            if (!result.Ok)
            {
                failures++;
            }
        }

        GD.Print($"[shots] done — {list.Shots.Count - failures}/{list.Shots.Count} written to captures/shots/");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    /// <summary>Remember every <c>--&lt;game&gt;-iso</c> passed, so the neutral provider path can resolve its source.</summary>
    private void RegisterCommandLineSources()
    {
        if (_args.Rac1Iso is { } rc1)
        {
            RememberCommandLineSource(ObpSourceGame.Rac1, rc1);
        }

        if (_args.GcIso is { } gc)
        {
            RememberCommandLineSource(ObpSourceGame.Rac2, gc);
        }

        if (_args.UyaIso is { } uya)
        {
            RememberCommandLineSource(ObpSourceGame.Rac3, uya);
        }
    }

    private void ApplyShotCamera(ShotSpec shot, RuntimeWorld world)
    {
        _camera.Current = true;
        _activeCamera = _camera;

        switch (shot.Camera)
        {
            case ShotCamera.TopDown:
                {
                    var b = world.Bounds;
                    var centre = RuntimeWorldScene.ToScene(
                        (b.Min.X + b.Max.X) * 0.5, (b.Min.Y + b.Max.Y) * 0.5, (b.Min.Z + b.Max.Z) * 0.5);
                    float span = (float)System.Math.Max(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z);
                    _camera.Position = centre + (Vector3.Up * ((span * 0.75f) + 50f));
                    _camera.LookAt(centre, Vector3.Forward);
                    _camera.Far = (span * 20f) + 2000f;
                    break;
                }

            case ShotCamera.Orbit:
                RuntimeWorldScene.FrameCamera(
                    _camera, world.Bounds, (float)shot.AzimuthDegrees, (float)shot.ElevationDegrees);
                break;

            case ShotCamera.Showcase:
            default:
                FrameShowcaseCamera(world);
                break;
        }
    }
}
