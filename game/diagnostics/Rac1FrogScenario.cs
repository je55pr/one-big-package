using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class Rac1FrogScenario(MovementDiagnosticContext context, IRac1EnemyDiagnosticFacts enemies,
    IRac1EnemyDiagnosticActions actors, IRac1FrogDiagnosticFixture frogs, IRac1EncounterLifecycle lifecycle)
    : Rac1EncounterScenarioSupport(context, enemies, actors)
{
    /// <summary>Staged *real Godot* class-827 source mesh, clock, physics,
    /// damage and cleanup fixture. Not an ordinary retail playthrough.</summary>
    public async Task RunAsync()
    {
        try
        {
            Require(World.Read() is { Source: ObpSourceGame.Rac1, LevelId: 6 or 10 },
                "Frog smoke requires retail LEVEL6 or LEVEL10");
            int expected = World.Read().LevelId == 6 ? 169 : 24;
            int expectedAdmitted = World.Read().LevelId == 6 ? 108 : 24;
            int expectedPaths = World.Read().LevelId == 6 ? 26 : 0;
            Require(frogs.ReadCounts().Registered == expected,
                "Frog smoke native level population differs from original");
            using var frameRate = World.LimitFrameRate(60);
            ClearMovementSmokeInput();
            Driver.Own(Actors.PreservePlayerPose());
            var population = ReadEnemies().Where(e =>
                e.Key.Target.NativeClassId == Rac1FrogAuthored.ClassId).ToArray();
            Require(population.Length == expected, "Frog host created an incorrect population");
            Require(frogs.ReadCounts().Presented == expectedAdmitted,
                "Frog carrier/hidden/switch admission differs from source census");
            Require(population.Where(e => !frogs.Get(e.Key.Target.InstanceIndex).Presented)
                .All(e => e.Read().CollisionLayer == 0), "Frog dormant forms still collide");
            Require(population.Count(e => frogs.Get(e.Key.Target.InstanceIndex).PathIndex >= 0) == expectedPaths,
                "Frog source paths were flattened");
            int expectedCarriers = World.Read().LevelId == 6 ? 10 : 0;
            var carriers = population.Where(e => frogs.Get(e.Key.Target.InstanceIndex).CarrierMode).ToArray();
            Require(carriers.Length == expectedCarriers && frogs.ReadCounts().Carriers == expectedCarriers,
                "Authored Frog carrier count or source-owned class-500 child population differs");
            Require(carriers.All(e =>
            {
                int id = e.Key.Target.InstanceIndex;
                return frogs.Get(id) is { NativeState: 1, CarrierChildOwned: true, Presented: false } &&
                    !e.Read().Visible && e.Read().CollisionLayer == 0 && e.Read().CollisionMask == 0 &&
                    frogs.ReadCarrier(id) is { Visible: true, HasMesh: true };
            }), "Native carrier child lacks retail visual or hidden-parent protection");
            Log($"[frog-smoke] LEVEL{World.Read().LevelId} {expected} registered, {frogs.ReadCounts().Presented} admitted, {expectedPaths} routed; {expectedCarriers} retail crate children, dormant collider check PASS");
            if (World.Read().LevelId == 6)
                Log("[frog-smoke] carriers " + string.Join(", ", carriers.Select(c =>
                    $"i{c.Key.Target.InstanceIndex}(switch={frogs.GetAuthored(c.Key.Target.InstanceIndex).SwitchSelector},target={frogs.GetAuthored(c.Key.Target.InstanceIndex).TargetInstance})")));
            await PhysicsFramesAsync(5);
            if (World.Read().LevelId == 6 && frogs.Shuttle is { } shuttle)
            {
                // Stage player at the authored class-1109 stop-1 clip volume
                // without advancing physics. Exercise the actual host gate,
                // renderer and contact masks, not merely the neutral session.
                Vector3 previousPlayer = Player.Read().GlobalPosition;
                try
                {
                    Player.SetPosition(FromEffectNative(shuttle.StopVolumeCentre(2)));
                    shuttle.TickSwitch();
                    Require(shuttle.SwitchEnabled, "Shuttle stop-1 clip did not select native Frog gate");
                    int alternate = population.Count(e =>
                    {
                        var entry = frogs.Get(e.Key.Target.InstanceIndex);
                        return entry.SwitchGated && !entry.CarrierMode && !entry.StartsHidden;
                    });
                    Require(alternate > 0 && frogs.ReadCounts().Presented == alternate,
                        "Shuttle native gate selected an incorrect Frog population");
                    if (frogs.Waves is { } gatedWaves)
                    {
                        int gatedController = (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Controllers.Single(id =>
                            gatedWaves.Group(id) == 6);
                        Player.SetPosition(FromEffectNative(gatedWaves.ZoneCentre(gatedController)));
                        for (int tick = 0; tick < 30; tick++)
                            (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Tick();
                        Require(gatedWaves.State(gatedController) == 0 &&
                            gatedWaves.Members(gatedController).All(id =>
                                frogs.Get(id) is { NativeState: 15, Presented: false }),
                            "Opposite shuttle group wrongly consumed a native hidden Frog wave.");
                        int alternateProducer = (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Controllers.Single(id =>
                            gatedWaves.Group(id) == 20);
                        var alternateMembers = gatedWaves.Members(alternateProducer).ToArray();
                        Require(alternateMembers.SequenceEqual(new[] { 442, 443, 444 }) &&
                            gatedWaves.Members((frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Controllers.Single(id =>
                                gatedWaves.Group(id) == 21)).SequenceEqual(new[] { 445, 446, 447 }),
                            "Wrong native switch-one wave groups.");
                        Player.SetPosition(FromEffectNative(gatedWaves.ZoneCentre(alternateProducer)));
                        for (int tick = 0; tick < 600 && gatedWaves.State(alternateProducer) != 3; tick++)
                            (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Tick();
                        Require(gatedWaves.State(alternateProducer) == 3 &&
                            alternateMembers.All(id =>
                                frogs.Get(id) is { NativeState: 6, Presented: true }) &&
                            Enumerable.Range(445, 3).All(id =>
                                frogs.Get(id) is { NativeState: 6, Presented: true }),
                            "Source switch-one class1066 wave failed to emit its group20/21 members.");
                        Player.SetPosition(FromEffectNative(shuttle.StopVolumeCentre(2)));
                    }
                    Require(population.All(e =>
                    {
                        bool present = frogs.Get(e.Key.Target.InstanceIndex).Presented;
                        return e.Read().CollisionLayer == (present ? 1u : 0u) &&
                            e.Read().CollisionMask == (present ? 1u : 0u);
                    }), "Switch-gated Frog collision masks differ from runtime presence");
                    // A confirmed shuttle arrival is supplied explicitly.
                    // Ordinary player proximity must never imply this callback.
                    Require(shuttle.AcceptShuttleArrival(0), "Shuttle return selection not accepted");
                    shuttle.ApplySelection();
                    Require(frogs.ReadCounts().Presented == 108 &&
                        population.All(e =>
                        {
                            bool present = frogs.Get(e.Key.Target.InstanceIndex).Presented;
                            return e.Read().CollisionLayer == (present ? 1u : 0u) &&
                                e.Read().CollisionMask == (present ? 1u : 0u);
                        }), "Shuttle arrival failed to restore original Frog collision population");
                    Log($"[frog-smoke] shuttle stop1 {alternate} admitted, original stop0 restored 108; mesh/contact admission PASS");
                }
                finally { Player.SetPosition(previousPlayer); }
            }

            if (World.Read().LevelId == 6)
            {
                // REAL class1066 animation and clip-volume producer, not a
                // directly injected call to the child Frog launch handler.
                var waves = frogs.Waves
                    ?? throw new InvalidOperationException("LEVEL6 original Frog wave controllers missing.");
                Require(waves.RegisteredCount == 6 && (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Controllers.Length == 6,
                    "Original native zone-trigger population differs from retail.");
                int producer = (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Controllers.Single(id => waves.Group(id) == 6);
                var originalMembers = waves.Members(producer).ToArray();
                Require(originalMembers.SequenceEqual(new[] { 383, 385, 435 }) &&
                    originalMembers.All(id => frogs.Get(id) is { NativeState: 15, Presented: false }),
                    "Wrong signed Moby-index list selected for native Frog trigger.");
                Vector3 originalPlayer = Player.Read().GlobalPosition;
                try
                {
                    Player.SetPosition(FromEffectNative(waves.ZoneCentre(producer)));
                    // Drive the same 60-Hz controller and source animation clock
                    // as normal gameplay, without dispatching unrelated player
                    // movement while the smoke fixture is teleported.
                    for (int tick = 0; tick < 600 && waves.State(producer) != 3; tick++)
                        (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Tick();
                    Require(waves.State(producer) == 3 &&
                        originalMembers.All(id =>
                            frogs.Get(id) is { NativeState: 6, Presented: true } &&
                            Enemy(id).Read().CollisionLayer == 1 &&
                            Enemy(id).Read().CollisionMask == 1),
                        "Retail clip-volume/animation trigger failed to launch its exact three Frogs.");
                    Require(originalMembers.All(id =>
                        Enemy(id).Read().Position.DistanceTo(
                            FromEffectNative(waves.NativePosition(producer))) < .001f),
                        "Retail class1066 trigger launch positions differ from native owner Moby.");
                    Log($"[frog-smoke] original class1066 i{producer} clip/animation/20-tick wave released exactly [{string.Join(",", originalMembers)}] PASS");
                    int nextProducer = (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Controllers.Single(id => waves.Group(id) == 9);
                    var nextMembers = waves.Members(nextProducer).ToArray();
                    Require(nextMembers.SequenceEqual(new[] { 436, 437, 438 }),
                        "Native Frog group9 membership differs.");
                    Player.SetPosition(FromEffectNative(waves.ZoneCentre(nextProducer)));
                    for (int tick = 0; tick < 600 && waves.State(nextProducer) != 3; tick++)
                        (frogs.Waves ?? throw new DiagnosticUnavailableException("Frog waves unavailable.")).Tick();
                    Require(waves.State(nextProducer) == 3 &&
                        nextMembers.All(id => frogs.Get(id) is { NativeState: 6, Presented: true }) &&
                        Enumerable.Range(439, 3).All(id =>
                            frogs.Get(id) is { NativeState: 6, Presented: true }),
                        "Source class1066 group9/10 waves failed to release the original hidden Frogs.");
                    Require(waves.RegisteredCount == 6 &&
                        new[] { 638, 639, 640, 641, 642, 643 }.All(id => waves.State(id) == 3),
                        "Not all six original class1066 zone controllers completed their waves.");
                    Require(new[] { 387, 388, 389, 390 }.All(id =>
                        frogs.Get(id) is { NativeState: 15, Presented: false }),
                        "Uncontrolled hidden group8 was mistakenly admitted.");
                    Log("[frog-smoke] all six retail class1066 trigger waves completed; group8 remains hidden PASS");
                }
                finally { Player.SetPosition(originalPlayer); }
            }

            if (World.Read().LevelId == 6)
                await new Rac1AlienQueenScenario(Context, Enemies, Actors, frogs,
                    frogs.Queen ?? throw new DiagnosticUnavailableException("The source Queen is unavailable.")).RunAsync();

            if (World.Read().LevelId == 6)
            {
                Require(Enumerable.Range(357, 3).All(frogs.HasPickupModel),
                    "Retail carrier debris class357/358/359 missing from LEVEL6 catalogue");
                // Stage an actual authored child's retail-mesh wrench contact
                // without moving Ratchet through unmodified level traversal.
                var disguise = carriers[1];
                int struckId = disguise.Key.Target.InstanceIndex;
                Vector3 contact = frogs.ReadCarrier(struckId)?.MeshCentre ?? throw new DiagnosticUnavailableException("The source carrier mesh is unavailable.");
                int beforeDebris = frogs.ReadCounts().Debris;
                int beforeCrates = frogs.ReadCounts().Crates;
                frogs.ResolveAnimatedWrenchContact(new(contact, contact, contact, contact, .02f), 1);
                Require(frogs.Get(struckId) is
                {
                    Terminal: true, Presented: false,
                    CarrierReleased: false, CarrierChildOwned: false
                } &&
                    frogs.ReadCarrier(struckId) is null,
                    "Wrench contact did not destroy the original carrier child and its parent");
                Require(frogs.ReadCounts().Debris == beforeDebris + Rac1FrogCarrierBurst.TotalCount &&
                    frogs.ReadCounts().Crates == beforeCrates,
                    "Native child-hit burst or authored crate census is incorrect");
                Log($"[frog-smoke] carrier i{struckId} native child struck by wrench, parent removed, 12 retail fragments, no bolts PASS");
            }
            if (World.Read().LevelId == 6)
            {
                // Stage one actual authored carrier's player target within the
                // retail four-unit threshold, exercising the host child
                // lifetime, hidden-parent admission and collision handoff.
                var carrier = carriers[0];
                int carrierId = carrier.Key.Target.InstanceIndex;
                int crateCount = frogs.ReadCounts().Crates;
                int pickupCount = frogs.ReadCounts().Pickups;
                int burstCount = frogs.ReadCounts().Debris;
                Vector3 oldPlayer = Player.Read().GlobalPosition;
                try
                {
                    Player.SetPosition(carrier.Read().Position +
                        FromEffectNative(new System.Numerics.Vector3(2, 0, 0)));
                    frogs.TickCarrier(carrier.Key);
                    var released = frogs.Get(carrierId);
                    Require(released is
                    {
                        NativeState: 8, Presented: true, CarrierReleased: true,
                        CarrierChildOwned: false
                    }, "Carrier failed native four-unit release");
                    Require(frogs.ReadCarrier(carrierId) is null &&
                        carrier.Read().Visible && carrier.Read().CollisionLayer == 1 &&
                        carrier.Read().CollisionMask == 1, "Carrier release retained child or hidden-parent physics");
                    Require(frogs.ReadCounts().Crates == crateCount &&
                        frogs.ReadCounts().Pickups == pickupCount &&
                        frogs.ReadCounts().Debris == burstCount + Rac1FrogCarrierBurst.TotalCount &&
                        frogs.ReadCounts().Registered == expected,
                        "Carrier child incorrectly minted world crate, reward or Frog");
                    Log($"[frog-smoke] carrier i{carrierId} class500 child removed, state1->8, contact on, no crate reward PASS");
                }
                finally { Player.SetPosition(oldPlayer); }
            }

            // Stage one *authored* ordinary frog into an isolated reachable
            // collision fixture. The physics/animation/gameplay loops below are
            // real; only the positions and supporting floor are synthetic.
            var candidate = population.First(e => frogs.Get(e.Key.Target.InstanceIndex) is
            { Presented: true, NativeState: 8, PathIndex: -1 });
            int instance = candidate.Key.Target.InstanceIndex;
            var fixture = Driver.Own(Geometry.Create("FrogEncounterFixture", new(2000, 64, 2600)));
            fixture.AddMesh("FrogFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(4);
            var origin = fixture.GlobalPosition;
            Actors.StageRoot(candidate.Key, origin);
            Player.ResetAt(origin + new Vector3(2.8f, .05f, 0));
            await PhysicsFramesAsync(4);
            Require(candidate.Read().CollisionLayer != 0, "Ordinary Frog lacks a host contact capsule");
            Require(candidate.Read().AnimationSample is not null, "Native Frog sequence was not decoded");
            var initialHealth = lifecycle.ReadNanotech().Nanotech;
            bool approached = false, attack = false, playerHit = false, windowTouch = false;
            float maxAttackFrame = 0, closestPlayer = float.PositiveInfinity, closestSocket = float.PositiveInfinity;
            for (int tick = 0; tick < 500; tick++)
            {
                await PhysicsFramesAsync(1);
                var state = frogs.Get(instance);
                approached |= state.NativeState == 9;
                attack |= state.NativeState == 12;
                if (state.NativeState == 12)
                {
                    maxAttackFrame = Math.Max(maxAttackFrame, candidate.Read().AnimationPosition);
                    closestPlayer = Math.Min(closestPlayer, candidate.Read().Position.DistanceTo(Player.Read().GlobalPosition));
                    closestSocket = Math.Min(closestSocket, candidate.Read().Socket.DistanceTo(Player.Read().GlobalPosition));
                    windowTouch |= candidate.Read().AnimationPosition is >= 10 and <= 15 && frogs.MeleeTouchesPlayer(candidate.Key);
                }
                playerHit |= lifecycle.ReadNanotech().Nanotech < initialHealth;
                if (attack && playerHit) break;
            }
            Log($"[frog-smoke] i{instance} attack window maxAnim={maxAttackFrame:F2} closestRoot={closestPlayer:F2} closestSocket={closestSocket:F2} touch={windowTouch} end={frogs.Get(instance).NativeState}");
            Require(approached, "Frog never entered native state9 approach");
            Require(attack, "Frog never entered native state12 attack");
            Require(playerHit, "Frog animation contact never damaged Ratchet");
            Log($"[frog-smoke] i{instance} state9 approach, state12 animation-window attack, Nanotech damage PASS");
            Require(Actors.TryStrike(candidate.Key), "Frog rejected real host wrench damage descriptor");
            Require(frogs.Get(instance).Terminal, "Health-1 Frog did not terminate on lethal wrench contact");
            await PhysicsFramesAsync(3);
            Require(candidate.Read().CollisionLayer == 0 && !candidate.Read().Visible,
                "Defeated Frog retained collider or visible mesh");
            Require(frogs.ReadCounts().Registered == expected, "Source Frog census grew through over-spawn");
            Log($"[frog-smoke] i{instance} one-hit defeat, collision/mesh cleanup and fixed population PASS");
            fixture.Dispose();

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"[frog-smoke] FAIL {ex}");
            throw;
        }
    }

}
