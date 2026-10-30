using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

// The persistent native inventory and dig-history session are borrowed, not
// copied. Scene-specific target registration, scan state and gadget focus die
// with the R&C1 world lease.
internal sealed record Rac1MetalDetectorPorts(
    Func<string?> SourcePath,
    Func<Rac1BuriedBoltProgress> BuriedBolts,
    Func<bool> PreviewEnabled,
    Func<bool> Ordinary,
    Func<string> OrdinaryUnavailability,
    Func<bool> OwnsGadget,
    Func<PlayerHost?> Player,
    Func<bool> Dead,
    Func<Vector3> Forward,
    Action<string> Report,
    Action<string> Status,
    Action RefreshHud,
    Action<Vector3, int> SpawnBursts,
    Action<string> Persist);

internal sealed class Rac1MetalDetectorHost(Rac1MetalDetectorPorts ports) : IDisposable
{
    private Rac1MetalDetectorSession? _session;
    private bool _equipped;
    private int _heldTicks;
    private double _carry;
    private Rac1MetalDetectorCandidate? _nearest;
    private bool _disposed;

    internal bool Equipped => !_disposed && _equipped;

    internal void Reset()
    {
        _session = null;
        _equipped = false;
        _heldTicks = 0;
        _carry = 0;
        _nearest = null;
    }

    internal void Configure(RuntimeWorld world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (world.LevelId == 0) return;
        var path = ports.SourcePath()
            ?? throw new InvalidOperationException("Metal Detector targets require the retail source.");
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(entry => entry.LevelId == world.LevelId);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var native = Rac1Instances.Parse(gameplay).MobyInstances.ToDictionary(moby => moby.Index);
        // Testing equipment may spawn bolts but must never alter genuine
        // campaign dig-history counters. Its copy is discarded on exit.
        var progress = ports.PreviewEnabled()
            ? Rac1BuriedBoltProgress.Restore(ports.BuriedBolts().Snapshot())
            : ports.BuriedBolts();
        _session = new(Rac1Paths.Read(gameplay), Rac1ClipVolumes.Read(gameplay), progress, world.LevelId);
        foreach (var moby in (world.DynamicObjects ?? [])
            .Where(item => item.NativeClassId == Rac1MetalDetectorSession.TargetClassId))
        {
            var authored = Rac1CommonMobyAuthored.ReadMetalDetectorTarget(moby)
                ?? throw new InvalidDataException($"Metal Detector target {moby.InstanceIndex} lacks native PVar.");
            _session.Register(native[moby.InstanceIndex], authored);
        }
        GD.Print($"[rac1-metal-detector] {_session.RegisteredCount} native buried sites registered");
    }

    // A preview change rebinds target progress without resetting the native
    // 60Hz carry accumulator or altering the persistent campaign dig history.
    internal void Refresh(RuntimeWorld world)
    {
        _equipped = false;
        _nearest = null;
        Configure(world);
    }

    /// <summary>
    /// Temporary keyboard shortcut until the native gadget wheel is hosted.
    /// M must not silently grant the Metal Detector's native item 27.
    /// </summary>
    internal bool Toggle()
    {
        if (_disposed) return false;
        if (ports.Ordinary())
        {
            ports.Report(ports.OrdinaryUnavailability());
            return true;
        }
        if (_session is null) return false;
        if (!ports.OwnsGadget())
        {
            ports.Report("Metal Detector not owned (native inventory item 27).");
            return true;
        }
        _equipped = !_equipped;
        _heldTicks = 0;
        _nearest = null;
        string status = _equipped
            ? "Metal Detector equipped: hold Circle to scan/dig"
            : "Metal Detector holstered";
        ports.Status(status);
        GD.Print($"[rac1-metal-detector] {status}");
        ports.RefreshHud();
        return true;
    }

    internal void Tick(double delta)
    {
        var player = ports.Player();
        if (_disposed || _session is null || !_equipped || player is null) return;
        if (!ports.OwnsGadget())
        {
            _equipped = false;
            return;
        }
        _carry += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_carry >= 1)
        {
            _carry--;
            bool held = player.Rac1EquippedItemHeld && !ports.Dead();
            _heldTicks = held ? _heldTicks + 1 : 0;
            // Retail target updater uses the Metal Detector's +0x10 tool
            // socket. The player-body probe remains the existing host adapter.
            NVector3 nativePlayer = Rac1LootPresentation.ToEffectNative(player.GlobalPosition);
            _nearest = _session.FindNearest(nativePlayer);
            if (_session.TryExtract(_nearest, held, _heldTicks) is not { } extracted) continue;

            _heldTicks = 0;
            // Retain the native five-value fragments and existing physical
            // bolt presentation. Extraction never directly credits the wallet.
            ports.SpawnBursts(player.GlobalPosition + Vector3.Up * .5f, extracted.FiveBoltBursts);
            string status = $"Metal Detector: unearthed {extracted.AvailableBolts} native bolts (site {extracted.InstanceIndex})";
            ports.Status(status);
            ports.Persist("buried-bolt history update");
            GD.Print($"[rac1-metal-detector] {status}");
        }
    }

    internal string HudText()
    {
        if (_disposed) return string.Empty;
        if (!_equipped)
            return _session is not null && ports.OwnsGadget() ? "   M: equip Metal Detector" : "";
        var player = ports.Player();
        if (_nearest is not { } nearest || player is null)
            return "   Detector: no eligible buried site";

        // Host-only directional substitute until the retail needle is hosted.
        Vector3 target = Rac1LootPresentation.FromEffectNative(nearest.NativePosition);
        Vector3 to = target - player.GlobalPosition;
        to.Y = 0;
        Vector3 forward = ports.Forward();
        string direction = to.LengthSquared() < .0001f ? "beneath you" :
            MathF.Abs(forward.SignedAngleTo(to.Normalized(), Vector3.Up)) < .35f ? "ahead" :
            forward.SignedAngleTo(to.Normalized(), Vector3.Up) < 0 ? "right" : "left";
        return $"   Detector: site {nearest.InstanceIndex} {nearest.Distance:0.0}m {direction}, " +
            $"{nearest.RemainingBolts} bolts";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
