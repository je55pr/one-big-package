using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original class-1066 LEVEL6 Frog trigger (FUN_L06_002fda30).
/// Enters state1 only when Ratchet intersects the selected native cuboid.
/// Source animation-1 completion admits state2. Every 20 native ticks it
/// attempts the FIRST state15 Frog in its original ordered Moby-index list;
/// after no eligible members remain, enters terminal state3.
/// </summary>
public sealed class Rac1FrogWaveTriggers
{
    public const int ClassId = 1066;
    public const int NativeWaveDelayTicks = 20;
    private sealed class Entry(int instance, int group, Rac1ClipVolume zone, Vector3 position)
    {
        public int Instance { get; } = instance;
        public int Group { get; } = group;
        public Rac1ClipVolume Zone { get; } = zone;
        public Vector3 Position { get; } = position;
        public int State { get; set; }
        public int TicksRemaining { get; set; }
    }
    private readonly Dictionary<int, Entry> _entries = [];
    private readonly IReadOnlyList<int[]> _groups;
    private readonly HashSet<int> _frogIds;

    public Rac1FrogWaveTriggers(IReadOnlyList<int[]> groups,
        IEnumerable<int> authoredFrogIndices)
    {
        _groups = groups;
        _frogIds = authoredFrogIndices.ToHashSet();
    }

    public int RegisteredCount => _entries.Count;
    public int State(int instance) => _entries[instance].State;
    public int Group(int instance) => _entries[instance].Group;
    public int Countdown(int instance) => _entries[instance].TicksRemaining;
    public Vector3 NativePosition(int instance) => _entries[instance].Position;
    public Vector3 ZoneCentre(int instance) => _entries[instance].Zone.Centre;
    public IReadOnlyList<int> Members(int instance) => _groups[_entries[instance].Group];

    public void Register(RuntimeDynamicObject source, IReadOnlyList<Rac1ClipVolume> zones,
        Vector3 nativePosition)
    {
        if (!Finite(nativePosition)) throw new ArgumentOutOfRangeException(nameof(nativePosition));
        if (source.SourceGame != "rac1" || source.NativeClassId != ClassId)
            throw new ArgumentException("Requires authored class1066 trigger.", nameof(source));
        var pvar = source.NativePayloads?.SingleOrDefault(p => p.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (pvar is not { Length: 32 })
            throw new InvalidDataException("Native Frog trigger requires original 32-byte PVar.");
        int group = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0));
        int zone = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(4));
        if (group < 0 || group >= _groups.Count || zone < 0 || zone >= zones.Count ||
            _groups[group].Length == 0 || _groups[group].Any(index => !_frogIds.Contains(index)))
            throw new InvalidDataException("Frog wave trigger refers to non-Frog members or invalid zone.");
        // The host converts source Y-up transforms to native Z-up once,
        // before registration. Positions cannot be changed after that.
        if (!_entries.TryAdd(source.InstanceIndex, new(source.InstanceIndex, group,
                zones[zone], nativePosition)))
            throw new InvalidOperationException("Duplicate native Frog trigger.");
    }

    public Rac1FrogWaveTick Step(int instance, Vector3 playerPosition,
        bool animationCompleted, Func<int, bool> isNativeState15)
    {
        if (!Finite(playerPosition)) throw new ArgumentOutOfRangeException(nameof(playerPosition));
        ArgumentNullException.ThrowIfNull(isNativeState15);
        var e = _entries[instance];
        int previous = e.State;
        int? launch = null;
        switch (e.State)
        {
            case 0:
                if (e.Zone.Contains(playerPosition)) e.State = 1;
                break;
            case 1:
                if (animationCompleted) e.State = 2;
                break;
            case 2:
                if (e.TicksRemaining > 0 && --e.TicksRemaining > 0) break;
                foreach (int candidate in _groups[e.Group])
                {
                    if (!isNativeState15(candidate)) continue;
                    launch = candidate;
                    e.TicksRemaining = NativeWaveDelayTicks;
                    break;
                }
                if (launch is null) e.State = 3;
                break;
        }
        return new(instance, previous, e.State, launch, e.Position);
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public sealed record Rac1FrogWaveTick(int ControllerInstance, int PreviousState,
    int State, int? LaunchInstance, Vector3 NativeLaunchPosition);
