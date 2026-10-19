using System.Buffers.Binary;
using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Novalis class-280 crank to class-665 paired-door relationships. The two
/// door endpoints are authored native XY floats at PVar +0xc/+0x10; PVar +0
/// is the crank's authored instance ordinal. Native turn rate, admission and
/// door easing remain unknown and are supplied by a clearly host-owned policy.
/// </summary>
public sealed class Rac1NovalisCrankDoorSession
{
    public const int NativeLevelId = 1;
    public const int CrankClass = 280;
    public const int DoorClass = 665;
    public const int ExpectedCranks = 2;
    public const int ExpectedDoorHalves = 4;

    public readonly record struct Door(
        int InstanceIndex, int CrankInstanceIndex, Vector3 ClosedNative, Vector3 OpenNative)
    {
        public Vector3 Interpolate(double progress) =>
            Vector3.Lerp(ClosedNative, OpenNative, (float)Math.Clamp(progress, 0d, 1d));
    }

    private readonly Dictionary<int, double> _cranks = [];
    private readonly Dictionary<int, Door> _doors = [];

    public int CrankCount => _cranks.Count;
    public int DoorCount => _doors.Count;
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

    public Vector3 DoorPosition(int doorInstanceIndex)
    {
        Door door = _doors[doorInstanceIndex];
        return door.Interpolate(Progress(door.CrankInstanceIndex));
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
