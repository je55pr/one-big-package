using System.Text.Json;
using OBP.Core.Math;
using OBP.RAC1.Camera;
using OBP.RAC1.Player;
using OBP.Runtime.Camera;

namespace OneBigPackage;

/// <summary>
/// OBP development movement bookmark. Stores no retail payload or source paths.
/// World object sessions and active airborne/action controllers are not serialized.
/// </summary>
public sealed record MovementReproState(
    int Version,
    string Destination,
    string SourceBuildId,
    DateTimeOffset SavedAtUtc,
    Guid HostAssemblyId,
    MovementReproPlayer Player)
{
    public const int CurrentVersion = 1;

    public MovementReproState Validate()
    {
        if (Version != CurrentVersion || string.IsNullOrWhiteSpace(SourceBuildId) || string.IsNullOrWhiteSpace(Destination) ||
            !Destination.StartsWith("rac1:LEVEL", StringComparison.Ordinal) ||
            !int.TryParse(Destination.AsSpan("rac1:LEVEL".Length), out int level) || level is < 0 or > 255 ||
            Player is null)
            throw new InvalidDataException("Unsupported or incomplete movement state.");
        Player.Validate();
        return this;
    }
}

public sealed record MovementReproPlayer(
    Vec3 Position,
    Vec3 Rotation,
    double FacingYaw,
    Vec3 HostCameraYawRotation,
    Vec3 HostCameraPitchRotation,
    Vec3 HostCameraLocalPosition,
    double CameraDistance,
    bool ForceHostCamera,
    bool CameraInitialized,
    Rac1OrdinaryCameraController.Snapshot CameraController,
    RuntimeCameraState? Camera,
    Rac1MovementCapabilities Equipment,
    Rac1TraversalEquipmentId? ActiveBackpack,
    bool ClankAvailable,
    int? PackPreview,
    bool SwingshotPreview)
{
    public void Validate()
    {
        Vec3[] vectors = [Position, Rotation, HostCameraYawRotation, HostCameraPitchRotation, HostCameraLocalPosition];
        if (vectors.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z) ||
                Math.Abs(v.X) > 1e7 || Math.Abs(v.Y) > 1e7 || Math.Abs(v.Z) > 1e7) ||
            !double.IsFinite(FacingYaw) || !double.IsFinite(CameraDistance) || CameraDistance is <= 0 or > 100 ||
            PackPreview is not (null or 2 or 3) || ActiveBackpack is not (null or
                Rac1TraversalEquipmentId.HeliPack or Rac1TraversalEquipmentId.ThrusterPack or Rac1TraversalEquipmentId.HydroPack) ||
            CameraController is null)
            throw new InvalidDataException("Invalid movement state player or camera pose.");
        CameraController.Validate();
        Camera?.Validate();
    }
}

public static class MovementReproStateFile
{
    private const long MaximumBytes = 65536;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static MovementReproState Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes)
            throw new InvalidDataException("Movement state is too large.");
        return (JsonSerializer.Deserialize<MovementReproState>(stream, Options)
            ?? throw new InvalidDataException("Movement state is empty.")).Validate();
    }

    public static void Save(string path, MovementReproState state)
    {
        state.Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
