using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

/// <summary>
/// Lower-level synthetic Godot host-contract gate for the bounded R&C1 Goal 1 loop.
/// This harness may stage host transforms to isolate spatial/contact contracts and
/// therefore must never be cited as ordinary-play or end-to-end playability proof.
/// </summary>
public partial class OBPGame
{
    private async Task RunRac1CombatContractSmokeAsync()
    {
        try
        {
            if (_world is not { Game: "rac1", LevelId: 0 } || _player is null)
                throw new InvalidOperationException(
                    "R&C1 combat smoke must begin in authored LEVEL0/Veldin.");

            GD.Print("[rac1-combat-contract] begin synthetic Veldin combat host contracts");
            if (!TryGetRac1RepresentativeHostile(out var navigationHostile, out _) ||
                navigationHostile is null ||
                !IsInstanceValid(navigationHostile.Root) ||
                !navigationHostile.Root.Visible)
                throw new InvalidOperationException(
                    "Ordinary Veldin launch has no admitted class-749 runtime witness.");

            Vector3 navigationStartPosition = navigationHostile.Root.GlobalPosition;
            Vector3 navigationStartForward = -navigationHostile.Root.GlobalTransform.Basis.Z;
            navigationStartForward.Y = 0f;
            if (navigationStartForward.LengthSquared() <= 1e-6f)
                throw new InvalidOperationException(
                    "Ordinary Veldin class-749 witness has no usable initial facing axis.");
            navigationStartForward = navigationStartForward.Normalized();

            await Rac1SmokeWaitAsync(
                () =>
                {
                    if (!IsInstanceValid(navigationHostile.Root)) return false;
                    Vector3 currentForward = -navigationHostile.Root.GlobalTransform.Basis.Z;
                    currentForward.Y = 0f;
                    if (currentForward.LengthSquared() <= 1e-6f) return false;
                    currentForward = currentForward.Normalized();
                    float turned = Math.Abs(
                        navigationStartForward.SignedAngleTo(currentForward, Vector3.Up));
                    return navigationHostile.Root.GlobalPosition.DistanceTo(navigationStartPosition) > 0.25f &&
                           turned > 0.15f;
                },
                180,
                "ordinary-launch class-749 visible movement and turn");

            Vector3 navigationEndForward = -navigationHostile.Root.GlobalTransform.Basis.Z;
            navigationEndForward.Y = 0f;
            navigationEndForward = navigationEndForward.Normalized();
            float navigationDistance =
                navigationHostile.Root.GlobalPosition.DistanceTo(navigationStartPosition);
            float navigationTurn = Math.Abs(
                navigationStartForward.SignedAngleTo(navigationEndForward, Vector3.Up));
            GD.Print(
                $"[rac1-smoke] ordinary class-749 navigation PASS; moved={navigationDistance:0.###}, " +
                $"turned={Mathf.RadToDeg(navigationTurn):0.##}deg before any smoke teleport");

            await Rac1SmokeWaitAsync(() => _player.IsOnFloor(), 240, "player grounding");
            Vector3 authoredRespawnPosition = _player.GlobalPosition;
            var naturalAttack = await Rac1SmokeProvokeClass749AttackAsync(
                navigationHostile,
                maxFrames: 3000);
            GD.Print(
                $"[rac1-smoke] ordinary-play class-749 attack PASS; " +
                $"Ratchet travel={naturalAttack.PlayerTravel:0.###}, hostile travel={naturalAttack.HostileTravel:0.###}, " +
                $"entry distance={naturalAttack.EntryDistance:0.###}, " +
                $"entry facing={Mathf.RadToDeg((float)naturalAttack.EntryFacing):0.##}deg, " +
                $"marker={naturalAttack.Attack.NativeMarker:0}, Nanotech={_rac1Nanotech.Probe().Nanotech}/4");

            await RunRac1NaturalVeldinFallRespawnSmokeAsync(authoredRespawnPosition);

            var crate = _rac1CrateNodes
                .Where(node => IsInstanceValid(node.Root) && node.Root.Visible)
                .OrderBy(node => node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No admitted live class-500 crate is available.");

            Vector3 crateDirection = crate.Root.GlobalPosition - _player.GlobalPosition;
            crateDirection.Y = 0f;
            if (crateDirection.LengthSquared() <= 1e-5f) crateDirection = Vector3.Forward;
            crateDirection = crateDirection.Normalized();
            Vector3 crateSide = new(-crateDirection.Z, 0f, crateDirection.X);
            Vector3 crateApproachPose =
                crate.Root.GlobalPosition -
                crateDirection * 4.1f +
                crateSide * 0.55f;
            Rac1SmokePlaceGroundedFacing(crateApproachPose, crateDirection);
            await Rac1SmokeWaitAsync(
                () => _player.IsOnFloor(),
                60,
                "crate approach grounding");
            if (Rac1SmokeCurrentWrenchPolicyAdmits(crate.Root.GlobalPosition))
                throw new InvalidOperationException(
                    "Crate approach seed unexpectedly began inside the host wrench contact policy.");

            float crateApproachTravel = await Rac1SmokeApproachWrenchTargetAsync(
                () => crate.Root.GlobalPosition,
                maxFrames: 180,
                "crate");
            GD.Print(
                $"[rac1-smoke] ordinary-play crate approach PASS; travel={crateApproachTravel:0.000}");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);
            OnRac1PrimaryAttackRequested();
            await WaitForPlayerAvatarSequenceAsync(
                Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
                120,
                "wrench player-avatar sequence 23");
            AssertRac1SmokeWrenchPresentation(visible: true, "first wrench sequence 23");
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
            await Rac1SmokeWaitAsync(
                () => CurrentPlayerAvatarSourceSequence() != Rac1RatchetSequenceSelection.WrenchAttackSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                120,
                "post-wrench player-avatar recovery");

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

            int bombAmmoBeforeFire = _rac1Weapons.FirstRangedAmmo;
            if (bombAmmoBeforeFire < Rac1BombGlove.AmmoCostPerShot)
                throw new InvalidOperationException(
                    "Bomb Glove smoke requires at least one recovered item-10 round.");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            AssertRac1SmokeHudWeapon(
                Rac1HudProjection.BombGlovePresentationKey,
                bombAmmoBeforeFire);
            AssertRac1SmokeWrenchPresentation(visible: false, "Bomb Glove equip");
            Vector3 bombForward = -hostile.Root.GlobalTransform.Basis.Z;
            bombForward.Y = 0f;
            if (bombForward.LengthSquared() <= 1e-5f)
                throw new InvalidOperationException(
                    "Moving class-749 witness lost its usable facing before Bomb Glove smoke.");
            bombForward = bombForward.Normalized();
            Vector3 bombPose = hostile.Root.GlobalPosition - bombForward * 2f;
            Rac1SmokePlaceFacing(bombPose, bombForward);
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

            Vector3 terminalForward = -hostile.Root.GlobalTransform.Basis.Z;
            terminalForward.Y = 0f;
            if (terminalForward.LengthSquared() <= 1e-5f)
                throw new InvalidOperationException(
                    "Moving class-749 witness lost its usable facing before terminal wrench smoke.");
            terminalForward = terminalForward.Normalized();

            // The authored hostile location is not a proven walkable wrench approach
            // corridor. Ordinary navigation was already asserted above before any
            // smoke teleport, so stage the same supported witness on the crate corridor
            // that just proved ordinary Ratchet movement. Re-pin only during this
            // synthetic contact regression; production navigation remains untouched.
            Vector3 stagedHostilePosition = crate.Root.GlobalPosition;
            hostile.Root.GlobalPosition = stagedHostilePosition;
            Rac1SmokePlaceGroundedFacing(crateApproachPose, crateDirection);
            await Rac1SmokeWaitAsync(
                () => _player.IsOnFloor(),
                60,
                "staged hostile approach grounding");
            hostile.Root.GlobalPosition = stagedHostilePosition;
            if (Rac1SmokeCurrentWrenchPolicyAdmits(stagedHostilePosition))
                throw new InvalidOperationException(
                    "Staged hostile approach seed unexpectedly began inside the host wrench contact policy.");

            float hostileApproachTravel = await Rac1SmokeApproachWrenchTargetAsync(
                () =>
                {
                    hostile.Root.GlobalPosition = stagedHostilePosition;
                    return stagedHostilePosition;
                },
                maxFrames: 180,
                "staged class-749 hostile");
            GD.Print(
                $"[rac1-combat-contract] synthetic staged-hostile approach PASS; travel={hostileApproachTravel:0.000}");

            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);
            OnRac1PrimaryAttackRequested();
            await Rac1SmokeWaitAsync(
                () => CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.WrenchAttackSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                30,
                "terminal wrench player-avatar sequence 23");
            AssertRac1SmokeWrenchPresentation(visible: true, "terminal wrench sequence 23");
            await Rac1SmokeWaitAsync(
                () => _rac1HostileProbes.TryGetValue(
                        Rac1WitnessHostileInstance,
                        out var witnessProbe) &&
                    witnessProbe.Health == 0f &&
                    !hostile.Root.Visible,
                120,
                "wrench hostile terminalization");
            GD.Print("[rac1-smoke] wrench hostile terminalization PASS");

            await RunRac1Level18Class749GateSmokeAsync();
            await RunRac1HostileLifecycleSmokeAsync(bombAmmoAfterFire);

            GD.Print("[rac1-combat-contract] PASS: synthetic host contracts and witness gating survived LEVEL0/LEVEL18/unload-reload");
            ApplicationLifecycle.RequestQuit(this, "rac1-combat-contract-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[rac1-combat-contract] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-combat-contract-fail", 3);
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

    private void AssertRac1SmokeWrenchPresentation(bool visible, string phase)
    {
        if (_rac1WrenchView is null || !IsInstanceValid(_rac1WrenchView))
            throw new InvalidOperationException(
                $"R&C1 Wrench presentation disappeared during {phase}.");
        if (_rac1WrenchView.Visible != visible)
            throw new InvalidOperationException(
                $"R&C1 Wrench visibility during {phase} was {_rac1WrenchView.Visible}, expected {visible}.");
        if (_playerAvatarView is null || _rac1WrenchView.GetParent() != _playerAvatarView)
            throw new InvalidOperationException(
                $"R&C1 Wrench was not parented to the animated Ratchet view during {phase}.");
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

    private async Task<(
        float PlayerTravel,
        float HostileTravel,
        double EntryDistance,
        double EntryFacing,
        Rac1Class749AttackEvent Attack)> Rac1SmokeProvokeClass749AttackAsync(
        RuntimeWorldScene.DynamicObjectNode hostile,
        int maxFrames)
    {
        if (_player is null ||
            _world is not { Game: "rac1", LevelId: Rac1Class749Hostile.RetainedRuntimeWitnessLevelId } ||
            !Rac1Class749Hostile.IsRetainedRuntimeWitness(_world.LevelId, hostile.Source))
            throw new InvalidOperationException(
                "Natural class-749 attack smoke requires the retained Veldin runtime witness.");

        var nanotechBefore = _rac1Nanotech.Probe();
        if (nanotechBefore.IsDead || nanotechBefore.Nanotech != 4)
            throw new InvalidOperationException(
                $"Natural class-749 attack smoke requires 4 live Nanotech, found {nanotechBefore.Nanotech}.");

        Vector3 playerStart = _player.GlobalPosition;
        Vector3 hostileStart = hostile.Root.GlobalPosition;
        double? entryDistance = null;
        double? entryFacing = null;
        bool sawPursuit = false;

        try
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (!IsInstanceValid(hostile.Root) || !hostile.Root.Visible ||
                    !_rac1HostileProbes.TryGetValue(hostile.Source.InstanceIndex, out var probe))
                    throw new InvalidOperationException(
                        "Retained class-749 witness disappeared during ordinary-play approach.");

                Vector3 toPlayer = _player.GlobalPosition - hostile.Root.GlobalPosition;
                double distance = toPlayer.Length();
                double facing = Rac1SmokeClass749FacingError(hostile, _player.GlobalPosition);

                sawPursuit |= probe.NativeState == Rac1Class749Hostile.TargetedNativeState;
                if (probe.NativeState == Rac1Class749Hostile.AttackNativeState &&
                    entryDistance is null)
                {
                    entryDistance = distance;
                    entryFacing = facing;
                    if (!(distance < Rac1Class749Hostile.AttackDistanceExclusive) ||
                        !(facing < Rac1Class749Hostile.AttackFacingErrorExclusive))
                        throw new InvalidOperationException(
                            $"Class-749 entered attack outside recovered gates: distance={distance:R}, facing={facing:R}.");
                }

                if (probe.Attack is { } attack)
                {
                    var nanotechAfter = _rac1Nanotech.Probe();
                    if (!sawPursuit || entryDistance is null || entryFacing is null)
                        throw new InvalidOperationException(
                            "Class-749 reached its attack marker without observed pursuit/attack entry.");
                    if (attack.NativeMarker != Rac1Class749Hostile.AttackMarker ||
                        attack.NativeDamage != Rac1Class749Hostile.AttackDamage)
                        throw new InvalidOperationException(
                            $"Class-749 emitted marker/damage {attack.NativeMarker:R}/{attack.NativeDamage:R}, " +
                            $"expected {Rac1Class749Hostile.AttackMarker:R}/{Rac1Class749Hostile.AttackDamage:R}.");
                    if (nanotechAfter.Nanotech != nanotechBefore.Nanotech - 1 || nanotechAfter.IsDead)
                        throw new InvalidOperationException(
                            $"Class-749 ordinary attack changed Nanotech {nanotechBefore.Nanotech}->{nanotechAfter.Nanotech}, expected exactly one.");

                    Vector3 playerTravel = _player.GlobalPosition - playerStart;
                    playerTravel.Y = 0f;
                    Vector3 hostileTravel = hostile.Root.GlobalPosition - hostileStart;
                    hostileTravel.Y = 0f;
                    if (playerTravel.Length() < 0.5f)
                        throw new InvalidOperationException(
                            "Natural class-749 attack did not require meaningful ordinary Ratchet movement.");
                    if (hostileTravel.Length() < 0.25f)
                        throw new InvalidOperationException(
                            "Natural class-749 attack did not visibly exercise hostile pursuit.");

                    return (
                        playerTravel.Length(),
                        hostileTravel.Length(),
                        entryDistance.Value,
                        entryFacing.Value,
                        attack);
                }

                if (_rac1Nanotech.Probe().Nanotech != nanotechBefore.Nanotech)
                    throw new InvalidOperationException(
                        "Nanotech changed before the smoke observed the recovered class-749 attack marker.");

                if (probe.NativeState == Rac1Class749Hostile.AttackNativeState &&
                    distance <= 1.25d)
                {
                    ClearMovementSmokeInput();
                }
                else
                {
                    Rac1SmokeDriveToward(hostile.Root.GlobalPosition);
                }

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        finally
        {
            ClearMovementSmokeInput();
        }

        var finalProbe = _rac1HostileProbes.TryGetValue(hostile.Source.InstanceIndex, out var current)
            ? current
            : null;
        throw new TimeoutException(
            $"Timed out provoking retained class-749 through ordinary play: " +
            $"player={_player.GlobalPosition}, hostile={hostile.Root.GlobalPosition}, " +
            $"state={finalProbe?.NativeState}, Nanotech={_rac1Nanotech.Probe().Nanotech}.");
    }

    private static double Rac1SmokeClass749FacingError(
        RuntimeWorldScene.DynamicObjectNode hostile,
        Vector3 target)
    {
        Vector3 toTarget = target - hostile.Root.GlobalPosition;
        toTarget.Y = 0f;
        Vector3 forward = -hostile.Root.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (toTarget.LengthSquared() <= 1e-6f || forward.LengthSquared() <= 1e-6f)
            return Math.PI;

        return Math.Abs(
            forward.Normalized().SignedAngleTo(toTarget.Normalized(), Vector3.Up));
    }

    private void Rac1SmokeDriveToward(Vector3 target)
    {
        if (_player is null)
            throw new InvalidOperationException("RAC1 smoke player disappeared.");

        Vector3 desired = target - _player.GlobalPosition;
        desired.Y = 0f;
        if (desired.LengthSquared() <= 1e-5f)
        {
            ClearMovementSmokeInput();
            return;
        }
        desired = desired.Normalized();

        double controlYaw = _player.Rac1ControlYaw;
        Vector3 controlForward = new(
            -(float)Math.Cos(controlYaw),
            0f,
            (float)Math.Sin(controlYaw));
        Vector3 controlRight = new(
            -(float)Math.Sin(controlYaw),
            0f,
            -(float)Math.Cos(controlYaw));
        SetAnalogueSmokeInput(
            Math.Clamp(desired.Dot(controlRight), -1f, 1f),
            Math.Clamp(desired.Dot(controlForward), -1f, 1f));
    }

    private async Task<float> Rac1SmokeApproachWrenchTargetAsync(
        Func<Vector3> targetCenterProvider,
        int maxFrames,
        string label)
    {
        if (_player is null)
            throw new InvalidOperationException("RAC1 smoke player disappeared.");

        Vector3 start = _player.GlobalPosition;
        Vector3 targetCenter = targetCenterProvider();
        try
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                targetCenter = targetCenterProvider();
                if (Rac1SmokeCurrentWrenchPolicyAdmits(targetCenter))
                {
                    Vector3 travelled = _player.GlobalPosition - start;
                    travelled.Y = 0f;
                    float distance = travelled.Length();
                    if (distance < 0.5f)
                        throw new InvalidOperationException(
                            $"{label} entered wrench policy after only {distance:0.000} host units; " +
                            "normal-play approach coverage requires meaningful movement.");
                    return distance;
                }

                Vector3 desired = targetCenter - _player.GlobalPosition;
                desired.Y = 0f;
                if (desired.LengthSquared() <= 1e-5f)
                    throw new InvalidOperationException(
                        $"{label} reached the target origin without entering the wrench host policy.");
                desired = desired.Normalized();

                double controlYaw = _player.Rac1ControlYaw;
                Vector3 controlForward = new(
                    -(float)Math.Cos(controlYaw),
                    0f,
                    (float)Math.Sin(controlYaw));
                Vector3 controlRight = new(
                    -(float)Math.Sin(controlYaw),
                    0f,
                    -(float)Math.Cos(controlYaw));
                SetAnalogueSmokeInput(
                    Math.Clamp(desired.Dot(controlRight), -1f, 1f),
                    Math.Clamp(desired.Dot(controlForward), -1f, 1f));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        finally
        {
            ClearMovementSmokeInput();
        }

        throw new TimeoutException(
            $"Timed out driving ordinary movement into wrench host policy for {label}: " +
            $"start={start}, current={_player?.GlobalPosition}, target={targetCenter}, " +
            $"yaw={_player?.Rac1CurrentYaw:R}, control={_player?.Rac1ControlYaw:R}.");
    }

    private bool Rac1SmokeCurrentWrenchPolicyAdmits(Vector3 targetCenter)
    {
        if (_player is null) return false;

        var facing = _rac1Wrench.ResolveFirstSwingFacing(_player.Rac1CurrentYaw);
        Vector3 forward = new(-(float)facing.X, 0f, (float)facing.Y);
        if (forward.LengthSquared() <= 1e-5f) return false;
        forward = forward.Normalized();
        Vector3 root =
            _player.GlobalPosition +
            Vector3.Up * Rac1WrenchHostRootHeight;
        return Rac1WrenchHostPolicyAdmits(root, forward, targetCenter);
    }

    private void Rac1SmokePlaceGroundedFacing(
        Vector3 position,
        Vector3 sceneDirection)
    {
        if (_player is null)
            throw new InvalidOperationException("RAC1 smoke player disappeared.");

        Rac1SmokePlaceFacing(position, sceneDirection);
        var query = PhysicsRayQueryParameters3D.Create(
            position + Vector3.Up * 8f,
            position + Vector3.Down * 128f);
        query.Exclude =
            new global::Godot.Collections.Array<Rid> { _player.GetRid() };
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            throw new InvalidOperationException(
                $"No imported collision lies below wrench approach seed {position}.");

        Vector3 normal = (Vector3)hit["normal"];
        if (normal.Y < 0.5f)
            throw new InvalidOperationException(
                $"Wrench approach seed {position} hit non-walkable normal {normal}.");

        _player.GlobalPosition = (Vector3)hit["position"] + Vector3.Up * 0.08f;
        _player.Velocity = Vector3.Zero;
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
