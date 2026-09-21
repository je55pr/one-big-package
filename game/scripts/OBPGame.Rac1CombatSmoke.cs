using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;

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
            if (_world is not { Game: "rac1", LevelId: 0 } || _player is null)
                throw new InvalidOperationException(
                    "R&C1 combat smoke must begin in authored LEVEL0/Veldin.");

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
            AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.WrenchAttackSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                30,
                "wrench player-avatar sequence 23");
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

            if (CountRac1AuthoredClass749() != 16 || CountRac1PresentedClass749() != 16)
                throw new InvalidOperationException(
                    $"Veldin class-749 census/presentation was {CountRac1AuthoredClass749()}/{CountRac1PresentedClass749()}, expected 16/16.");
            if (_rac1HostileNodes.Count != 1 || _rac1HostileProbes.Count != 1)
                throw new InvalidOperationException(
                    $"Veldin active class-749 runtime count was {_rac1HostileNodes.Count}/{_rac1HostileProbes.Count}, expected witness-only 1/1.");
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
                () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                120,
                "incoming-damage standing presentation");
            await Rac1SmokeWaitAsync(
                () => _rac1Nanotech.Probe().Nanotech < 4,
                180,
                "class-749 incoming damage");
            if (CurrentPlayerAvatarSourceSequence() != Rac1RatchetSequenceSelection.StandingSequenceId ||
                !PlayerAvatarPresentationIsSynchronized())
            {
                throw new InvalidOperationException(
                    "incoming damage invented an unrecovered player-avatar reaction sequence");
            }
            GD.Print($"[rac1-smoke] class-749 incoming damage PASS; Nanotech={_rac1Nanotech.Probe().Nanotech}/4");

            int bombAmmoBeforeFire = _rac1Weapons.FirstRangedAmmo;
            if (bombAmmoBeforeFire < Rac1BombGlove.AmmoCostPerShot)
                throw new InvalidOperationException(
                    "Bomb Glove smoke requires at least one recovered item-10 round.");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            AssertRac1SmokeHudWeapon(
                Rac1HudProjection.BombGlovePresentationKey,
                bombAmmoBeforeFire);
            Vector3 bombPose = hostile.Root.GlobalPosition - hostileForward * 4f;
            Rac1SmokePlaceFacing(bombPose, hostileForward);
            OnRac1PrimaryAttackRequested();
            int bombAmmoAfterFire = bombAmmoBeforeFire - Rac1BombGlove.AmmoCostPerShot;
            await Rac1SmokeWaitAsync(
                () => _rac1Weapons.FirstRangedAmmo == bombAmmoAfterFire,
                60,
                "Bomb Glove fire admission");
            if (CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.WrenchAttackSequenceId)
                throw new InvalidOperationException(
                    "Bomb Glove fire incorrectly reused the wrench player-avatar sequence 23");
            AssertRac1SmokeHudWeapon(
                Rac1HudProjection.BombGlovePresentationKey,
                bombAmmoAfterFire);

            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => !_rac1BombFireRequested,
                60,
                "Bomb Glove cadence rejection");
            if (_rac1Weapons.FirstRangedAmmo != bombAmmoAfterFire)
                throw new InvalidOperationException(
                    $"Bomb Glove cadence spent a second round: {_rac1Weapons.FirstRangedAmmo}, expected {bombAmmoAfterFire}.");

            await Rac1SmokeWaitAsync(
                () => _rac1LastBombContactResolution is { ProjectileCompleted: true },
                180,
                "Bomb Glove class-749 contact");
            var bombContact = _rac1LastBombContactResolution
                ?? throw new InvalidOperationException("Bomb Glove contact result disappeared.");
            if (bombContact.AdmittedContactCount != 1)
                throw new InvalidOperationException(
                    $"Bomb Glove admitted {bombContact.AdmittedContactCount} contacts, expected one retained Veldin class-749 witness.");
            var bombDamage = bombContact.DamageResults[0];
            if (bombDamage.TargetNativeClassId != Rac1Class749Hostile.NativeClassId ||
                bombDamage.NativeDamage != Rac1BombGlove.NativeDamage ||
                bombDamage.NativeDamageFlags != Rac1BombGlove.NativeDamageFlags)
                throw new InvalidOperationException("Bomb Glove contact drifted from the retained native damage envelope.");
            if (!_rac1HostileProbes.TryGetValue(Rac1WitnessHostileInstance, out var postBombProbe) ||
                postBombProbe.Health != 1f ||
                !hostile.Root.Visible)
                throw new InvalidOperationException(
                    "Bomb Glove host contact invented an unrecovered class-749 damage consequence.");
            if (_rac1Projectiles.ContainsKey(bombContact.ProjectileId))
                throw new InvalidOperationException("Bomb Glove contact did not retire the hosted projectile.");
            GD.Print(
                $"[rac1-smoke] Bomb Glove contact PASS; ammo {bombAmmoBeforeFire}->{bombAmmoAfterFire}, " +
                "immediate refire cadence-blocked and class-749 consequence remains unresolved");

            Rac1SmokePlaceFacing(hostile.Root.GlobalPosition + hostileForward, -hostileForward);
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.WrenchAttackSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                30,
                "terminal wrench player-avatar sequence 23");
            await Rac1SmokeWaitAsync(
                () => _rac1HostileProbes.TryGetValue(
                        Rac1WitnessHostileInstance,
                        out var witnessProbe) &&
                    witnessProbe.Health == 0f &&
                    !hostile.Root.Visible,
                120,
                "wrench hostile terminalization");
            GD.Print("[rac1-smoke] wrench hostile terminalization PASS");

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
            if (CurrentPlayerAvatarSourceSequence() is
                Rac1RatchetSequenceSelection.EnvironmentalDeathStartSequenceId or
                Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId)
            {
                throw new InvalidOperationException(
                    "Godot player avatar invented unrecovered environmental-death playback");
            }
            if (!PlayerAvatarPresentationIsSynchronized())
                throw new InvalidOperationException("player-avatar presentation desynchronized at the death boundary");
            GD.Print("[rac1-smoke] natural Veldin death-plane crossing PASS; state=0x77 sequence=11 frame=0 Nanotech=0");

            OnRac1RespawnRequested();
            await Rac1SmokeWaitAsync(
                () => !_rac1Nanotech.Probe().IsDead && _player.IsOnFloor(),
                240,
                "authored Veldin respawn");
            await Rac1SmokeWaitAsync(
                () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.StandingSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                120,
                "respawn standing player-avatar presentation");
            var respawn = _rac1Nanotech.Probe();
            if (respawn.Nanotech != 4 || !_player.Rac1GameplayAlive)
                throw new InvalidOperationException("Veldin respawn did not restore four Nanotech/alive state.");
            if (_player.GlobalPosition.DistanceTo(authoredRespawnPosition) > 0.2f)
                throw new InvalidOperationException(
                    $"Veldin respawn missed authored start: {_player.GlobalPosition} vs {authoredRespawnPosition}.");

            GD.Print("[rac1-smoke] authored Veldin respawn PASS; Nanotech=4");

            await RunRac1Level18Class749GateSmokeAsync();
            await RunRac1HostileLifecycleSmokeAsync(bombAmmoAfterFire);

            GD.Print("[rac1-smoke] PASS: class-749 runtime stays witness-gated while authored presentation survives LEVEL0/LEVEL18/unload-reload");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            GetTree().Quit(3);
        }
    }

    private async Task RunRac1Level18Class749GateSmokeAsync()
    {
        OpenDestinationFromBootstrap("rac1:LEVEL18");
        await Rac1SmokeWaitAsync(
            () => _world is { Game: "rac1", LevelId: 18 } &&
                _player is not null &&
                IsInstanceValid(_player) &&
                _player.IsOnFloor(),
            360,
            "LEVEL18 load and grounding");

        int authored = CountRac1AuthoredClass749();
        int presented = CountRac1PresentedClass749();
        if (authored != 90 || presented != 90)
            throw new InvalidOperationException(
                $"LEVEL18 class-749 census/presentation was {authored}/{presented}, expected 90/90.");
        if (_rac1HostileNodes.Count != 0 || _rac1HostileProbes.Count != 0 ||
            TryGetRac1RepresentativeHostile(out _, out _))
            throw new InvalidOperationException(
                $"LEVEL18 promoted unsupported class-749 runtime behavior: {_rac1HostileNodes.Count}/{_rac1HostileProbes.Count} active.");

        GD.Print(
            "[rac1-smoke] LEVEL18 class-749 gate PASS; 90 authored/presented placements, 0 active runtime hostiles");
    }

    private async Task RunRac1HostileLifecycleSmokeAsync(int expectedBombAmmo)
    {
        OpenDestinationFromBootstrap("rac1:LEVEL1");
        await Rac1SmokeWaitAsync(
            () => _world is { Game: "rac1", LevelId: 1 } &&
                _player is not null &&
                IsInstanceValid(_player) &&
                _player.IsOnFloor(),
            360,
            "LEVEL1 load and grounding");

        if (_rac1HostileNodes.Count != 0 || _rac1HostileProbes.Count != 0 ||
            _rac1Projectiles.Count != 0 || TryGetRac1RepresentativeHostile(out _, out _))
            throw new InvalidOperationException(
                "LEVEL1 retained class-749 or projectile state after LEVEL18 unload.");

        if (_rac1Nanotech.Probe().Nanotech != 4)
            throw new InvalidOperationException(
                $"LEVEL1 local Nanotech did not reset on world load: {_rac1Nanotech.Probe().Nanotech}.");
        GD.Print("[rac1-smoke] LEVEL1 unload PASS; no authored class-749 family and no leaked combat state");

        OpenDestinationFromBootstrap("rac1:LEVEL0");
        await Rac1SmokeWaitAsync(
            () => _world is { Game: "rac1", LevelId: 0 } &&
                _player is not null &&
                IsInstanceValid(_player) &&
                _player.IsOnFloor(),
            360,
            "LEVEL0 reload and grounding");

        if (CountRac1AuthoredClass749() != 16 || CountRac1PresentedClass749() != 16)
            throw new InvalidOperationException(
                $"LEVEL0 reload class-749 census/presentation was {CountRac1AuthoredClass749()}/{CountRac1PresentedClass749()}, expected 16/16.");
        if (_rac1HostileNodes.Count != 1 || _rac1HostileProbes.Count != 1 ||
            !TryGetRac1RepresentativeHostile(out var hostile, out _) ||
            hostile is null || !IsInstanceValid(hostile.Root) || !hostile.Root.Visible)
            throw new InvalidOperationException(
                $"LEVEL0 reload active class-749 runtime count was {_rac1HostileNodes.Count}/{_rac1HostileProbes.Count}, expected witness-only 1/1.");
        if (_rac1Weapons.FirstRangedAmmo != expectedBombAmmo)
            throw new InvalidOperationException(
                $"Process-lifetime Bomb Glove ammo was {_rac1Weapons.FirstRangedAmmo}, expected persistent value {expectedBombAmmo}.");
        AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);

        GD.Print(
            $"[rac1-smoke] LEVEL0 reload PASS; 16 authored/presented placements, witness-only runtime restored, item-10 ammo remained {expectedBombAmmo}");
    }

    private void AssertRac1SmokeHudWeapon(string expectedPresentationKey, int? expectedAmmo)
    {
        var weapon = _hudState.Current.CurrentWeapon
            ?? throw new InvalidOperationException("R&C1 HUD has no current weapon.");
        if (weapon.PresentationKey != expectedPresentationKey)
            throw new InvalidOperationException(
                $"R&C1 HUD weapon was '{weapon.PresentationKey}', expected '{expectedPresentationKey}'.");

        if (expectedAmmo is null)
        {
            if (weapon.Ammo is not null)
                throw new InvalidOperationException("Wrench HUD unexpectedly exposed ammo.");
            return;
        }

        if (weapon.Ammo?.Current != expectedAmmo ||
            weapon.Ammo.Capacity != Rac1BombGlove.MaxAmmo)
            throw new InvalidOperationException(
                $"Bomb Glove HUD ammo was {weapon.Ammo?.Current}/{weapon.Ammo?.Capacity}, " +
                $"expected {expectedAmmo}/{Rac1BombGlove.MaxAmmo}.");
    }

    private int CountRac1AuthoredClass749() =>
        _world?.DynamicObjects?.Count(source =>
            source.NativeClassId == Rac1Class749Hostile.NativeClassId) ?? 0;

    private int CountRac1PresentedClass749() =>
        _rac1Class749PresentationNodes.Count(node =>
            IsInstanceValid(node.Root) && node.Root.Visible);

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
