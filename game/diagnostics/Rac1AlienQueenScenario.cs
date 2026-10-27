using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class Rac1AlienQueenScenario(MovementDiagnosticContext context, IRac1EnemyDiagnosticFacts enemies,
    IRac1EnemyDiagnosticActions actors, IRac1FrogDiagnosticFixture frogs, IRac1QueenDiagnosticFixture queenFixture)
    : Rac1EncounterScenarioSupport(context, enemies, actors)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality",
        Justification = "The source health/state constants and completed camera blend are exact native programme states, not measured trajectory tolerances.")]
    public Task RunAsync()
    {
        Driver.Own(queenFixture.PreserveCameraOwnership());

        // Stage only an admitted native combat hit on the real
        // Queen. The retail health helper then owns the 30-tick
        // threshold recovery into state3; the original class1051
        // animation clock supplies sequence6 completion naturally.
        // Boss movement, hitbox and camera gameplay remain unhosted.
        var queen = queenFixture.ReadGroup();
        Require(queen.QueenInstance == 626 && queen.GroupIndex == 22 &&
            queen.Members.SequenceEqual(Enumerable.Range(448, 12)),
            "Original Alien Queen source group does not match retail.");
        var originalQueenMembers = queen.Members.ToDictionary(id => id,
            id => ToEffectNative(Enemy(id).Read().Position));
        Require(queenFixture.ObserveFrogEvent(3, 6, false) == 0 &&
            queenFixture.ObserveFrogEvent(3, 5, true) == 0 &&
            queen.Members.All(id => frogs.Get(id).NativeState == 15),
            "Queen wrong-sequence/noncompleted animation prematurely launched Frogs");

        Require(queenFixture.ReadRecovery() is { NativeState: 1, Health: 30f },
            "Original Queen did not retain native 30 health / pre-encounter state.");
        // Boss entrance/camera is explicitly staged. Afterwards
        // source class71 damage is admitted via real retail model
        // geometry sweeps and the shared damage transport.

        Vector3 queenContact = queenFixture.ReadBody().MeshCentre;
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.AnimatedSweepHostGeometry, 1f,
            Rac1WrenchAttackProfile.NativeDamageFlags);
        long beforeQueenHits = queenFixture.ReadDamageSequence();
        queenFixture.ResolveWrenchContact(new(queenContact, queenContact, queenContact, queenContact, .01f), wrench);
        Require(queenFixture.ReadRecovery().Health == 30f &&
            queenFixture.ReadDamageSequence() == beforeQueenHits,
            "Queen accepted an attack before native combat admission.");
        // Source-mapped entrance: two class1021 actors and camera
        // selector are not hosted yet, so their success is supplied
        // explicitly. Unlike the old direct state8 override, the
        // 10-tick timer, linked admission and flight/landing handoff
        // are independently exercised before combat becomes valid.

        Require(queenFixture.ReadEntrance() is
        {
            FirstLinkedInstance: 573,
            SecondLinkedInstance: 578, LinkedEventSelector: 25,
            CameraSelector: 59, NativePathSelector: 54, NativeState: 1
        },
            "Queen entrance selectors differ from the original LEVEL6 data.");
        Require(queenFixture.StepWaiting(false, false, true).NativeState == 1 &&
            queenFixture.StepWaiting(true, true, true).NativeState == 1,
            "Queen activated without her native class0 target and mode gate.");
        queenFixture.StepWaiting(true, false);
        for (int tick = 0; tick < Rac1AlienQueenEntrance.StartupDelayTicks; tick++)
            queenFixture.StepWaiting(true, false);
        Require(queenFixture.ReadEntrance().NativeState == 1 && queenFixture.ReadEntrance().LinkedActivationRequired &&
            !queenFixture.ReadRecovery().CanReceiveHostWeaponContact,
            "Queen activated before authentic linked actor and camera callbacks.");

        queenFixture.TickLinkedChildren();
        var gateTuning = queenFixture.ReadGateTuning();
        Require(gateTuning is
        {
            AngularApproachDegrees: 270f,
            AngularBrakeDegrees: 360f, AngularMaximumDegrees: 180f,
            InitialProximityGate: 0f
        } &&
            Math.Abs(gateTuning.ChildRadialDistance - 3.109f) < .0001f,
            "Retail LEVEL6 executable SDA tuning differs.");
        Require(queenFixture.OriginalParentCount == 2 && queenFixture.RuntimeChildCount == 14 &&
            queenFixture.ReadLinkedChildVisuals().Length == 14 &&
            queenFixture.OriginalParentIds.SequenceEqual(new[] { 573, 578 }),
            "Source Queen linked class1021 retail-mesh children do not match source population.");
        foreach (int parentId in queenFixture.OriginalParentIds)
        {
            Vector3 nativeParent = queenFixture.ReadLinkedParent(parentId).NativePosition;
            Require(queenFixture.ReadLinkedParent(parentId).NativeState == 2 &&
                queenFixture.ReadLinkedParent(parentId).Children.Length == 7 &&
                queenFixture.ReadLinkedParent(parentId).Children.All(child => child.NativeState == 3 &&
                    queenFixture.ReadLinkedChildVisual(child.RuntimeId) is { Visible: true, HasMesh: true } visual &&
                    Math.Abs(visual.Position.DistanceTo(
                        queenFixture.ReadLinkedParentVisual(parentId).Position) -
                        gateTuning.ChildRadialDistance) < .001f),
                "Seven original class1021 source-mesh children lack native 3.109-unit offsets.");
            Require(queenFixture.StepLinkedParent(parentId, nativeParent + Vector3.UnitX * 6, true, 0)
                    .NativeState == 2 &&
                queenFixture.StepLinkedParent(parentId, nativeParent, false, 0).NativeState == 2,
                "Native strict six-unit/globally gated class1021 trigger admitted early.");
        }
        // The authored parent and its children now tick during normal
        // play. Stage only Ratchet's position; the host must read it,
        // evaluate the original strict <6 gate and admit state4 itself.
        Vector3 savedQueenGatePlayer = Player.Read().GlobalPosition;
        try
        {
            foreach (int parentId in queenFixture.OriginalParentIds)
            {
                Player.SetPosition(queenFixture.ReadLinkedParentVisual(parentId).Position);
                queenFixture.TickLinkedChildren();
                Require(queenFixture.ReadLinkedParent(parentId).NativeState == 4,
                    "Normal Godot class1021 update did not admit native player proximity.");
            }
        }
        finally
        {
            Player.SetPosition(savedQueenGatePlayer);
        }
        foreach (int parentId in queenFixture.OriginalParentIds)
        {
            Vector3 nativeParent = queenFixture.ReadLinkedParent(parentId).NativePosition;
            Require(queenFixture.ReadLinkedParent(parentId).NativeState == 4 &&
                queenFixture.ReadLinkedParent(parentId).Children.All(child => child.NativeState == 5) &&
                !queenFixture.ActivateLinkedParent(parentId),
                "Source class1021 trigger failed parent state4 and child state5.");
            for (int j = 0; j < 8 && queenFixture.ReadLinkedParent(parentId).NativeState != 6; j++)
                queenFixture.StepLinkedParent(parentId, nativeParent, false, .2f);
            Require(queenFixture.ReadLinkedParent(parentId) is { NativeState: 6, Visible: false },
                "Native class1021 parent did not reach hidden state6.");
            foreach (var child in queenFixture.ReadLinkedParent(parentId).Children)
            {
                queenFixture.StepLinkedChild(child.RuntimeId, 1.1f);
                Require(queenFixture.ReadLinkedChildState(child.RuntimeId) == 7,
                    "Independent native child state5 failed to rotate and hide.");
            }
            queenFixture.SynchronizeLinkedParents();
            Require(queenFixture.ReadLinkedParent(parentId).Children.All(child =>
                !(queenFixture.ReadLinkedChildVisual(child.RuntimeId)?.Visible == true)),
                "Native child state7 failed to hide its original class1021 visual.");
            Require(!queenFixture.ReadLinkedParentVisual(parentId).Visible,
                "Original linked class1021 parent mesh failed to hide.");
        }
        Require(queenFixture.RuntimeChildCount == 14,
            "Queen linked class1021 child creation changed retail authored source population.");
        foreach (int parentId in queenFixture.OriginalParentIds)
            Require(queenFixture.ActivateLinkedParent(parentId) &&
                queenFixture.ReadLinkedParent(parentId) is { NativeState: 8, Visible: true } &&
                queenFixture.ReadLinkedParent(parentId).Children.All(child => child.NativeState == 9) &&
                !queenFixture.ActivateLinkedParent(parentId),
                "Queen linked native activation failed source parent6->8 / child5->9.");
        queenFixture.SynchronizeLinkedParents();
        Require(queenFixture.OriginalParentIds.All(id => queenFixture.ReadLinkedParentVisual(id).Visible) &&
            queenFixture.ReadLinkedChildVisuals().Length == 14 &&
            queenFixture.ReadLinkedChildVisuals().All(node => node.Visible),
            "Original class1021 parent or child visibility did not restore on Queen activation.");
        Log("[frog-smoke] class1021 i573/i578: fourteen independent retail-mesh children 1->3->5->7(hidden)->9(visible), parents0->2->4->6(hidden)->8(visible) PASS");
        queenFixture.StepWaiting(true, false, linkedActorAndCameraCallbacksCommitted: true);
        Require(queenFixture.ReadEntrance().NativeState == 2, "Queen did not enter native airborne state2.");
        float sourceLandingZ = queenFixture.ReadBody().AuthoredNativeZ;
        Require(Math.Abs(ToEffectNative(queenFixture.ReadBody().Position).Z -
            sourceLandingZ - Rac1AlienQueenFlight.SourceHeight) < .001f,
            "Native Queen state0 model placement was not elevated 17.5 units.");
        queenFixture.TickFlight();
        Require(queenFixture.ReadFlight() is { Sequence: 8, NativeState: 2 } &&
            Math.Abs(queenFixture.ReadFlight()!.Z - sourceLandingZ -
                Rac1AlienQueenFlight.SourceHeight) < .001f &&
            queenFixture.ReadRecovery().NativeState == 2 &&
            queenFixture.ReadCamera().QueenCurrent &&
            queenFixture.ReadCamera().Viewport == Rac1DiagnosticCameraOwner.Queen &&
            queenFixture.ReadCamera().Active == Rac1DiagnosticCameraOwner.Queen &&
            queenFixture.ReadCamera().RemoteControl,
            "Original Queen failed elevated flight or real scripted-camera ownership.");
        var stolen = Driver.Own(queenFixture.CreateConflictingCamera());
        // Ordinary flight ticking (not diagnostic cleanup) must
        // notice the lost lease, release Ratchet and leave the new
        // camera's viewport ownership untouched.
        queenFixture.TickFlight();
        Require(queenFixture.ReadFlight()?.NativeState == 2 &&
            !queenFixture.ReadCamera().QueenPresent &&
            !queenFixture.ReadCamera().RemoteControl &&
            queenFixture.ReadCamera().Viewport == Rac1DiagnosticCameraOwner.Conflict &&
            queenFixture.ReadCamera().Active == Rac1DiagnosticCameraOwner.Player,
            "Queen did not automatically retire a stolen camera lease.");
        stolen.Dispose();
        queenFixture.MakePlayerCameraCurrent();
        queenFixture.TickFlight();
        Require(queenFixture.ReadCamera().QueenCurrent &&
            queenFixture.ReadCamera().RemoteControl && queenFixture.ReadFlight()?.NativeState == 2,
            "Queen camera could not reacquire after an external lease ended.");
        // Visibomb keeps its independent input lock: releasing the
        // Queen lease must never disable another owner's controls.
        queenFixture.BeginGuidedCamera();
        queenFixture.TickFlight();
        Require(queenFixture.ReadCamera().GuidedCurrent &&
            queenFixture.ReadFlight()?.NativeState == 2 &&
            !queenFixture.ReadCamera().QueenPresent &&
            queenFixture.ReadCamera().RemoteControl &&
            queenFixture.ReadCamera().Active == Rac1DiagnosticCameraOwner.Player &&
            queenFixture.ReadCamera().Viewport == Rac1DiagnosticCameraOwner.Guided,
            "Queen did not relinquish its lease while preserving Visibomb's input lock.");
        // Even on the next normal tick, Queen must not reacquire
        // until Visibomb itself has finished and restored Ratchet.
        queenFixture.TickFlight();
        Require(!queenFixture.ReadCamera().QueenPresent &&
            queenFixture.ReadCamera().RemoteControl &&
            queenFixture.ReadCamera().Viewport == Rac1DiagnosticCameraOwner.Guided,
            "Queen reacquired the viewport during Visibomb remote control.");
        queenFixture.EndGuidedCamera();
        Require(!queenFixture.ReadCamera().RemoteControl && queenFixture.ReadCamera().PlayerCurrent,
            "Visibomb camera did not release its own input lock.");
        queenFixture.TickFlight();
        Require(queenFixture.ReadCamera().QueenCurrent &&
            queenFixture.ReadCamera().RemoteControl && queenFixture.ReadFlight()?.NativeState == 2,
            "Queen camera could not reacquire after Visibomb handoff.");
        queenFixture.EndQueenCamera();
        Require(!queenFixture.ReadCamera().QueenPresent &&
            !queenFixture.ReadCamera().RemoteControl && queenFixture.ReadCamera().PlayerCurrent &&
            queenFixture.ReadCamera().Active == Rac1DiagnosticCameraOwner.Player &&
            queenFixture.ReadCamera().Viewport == Rac1DiagnosticCameraOwner.Player,
            "Queen owned-camera cancellation failed to restore Ratchet.");
        queenFixture.TickFlight();
        Require(queenFixture.ReadCamera().QueenCurrent &&
            queenFixture.ReadCamera().RemoteControl && queenFixture.ReadFlight()?.NativeState == 2,
            "Queen camera could not reacquire after owned cancellation.");
        int flightTicks = 1;
        while (flightTicks++ < 1000 &&
            !(queenFixture.ReadFlight()?.CameraCompletionCandidate ?? false))
            queenFixture.TickFlight();
        Require(flightTicks < 1000 &&
            queenFixture.ReadFlight() is { Sequence: 1, NativeState: 7, CameraBlend: 1f } &&
            Math.Abs(queenFixture.ReadFlight()!.Z - sourceLandingZ) < .001f &&
            queenFixture.ReadPendingCamera() is { CanCompleteWithAppliedCamera: true } &&
            queenFixture.ReadEntrance().NativeState == 7 &&
            !queenFixture.ReadCamera().QueenPresent &&
            !queenFixture.ReadCamera().RemoteControl &&
            queenFixture.ReadCamera().PlayerCurrent && queenFixture.ReadCamera().Active == Rac1DiagnosticCameraOwner.Player &&
            queenFixture.ReadCamera().Viewport == Rac1DiagnosticCameraOwner.Player &&
            queenFixture.ObserveLanding(false, true).NativeState == 7 &&
            queenFixture.ObserveLanding(false, false).NativeState == 8,
            "Native Queen camera takeover, source completion and player-camera restore failed.");
        Log($"[frog-smoke] Queen class1051 source gravity +17.5->ground, retail seq8->9->6->1, real camera takeover/restore after {flightTicks} native ticks, 2->7->8 PASS");
        queenFixture.AdmitRecoveryState(8, 3);
        queenContact = queenFixture.ReadBody().MeshCentre;
        queenFixture.InvalidateEquipmentGeometry();
        Require(queenFixture.GeometryContainsQueen &&
            queenFixture.AimTargetAdmitted,
            "Native combat-ready Queen was excluded from ranged-weapon retail mesh targeting.");
        for (int hit = 0; hit < 10; hit++)
        {
            // Ten independent 1-damage wrench swings; each new
            // swing clears the per-attack duplicate-contact marker.
            queenFixture.ForgetWrenchContact();
            queenFixture.ResolveWrenchContact(new(queenContact, queenContact, queenContact, queenContact, .01f), wrench);
            Require(queenFixture.ReadRecovery().Health == 29f - hit &&
                queenFixture.ReadDamageSequence() == beforeQueenHits + hit + 1,
                "Original Queen wrench contact was rejected or mis-scaled.");
        }
        queenFixture.ResolveWrenchContact(new(queenContact, queenContact, queenContact, queenContact, .01f), wrench);
        Require(queenFixture.ReadRecovery() is { Health: 20f, Recovering: true } &&
            queenFixture.ReadRecovery().Timer == Rac1AlienQueenRecovery.RecoveryTicks &&
            queenFixture.ReadDamageSequence() == beforeQueenHits + 10,
            "Queen wrench coalescing or 20-HP recovery is incorrect.");
        Log("[frog-smoke] class1051 retail mesh, 10 source class71 wrench hits, damage transport, native 20HP recovery PASS");
        for (int tick = 0; tick < Rac1AlienQueenRecovery.RecoveryTicks - 1; tick++)
        {
            queenFixture.TickRecovery();
            Require(queenFixture.ReadRecovery().NativeState == 8 &&
                queen.Members.All(id => frogs.Get(id).NativeState == 15),
                "Alien Queen Frogs woke before native recovery elapsed.");
        }
        queenFixture.TickRecovery();
        Require(queenFixture.ReadRecovery() is { NativeState: 3, Sequence: 6, Health: 19.9f } &&
            queenFixture.ReadAnimationSequence() == 6,
            "Native Queen 20-HP recovery failed to select original sequence6.");
        int animationTicks = 0;
        while (queenFixture.ReadRecovery().Sequence == 6 && animationTicks++ < 2400)
            queenFixture.TickRecovery();
        Require(animationTicks < 2400 && queenFixture.ReadRecovery() is
        { NativeState: 3, Sequence: 1 } &&
            queenFixture.ReadAnimationSequence() == 1 &&
            queen.Members.All(id => frogs.Get(id).NativeState == 6) &&
            queenFixture.ObserveFrogEvent(3, 6, true) == 0,
            "Real retail Queen sequence6 completion did not launch exactly twelve source Frogs.");
        Require(queen.Members.All(id =>
        {
            Vector3 before = originalQueenMembers[id];
            Vector3 after = ToEffectNative(Enemy(id).Read().Position);
            return frogs.Get(id) is { NativeState: 6, Presented: true, Lives: 1 } &&
                Math.Abs(after.X - before.X) < .001f &&
                Math.Abs(after.Y - before.Y) < .001f &&
                after.Z - before.Z is >= 15f and <= 20.001f &&
                Enemy(id).Read().CollisionLayer == 1 &&
                Enemy(id).Read().CollisionMask == 1;
        }), "Queen group22 did not preserve all twelve original airborne Moby identities.");
        Require(Enumerable.Range(387, 4).All(id =>
            frogs.Get(id) is { NativeState: 15, Presented: false }),
            "Unowned Frog group8 was awakened by the Queen event.");
        Log($"[frog-smoke] Alien Queen i626 staged hit, native 30-tick 20-HP recovery, retail seq6 wrap after {animationTicks} ticks; group22 twelve Frogs +15..20z and group8 parked PASS");
        var dormantLaunch = Enemy(448);
        int launchId = 448;
        Vector3 beforeFlight = dormantLaunch.Read().Position;
        frogs.TickFrog(dormantLaunch.Key);
        Require(frogs.Get(launchId).NativeState == 6 &&
            dormantLaunch.Read().Position.Y > beforeFlight.Y,
            "Native state6 launch gravity/rise never moved the hosted Frog");
        // Landing fact supplied explicitly in the smoke fixture.
        Actors.SetGrounded(dormantLaunch.Key, true);
        frogs.TickFrog(dormantLaunch.Key);
        Require(frogs.Get(launchId).NativeState == 7,
            "Confirmed native landing did not enter Frog state7");
        Log($"[frog-smoke] original hidden i{launchId} explicitly launched state15->6->7; mesh/contact and gravity PASS");

        return Task.CompletedTask;
    }
}
