using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native Nebula G34 class-1109 shuttle stop selection affecting Frog global
/// D_L06_0015FDF8 (L06:00302578). Host only emulates the idle/volume
/// transitions; travel/cutscene and forced-return callbacks are not inferred.
/// </summary>
public sealed class Rac1FrogShuttleSwitch
{
    public const int ShuttleClassId = 1109;
    private readonly Rac1ClipVolume[] _zones;

    private Rac1FrogShuttleSwitch(int shuttleInstance, Rac1ClipVolume[] zones)
    {
        ShuttleInstance = shuttleInstance;
        _zones = zones;
    }

    public int ShuttleInstance { get; }
    public int SelectedStop { get; private set; }
    public bool SwitchEnabled => SelectedStop == 1;

    public Vector3 StopVolumeCentre(int stop)
    {
        if (stop is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(stop));
        return _zones[stop].Centre;
    }

    public static Rac1FrogShuttleSwitch? FromRetail(
        IEnumerable<RuntimeDynamicObject> placements, IReadOnlyList<Rac1ClipVolume> volumes)
    {
        var shuttles = placements.Where(p => p.SourceGame == ObpSourceGame.Rac1 &&
            p.NativeClassId == ShuttleClassId).ToArray();
        if (shuttles.Length == 0) return null;
        if (shuttles.Length != 1)
            throw new InvalidDataException("Expected exactly one LEVEL6 Frog-switch shuttle.");
        var source = shuttles[0];
        var pvar = source.NativePayloads?.SingleOrDefault(p =>
            p.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (pvar is null || pvar.Length < 0x10C)
            throw new InvalidDataException("Class-1109 shuttle lacks native stop-volume selectors.");
        var zones = new Rac1ClipVolume[3];
        for (int i = 0; i < zones.Length; i++)
        {
            int index = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x100 + i * 4));
            if (index < 0 || index >= volumes.Count)
                throw new InvalidDataException($"Class-1109 shuttle stop {i} has invalid clip volume {index}.");
            zones[i] = volumes[index];
        }
        return new(source.InstanceIndex, zones);
    }

    /// <summary>
    /// In native shuttle state 1 only: sel0/zone0 => sel2 (switch 0);
    /// sel2/zone1 => sel0 (switch 0); sel!=1/zone2 => sel1 (switch 1).
    /// The real game also resets this selector on a completed shuttle ride.
    /// That event must be supplied explicitly, never inferred from proximity.
    /// </summary>
    public bool ObserveWaitingShuttle(Vector3 playerPosition)
    {
        if (!float.IsFinite(playerPosition.X) || !float.IsFinite(playerPosition.Y) ||
            !float.IsFinite(playerPosition.Z))
            throw new ArgumentOutOfRangeException(nameof(playerPosition));
        int previous = SelectedStop;
        if (SelectedStop == 0 && _zones[0].Contains(playerPosition))
            SelectedStop = 2;
        else if (SelectedStop == 2 && _zones[1].Contains(playerPosition))
            SelectedStop = 0;
        else if (SelectedStop != 1 && _zones[2].Contains(playerPosition))
            SelectedStop = 1;
        return previous != SelectedStop;
    }

    /// <summary>Use only with a confirmed native shuttle-arrival selection,
    /// not with an ordinary Frog target, level distance or stale saved state.</summary>
    public bool AcceptShuttleArrival(int selectedStop)
    {
        if (selectedStop is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(selectedStop));
        bool changed = SelectedStop != selectedStop;
        SelectedStop = selectedStop;
        return changed;
    }
}
