using Godot;
using OBP.RAC1.Gameplay;

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

            if (_rac1HostileNode is not { } hostile || _rac1HostileProbe is null ||
                !IsInstanceValid(hostile.Root) || !hostile.Root.Visible)
                throw new InvalidOperationException("Representative live class-749 hostile is unavailable.");

            Vector3 hostileForward = -hostile.Root.GlobalTransform.Basis.Z;
            hostileForward.Y = 0f;
            if (hostileForward.LengthSquared() <= 1e-5f)
                throw new InvalidOperationException("Representative class-749 has no usable host facing axis.");
            hostileForward = hostileForward.Normalized();
            Rac1SmokePlaceFacing(hostile.Root.GlobalPosition + hostileForward, -hostileForward);
            await Rac1SmokeWaitAsync(
                () => _rac1Nanotech.Probe().Nanotech == 3,
                180,
                "class-749 incoming damage");
            GD.Print("[rac1-smoke] class-749 incoming damage PASS; Nanotech=3/4");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            Vector3 bombPose = hostile.Root.GlobalPosition - hostileForward * 4f;
            Rac1SmokePlaceFacing(bombPose, hostileForward);
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => _rac1HostileProbe is { Health: 0f } && !hostile.Root.Visible,
                180,
                "Bomb Glove projectile impact");
            if (_rac1Weapons.FirstRangedAmmo != 5)
                throw new InvalidOperationException($"Bomb Glove ammo was {_rac1Weapons.FirstRangedAmmo}, expected 5.");
            GD.Print("[rac1-smoke] Bomb Glove projectile impact PASS; ammo=5");

            OnRac1RespawnRequested();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var dead = _rac1Nanotech.Probe();
            if (!dead.IsDead || dead.Nanotech != 0)
                throw new InvalidOperationException("Veldin environmental reset did not reach Nanotech 0/dead.");
            GD.Print("[rac1-smoke] Veldin death/reset PASS; Nanotech=0");

            OnRac1RespawnRequested();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var respawn = _rac1Nanotech.Probe();
            if (respawn.IsDead || respawn.Nanotech != 4 || !_player.Rac1GameplayAlive)
                throw new InvalidOperationException("Veldin respawn did not restore four Nanotech/alive state.");

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
