using System.Text.Json;
using OBP.Core.Math;
using OBP.RAC1.Camera;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Camera;

namespace OneBigPackage;

/// <summary>
/// OBP development movement bookmark. Stores no retail payload or source paths.
/// Version2 also retains removed world objects and the live inventory. Active AI,
/// airborne/action controllers and transient effects are not serialized.
/// </summary>
public sealed record MovementReproState(
    int Version,
    string Destination,
    string SourceBuildId,
    DateTimeOffset SavedAtUtc,
    Guid HostAssemblyId,
    MovementReproPlayer Player,
    MovementReproWorld? World = null)
{
    public const int CurrentVersion = 2;

    public MovementReproState Validate()
    {
        if (Version is not (1 or CurrentVersion) || Version == CurrentVersion && World is null ||
            string.IsNullOrWhiteSpace(SourceBuildId) || string.IsNullOrWhiteSpace(Destination) ||
            !Destination.StartsWith("rac1:LEVEL", StringComparison.Ordinal) ||
            !int.TryParse(Destination.AsSpan("rac1:LEVEL".Length), out int level) || level is < 0 or > 255 ||
            Player is null)
            throw new InvalidDataException("Unsupported or incomplete movement state.");
        Player.Validate();
        World?.Validate();
        return this;
    }
}

/// <summary>OBP repro data, not a claim about the retail save format. No PVar or asset bytes.</summary>
public sealed record MovementReproWorld(
    IReadOnlyList<MovementReproInactiveObject> InactiveObjects,
    Rac1WeaponPersistentState Inventory,
    int CurrentItemId,
    int Bolts,
    IReadOnlyList<Rac1MobyRuntimeKey>? CapturedMobies = null,
    bool WeaponPreview = false,
    IReadOnlyList<Rac1MobyRuntimeKey>? MorphedMobies = null)
{
    public int? HeldItemId { get; init; }

    public void Validate()
    {
        if (InactiveObjects is null || InactiveObjects.Count > 4096 || Bolts < 0 ||
            InactiveObjects.Any(item => item is null || item.Key.NativeClassId is < 0 or > ushort.MaxValue ||
                item.Key.InstanceIndex is < 0 or > 1_000_000 || item.NativeState is < 0 or > byte.MaxValue ||
                Rac1MobyRuntime.IsTerminalState(item.NativeState) && !item.Terminalized) ||
            InactiveObjects.Select(item => item.Key).Distinct().Count() != InactiveObjects.Count)
            throw new InvalidDataException("Invalid movement state world objects or bolt balance.");
        try
        {
            var inventory = new Rac1WeaponInventory(Inventory, CurrentItemId);
            new Rac1HeldItemSession().Restore(HeldItemId ?? CurrentItemId, inventory);
        }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid movement state inventory.", ex); }
        if (CapturedMobies is { } cargo && (cargo.Count > Rac1AdditionalWeaponSession.SuckCapacity ||
            cargo.Distinct().Count() != cargo.Count || cargo.Any(key =>
                key.NativeClassId is not (500 or 749) && !(key.NativeClassId == 1440 && MorphedMobies?.Contains(key) == true) ||
                !InactiveObjects.Any(item => item.Key == key))))
            throw new InvalidDataException("Invalid movement state Suck Cannon cargo.");
        if (MorphedMobies is { } morphed && (morphed.Count > 4096 || morphed.Distinct().Count() != morphed.Count ||
            morphed.Any(key => key.NativeClassId is not (749 or 1440) || key.InstanceIndex is < 0 or > 1_000_000 ||
                InactiveObjects.Any(item => item.Key == key) && CapturedMobies?.Contains(key) != true)))
            throw new InvalidDataException("Invalid movement state Morph-o-Ray transformations.");
    }
}

public sealed record MovementReproInactiveObject(Rac1MobyRuntimeKey Key, int NativeState, bool Terminalized);

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
    private const long MaximumBytes = 1024 * 1024;
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
