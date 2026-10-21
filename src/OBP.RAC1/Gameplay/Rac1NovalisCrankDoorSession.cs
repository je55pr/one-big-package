using System.Buffers.Binary;
using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Novalis class-280 crank and its two native followers: class-665 sliding
/// door halves and class-641 rotating cave door. The retail follower updates
/// consume the crank's PVar +0 progress (0..1). Host crank input cadence and
/// collision geometry remain explicitly provisional.
/// </summary>
public sealed class Rac1NovalisCrankDoorSession
{
    public const int NativeLevelId = 1;
    public const int CrankClass = 280;
    public const int DoorClass = 665;
    // Retail source: daino-selvatico/rac1-native-game@3fd14c1,
    // novalis_mobys.cpp and novalis_platforms.cpp (function original addresses).
    public const int CaveDoorClass = 641;
    public const int ExpectedCranks = 2;
    public const int ExpectedDoorHalves = 4;
    public const int ExpectedCaveDoors = 1;

    public readonly record struct Door(
        int InstanceIndex, int CrankInstanceIndex, Vector3 ClosedNative, Vector3 OpenNative)
    {
        public Vector3 Interpolate(double progress) =>
            Vector3.Lerp(ClosedNative, OpenNative, (float)Math.Clamp(progress, 0d, 1d));
    }

    /// <summary>Native state 0 preserves the initial yaw, and PVar +8 gives a target in degrees.</summary>
    public readonly record struct CaveDoor(int InstanceIndex, int CrankInstanceIndex,
        float InitialNativeYaw, float OpenNativeYaw)
    {
        public float Interpolate(double progress)
        {
            float delta = MathF.Atan2(MathF.Sin(OpenNativeYaw - InitialNativeYaw),
                MathF.Cos(OpenNativeYaw - InitialNativeYaw));
            return InitialNativeYaw + delta * (float)Math.Clamp(progress, 0d, 1d);
        }
    }

    private readonly Dictionary<int, double> _cranks = [];
    private readonly Dictionary<int, Door> _doors = [];
    private readonly Dictionary<int, CaveDoor> _caveDoors = [];

    public int CrankCount => _cranks.Count;
    public int DoorCount => _doors.Count;
    public int CaveDoorCount => _caveDoors.Count;
    public IEnumerable<CaveDoor> CaveDoors => _caveDoors.Values;
    public IEnumerable<Door> Doors => _doors.Values;

    public void RegisterCrank(int instanceIndex)
    {
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        if (!_cranks.TryAdd(instanceIndex, 0d))
            throw new InvalidOperationException($"Duplicate Novalis crank {instanceIndex}.");
    }

    public void RegisterDoor(Door door)
    {
        if (door.InstanceIndex < 0 || door.CrankInstanceIndex < 0 ||
            !Finite(door.ClosedNative) || !Finite(door.OpenNative))
            throw new ArgumentException("Invalid authored Novalis crank-door identity/position.");
        if (!_doors.TryAdd(door.InstanceIndex, door))
            throw new InvalidOperationException($"Duplicate Novalis door {door.InstanceIndex}.");
    }

    public static CaveDoor DecodeCaveDoor(int instanceIndex, ReadOnlySpan<byte> pvar,
        float initialNativeYaw)
    {
        if (pvar.Length != 0x10)
            throw new InvalidDataException("Novalis class-641 cave door PVar must be 16 bytes.");
        int crank = BinaryPrimitives.ReadInt32LittleEndian(pvar);
        float degrees = BinaryPrimitives.ReadSingleLittleEndian(pvar.Slice(8));
        float opened = degrees * MathF.PI / 180f;
        if (instanceIndex < 0 || crank < 0 || !float.IsFinite(initialNativeYaw) ||
            !float.IsFinite(opened))
            throw new InvalidDataException("Invalid Novalis cave door link or angles.");
        return new CaveDoor(instanceIndex, crank, initialNativeYaw, opened);
    }

    public void RegisterCaveDoor(CaveDoor door)
    {
        if (!_caveDoors.TryAdd(door.InstanceIndex, door))
            throw new InvalidOperationException($"Duplicate Novalis cave door {door.InstanceIndex}.");
    }

    public static Door DecodeDoor(int instanceIndex, ReadOnlySpan<byte> pvar, Vector3 closedNative)
    {
        if (pvar.Length != 0x20)
            throw new InvalidDataException("Novalis class-665 door PVar must be 32 bytes.");
        int link = BinaryPrimitives.ReadInt32LittleEndian(pvar);
        float x = BinaryPrimitives.ReadSingleLittleEndian(pvar.Slice(0x0c));
        float y = BinaryPrimitives.ReadSingleLittleEndian(pvar.Slice(0x10));
        var target = new Vector3(x, y, closedNative.Z);
        var door = new Door(instanceIndex, link, closedNative, target);
        if (instanceIndex < 0 || link < 0 || !Finite(closedNative) || !Finite(target))
            throw new InvalidDataException("Novalis door PVar has invalid linked identity or target.");
        return door;
    }

    public void ValidateLinks()
    {
        foreach (var door in _doors.Values)
            if (!_cranks.ContainsKey(door.CrankInstanceIndex))
                throw new InvalidDataException($"Door {door.InstanceIndex} references missing crank {door.CrankInstanceIndex}.");
        foreach (var door in _caveDoors.Values)
            if (!_cranks.ContainsKey(door.CrankInstanceIndex))
                throw new InvalidDataException($"Cave door {door.InstanceIndex} references missing crank {door.CrankInstanceIndex}.");
    }

    /// <summary>
    /// Apply host-supplied normalized progress (0..1) to the correct crank,
    /// without inventing the retail stick-angle recurrence or total duration.
    /// </summary>
    public double Turn(int crankInstanceIndex, double progressDelta)
    {
        if (!_cranks.TryGetValue(crankInstanceIndex, out double progress))
            throw new ArgumentOutOfRangeException(nameof(crankInstanceIndex));
        if (!double.IsFinite(progressDelta) || progressDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(progressDelta));
        progress = Math.Clamp(progress + progressDelta, 0, 1);
        _cranks[crankInstanceIndex] = progress;
        return progress;
    }

    public double Progress(int crankInstanceIndex) => _cranks[crankInstanceIndex];

    public float CaveDoorYaw(int instanceIndex) =>
        _caveDoors[instanceIndex].Interpolate(Progress(_caveDoors[instanceIndex].CrankInstanceIndex));

    public Vector3 DoorPosition(int doorInstanceIndex)
    {
        Door door = _doors[doorInstanceIndex];
        return door.Interpolate(Progress(door.CrankInstanceIndex));
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
