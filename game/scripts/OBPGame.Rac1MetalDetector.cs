using Godot;
using OBP.Core;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1MetalDetectorSession? _rac1MetalDetector;
    private bool _rac1DetectorEquipped;
    private int _rac1DetectorHeldTicks;
    private double _rac1DetectorCarry;
    private Rac1MetalDetectorCandidate? _rac1DetectorNearest;

    private void ResetRac1MetalDetector()
    {
        _rac1MetalDetector = null;
        _rac1DetectorEquipped = false;
        _rac1DetectorHeldTicks = 0;
        _rac1DetectorCarry = 0;
        _rac1DetectorNearest = null;
    }

    private void ConfigureRac1MetalDetector(RuntimeWorld world)
    {
        if (world.LevelId == 0) return;
        var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1)
            ?? throw new InvalidOperationException("Metal Detector targets require the retail source.");
        using var reader = new FileRandomAccessReader(source.Path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(entry => entry.LevelId == world.LevelId);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var native = Rac1Instances.Parse(gameplay).MobyInstances.ToDictionary(moby => moby.Index);
        // Testing equipment may spawn bolts but must never alter genuine
        // campaign dig-history counters. Its copy is discarded on exit.
        var progress = _persistentPlayer.WeaponPreview is null
            ? _rac1Campaign.Session.BuriedBolts
            : Rac1BuriedBoltProgress.Restore(_rac1Campaign.Session.BuriedBolts.Snapshot());
        _rac1MetalDetector = new(Rac1Paths.Read(gameplay), Rac1ClipVolumes.Read(gameplay),
            progress, world.LevelId);
        foreach (var moby in (world.DynamicObjects ?? [])
            .Where(item => item.NativeClassId == Rac1MetalDetectorSession.TargetClassId))
        {
            var authored = Rac1CommonMobyAuthored.ReadMetalDetectorTarget(moby)
                ?? throw new InvalidDataException($"Metal Detector target {moby.InstanceIndex} lacks native PVar.");
            _rac1MetalDetector.Register(native[moby.InstanceIndex], authored);
        }
        GD.Print($"[rac1-metal-detector] {_rac1MetalDetector.RegisteredCount} native buried sites registered");
    }

    /// <summary>
    /// Temporary keyboard equipment shortcut until the R&C1 gadget wheel is
    /// hosted. M only equips a gadget genuinely owned by the native inventory.
    /// It does not enable the Metal Detector by silently granting item 27.
    /// </summary>
    private bool TryToggleRac1MetalDetector()
    {
        if (OrdinaryPlayerSession)
        {
            var entry = ReadPlayerEquipment().Single(entry => entry.Definition.Id == "rac1/item/27");
            MovementReproduction.Report(entry.Reason ?? "The common Metal Detector action is unavailable.");
            return true;
        }
        if (_rac1MetalDetector is null) return false;
        if (!_rac1Weapons.OwnsNativeItem(Rac1MetalDetectorSession.NativeGadgetItem))
        {
            MovementReproduction.Report("Metal Detector not owned (native inventory item 27).");
            return true;
        }
        _rac1DetectorEquipped = !_rac1DetectorEquipped;
        _rac1DetectorHeldTicks = 0;
        _rac1DetectorNearest = null;
        Rac1World.Context.CombatStatus = _rac1DetectorEquipped
            ? "Metal Detector equipped: hold Circle to scan/dig"
            : "Metal Detector holstered";
        GD.Print($"[rac1-metal-detector] {Rac1World.Context.CombatStatus}");
        RefreshRac1HudState();
        return true;
    }

    private void TickRac1MetalDetector(double delta)
    {
        if (_rac1MetalDetector is null || !_rac1DetectorEquipped || _player is null) return;
        if (!_rac1Weapons.OwnsNativeItem(Rac1MetalDetectorSession.NativeGadgetItem))
        {
            _rac1DetectorEquipped = false;
            return;
        }
        _rac1DetectorCarry += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_rac1DetectorCarry >= 1)
        {
            _rac1DetectorCarry--;
            bool held = _player.Rac1EquippedItemHeld && !_rac1Nanotech.Probe().IsDead;
            _rac1DetectorHeldTicks = held ? _rac1DetectorHeldTicks + 1 : 0;
            // The retail target updater uses the Metal Detector's +0x10
            // tool-socket position. The player-body probe is an explicit
            // host adapter until the gadget's original socket is imported.
            NVector3 nativePlayer = ToEffectNative(_player.GlobalPosition);
            _rac1DetectorNearest = _rac1MetalDetector.FindNearest(nativePlayer);
            if (_rac1MetalDetector.TryExtract(_rac1DetectorNearest, held, _rac1DetectorHeldTicks)
                is not { } extracted) continue;

            _rac1DetectorHeldTicks = 0;
            // Source emits 5-value bolt fragments via class-0x13 machinery
            // before they can be collected. Use the existing physical-bolt
            // host rather than immediately crediting the wallet.
            SpawnRac1BoltPickups(_player.GlobalPosition + Vector3.Up * .5f,
                Enumerable.Range(0, extracted.FiveBoltBursts)
                    .SelectMany(_ => Rac1World.Context.BoltPickups.Emit(Rac1MetalDetectorSession.BoltsPerBurst)).ToArray());
            Rac1World.Context.CombatStatus =
                $"Metal Detector: unearthed {extracted.AvailableBolts} native bolts (site {extracted.InstanceIndex})";
            PersistRac1CampaignState("buried-bolt history update");
            GD.Print($"[rac1-metal-detector] {Rac1World.Context.CombatStatus}");
        }
    }

    private string MetalDetectorHudText()
    {
        if (!_rac1DetectorEquipped)
            return _rac1MetalDetector is not null &&
                _rac1Weapons.OwnsNativeItem(Rac1MetalDetectorSession.NativeGadgetItem)
                ? "   M: equip Metal Detector" : "";
        if (_rac1DetectorNearest is not { } nearest || _player is null)
            return "   Detector: no eligible buried site";

        // Host-only directional substitute until the original handheld
        // detector mesh, pointer bone and native needle limits are hosted.
        Vector3 target = FromEffectNative(nearest.NativePosition);
        Vector3 to = target - _player.GlobalPosition;
        to.Y = 0;
        Vector3 forward = EquipmentForward;
        string direction = to.LengthSquared() < .0001f ? "beneath you" :
            MathF.Abs(forward.SignedAngleTo(to.Normalized(), Vector3.Up)) < .35f ? "ahead" :
            forward.SignedAngleTo(to.Normalized(), Vector3.Up) < 0 ? "right" : "left";
        return $"   Detector: site {nearest.InstanceIndex} {nearest.Distance:0.0}m {direction}, " +
            $"{nearest.RemainingBolts} bolts";
    }
}
