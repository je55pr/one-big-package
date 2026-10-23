using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Staged *real Godot* class-827 source mesh, clock, physics,
    /// damage and cleanup fixture. Not an ordinary retail playthrough.</summary>
    private async Task RunRac1FrogSmokeAsync()
    {
        try
        {
            Require(_world is { Game: "rac1", LevelId: 6 or 10 } && _player is not null &&
                _rac1Frogs is not null,
                "Frog smoke requires retail LEVEL6 or LEVEL10");
            int expected = _world!.LevelId == 6 ? 169 : 24;
            int expectedAdmitted = _world.LevelId == 6 ? 108 : 24;
            int expectedPaths = _world.LevelId == 6 ? 26 : 0;
            Require(_rac1Frogs!.RegisteredCount == expected,
                "Frog smoke native level population differs from original");
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            var frogs = _rac1Frogs!;
            var population = _rac1Enemies.Values.Where(e =>
                e.Node.Source.NativeClassId == Rac1FrogAuthored.ClassId).ToArray();
            Require(population.Length == expected, "Frog host created an incorrect population");
            Require(frogs.PresentedCount == expectedAdmitted,
                "Frog carrier/hidden/switch admission differs from source census");
            Require(population.Where(e => !frogs.Get(e.Node.Source.InstanceIndex).Presented)
                .All(e => e.Body.CollisionLayer == 0), "Frog dormant forms still collide");
            Require(population.Count(e => frogs.Get(e.Node.Source.InstanceIndex).PathIndex >= 0) == expectedPaths,
                "Frog source paths were flattened");
            int expectedCarriers = _world.LevelId == 6 ? 10 : 0;
            var carriers = population.Where(e => frogs.Get(e.Node.Source.InstanceIndex).CarrierMode).ToArray();
            Require(carriers.Length == expectedCarriers && _rac1FrogCarrierChildren.Count == expectedCarriers,
                "Authored Frog carrier count or source-owned class-500 child population differs");
            Require(carriers.All(e =>
            {
                int id = e.Node.Source.InstanceIndex;
                return frogs.Get(id) is { NativeState: 1, CarrierChildOwned: true, Presented: false } &&
                    !e.Node.Root.Visible && e.Body.CollisionLayer == 0 && e.Body.CollisionMask == 0 &&
                    _rac1FrogCarrierChildren.TryGetValue(id, out var child) && child.Visible &&
                    child.GetChildren().OfType<MeshInstance3D>().Any();
            }), "Native carrier child lacks retail visual or hidden-parent protection");
            GD.Print($"[frog-smoke] LEVEL{_world.LevelId} {expected} registered, {frogs.PresentedCount} admitted, {expectedPaths} routed; {expectedCarriers} retail crate children, dormant collider check PASS");
            if (_world.LevelId == 6)
                GD.Print("[frog-smoke] carriers " + string.Join(", ", carriers.Select(c =>
                    $"i{c.Node.Source.InstanceIndex}(switch={frogs.GetAuthored(c.Node.Source.InstanceIndex).SwitchSelector},target={frogs.GetAuthored(c.Node.Source.InstanceIndex).TargetInstance})")));
            await PhysicsFramesAsync(5);
            if (_world.LevelId == 6 && _rac1FrogShuttle is { } shuttle)
            {
                // Stage player at the authored class-1109 stop-1 clip volume
                // without advancing physics. Exercise the actual host gate,
                // renderer and contact masks, not merely the neutral session.
                Vector3 previousPlayer = _player!.GlobalPosition;
                try
                {
                    _player.GlobalPosition = FromEffectNative(shuttle.StopVolumeCentre(2));
                    TickRac1FrogShuttleSwitch();
                    Require(shuttle.SwitchEnabled, "Shuttle stop-1 clip did not select native Frog gate");
                    int alternate = population.Count(e =>
                    {
                        var entry = frogs.Get(e.Node.Source.InstanceIndex);
                        return entry.SwitchGated && !entry.CarrierMode && !entry.StartsHidden;
                    });
                    Require(alternate > 0 && frogs.PresentedCount == alternate,
                        "Shuttle native gate selected an incorrect Frog population");
                    Require(population.All(e =>
                    {
                        bool present = frogs.Get(e.Node.Source.InstanceIndex).Presented;
                        return e.Body.CollisionLayer == (present ? 1u : 0u) &&
                            e.Body.CollisionMask == (present ? 1u : 0u);
                    }), "Switch-gated Frog collision masks differ from runtime presence");
                    // A confirmed shuttle arrival is supplied explicitly.
                    // Ordinary player proximity must never imply this callback.
                    Require(shuttle.AcceptShuttleArrival(0), "Shuttle return selection not accepted");
                    ApplyRac1FrogShuttleSelection();
                    Require(frogs.PresentedCount == 108 &&
                        population.All(e =>
                        {
                            bool present = frogs.Get(e.Node.Source.InstanceIndex).Presented;
                            return e.Body.CollisionLayer == (present ? 1u : 0u) &&
                                e.Body.CollisionMask == (present ? 1u : 0u);
                        }), "Shuttle arrival failed to restore original Frog collision population");
                    GD.Print($"[frog-smoke] shuttle stop1 {alternate} admitted, original stop0 restored 108; mesh/contact admission PASS");
                }
                finally { _player.GlobalPosition = previousPlayer; }
            }

            if (_world.LevelId == 6)
            {
                // Source-backed FUN_L06_002e9d10 launch, with an explicit
                // simulated producer callback. Source group membership and
                // actual Queen/trigger event timing are not yet hosted.
                var dormantLaunch = population.First(e =>
                    frogs.Get(e.Node.Source.InstanceIndex) is
                    { NativeState: 15, StartsHidden: true, Lives: 1, SwitchGated: false });
                int launchId = dormantLaunch.Node.Source.InstanceIndex;
                NVector3 launchPosition = ToEffectNative(dormantLaunch.Node.Root.GlobalPosition) +
                    NVector3.UnitZ * 15f;
                Require(LaunchRac1FrogFromNativeProducer(launchId, launchPosition,
                    Rac1FrogLaunchProducer.QueenGroup),
                    "Confirmed native group callback could not activate original hidden Frog");
                Require(frogs.Get(launchId) is { NativeState: 6, Presented: true, Lives: 1 } &&
                    dormantLaunch.Node.Root.Visible &&
                    dormantLaunch.Body.CollisionLayer == 1 &&
                    dormantLaunch.Body.CollisionMask == 1 &&
                    dormantLaunch.Node.Root.GlobalPosition.DistanceTo(
                        FromEffectNative(launchPosition)) < .001f,
                    "Native hidden Frog state6 spawn did not restore pose/presentation/contact");
                Require(!LaunchRac1FrogFromNativeProducer(launchId, launchPosition,
                        Rac1FrogLaunchProducer.QueenGroup),
                    "Duplicate list callback woke a non-state15 Frog");
                Vector3 beforeFlight = dormantLaunch.Node.Root.GlobalPosition;
                TickRac1Frog(dormantLaunch);
                Require(frogs.Get(launchId).NativeState == 6 &&
                    dormantLaunch.Node.Root.GlobalPosition.Y > beforeFlight.Y,
                    "Native state6 launch gravity/rise never moved the hosted Frog");
                // Landing fact supplied explicitly in the smoke fixture.
                dormantLaunch.Grounded = true;
                TickRac1Frog(dormantLaunch);
                Require(frogs.Get(launchId).NativeState == 7,
                    "Confirmed native landing did not enter Frog state7");
                GD.Print($"[frog-smoke] original hidden i{launchId} explicitly launched state15->6->7; mesh/contact and gravity PASS");
            }
            if (_world.LevelId == 6)
            {
                Require(_rac1PickupAssets is not null &&
                    Enumerable.Range(357, 3).All(_rac1PickupAssets.Models.ContainsKey),
                    "Retail carrier debris class357/358/359 missing from LEVEL6 catalogue");
                // Stage an actual authored child's retail-mesh wrench contact
                // without moving Ratchet through unmodified level traversal.
                var disguise = carriers[1];
                int struckId = disguise.Node.Source.InstanceIndex;
                var disguiseMesh = _rac1FrogCarrierChildren[struckId]
                    .GetChildren().OfType<MeshInstance3D>().First();
                Vector3 contact = (disguiseMesh.GlobalTransform * disguiseMesh.GetAabb()).GetCenter();
                int beforeDebris = _rac1Debris.Count;
                int beforeCrates = _rac1CrateNodes.Count;
                ResolveRac1AnimatedWrenchContacts(contact, contact, contact, contact, .02f, 1);
                Require(frogs.Get(struckId) is
                {
                    Terminal: true, Presented: false,
                    CarrierReleased: false, CarrierChildOwned: false
                } &&
                    !_rac1FrogCarrierChildren.ContainsKey(struckId),
                    "Wrench contact did not destroy the original carrier child and its parent");
                Require(_rac1Debris.Count == beforeDebris + Rac1FrogCarrierBurst.TotalCount &&
                    _rac1CrateNodes.Count == beforeCrates,
                    "Native child-hit burst or authored crate census is incorrect");
                GD.Print($"[frog-smoke] carrier i{struckId} native child struck by wrench, parent removed, 12 retail fragments, no bolts PASS");
            }
            if (_world.LevelId == 6)
            {
                // Stage one actual authored carrier's player target within the
                // retail four-unit threshold, exercising the host child
                // lifetime, hidden-parent admission and collision handoff.
                var carrier = carriers[0];
                int carrierId = carrier.Node.Source.InstanceIndex;
                int crateCount = _rac1CrateNodes.Count;
                int pickupCount = _rac1PickupMotion.Count;
                int burstCount = _rac1Debris.Count;
                Vector3 oldPlayer = _player!.GlobalPosition;
                try
                {
                    _player.GlobalPosition = carrier.Node.Root.GlobalPosition +
                        FromEffectNative(new System.Numerics.Vector3(2, 0, 0));
                    TickRac1FrogCarrier(carrier);
                    var released = frogs.Get(carrierId);
                    Require(released is
                    {
                        NativeState: 8, Presented: true, CarrierReleased: true,
                        CarrierChildOwned: false
                    }, "Carrier failed native four-unit release");
                    Require(!_rac1FrogCarrierChildren.ContainsKey(carrierId) &&
                        carrier.Node.Root.Visible && carrier.Body.CollisionLayer == 1 &&
                        carrier.Body.CollisionMask == 1, "Carrier release retained child or hidden-parent physics");
                    Require(_rac1CrateNodes.Count == crateCount &&
                        _rac1PickupMotion.Count == pickupCount &&
                        _rac1Debris.Count == burstCount + Rac1FrogCarrierBurst.TotalCount &&
                        frogs.RegisteredCount == expected,
                        "Carrier child incorrectly minted world crate, reward or Frog");
                    GD.Print($"[frog-smoke] carrier i{carrierId} class500 child removed, state1->8, contact on, no crate reward PASS");
                }
                finally { _player.GlobalPosition = oldPlayer; }
            }

            // Stage one *authored* ordinary frog into an isolated reachable
            // collision fixture. The physics/animation/gameplay loops below are
            // real; only the positions and supporting floor are synthetic.
            var candidate = population.First(e => frogs.Get(e.Node.Source.InstanceIndex) is
            { Presented: true, NativeState: 8, PathIndex: -1 });
            int instance = candidate.Node.Source.InstanceIndex;
            var fixture = new Node3D { Name = "FrogEncounterFixture", Position = new(2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "FrogFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(4);
            var origin = fixture.GlobalPosition;
            candidate.Node.Root.GlobalPosition = origin;
            _player!.ApplyDevelopmentReset(origin + new Vector3(2.8f, .05f, 0));
            await PhysicsFramesAsync(4);
            Require(candidate.Body.CollisionLayer != 0, "Ordinary Frog lacks a host contact capsule");
            Require(candidate.Node.AnimationSample is not null, "Native Frog sequence was not decoded");
            var initialHealth = _rac1Nanotech.Probe().Nanotech;
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
                    maxAttackFrame = Math.Max(maxAttackFrame, candidate.Animation.Position);
                    closestPlayer = Math.Min(closestPlayer, candidate.Node.Root.GlobalPosition.DistanceTo(_player.GlobalPosition));
                    closestSocket = Math.Min(closestSocket, EnemySocket(candidate).DistanceTo(_player.GlobalPosition));
                    windowTouch |= candidate.Animation.Position is >= 10 and <= 15 && EnemyMeleeTouchesPlayer(candidate);
                }
                playerHit |= _rac1Nanotech.Probe().Nanotech < initialHealth;
                if (attack && playerHit) break;
            }
            GD.Print($"[frog-smoke] i{instance} attack window maxAnim={maxAttackFrame:F2} closestRoot={closestPlayer:F2} closestSocket={closestSocket:F2} touch={windowTouch} end={frogs.Get(instance).NativeState}");
            Require(approached, "Frog never entered native state9 approach");
            Require(attack, "Frog never entered native state12 attack");
            Require(playerHit, "Frog animation contact never damaged Ratchet");
            GD.Print($"[frog-smoke] i{instance} state9 approach, state12 animation-window attack, Nanotech damage PASS");
            Require(TryStrikeRac1Hostile(candidate.Node), "Frog rejected real host wrench damage descriptor");
            Require(frogs.Get(instance).Terminal, "Health-1 Frog did not terminate on lethal wrench contact");
            await PhysicsFramesAsync(3);
            Require(candidate.Body.CollisionLayer == 0 && !candidate.Node.Root.Visible,
                "Defeated Frog retained collider or visible mesh");
            Require(frogs.RegisteredCount == expected, "Source Frog census grew through over-spawn");
            GD.Print($"[frog-smoke] i{instance} one-hit defeat, collision/mesh cleanup and fixed population PASS");
            fixture.QueueFree();
            ApplicationLifecycle.RequestQuit(this, "rac1-frog-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PushError($"[frog-smoke] FAIL {ex}");
            ApplicationLifecycle.RequestQuit(this, "rac1-frog-smoke-fail", 1);
        }
    }
}
