using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

/// <summary>Synthetic combat host contract; not an ordinary-play or retail-playthrough witness.</summary>
public sealed class Rac1CombatContractScenario(MovementDiagnosticContext context, Rac1CombatScenarioPorts ports) : Rac1CombatScenarioSupport(context, ports)
{
    public async Task RunAsync()
    {
        try
        {
            if (World.Read() is not { Source: ObpSourceGame.Rac1, LevelId: 0 } || Player is null)
                throw new InvalidOperationException(
                    "R&C1 combat smoke must begin in authored LEVEL0/Veldin.");

            Log("[rac1-combat-contract] begin synthetic Veldin combat host contracts");
            using var frameRate = World.LimitFrameRate(60);
            if (Hostiles.Count != Rac1Class749VeldinPopulation.AuthoredPlacementCount ||
                Combat.ReadPopulation().RegisteredClass749 != Rac1Class749VeldinPopulation.AuthoredPlacementCount)
                throw new InvalidOperationException(
                    $"Ordinary Veldin launch registered {Hostiles.Count}/{Combat.ReadPopulation().RegisteredClass749} " +
                    $"class-749 placements, expected {Rac1Class749VeldinPopulation.AuthoredPlacementCount}/" +
                    $"{Rac1Class749VeldinPopulation.AuthoredPlacementCount}.");

            // Capture the stationary baseline after the host has corrected
            // authored below-floor placements. Keep the full XYZ idle check;
            // initial grounding is covered independently by the enemy smoke.
            await Rac1SmokeWaitAsync(
                () => ReadEnemies().All(enemy => !enemy.Read().NeedsGroundPlacement),
                60,
                "initial enemy ground placement before idle baseline");
            var launchPositions = Hostiles.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Read().Position);
            for (int frame = 0; frame < 30; frame++)
                await ProcessFrameAsync();

            foreach (var pair in Hostiles)
            {
                if (pair.Value.Read().Position.DistanceTo(launchPositions[pair.Key]) > 0.02f)
                    throw new InvalidOperationException(
                        $"Class-749 i{pair.Key} moved from the authored Veldin start before polygon admission.");
                var probe = Enemies.ReadToad(pair.Value.Key);
                if (probe.NativeState is Rac1Class749Hostile.TargetedNativeState or Rac1Class749Hostile.AttackNativeState)
                    throw new InvalidOperationException(
                        $"Class-749 i{pair.Key} pursued from the authored Veldin start in state {probe.NativeState}.");
            }

            if (!TryGetRac1Hostile(143, out var navigationHostile, out _) ||
                navigationHostile is null ||
                !Combat.IsAvailable(navigationHostile.Key) ||
                !navigationHostile.Read().Visible)
                throw new InvalidOperationException(
                    "Recovered Veldin class-749 instance 143 is unavailable for polygon-admission smoke.");

            Log(
                "[rac1-smoke] class-749 launch admission PASS; all 16 registered and none pursued at authored start");

            await Rac1SmokeWaitAsync(() => Player.Read().Grounded, 240, "player grounding");
            Vector3 authoredRespawnPosition = Player.Read().GlobalPosition;

            // Keep the natural fall/death witness ahead of polygon admission. Once
            // Ratchet enters group 0, both authored i143/i144 are legitimately live
            // and can attack during later synthetic phases.
            await new Rac1EnvironmentalDeathRegression(Context, Ports.Environment).RunAsync(authoredRespawnPosition);

            var naturalAttack = await Rac1SmokeProvokeClass749AttackAsync(
                navigationHostile,
                maxFrames: 3000);
            Log(
                $"[rac1-smoke] ordinary-play class-749 attack PASS; " +
                $"Ratchet travel={naturalAttack.PlayerTravel:0.###}, hostile travel={naturalAttack.HostileTravel:0.###}, " +
                $"entry distance={naturalAttack.EntryDistance:0.###}, " +
                $"entry facing={Rac1CombatAssertions.RadToDeg((float)naturalAttack.EntryFacing):0.##}deg, " +
                $"marker={naturalAttack.Attack.NativeMarker:0}, Nanotech={lifecycle.ReadNanotech().Nanotech}/4");

            if (CountRac1AuthoredClass749() != 16 || CountRac1PresentedClass749() != 16)
                throw new InvalidOperationException(
                    $"Veldin class-749 census/presentation was {CountRac1AuthoredClass749()}/{CountRac1PresentedClass749()}, expected 16/16.");
            if (Hostiles.Count != 16 || Combat.ReadPopulation().RegisteredClass749 != 16)
                throw new InvalidOperationException(
                    $"Veldin active class-749 runtime count was {Hostiles.Count}/{Combat.ReadPopulation().RegisteredClass749}, expected recovered population 16/16.");
            var hostile = Hostiles[naturalAttack.AttackerInstance];
            if (!Hostiles.ContainsKey(hostile.Key.Target.InstanceIndex) ||
                !Combat.IsAvailable(hostile.Key) ||
                !hostile.Read().Visible)
                throw new InvalidOperationException(
                    $"Recovered class-749 instance {hostile.Key.Target.InstanceIndex} is unavailable.");

            // From here onward this is the explicitly synthetic contract harness.
            // Quarantine the recovered population while isolating crate/player-avatar
            // presentation; later re-enable only the selected placement and stage it
            // outside its activation polygon for projectile/wrench contact checks.
            foreach (var enemyNode in ReadEnemies().Select(enemy => new Rac1DiagnosticObject(enemy.Key, Combat)))
                Combat.SetPresence(enemyNode.Key, RuntimeEntityPresence.Inactive);

            var crate = Crates
                .Where(node => Combat.IsAvailable(node.Key) && node.Read().Visible)
                .OrderBy(node => node.Read().Position.DistanceTo(Player.Read().GlobalPosition))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No admitted live class-500 crate is available.");

            Vector3 crateDirection = crate.Read().Position - Player.Read().GlobalPosition;
            crateDirection.Y = 0f;
            if (crateDirection.LengthSquared() <= 1e-5f) crateDirection = -Vector3.UnitZ;
            crateDirection = crateDirection.Normalized();
            Vector3 crateSide = new(-crateDirection.Z, 0f, crateDirection.X);
            Vector3 crateApproachPose =
                crate.Read().Position -
                crateDirection * 4.1f +
                crateSide * 0.55f;
            Rac1SmokePlaceGroundedFacing(crateApproachPose, crateDirection);
            await Rac1SmokeWaitAsync(
                () => Player.Read().Grounded,
                60,
                "crate approach grounding");
            if (Ports.Approach.Admits(crate.Read().Position))
                throw new InvalidOperationException(
                    "Crate approach seed unexpectedly began inside the host wrench contact policy.");

            float crateApproachTravel = await Rac1SmokeApproachWrenchTargetAsync(
                () => crate.Read().Position,
                maxFrames: 180,
                "crate");
            Log(
                $"[rac1-smoke] ordinary-play crate approach PASS; travel={crateApproachTravel:0.000}");

            Ports.Equipment.Select(Rac1WeaponId.Wrench);
            AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);
            await Rac1EncounterInput.TapWrenchAsync(Context);
            await WaitForPlayerAvatarSequenceAsync(
                Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
                120,
                "wrench player-avatar sequence 23");
            AssertRac1SmokeWrenchPresentation(visible: true, "first wrench sequence 23");
            await Rac1SmokeWaitAsync(
                () => Combat.ReadLoot().DestroyedCrates > 0,
                120,
                "wrench crate break");
            Log($"[rac1-smoke] wrench crate break PASS; pickups={Combat.ReadLoot().Pickups.Length}");

            var pickup = Combat.ReadLoot().Pickups.FirstOrDefault(pickup => pickup.CrateOwned && pickup.Available);
            if (pickup is null)
                throw new InvalidOperationException("Crate break emitted no hosted bolt pickup.");
            // Authored world bolts share the presentation dictionary. Follow an
            // emitted crate bolt through its scatter instead of selecting a world bolt.
            for (int frame = 0; frame < 240 && Combat.ReadLoot().CollectedBolts == 0; frame++)
            {
                if (frame % 30 == 0 && Combat.ReadLoot().Pickups.Any(item => item.Id == pickup.Id && item.Available))
                {
                    Combat.StagePlayerPosition(Combat.ReadLoot().Pickups.Single(item => item.Id == pickup.Id).Position, true);
                }
                await ProcessFrameAsync();
            }
            await Rac1SmokeWaitAsync(
                () => Combat.ReadLoot().CollectedBolts > 0,
                1,
                "bolt collection");
            Log($"[rac1-smoke] bolt collection PASS; total={Combat.ReadLoot().CollectedBolts}");
            await Rac1SmokeWaitAsync(
                () => CurrentPlayerAvatarSourceSequence() != Rac1RatchetSequenceSelection.WrenchAttackSequenceId &&
                      PlayerAvatarPresentationIsSynchronized(),
                120,
                "post-wrench player-avatar recovery");

            // The crate contract is proven. Isolate the subsequent projectile
            // and wrench victims from the remaining neighbouring crate contacts.
            foreach (var crateNode in Crates.Where(node => Combat.IsAvailable(node.Key)))
                Combat.SetPresence(crateNode.Key, RuntimeEntityPresence.Inactive);

            Vector3 stagedHostilePosition = crate.Read().Position;
            Combat.StageObject(hostile.Key, stagedHostilePosition);
            Combat.SetPresence(hostile.Key, RuntimeEntityPresence.Active);

            int bombAmmoBeforeFire = Ports.Equipment.Read().BombAmmo;
            if (bombAmmoBeforeFire < Rac1BombGlove.AmmoCostPerShot)
                throw new InvalidOperationException(
                    "Bomb Glove smoke requires at least one recovered item-10 round.");

            Ports.Equipment.Select(Rac1WeaponId.FirstRanged);
            AssertRac1SmokeHudWeapon(
                Rac1HudProjection.BombGlovePresentationKey,
                bombAmmoBeforeFire);
            AssertRac1SmokeWrenchPresentation(visible: false, "Bomb Glove equip");
            Vector3 bombForward = hostile.Read().Forward;
            bombForward.Y = 0f;
            if (bombForward.LengthSquared() <= 1e-5f)
                throw new InvalidOperationException(
                    "Selected class-749 placement lost its usable facing before Bomb Glove smoke.");
            bombForward = bombForward.Normalized();
            Vector3 bombPose = hostile.Read().Position - bombForward * 2f;
            Rac1SmokePlaceFacing(bombPose, bombForward);
            Combat.RequestPrimaryAttack();
            int bombAmmoAfterFire = bombAmmoBeforeFire - Rac1BombGlove.AmmoCostPerShot;
            await Rac1SmokeWaitAsync(
                () => Ports.Equipment.Read().BombAmmo == bombAmmoAfterFire,
                60,
                "Bomb Glove fire admission");
            if (CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.WrenchAttackSequenceId)
                throw new InvalidOperationException(
                    "Bomb Glove fire incorrectly reused the wrench player-avatar sequence 23");
            AssertRac1SmokeHudWeapon(
                Rac1HudProjection.BombGlovePresentationKey,
                bombAmmoAfterFire);

            Combat.RequestPrimaryAttack();
            await Rac1SmokeWaitAsync(
                () => !Combat.ReadBombs().FireRequested,
                60,
                "Bomb Glove cadence rejection");
            if (Ports.Equipment.Read().BombAmmo != bombAmmoAfterFire)
                throw new InvalidOperationException(
                    $"Bomb Glove cadence spent a second round: {Ports.Equipment.Read().BombAmmo}, expected {bombAmmoAfterFire}.");

            var bombWitness = Combat.CaptureBombWitness();
            try
            {
                await Rac1SmokeWaitAsync(
                    () => Combat.ReadBombs().LastContact is { AdmittedContactCount: > 0 },
                    180,
                    "Bomb Glove class-749 contact");
            }
            catch
            {
                LogError($"[rac1-smoke] bomb fixture: player={Player.Read().GlobalPosition}, target={hostile.Read().Position}, " +
                    $"impact={bombWitness?.Read().NativePosition}, projectile-state={bombWitness?.Read().NativeState}, " +
                    $"target-active={Combat.ReadObject(hostile.Key).GameplayActive}, target-health={Enemies.ReadToad(hostile.Key).Health}");
                throw;
            }
            var bombContact = Combat.ReadBombs().LastContact
                ?? throw new InvalidOperationException("Bomb Glove contact result disappeared.");
            if (bombContact.AdmittedContactCount != 1)
                throw new InvalidOperationException(
                    $"Bomb Glove admitted {bombContact.AdmittedContactCount} contacts, expected one staged recovered Veldin class-749 placement.");
            var bombDamage = bombContact.DamageResults[0];
#pragma warning disable S1244 // Native damage constants and the resulting exact health are retained acceptance witnesses.
            if (bombDamage.TargetNativeClassId != Rac1Class749Hostile.NativeClassId ||
                bombDamage.NativeDamage != Rac1BombGlove.NativeDamage ||
                bombDamage.NativeDamageFlags != Rac1BombGlove.NativeDamageFlags)
                throw new InvalidOperationException("Bomb Glove contact drifted from the retained native damage envelope.");
            var postBombProbe = Enemies.ReadToad(hostile.Key);
            if (postBombProbe.Health != -1f ||
                !hostile.Read().Visible)
                throw new InvalidOperationException(
                    "Bomb Glove contact failed the two-point descriptor and visible lethal reaction.");
#pragma warning restore S1244
            await Rac1SmokeWaitAsync(() => !Combat.ReadBombs().Projectiles.Contains(bombContact.ProjectileId),
                30, "Bomb Glove blast completion");
            Log(
                $"[rac1-smoke] Bomb Glove contact PASS; ammo {bombAmmoBeforeFire}->{bombAmmoAfterFire}, " +
                "immediate refire cadence-blocked and class-749 lethal reaction admitted");
            await Rac1SmokeWaitAsync(() => !hostile.Read().Visible, 240, "Bomb Glove enemy reaction completion");
            hostile = Hostiles.Values.First(node => Enemies.ReadToad(node.Key).Health > 0);
            Combat.SetPresence(hostile.Key, RuntimeEntityPresence.Active);

            Vector3 terminalForward = hostile.Read().Forward;
            terminalForward.Y = 0f;
            if (terminalForward.LengthSquared() <= 1e-5f)
                throw new InvalidOperationException(
                    "Selected class-749 placement lost its usable facing before terminal wrench smoke.");
            terminalForward = terminalForward.Normalized();

            // The authored hostile location is not a proven walkable wrench approach
            // corridor. Ordinary navigation was already asserted above before any
            // smoke teleport, so stage the selected recovered placement on the crate corridor
            // that just proved ordinary Ratchet movement. Re-pin only during this
            // synthetic contact regression; production navigation remains untouched.
            Combat.StageObject(hostile.Key, stagedHostilePosition);
            Rac1SmokePlaceGroundedFacing(crateApproachPose, crateDirection);
            await Rac1SmokeWaitAsync(
                () => Player.Read().Grounded,
                60,
                "staged hostile approach grounding");
            Combat.StageObject(hostile.Key, stagedHostilePosition);
            if (Ports.Approach.Admits(stagedHostilePosition))
                throw new InvalidOperationException(
                    "Staged hostile approach seed unexpectedly began inside the host wrench contact policy.");

            float hostileApproachTravel = await Rac1SmokeApproachWrenchTargetAsync(
                () =>
                {
                    Combat.StageObject(hostile.Key, stagedHostilePosition);
                    return stagedHostilePosition;
                },
                maxFrames: 180,
                "staged class-749 hostile",
                maximumPlanarDistance: 1.3f);
            Log(
                $"[rac1-combat-contract] synthetic staged-hostile approach PASS; travel={hostileApproachTravel:0.000}");

            Ports.Equipment.Select(Rac1WeaponId.Wrench);
            AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);
            await Rac1EncounterInput.TapWrenchAsync(Context);
            await Rac1SmokeWaitAsync(
                () =>
                {
                    Combat.StageObject(hostile.Key, stagedHostilePosition);
                    return CurrentPlayerAvatarSourceSequence() == Rac1RatchetSequenceSelection.WrenchAttackSequenceId &&
                           PlayerAvatarPresentationIsSynchronized();
                },
                30,
                "terminal wrench player-avatar sequence 23");
            AssertRac1SmokeWrenchPresentation(visible: true, "terminal wrench sequence 23");
            await Rac1SmokeWaitAsync(
                () =>
                {
                    Combat.StageObject(hostile.Key, stagedHostilePosition);
                    return Enemies.ReadToad(hostile.Key).Health == 0f &&
                           !hostile.Read().Visible;
                },
                240,
                "wrench hostile terminalization");
            Log("[rac1-smoke] wrench hostile terminalization PASS");

            await RunRac1Level18Class749GateSmokeAsync();
            await RunRac1HostileLifecycleSmokeAsync(bombAmmoAfterFire);

            Log("[rac1-combat-contract] PASS: recovered Veldin population contracts survived LEVEL0/LEVEL18/unload-reload");

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[rac1-combat-contract] FAIL: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }


    private async Task RunRac1Level18Class749GateSmokeAsync()
    {
        await Ports.Navigation.NavigateAsync("rac1:LEVEL18", Driver.Lifetime);
        await Rac1SmokeWaitAsync(
            () => World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 18 } &&
                Player is not null &&
                Player.Read().Grounded,
            360,
            "LEVEL18 load and grounding");

        int authored = CountRac1AuthoredClass749();
        int presented = CountRac1PresentedClass749();
        if (authored != 90 || presented != 90)
            throw new InvalidOperationException(
                $"LEVEL18 class-749 census/presentation was {authored}/{presented}, expected 90/90.");
        if (Hostiles.Count != 0 || Combat.ReadPopulation().RegisteredClass749 != 0)
            throw new InvalidOperationException(
                $"LEVEL18 promoted unsupported class-749 runtime behavior: {Hostiles.Count}/{Combat.ReadPopulation().RegisteredClass749} active.");

        Log(
            "[rac1-smoke] LEVEL18 class-749 gate PASS; 90 authored/presented placements, 0 active runtime hostiles");
    }


    private async Task RunRac1HostileLifecycleSmokeAsync(int expectedBombAmmo)
    {
        await Ports.Navigation.NavigateAsync("rac1:LEVEL1", Driver.Lifetime);
        await Rac1SmokeWaitAsync(
            () => World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 1 } &&
                Player is not null &&
                Player.Read().Grounded,
            360,
            "LEVEL1 load and grounding");

        if (Hostiles.Count != 0 || Combat.ReadPopulation().RegisteredClass749 != 0 ||
            Combat.ReadBombs().Projectiles.Length != 0)
            throw new InvalidOperationException(
                "LEVEL1 retained class-749 or projectile state after LEVEL18 unload.");

        if (lifecycle.ReadNanotech().Nanotech != 4)
            throw new InvalidOperationException(
                $"LEVEL1 local Nanotech did not reset on world load: {lifecycle.ReadNanotech().Nanotech}.");
        Log("[rac1-smoke] LEVEL1 unload PASS; no authored class-749 family and no leaked combat state");

        await Ports.Navigation.NavigateAsync("rac1:LEVEL0", Driver.Lifetime);
        await Rac1SmokeWaitAsync(
            () => World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 0 } &&
                Player is not null &&
                Player.Read().Grounded,
            360,
            "LEVEL0 reload and grounding");

        if (CountRac1AuthoredClass749() != 16 || CountRac1PresentedClass749() != 16)
            throw new InvalidOperationException(
                $"LEVEL0 reload class-749 census/presentation was {CountRac1AuthoredClass749()}/{CountRac1PresentedClass749()}, expected 16/16.");
        if (Hostiles.Count != 16 || Combat.ReadPopulation().RegisteredClass749 != 16 ||
            !TryGetRac1Hostile(143, out var hostile, out _) ||
            hostile is null || !Combat.IsAvailable(hostile.Key) || !hostile.Read().Visible)
            throw new InvalidOperationException(
                $"LEVEL0 reload active class-749 runtime count was {Hostiles.Count}/{Combat.ReadPopulation().RegisteredClass749}, expected recovered population 16/16.");
        if (Ports.Equipment.Read().BombAmmo != expectedBombAmmo)
            throw new InvalidOperationException(
                $"Process-lifetime Bomb Glove ammo was {Ports.Equipment.Read().BombAmmo}, expected persistent value {expectedBombAmmo}.");
        AssertRac1SmokeHudWeapon(Rac1HudProjection.WrenchPresentationKey, expectedAmmo: null);

        Log(
            $"[rac1-smoke] LEVEL0 reload PASS; 16 authored/presented/runtime placements restored, item-10 ammo remained {expectedBombAmmo}");
    }

}
