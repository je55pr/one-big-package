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
                    if (_rac1FrogWaves is { } gatedWaves)
                    {
                        int gatedController = _rac1FrogWaveNodes.Keys.Single(id =>
                            gatedWaves.Group(id) == 6);
                        _player.GlobalPosition = FromEffectNative(gatedWaves.ZoneCentre(gatedController));
                        for (int tick = 0; tick < 30; tick++)
                            TickRac1FrogWaves();
                        Require(gatedWaves.State(gatedController) == 0 &&
                            gatedWaves.Members(gatedController).All(id =>
                                frogs.Get(id) is { NativeState: 15, Presented: false }),
                            "Opposite shuttle group wrongly consumed a native hidden Frog wave.");
                        int alternateProducer = _rac1FrogWaveNodes.Keys.Single(id =>
                            gatedWaves.Group(id) == 20);
                        var alternateMembers = gatedWaves.Members(alternateProducer).ToArray();
                        Require(alternateMembers.SequenceEqual(new[] { 442, 443, 444 }) &&
                            gatedWaves.Members(_rac1FrogWaveNodes.Keys.Single(id =>
                                gatedWaves.Group(id) == 21)).SequenceEqual(new[] { 445, 446, 447 }),
                            "Wrong native switch-one wave groups.");
                        _player.GlobalPosition = FromEffectNative(gatedWaves.ZoneCentre(alternateProducer));
                        for (int tick = 0; tick < 600 && gatedWaves.State(alternateProducer) != 3; tick++)
                            TickRac1FrogWaves();
                        Require(gatedWaves.State(alternateProducer) == 3 &&
                            alternateMembers.All(id =>
                                frogs.Get(id) is { NativeState: 6, Presented: true }) &&
                            Enumerable.Range(445, 3).All(id =>
                                frogs.Get(id) is { NativeState: 6, Presented: true }),
                            "Source switch-one class1066 wave failed to emit its group20/21 members.");
                        _player.GlobalPosition = FromEffectNative(shuttle.StopVolumeCentre(2));
                    }
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
                // REAL class1066 animation and clip-volume producer, not a
                // directly injected call to the child Frog launch handler.
                var waves = _rac1FrogWaves
                    ?? throw new InvalidOperationException("LEVEL6 original Frog wave controllers missing.");
                Require(waves.RegisteredCount == 6 && _rac1FrogWaveNodes.Count == 6,
                    "Original native zone-trigger population differs from retail.");
                int producer = _rac1FrogWaveNodes.Keys.Single(id => waves.Group(id) == 6);
                var originalMembers = waves.Members(producer).ToArray();
                Require(originalMembers.SequenceEqual(new[] { 383, 385, 435 }) &&
                    originalMembers.All(id => frogs.Get(id) is { NativeState: 15, Presented: false }),
                    "Wrong signed Moby-index list selected for native Frog trigger.");
                Vector3 originalPlayer = _player!.GlobalPosition;
                try
                {
                    _player.GlobalPosition = FromEffectNative(waves.ZoneCentre(producer));
                    // Drive the same 60-Hz controller and source animation clock
                    // as normal gameplay, without dispatching unrelated player
                    // movement while the smoke fixture is teleported.
                    for (int tick = 0; tick < 600 && waves.State(producer) != 3; tick++)
                        TickRac1FrogWaves();
                    Require(waves.State(producer) == 3 &&
                        originalMembers.All(id =>
                            frogs.Get(id) is { NativeState: 6, Presented: true } &&
                            _rac1Enemies[id].Body.CollisionLayer == 1 &&
                            _rac1Enemies[id].Body.CollisionMask == 1),
                        "Retail clip-volume/animation trigger failed to launch its exact three Frogs.");
                    Require(originalMembers.All(id =>
                        _rac1Enemies[id].Node.Root.GlobalPosition.DistanceTo(
                            FromEffectNative(waves.NativePosition(producer))) < .001f),
                        "Retail class1066 trigger launch positions differ from native owner Moby.");
                    GD.Print($"[frog-smoke] original class1066 i{producer} clip/animation/20-tick wave released exactly [{string.Join(",", originalMembers)}] PASS");
                    int nextProducer = _rac1FrogWaveNodes.Keys.Single(id => waves.Group(id) == 9);
                    var nextMembers = waves.Members(nextProducer).ToArray();
                    Require(nextMembers.SequenceEqual(new[] { 436, 437, 438 }),
                        "Native Frog group9 membership differs.");
                    _player.GlobalPosition = FromEffectNative(waves.ZoneCentre(nextProducer));
                    for (int tick = 0; tick < 600 && waves.State(nextProducer) != 3; tick++)
                        TickRac1FrogWaves();
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
                    GD.Print("[frog-smoke] all six retail class1066 trigger waves completed; group8 remains hidden PASS");
                }
                finally { _player.GlobalPosition = originalPlayer; }
            }

            if (_world.LevelId == 6)
            {
                // Stage only an admitted native combat hit on the real
                // Queen. The retail health helper then owns the 30-tick
                // threshold recovery into state3; the original class1051
                // animation clock supplies sequence6 completion naturally.
                // Boss movement, hitbox and camera gameplay remain unhosted.
                var queen = _rac1FrogQueen
                    ?? throw new InvalidOperationException("Retail class1051 Queen missing.");
                Require(queen.QueenInstance == 626 && queen.GroupIndex == 22 &&
                    queen.Members.SequenceEqual(Enumerable.Range(448, 12)),
                    "Original Alien Queen source group does not match retail.");
                var originalQueenMembers = queen.Members.ToDictionary(id => id,
                    id => ToEffectNative(_rac1Enemies[id].Node.Root.GlobalPosition));
                Require(ObserveRac1QueenFrogEvent(3, 6, false) == 0 &&
                    ObserveRac1QueenFrogEvent(3, 5, true) == 0 &&
                    queen.Members.All(id => frogs.Get(id).NativeState == 15),
                    "Queen wrong-sequence/noncompleted animation prematurely launched Frogs");
                var queenRecovery = _rac1QueenRecovery
                    ?? throw new InvalidOperationException("Original Queen recovery not configured.");
                Require(queenRecovery is { NativeState: 1, Health: 30f },
                    "Original Queen did not retain native 30 health / pre-encounter state.");
                // Boss entrance/camera is explicitly staged. Afterwards
                // source class71 damage is admitted via real retail model
                // geometry sweeps and the shared damage transport.
                var queenNode = _rac1QueenNode
                    ?? throw new InvalidOperationException("Original Queen mesh not hosted.");
                var queenMesh = queenNode.Root.GetChildren().OfType<MeshInstance3D>().First();
                Vector3 queenContact = (queenMesh.GlobalTransform * queenMesh.GetAabb()).GetCenter();
                var queenKey = new Rac1MobyRuntimeKey(Rac1FrogQueenGroup.QueenClassId, 626);
                var wrench = new Rac1WrenchDamageResult(
                    Rac1WrenchContactPath.AnimatedSweepHostGeometry, 1f,
                    Rac1WrenchAttackProfile.NativeDamageFlags);
                long beforeQueenHits = _rac1DamageTransport.Sequence;
                ResolveRac1QueenWrenchContacts(queenContact, queenContact,
                    queenContact, queenContact, .01f, wrench);
                Require(queenRecovery.Health == 30f &&
                    _rac1DamageTransport.Sequence == beforeQueenHits,
                    "Queen accepted an attack before native combat admission.");
                // Source-mapped entrance: two class1021 actors and camera
                // selector are not hosted yet, so their success is supplied
                // explicitly. Unlike the old direct state8 override, the
                // 10-tick timer, linked admission and flight/landing handoff
                // are independently exercised before combat becomes valid.
                var entrance = _rac1QueenEntrance
                    ?? throw new InvalidOperationException("Original Queen entrance not configured.");
                Require(entrance is
                {
                    FirstLinkedInstance: 573,
                    SecondLinkedInstance: 578, LinkedEventSelector: 25,
                    CameraSelector: 59, NativePathSelector: 54, NativeState: 1
                },
                    "Queen entrance selectors differ from the original LEVEL6 data.");
                Require(entrance.StepWaiting(false, false, true).NativeState == 1 &&
                    entrance.StepWaiting(true, true, true).NativeState == 1,
                    "Queen activated without her native class0 target and mode gate.");
                entrance.StepWaiting(true, false);
                for (int tick = 0; tick < Rac1AlienQueenEntrance.StartupDelayTicks; tick++)
                    entrance.StepWaiting(true, false);
                Require(entrance.NativeState == 1 && entrance.LinkedActivationRequired &&
                    !queenRecovery.CanReceiveHostWeaponContact,
                    "Queen activated before authentic linked actor and camera callbacks.");
                var linked = _rac1QueenLinkedGate
                    ?? throw new InvalidOperationException("Original class1021 linked gate parents missing.");
                TickRac1QueenLinkedChildren();
                var gateTuning = _rac1QueenGateNative
                    ?? throw new InvalidOperationException("Original class1021 SDA tuning missing.");
                Require(gateTuning is
                {
                    AngularApproachDegrees: 270f,
                    AngularBrakeDegrees: 360f, AngularMaximumDegrees: 180f,
                    InitialProximityGate: 0f
                } &&
                    Math.Abs(gateTuning.ChildRadialDistance - 3.109f) < .0001f,
                    "Retail LEVEL6 executable SDA tuning differs.");
                Require(linked.OriginalParentCount == 2 && linked.RuntimeChildCount == 14 &&
                    _rac1QueenLinkedChildVisuals.Count == 14 &&
                    linked.OriginalParentIds.SequenceEqual(new[] { 573, 578 }),
                    "Source Queen linked class1021 retail-mesh children do not match source population.");
                foreach (int parentId in linked.OriginalParentIds)
                {
                    NVector3 nativeParent = linked.Get(parentId).NativePosition;
                    Require(linked.Get(parentId).NativeState == 2 &&
                        linked.Get(parentId).Children.Count == 7 &&
                        linked.Get(parentId).Children.All(child => child.NativeState == 3 &&
                            _rac1QueenLinkedChildVisuals.TryGetValue(child.RuntimeId, out var visual) &&
                            IsInstanceValid(visual) && visual.Visible &&
                            visual.GetChildren().OfType<MeshInstance3D>().Any(m => m.Mesh is not null) &&
                            Math.Abs(visual.GlobalPosition.DistanceTo(
                                _rac1QueenLinkedNodes[parentId].Root.GlobalPosition) -
                                gateTuning.ChildRadialDistance) < .001f),
                        "Seven original class1021 source-mesh children lack native 3.109-unit offsets.");
                    Require(linked.Step(parentId, nativeParent + NVector3.UnitX * 6, true, 0)
                            .NativeState == 2 &&
                        linked.Step(parentId, nativeParent, false, 0).NativeState == 2,
                        "Native strict six-unit/globally gated class1021 trigger admitted early.");
                }
                // The authored parent and its children now tick during normal
                // play. Stage only Ratchet's position; the host must read it,
                // evaluate the original strict <6 gate and admit state4 itself.
                Vector3 savedQueenGatePlayer = _player!.GlobalPosition;
                try
                {
                    foreach (int parentId in linked.OriginalParentIds)
                    {
                        _player.GlobalPosition = _rac1QueenLinkedNodes[parentId].Root.GlobalPosition;
                        TickRac1QueenLinkedChildren();
                        Require(linked.Get(parentId).NativeState == 4,
                            "Normal Godot class1021 update did not admit native player proximity.");
                    }
                }
                finally
                {
                    _player.GlobalPosition = savedQueenGatePlayer;
                }
                foreach (int parentId in linked.OriginalParentIds)
                {
                    NVector3 nativeParent = linked.Get(parentId).NativePosition;
                    Require(linked.Get(parentId).NativeState == 4 &&
                        linked.Get(parentId).Children.All(child => child.NativeState == 5) &&
                        !linked.ActivateFromQueen(parentId),
                        "Source class1021 trigger failed parent state4 and child state5.");
                    for (int j = 0; j < 8 && linked.Get(parentId).NativeState != 6; j++)
                        linked.Step(parentId, nativeParent, false, .2f);
                    Require(linked.Get(parentId) is { NativeState: 6, Visible: false },
                        "Native class1021 parent did not reach hidden state6.");
                    foreach (var child in linked.Get(parentId).Children)
                    {
                        linked.StepChild(child.RuntimeId, 1.1f);
                        Require(linked.GetChildState(child.RuntimeId) == 7,
                            "Independent native child state5 failed to rotate and hide.");
                    }
                    SynchronizeRac1QueenLinkedParents();
                    Require(linked.Get(parentId).Children.All(child =>
                        !_rac1QueenLinkedChildVisuals[child.RuntimeId].Visible),
                        "Native child state7 failed to hide its original class1021 visual.");
                    Require(!_rac1QueenLinkedNodes[parentId].Root.Visible,
                        "Original linked class1021 parent mesh failed to hide.");
                }
                Require(linked.RuntimeChildCount == 14,
                    "Queen linked class1021 child creation changed retail authored source population.");
                foreach (int parentId in linked.OriginalParentIds)
                    Require(linked.ActivateFromQueen(parentId) &&
                        linked.Get(parentId) is { NativeState: 8, Visible: true } &&
                        linked.Get(parentId).Children.All(child => child.NativeState == 9) &&
                        !linked.ActivateFromQueen(parentId),
                        "Queen linked native activation failed source parent6->8 / child5->9.");
                SynchronizeRac1QueenLinkedParents();
                Require(linked.OriginalParentIds.All(id => _rac1QueenLinkedNodes[id].Root.Visible) &&
                    _rac1QueenLinkedChildVisuals.Count == 14 &&
                    _rac1QueenLinkedChildVisuals.Values.All(node => node.Visible),
                    "Original class1021 parent or child visibility did not restore on Queen activation.");
                GD.Print("[frog-smoke] class1021 i573/i578: fourteen independent retail-mesh children 1->3->5->7(hidden)->9(visible), parents0->2->4->6(hidden)->8(visible) PASS");
                entrance.StepWaiting(true, false, linkedActorAndCameraCallbacksCommitted: true);
                Require(entrance.NativeState == 2, "Queen did not enter native airborne state2.");
                float sourceLandingZ = _rac1QueenAuthoredNativeZ;
                Require(Math.Abs(ToEffectNative(queenNode.Root.GlobalPosition).Z -
                    sourceLandingZ - Rac1AlienQueenFlight.SourceHeight) < .001f,
                    "Native Queen state0 model placement was not elevated 17.5 units.");
                TickRac1QueenFlight();
                Require(_rac1QueenFlight is { Sequence: 8, NativeState: 2 } &&
                    Math.Abs(_rac1QueenFlight.Z - sourceLandingZ -
                        Rac1AlienQueenFlight.SourceHeight) < .001f &&
                    queenRecovery.NativeState == 2 &&
                    _rac1QueenScriptedCamera is { Current: true } &&
                    GetViewport().GetCamera3D() == _rac1QueenScriptedCamera &&
                    _activeCamera == _rac1QueenScriptedCamera &&
                    _player!.Rac1RemoteControlActive,
                    "Original Queen failed elevated flight or real scripted-camera ownership.");
                var stolen = new Camera3D { Name = "QueenCameraOwnerConflictTest" };
                _sceneResult!.Root.AddChild(stolen);
                stolen.MakeCurrent();
                TickRac1QueenFlight();
                Require(_rac1QueenFlight?.NativeState == 2 &&
                    _rac1QueenScriptedCamera is { Current: false } &&
                    GetViewport().GetCamera3D() == stolen,
                    "Queen camera falsely accepted a cue when another owner took the viewport.");
                // Cancel while another camera STILL owns the viewport. The
                // Queen must release its input lock and stale _activeCamera
                // without stealing this unrelated camera's lease.
                EndRac1QueenScriptedCamera();
                Require(_rac1QueenScriptedCamera is null &&
                    !_player!.Rac1RemoteControlActive &&
                    GetViewport().GetCamera3D() == stolen &&
                    _activeCamera == _player.Camera &&
                    _rac1QueenFlight?.NativeState == 2,
                    "Queen cancellation stole an external camera or left input locked.");
                stolen.QueueFree();
                _player.Camera.MakeCurrent();
                TickRac1QueenFlight();
                Require(_rac1QueenScriptedCamera is { Current: true } &&
                    _player.Rac1RemoteControlActive && _rac1QueenFlight?.NativeState == 2,
                    "Queen camera could not reacquire after an external lease ended.");
                // Visibomb keeps its independent input lock: releasing the
                // Queen lease must never disable another owner's controls.
                BeginRac1GuidedCamera();
                TickRac1QueenFlight();
                Require(_rac1MissileCamera is { Current: true } &&
                    _rac1QueenFlight?.NativeState == 2 &&
                    GetViewport().GetCamera3D() == _rac1MissileCamera,
                    "Queen stole Visibomb camera or falsely finished its sequence.");
                EndRac1QueenScriptedCamera();
                Require(_rac1QueenScriptedCamera is null &&
                    _player.Rac1RemoteControlActive &&
                    _activeCamera == _player.Camera &&
                    GetViewport().GetCamera3D() == _rac1MissileCamera,
                    "Queen cancellation released Visibomb input or stole its viewport.");
                EndRac1GuidedCamera();
                Require(!_player.Rac1RemoteControlActive && _player.Camera.Current,
                    "Visibomb camera did not release its own input lock.");
                TickRac1QueenFlight();
                Require(_rac1QueenScriptedCamera is { Current: true } &&
                    _player.Rac1RemoteControlActive && _rac1QueenFlight?.NativeState == 2,
                    "Queen camera could not reacquire after Visibomb handoff.");
                EndRac1QueenScriptedCamera();
                Require(_rac1QueenScriptedCamera is null &&
                    !_player.Rac1RemoteControlActive && _player.Camera.Current &&
                    _activeCamera == _player.Camera &&
                    GetViewport().GetCamera3D() == _player.Camera,
                    "Queen owned-camera cancellation failed to restore Ratchet.");
                TickRac1QueenFlight();
                Require(_rac1QueenScriptedCamera is { Current: true } &&
                    _player.Rac1RemoteControlActive && _rac1QueenFlight?.NativeState == 2,
                    "Queen camera could not reacquire after owned cancellation.");
                int flightTicks = 1;
                while (flightTicks++ < 1000 &&
                    !(_rac1QueenFlight?.CameraCompletionCandidate ?? false))
                    TickRac1QueenFlight();
                Require(flightTicks < 1000 &&
                    _rac1QueenFlight is { Sequence: 1, NativeState: 7, CameraBlend: 1f } &&
                    Math.Abs(_rac1QueenFlight.Z - sourceLandingZ) < .001f &&
                    _rac1QueenPendingCamera is { CanCompleteWithAppliedCamera: true } &&
                    entrance.NativeState == 7 &&
                    _rac1QueenScriptedCamera is null &&
                    !_player!.Rac1RemoteControlActive &&
                    _player.Camera.Current && _activeCamera == _player.Camera &&
                    GetViewport().GetCamera3D() == _player.Camera &&
                    entrance.ObserveLanding(false, true).NativeState == 7 &&
                    entrance.ObserveLanding(false, false).NativeState == 8,
                    "Native Queen camera takeover, source completion and player-camera restore failed.");
                GD.Print($"[frog-smoke] Queen class1051 source gravity +17.5->ground, retail seq8->9->6->1, real camera takeover/restore after {flightTicks} native ticks, 2->7->8 PASS");
                queenRecovery.AdmitNativeState(8, 3);
                queenContact = (queenMesh.GlobalTransform * queenMesh.GetAabb()).GetCenter();
                _rac1EquipmentGeometryFrame = ulong.MaxValue;
                Require(EquipmentGeometry.Any(entry => entry.Node == queenNode) &&
                    IsRac1WeaponAimTarget(queenNode),
                    "Native combat-ready Queen was excluded from ranged-weapon retail mesh targeting.");
                for (int hit = 0; hit < 10; hit++)
                {
                    // Ten independent 1-damage wrench swings; each new
                    // swing clears the per-attack duplicate-contact marker.
                    _rac1WrenchHitTargets.Remove(queenKey);
                    ResolveRac1QueenWrenchContacts(queenContact, queenContact,
                        queenContact, queenContact, .01f, wrench);
                    Require(queenRecovery.Health == 29f - hit &&
                        _rac1DamageTransport.Sequence == beforeQueenHits + hit + 1,
                        "Original Queen wrench contact was rejected or mis-scaled.");
                }
                ResolveRac1QueenWrenchContacts(queenContact, queenContact,
                    queenContact, queenContact, .01f, wrench);
                Require(queenRecovery is { Health: 20f, Recovering: true } &&
                    queenRecovery.Timer == Rac1AlienQueenRecovery.RecoveryTicks &&
                    _rac1DamageTransport.Sequence == beforeQueenHits + 10,
                    "Queen wrench coalescing or 20-HP recovery is incorrect.");
                GD.Print("[frog-smoke] class1051 retail mesh, 10 source class71 wrench hits, damage transport, native 20HP recovery PASS");
                for (int tick = 0; tick < Rac1AlienQueenRecovery.RecoveryTicks - 1; tick++)
                {
                    TickRac1QueenRecovery();
                    Require(queenRecovery.NativeState == 8 &&
                        queen.Members.All(id => frogs.Get(id).NativeState == 15),
                        "Alien Queen Frogs woke before native recovery elapsed.");
                }
                TickRac1QueenRecovery();
                Require(queenRecovery is { NativeState: 3, Sequence: 6, Health: 19.9f } &&
                    _rac1QueenAnimation is { Sequence: 6 },
                    "Native Queen 20-HP recovery failed to select original sequence6.");
                int animationTicks = 0;
                while (queenRecovery.Sequence == 6 && animationTicks++ < 2400)
                    TickRac1QueenRecovery();
                Require(animationTicks < 2400 && queenRecovery is
                { NativeState: 3, Sequence: 1 } &&
                    _rac1QueenAnimation is { Sequence: 1 } &&
                    queen.Members.All(id => frogs.Get(id).NativeState == 6) &&
                    ObserveRac1QueenFrogEvent(3, 6, true) == 0,
                    "Real retail Queen sequence6 completion did not launch exactly twelve source Frogs.");
                Require(queen.Members.All(id =>
                {
                    NVector3 before = originalQueenMembers[id];
                    NVector3 after = ToEffectNative(_rac1Enemies[id].Node.Root.GlobalPosition);
                    return frogs.Get(id) is { NativeState: 6, Presented: true, Lives: 1 } &&
                        Math.Abs(after.X - before.X) < .001f &&
                        Math.Abs(after.Y - before.Y) < .001f &&
                        after.Z - before.Z is >= 15f and <= 20.001f &&
                        _rac1Enemies[id].Body.CollisionLayer == 1 &&
                        _rac1Enemies[id].Body.CollisionMask == 1;
                }), "Queen group22 did not preserve all twelve original airborne Moby identities.");
                Require(Enumerable.Range(387, 4).All(id =>
                    frogs.Get(id) is { NativeState: 15, Presented: false }),
                    "Unowned Frog group8 was awakened by the Queen event.");
                GD.Print($"[frog-smoke] Alien Queen i626 staged hit, native 30-tick 20-HP recovery, retail seq6 wrap after {animationTicks} ticks; group22 twelve Frogs +15..20z and group8 parked PASS");
                var dormantLaunch = _rac1Enemies[448];
                int launchId = 448;
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
