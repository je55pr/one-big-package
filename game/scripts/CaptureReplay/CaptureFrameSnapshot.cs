using System.Numerics;
using System.Text.Json;
using OBP.Core;

namespace OBP.Game;

internal sealed record CaptureFrameSnapshot(long Frame, string Renderer, string Engine, string AuthorityBuild,
    WorldContentIdentity? Game, string? Planet, string? Location, int? Level, int WorldSwitches, Vector3 Camera,
    string? Overlays, double AnimationClock, CaptureAnimationSnapshot[] Animations, int Meshes, int Triangles,
    int Textures, int CollisionBodies, int CollisionTriangles, int Ties, int Shrubs, int Mobies, int DynamicObjects,
    JsonElement? Crates, JsonElement? Gameplay, JsonElement Hud, CaptureFramePlayer? Player);
internal sealed record CaptureAnimationSnapshot(string Name, int CurrentFrame, int FrameCount, bool Playing);
internal sealed record CaptureFramePlayer(Vector3 Position, bool OnFloor, string MovementController, string CameraController,
    bool RecoveredCamera, double? Heading, double? PreferredDistance, double? EffectiveDistance, double ManualYaw,
    double ManualPitch, double ObstructionCorrection, int ObstructionReleaseTicks, string Locomotion, string YawMode, string Animation);

internal static class CaptureFrameMetadata
{
    internal static Dictionary<string, object?> Build(CaptureFrameSnapshot snapshot) => new()
    {
        ["renderedFrames"] = snapshot.Frame,
        ["renderer"] = snapshot.Renderer,
        ["engine"] = snapshot.Engine,
        ["authorityBuild"] = snapshot.AuthorityBuild,
        ["game"] = snapshot.Game,
        ["planet"] = snapshot.Planet,
        ["location"] = snapshot.Location,
        ["levelId"] = snapshot.Level,
        ["worldSwitches"] = snapshot.WorldSwitches,
        ["camera"] = new[] { snapshot.Camera.X, snapshot.Camera.Y, snapshot.Camera.Z },
        ["overlays"] = snapshot.Overlays,
        ["animClockSeconds"] = Math.Round(snapshot.AnimationClock, 3),
        ["animations"] = snapshot.Animations,
        ["meshInstances"] = snapshot.Meshes,
        ["triangles"] = snapshot.Triangles,
        ["textures"] = snapshot.Textures,
        ["collisionBodies"] = snapshot.CollisionBodies,
        ["collisionTriangles"] = snapshot.CollisionTriangles,
        ["tieInstances"] = snapshot.Ties,
        ["shrubInstances"] = snapshot.Shrubs,
        ["mobyInstances"] = snapshot.Mobies,
        ["dynamicObjects"] = snapshot.DynamicObjects,
        ["crateDebug"] = snapshot.Crates,
        ["rac1Gameplay"] = snapshot.Gameplay,
        ["hud"] = snapshot.Hud,
        ["player"] = snapshot.Player is { } player ? new
        {
            position = new[] { player.Position.X, player.Position.Y, player.Position.Z },
            onFloor = player.OnFloor,
            movementController = player.MovementController,
            cameraController = player.CameraController,
            recoveredCameraActive = player.RecoveredCamera,
            cameraControlHeading = player.Heading,
            cameraPreferredDistance = player.PreferredDistance,
            cameraEffectiveDistance = player.EffectiveDistance,
            cameraManualYaw = player.ManualYaw,
            cameraManualPitch = player.ManualPitch,
            cameraObstructionCorrection = player.ObstructionCorrection,
            cameraObstructionReleaseTicks = player.ObstructionReleaseTicks,
            locomotionState = player.Locomotion,
            yawMode = player.YawMode,
            animationState = player.Animation,
        } : null,
    };
}
