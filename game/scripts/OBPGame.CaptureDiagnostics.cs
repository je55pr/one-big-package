using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC2;

namespace OneBigPackage;

/// <summary>Capture telemetry and optional retail authority verification diagnostics.</summary>
public partial class OBPGame
{
    // --- authority verification (background) ----------------------------

    private void StartBackgroundVerify(string isoPath)
    {
        System.Threading.Interlocked.Exchange(ref _verifyProgressBits, System.BitConverter.DoubleToInt64Bits(0));
        var progress = new System.Progress<double>(p =>
            System.Threading.Interlocked.Exchange(ref _verifyProgressBits, System.BitConverter.DoubleToInt64Bits(p)));

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var reader = new FileRandomAccessReader(isoPath);
                var verified = GcIsoLoad.Verify(reader, progress);
                _verifyOutcome = $"✓ authority build verified — {verified.Serial} {verified.Revision}";
            }
            catch (System.Exception ex)
            {
                _verifyOutcome = $"⚠ disc does not match the known-good dump: {ex.Message}";
            }
        });
    }

    private void ReportVerificationStatus(string text, bool error = false)
    {
        GD.Print($"[verification] {text}");
        _sourceManager?.SetStatus(text, error);
    }

    // --- deterministic capture ----------------------------------------

    private async System.Threading.Tasks.Task RunCaptureAsync(int frameArg)
    {
        var result = await CaptureHarness.CaptureAsync(
            this,
            _args.CaptureOut ?? $"captures/{_sceneKind}.png",
            frameArg,
            () =>
            {
                var meta = CaptureMetadata();
                meta["capture"] = _sceneKind;
                meta["captureFrameArg"] = frameArg;
                return meta;
            });
        ApplicationLifecycle.RequestQuit(
            this,
            result.Ok ? "capture-complete" : "capture-save-failed",
            result.Ok ? 0 : 1);
    }

    /// <summary>The common world / render / player metadata for any capture (shot or single frame). Call after the world has settled.</summary>
    private System.Collections.Generic.Dictionary<string, object?> CaptureMetadata()
    {
        var r = _sceneResult;
        return new System.Collections.Generic.Dictionary<string, object?>
        {
            ["renderedFrames"] = _frame,
            ["renderer"] = CaptureHarness.ActiveRenderer(),
            ["engine"] = (string)Engine.GetVersionInfo()["string"],
            ["authorityBuild"] = _world?.BuildId ?? Rac2Authority.Primary.BuildId,
            ["game"] = _world?.Game,
            ["planet"] = _world?.PlanetName,
            ["location"] = _world?.LocationName,
            ["levelId"] = _world?.LevelId,
            ["worldSwitches"] = _worldSwitches,
            ["camera"] = new[] { _activeCamera.GlobalPosition.X, _activeCamera.GlobalPosition.Y, _activeCamera.GlobalPosition.Z },
            ["overlays"] = _overlay?.StatusLine(),
            ["animClockSeconds"] = System.Math.Round(_worldHost.AnimationClockSeconds, 3),
            ["animations"] = _worldHost.AnimationStates()
                .Select(a => new { a.Name, a.CurrentFrame, a.FrameCount, a.Playing }).ToArray(),
            ["meshInstances"] = r?.MeshInstances ?? 0,
            ["triangles"] = r?.Triangles ?? 0,
            ["textures"] = r?.Textures ?? 0,
            ["collisionBodies"] = r?.CollisionBodies ?? 0,
            ["collisionTriangles"] = r?.CollisionTriangles ?? 0,
            ["tieInstances"] = r?.TieInstances ?? 0,
            ["shrubInstances"] = r?.ShrubInstances ?? 0,
            ["mobyInstances"] = r?.MobyInstances ?? 0,
            ["dynamicObjects"] = r?.DynamicObjects ?? 0,
            ["crateDebug"] = GetCrateDebugSnapshot(),
            ["rac1Gameplay"] = GetRac1GameplaySnapshot(),
            ["hud"] = _hudState.Current,
            ["player"] = _player is { } pl && IsInstanceValid(pl)
                ? new
                {
                    position = new[] { pl.GlobalPosition.X, pl.GlobalPosition.Y, pl.GlobalPosition.Z },
                    onFloor = pl.IsOnFloor(),
                    movementController = pl.MovementControllerLabel,
                    cameraController = pl.CameraControllerLabel,
                    recoveredCameraActive = pl.HasActiveRecoveredCamera,
                    cameraControlHeading = pl.Rac1RuntimeCameraState?.ControlHeadingRadians,
                    cameraPreferredDistance = pl.Rac1RuntimeCameraState?.PreferredDistance,
                    cameraEffectiveDistance = pl.Rac1RuntimeCameraState?.EffectiveDistance,
                    cameraManualYaw = pl.Rac1CameraManualYaw,
                    cameraManualPitch = pl.Rac1CameraManualPitch,
                    cameraObstructionCorrection = pl.Rac1CameraObstructionCorrection,
                    cameraObstructionReleaseTicks = pl.Rac1CameraObstructionReleaseTicks,
                    locomotionState = pl.Rac1LocomotionState.ToString(),
                    yawMode = pl.Rac1YawMode.ToString(),
                    animationState = pl.AnimationState.ToString(),
                }
                : null,
        };
    }
}
