using System.Numerics;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original class-605 buried-bolt site selection. The native updater tests
/// optional authored path and clip-volume admission, then supplies the nearest
/// eligible target and its 3D range to the Metal Detector (native item 27).
/// Its source PVar +0x18 is the available bolt budget, consumed five at a time.
/// </summary>
public sealed class Rac1MetalDetectorSession
{
    public const int TargetClassId = 605;
    public const int NativeGadgetItem = 27;
    public const int NativeCollectionRange = 1;
    public const int NativeProximityRange = 20;
    public const int BoltsPerBurst = 5;
    private readonly IReadOnlyList<Vector3[]> _paths;
    private readonly IReadOnlyList<Rac1ClipVolume> _volumes;
    private readonly Rac1BuriedBoltProgress _progress;
    private readonly int _levelId;
    private readonly List<Target> _targets = [];
    private readonly Dictionary<int, Target> _byIndex = [];

    private sealed class Target(Rac1Instances.MobyInstance source, Rac1MetalDetectorTargetAuthored authored)
    {
        public Rac1Instances.MobyInstance Source { get; } = source;
        public Rac1MetalDetectorTargetAuthored Authored { get; } = authored;
        public int Remaining { get; set; } = authored.InitialResources;
    }

    public Rac1MetalDetectorSession(IReadOnlyList<Vector3[]> paths, IReadOnlyList<Rac1ClipVolume> volumes,
        Rac1BuriedBoltProgress? progress = null, int levelId = 0)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
        _progress = progress ?? new Rac1BuriedBoltProgress();
        if (levelId is < 0 or >= Rac1BuriedBoltProgress.LevelCount)
            throw new ArgumentOutOfRangeException(nameof(levelId));
        _levelId = levelId;
    }

    public int RegisteredCount => _targets.Count;
    public int ActiveCount => _targets.Count(target => target.Remaining > 0);

    public void Register(Rac1Instances.MobyInstance source, Rac1MetalDetectorTargetAuthored authored)
    {
        if (source.OClass != TargetClassId)
            throw new ArgumentException("Expected native class-605 source.", nameof(source));
        if (authored.PathIndex >= _paths.Count || authored.VolumeIndex >= _volumes.Count ||
            authored.PathIndex < -1 || authored.VolumeIndex < -1 || authored.InitialResources < 0)
            throw new InvalidDataException($"Buried-bolt target i{source.Index} references an unavailable source path/volume.");
        if (_byIndex.ContainsKey(source.Index))
            throw new InvalidOperationException($"Duplicate buried-bolt target i{source.Index}.");
        if (_targets.Count >= Rac1BuriedBoltProgress.TargetsPerLevel)
            throw new InvalidDataException("Native buried-target history has no remaining packed nibble.");
        var entry = new Target(source, authored);
        entry.Remaining = Math.Max(0, authored.InitialResources -
            Rac1MetalDetectorSession.BoltsPerBurst * _progress.Read(_levelId, _targets.Count));
        _byIndex.Add(source.Index, entry);
        _targets.Add(entry);
    }

    /// <summary>
    /// Recovered native nearest-target selection in original world XYZ (Z up).
    /// Point-in-path tests are XY polygons, and clip volumes are full 3D.
    /// The Metal Detector's active hand socket is the probe point where known.
    /// </summary>
    public Rac1MetalDetectorCandidate? FindNearest(Vector3 probePosition)
    {
        Rac1MetalDetectorCandidate? best = null;
        foreach (var target in _targets)
        {
            if (target.Remaining <= 0) continue;
            bool inPath = target.Authored.PathIndex >= 0 &&
                Rac1Paths.ContainsPlanar(_paths[target.Authored.PathIndex], probePosition);
            bool inVolume = target.Authored.VolumeIndex >= 0 &&
                _volumes[target.Authored.VolumeIndex].Contains(probePosition);
            if (!target.Authored.Eligible(inPath, inVolume)) continue;
            var native = target.Source.Position;
            float distance = Vector3.Distance(new(native.X, native.Y, native.Z), probePosition);
            if (best is null || distance < best.Distance)
                best = new(target.Source.Index, new(native.X, native.Y, native.Z),
                    distance, distance < NativeProximityRange, target.Remaining);
        }
        return best;
    }

    /// <summary>
    /// Native detector scans only while the use button is held, following a
    /// fifteen-frame activation age, with the target closer than one unit.
    /// The native collection loop drains the remaining PVar count in five-unit
    /// steps and removes the target. Return discrete bolt-emission requests,
    /// rather than inventing immediate wallet credits.
    /// </summary>
    public Rac1BuriedBoltExtraction? TryExtract(
        Rac1MetalDetectorCandidate? selected, bool gadgetHeld, int heldTicks)
    {
        if (!gadgetHeld || heldTicks <= 15 || selected is null ||
            selected.Distance >= NativeCollectionRange ||
            !_byIndex.TryGetValue(selected.InstanceIndex, out var target) ||
            target.Remaining <= 0)
            return null;
        int budget = target.Remaining;
        int bursts = (budget + BoltsPerBurst - 1) / BoltsPerBurst;
        int ordinal = _targets.IndexOf(target);
        for (int i = 0; i < bursts; i++)
            _progress.Increment(_levelId, ordinal);
        target.Remaining = 0;
        return new(selected.InstanceIndex, budget, bursts);
    }

    public int Remaining(int instanceIndex) => _byIndex[instanceIndex].Remaining;
}

public sealed record Rac1MetalDetectorCandidate(
    int InstanceIndex, Vector3 NativePosition, double Distance,
    bool InProximityRange, int RemainingBolts);

public sealed record Rac1BuriedBoltExtraction(int InstanceIndex, int AvailableBolts, int FiveBoltBursts);
