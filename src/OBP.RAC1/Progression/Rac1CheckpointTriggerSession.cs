using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Progression;

/// <summary>
/// Source-owned class-805 volume entry controller. This recovers the common
/// native checkpoint producer using actual authored clip-volume indices,
/// deliberately excluding mission/state-gated markers until their globals
/// and save-slot admission can be supplied by the host.
/// </summary>
public sealed class Rac1CheckpointTriggerSession
{
    private readonly IReadOnlyList<Rac1ClipVolume> _volumes;
    private readonly List<Entry> _entries = [];

    private sealed class Entry(
        Rac1Instances.MobyInstance source,
        Rac1CheckpointMarkerAuthored authored,
        Rac1ClipVolume? volume)
    {
        public Rac1Instances.MobyInstance Source { get; } = source;
        public Rac1CheckpointMarkerAuthored Authored { get; } = authored;
        public Rac1ClipVolume? Volume { get; } = volume;
        public bool Initialized;
        public bool WasInside;
        public bool WasArmed;
    }

    public Rac1CheckpointTriggerSession(IReadOnlyList<Rac1ClipVolume> volumes) =>
        _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));

    public int RegisteredCount => _entries.Count;
    public int UngatedCount => _entries.Count(entry => entry.Authored.Flags == 0 && entry.Authored.SpawnSlot == -1 && entry.Volume is not null);

    public void Register(Rac1Instances.MobyInstance instance, Rac1CheckpointMarkerAuthored authored)
    {
        if (instance.OClass != Rac1CommonMobyAuthored.CheckpointMarker)
            throw new ArgumentException("Expected class-805 checkpoint.", nameof(instance));
        if (authored.VolumeIndex < -1 || authored.VolumeIndex >= _volumes.Count)
            throw new InvalidDataException($"Checkpoint i{instance.Index} has invalid authored cuboid {authored.VolumeIndex}.");
        if (_entries.Any(entry => entry.Source.Index == instance.Index))
            throw new InvalidOperationException($"Duplicate native checkpoint i{instance.Index}.");
        _entries.Add(new Entry(instance, authored,
            authored.VolumeIndex < 0 ? null : _volumes[authored.VolumeIndex]));
    }

    /// <summary>
    /// Return newly triggered authored checkpoint markers in Moby update order.
    /// Native flags 2/4/16 and optional spawn slots depend on globals that OBP
    /// cannot yet provide. Their markers remain registered but not enabled.
    /// </summary>
    public IReadOnlyList<Rac1CheckpointSourceActivation> Step(Vector3 nativePlayerPosition)
    {
        var output = new List<Rac1CheckpointSourceActivation>();
        foreach (var entry in _entries)
        {
            // Native command==0 initializes and skips first update.
            if (!entry.Initialized) { entry.Initialized = true; continue; }
            if (entry.Authored.Flags != 0 || entry.Authored.SpawnSlot != -1 || entry.Volume is null)
                continue;
            bool inside = entry.Volume.Contains(nativePlayerPosition);
            bool entered = inside && !entry.WasInside;
            entry.WasInside = inside;
            if (!inside && !entry.Authored.CanRearm) entry.WasArmed = false;
            if (!entered || entry.WasArmed && !entry.Authored.CanRearm) continue;
            entry.WasArmed = true;
            var source = entry.Source;
            output.Add(new(source.Index, entry.Authored.VolumeIndex,
                source.Position.X, source.Position.Y, source.Position.Z,
                source.Rotation.Z));
        }
        return output;
    }
}

public sealed record Rac1CheckpointSourceActivation(
    int InstanceIndex, int CuboidIndex,
    double NativeX, double NativeY, double NativeZ, double NativeYaw);
