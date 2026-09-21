using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OneBigPackage;

/// <summary>
/// Deterministic Godot-host integration gate for the bounded R&C1 Goal 1 loop.
/// It moves the debug host between admitted retail targets, but all gameplay
/// transitions remain owned by OBP.RAC1 sessions already used by manual play.
/// </summary>
public partial class OBPGame
{
    private async Task RunRac1CombatSmokeAsync()
    {
        try
        {
            if (_world?.Game != "rac1" || _player is null)
                throw new InvalidOperationException("R&C1 combat smoke requires a live RAC1 player world.");

            GD.Print("[rac1-smoke] begin live Veldin combat loop");
            await Rac1SmokeWaitAsync(() => _player.IsOnFloor(), 240, "player grounding");
            Vector3 authoredRespawnPosition = _player.GlobalPosition;

            var crate = _rac1CrateNodes
                .Where(node => IsInstanceValid(node.Root) && node.Root.Visible)
                .OrderBy(node => node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No admitted live class-500 crate is available.");

            Vector3 crateDirection = crate.Root.GlobalPosition - _player.GlobalPosition;
            crateDirection.Y = 0f;
            if (crateDirection.LengthSquared() <= 1e-5f) crateDirection = Vector3.Forward;
            crateDirection = crateDirection.Normalized();
            Vector3 cratePose = crate.Root.GlobalPosition - crateDirection * 2.0f;
            Rac1SmokePlaceFacing(cratePose, crateDirection);
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => _rac1BoltCrates.DestroyedCrateCount > 0,
                120,
                "wrench crate break");
            GD.Print($"[rac1-smoke] wrench crate break PASS; pickups={_rac1PickupNodes.Count}");

            var pickup = _rac1PickupNodes.FirstOrDefault(pair => IsInstanceValid(pair.Value));
            if (pickup.Value is null)
                throw new InvalidOperationException("Crate break emitted no hosted bolt pickup.");
            _player.GlobalPosition = pickup.Value.GlobalPosition;
            _player.Velocity = Vector3.Zero;
            await Rac1SmokeWaitAsync(
                () => _rac1BoltCrates.CollectedBolts > 0,
                60,
                "bolt collection");
            GD.Print($"[rac1-smoke] bolt collection PASS; total={_rac1BoltCrates.CollectedBolts}");

            if (_world.LevelId == 0 && _rac1HostileNodes.Count != 16)
                throw new InvalidOperationException(
                    $"Veldin class-749 family count was {_rac1HostileNodes.Count}, expected 16.");
            if (!_rac1HostileNodes.TryGetValue(Rac1WitnessHostileInstance, out var hostile) ||
                !_rac1HostileProbes.ContainsKey(Rac1WitnessHostileInstance) ||
                !IsInstanceValid(hostile.Root) ||
                !hostile.Root.Visible)
                throw new InvalidOperationException("Class-749 witness instance 149 is unavailable.");

            Vector3 hostileForward = -hostile.Root.GlobalTransform.Basis.Z;
            hostileForward.Y = 0f;
            if (hostileForward.LengthSquared() <= 1e-5f)
                throw new InvalidOperationException("Representative class-749 has no usable host facing axis.");
            hostileForward = hostileForward.Normalized();
            Rac1SmokePlaceFacing(hostile.Root.GlobalPosition + hostileForward, -hostileForward);
            await Rac1SmokeWaitAsync(
                () => _rac1Nanotech.Probe().Nanotech < 4,
                180,
                "class-749 incoming damage");
            GD.Print($"[rac1-smoke] class-749 incoming damage PASS; Nanotech={_rac1Nanotech.Probe().Nanotech}/4");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            Vector3 bombPose = hostile.Root.GlobalPosition - hostileForward * 4f;
            Rac1SmokePlaceFacing(bombPose, hostileForward);
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => _rac1HostileProbes.TryGetValue(
                        Rac1WitnessHostileInstance,
                        out var witnessProbe) &&
                    witnessProbe.Health == 0f &&
                    !hostile.Root.Visible,
                180,
                "Bomb Glove projectile impact");
            if (_rac1Weapons.FirstRangedAmmo != 5)
                throw new InvalidOperationException($"Bomb Glove ammo was {_rac1Weapons.FirstRangedAmmo}, expected 5.");
            GD.Print("[rac1-smoke] Bomb Glove projectile impact PASS; ammo=5");

            var environment = _world.Environment
                ?? throw new InvalidOperationException("Veldin smoke requires the imported environment.");
            if (_world.LevelId != 0 || Math.Abs(environment.DeathHeight - 27f) > 0.0001f)
                throw new InvalidOperationException(
                    $"Veldin smoke death height drifted: level={_world.LevelId}, height={environment.DeathHeight:R}.");

            Vector3 fallStart = _player.GlobalPosition;
            fallStart.Y = environment.DeathHeight + 0.5f;
            _player.GlobalPosition = fallStart;
            _player.Velocity = Vector3.Zero;
            _rac1NativePlayerState20A4 = 0;
            double fallSeparation = MeasureRac1ContactSeparation();
            if (!(fallSeparation > Rac1RatchetNanotechSession.RetailVeldinDeathContactSeparationExclusive))
                throw new InvalidOperationException(
                    $"Natural fall setup contact separation was {fallSeparation:R}, expected >2.");

            await Rac1SmokeWaitAsync(
                () => _rac1Nanotech.Probe().IsDead,
                180,
                "natural Veldin death-plane crossing");
            var dead = _rac1Nanotech.Probe();
            if (dead.Nanotech != 0 ||
                dead.NativePlayerState != Rac1RatchetNanotechSession.RetailVeldinDeathNativeState ||
                dead.NativeSequence != Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequence ||
                dead.NativeSequenceFrame != Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequenceFrame)
                throw new InvalidOperationException("Veldin death-plane crossing did not enter state 0x77 / sequence 11 frame 0.");
            GD.Print("[rac1-smoke] natural Veldin death-plane crossing PASS; state=0x77 sequence=11 frame=0 Nanotech=0");

            OnRac1RespawnRequested();
            await Rac1SmokeWaitAsync(
                () => !_rac1Nanotech.Probe().IsDead && _player.IsOnFloor(),
                240,
                "authored Veldin respawn");
            var respawn = _rac1Nanotech.Probe();
            if (respawn.Nanotech != 4 || !_player.Rac1GameplayAlive)
                throw new InvalidOperationException("Veldin respawn did not restore four Nanotech/alive state.");
            if (_player.GlobalPosition.DistanceTo(authoredRespawnPosition) > 0.2f)
                throw new InvalidOperationException(
                    $"Veldin respawn missed authored start: {_player.GlobalPosition} vs {authoredRespawnPosition}.");

            GD.Print("[rac1-smoke] authored Veldin respawn PASS; Nanotech=4");
            GD.Print("[rac1-smoke] PASS: live combat loop complete; Nanotech=4, ammo=5");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            GetTree().Quit(3);
        }
    }

    private async Task Rac1SmokeWaitAsync(Func<bool> condition, int maxFrames, string label)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (condition()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        throw new TimeoutException($"Timed out waiting for RAC1 smoke phase: {label}.");
    }

    private void Rac1SmokePlaceFacing(Vector3 position, Vector3 sceneDirection)
    {
        if (_player is null) throw new InvalidOperationException("RAC1 smoke player disappeared.");
        sceneDirection.Y = 0f;
        if (sceneDirection.LengthSquared() <= 1e-5f)
            throw new ArgumentException("RAC1 smoke facing direction must be planar and non-zero.");

        sceneDirection = sceneDirection.Normalized();
        _player.GlobalPosition = position;
        _player.Velocity = Vector3.Zero;
        _player.Rac1CurrentYaw = Math.Atan2(sceneDirection.Z, -sceneDirection.X);
    }
}
