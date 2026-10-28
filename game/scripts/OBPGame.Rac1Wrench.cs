using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class OBPGame
{
    private readonly Rac1WrenchThrowSession _rac1WrenchThrow = new();
    private readonly HashSet<Rac1MobyRuntimeKey> _rac1WrenchHitTargets = [];
    private long _rac1WrenchHostGeneration;
    private Vector3? _rac1PreviousWrenchHand;
    private Vector3? _rac1PreviousWrenchTip;

    private void ResetRac1WrenchHost()
    {
        ResetRac1PlayerAudio(includeReactions: false);
        _rac1WrenchThrow.Reset();
        _rac1WrenchHitTargets.Clear();
        _rac1WrenchHostGeneration = 0;
        _rac1PreviousWrenchHand = _rac1PreviousWrenchTip = null;
        if (_player is not null) _player.Rac1WrenchThrown = false;
    }

    // Runs after the player's fixed-update animation sample, so the visible
    // wrench, swept contact points and return socket use the same pose.
    private void OnRac1WrenchPhysicsUpdated(Rac1WrenchAttackSession.Frame frame)
    {
        if (_player is null || !_avatarPresentation.TryWrenchContactPose(out var wrenchPose))
            return;
        if (!PlayerAlive)
        {
            ResetRac1WrenchHost();
            return;
        }
        if (frame.Active && frame.Generation != _rac1WrenchHostGeneration)
        {
            _rac1HeldItem.HoldWrench();
            _rac1WrenchHostGeneration = frame.Generation;
            _rac1WrenchHitTargets.Clear();
            _rac1Nanotech.Actions.ApplyWrenchAttack(Rac1WrenchAttackProfile.Profiles[frame.ProfileId]);
            _player.Rac1GameplayState = _rac1Nanotech.Probe();
        }
        else if (!frame.Active && _rac1Nanotech.Actions.Probe().CurrentNativeState is 0x13 or 0x14 or 0x15)
        {
            _rac1Nanotech.Actions.EnterState(Rac1PlayerActionDomain.Neutral);
            _player.Rac1GameplayState = _rac1Nanotech.Probe();
        }

        Vector3 hand = wrenchPose.Hand;
        Vector3 tip = wrenchPose.Tip;
        if (frame.ReleaseWrench)
        {
            float yaw = (float)_player.Rac1CurrentYaw;
            _rac1WrenchThrow.Begin(ToWrenchNative(hand), new(-Mathf.Cos(yaw), Mathf.Sin(yaw), 0));
        }
        if (_rac1WrenchThrow.Active)
        {
            Vector3 previous = WrenchWorldPosition();
            bool obstruction = false;
            if (_rac1WrenchThrow.NativeState == 10)
            {
                float yaw = (float)_player.Rac1CurrentYaw;
                Vector3 end = previous + new Vector3(-Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * _rac1WrenchThrow.StepLength;
                var query = PhysicsRayQueryParameters3D.Create(previous, end);
                query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() };
                var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
                obstruction = hit.Count != 0;
                TryPlayRac1WrenchWorldContact(hit);
            }
            _rac1WrenchThrow.Step(ToWrenchNative(hand), obstruction);
            Vector3 next = WrenchWorldPosition();
            ResolveRac1AnimatedWrenchContacts(previous, next, previous, next,
                Rac1WrenchThrowSession.CollisionRadius, damage: 1);
            if (_rac1WrenchThrow.Active)
                _avatarPresentation.UpdateThrownWrenchVisual(next, _rac1WrenchThrow.Spin);
        }
        else if (frame.HitActive)
        {
            // The native four-point hand/tip sweep is recovered. Victim geometry
            // is still a host adapter: rendered mesh bounds plus a small margin,
            // not the unrecovered PS2 narrow-phase volume/query implementation.
            ResolveRac1AnimatedWrenchContacts(_rac1PreviousWrenchHand ?? hand,
                _rac1PreviousWrenchTip ?? tip, hand, tip, .05f, frame.Damage);
            if (_rac1PlayerAudio?.WrenchImpactAdmitted == true)
            {
                var query = PhysicsRayQueryParameters3D.Create(hand, tip);
                query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() };
                TryPlayRac1WrenchWorldContact(_player.GetWorld3D().DirectSpaceState.IntersectRay(query));
            }
        }
        _player.Rac1WrenchThrown = _rac1WrenchThrow.Active;
        StepRac1WrenchAudio(frame, hand);
        _rac1PreviousWrenchHand = hand;
        _rac1PreviousWrenchTip = tip;
        RefreshRac1WrenchPresentationVisibility();
    }

    private Vector3 WrenchWorldPosition() => new((float)_rac1WrenchThrow.Position.X,
        (float)_rac1WrenchThrow.Position.Z, (float)_rac1WrenchThrow.Position.Y);

    private static Rac1NativeVector3 ToWrenchNative(Vector3 point) => new(point.X, point.Z, point.Y);

    private void ResolveRac1AnimatedWrenchContacts(Vector3 previousHand, Vector3 previousTip,
        Vector3 hand, Vector3 tip, float radius, float damage)
    {
        var result = new Rac1WrenchDamageResult(Rac1WrenchContactPath.AnimatedSweepHostGeometry,
            damage, Rac1WrenchAttackProfile.NativeDamageFlags);
        ResolveRac1FrogCarrierWrenchContacts(previousHand, previousTip, hand, tip, radius);
        ResolveRac1QueenWrenchContacts(previousHand, previousTip, hand, tip, radius, result);
        ActorHost.Combat.ResolveWrench(previousHand, previousTip, hand, tip, radius, damage);
        foreach (var target in _rac1CrateNodes.Concat(Rac1EnemyNodes).Concat(_rac1NovalisBreakableNodes.Values))
        {
            var key = new Rac1MobyRuntimeKey(target.Source.NativeClassId, target.Source.InstanceIndex);
            bool isNovalisProp = Rac1NovalisBreakableSession.IsAdmitted(
                _world?.LevelId ?? -1, target.Source.NativeClassId);
            if (_rac1WrenchHitTargets.Contains(key) || !IsInstanceValid(target.Root) ||
                (isNovalisProp ? _rac1NovalisBreakables.IsDestroyed(key.InstanceIndex) : !IsRac1GameplayActive(target)))
                continue;
            var meshes = _rac1Chickens.TryGetValue(target.Source.InstanceIndex, out var chicken)
                ? chicken.View.GetChildren().OfType<MeshInstance3D>() : target.Root.GetChildren().OfType<MeshInstance3D>();
            bool contact = meshes.Any(mesh =>
            {
                Aabb bounds = (mesh.GlobalTransform * mesh.GetAabb()).Grow(radius);
                return WrenchSegmentIntersects(bounds, previousHand, previousTip) ||
                    WrenchSegmentIntersects(bounds, hand, tip) ||
                    WrenchSegmentIntersects(bounds, previousHand, hand) ||
                    WrenchSegmentIntersects(bounds, previousTip, tip) ||
                    WrenchSegmentIntersects(bounds, previousHand, tip) ||
                    WrenchSegmentIntersects(bounds, previousTip, hand);
            });
            if (!contact) continue;
            bool applied = target.Source.NativeClassId switch
            {
                int nativeClass when Rac1BoltCrate.IsCrateClass(nativeClass) ||
                    nativeClass == Rac1ExplosiveCrateSession.ClassId => TryStrikeRac1Crate(target, result),
                Rac1Class749Hostile.NativeClassId or Rac1Class1440Session.ClassId or
                    Rac1AridiaEnemies.Sandshark or Rac1AridiaEnemies.Constructorbot or Rac1AridiaEnemies.Nest or
                    Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId or Rac1FrogAuthored.ClassId =>
                    TryStrikeRac1Hostile(target, result),
                Rac1NovalisBreakableSession.MushroomClass or Rac1NovalisBreakableSession.RockFormationClass =>
                    TryBreakRac1NovalisProp(target, Rac1NovalisBreakableSession.ContactKind.Wrench),
                _ => false,
            };
            // Per-target coalescing is an explicit bounded host policy. Retail's
            // shared damage-record identity/cooldown producer remains unresolved.
            if (applied)
            {
                _rac1WrenchHitTargets.Add(key);
                // Unknown victim layouts use a generic host impact until their
                // common hit-record producer is recovered independently.
                bool soft = Rac1PlayerAudioSession.WrenchVictimSoftness(target.Source.NativeClassId) ?? false;
                if (_rac1PlayerAudio?.WrenchContact(soft) is { } cue) PlayRac1PlayerSound(cue);
            }
        }
    }

    private static bool WrenchSegmentIntersects(Aabb bounds, Vector3 from, Vector3 to)
        => ActorContactGeometry.WrenchSegmentIntersects(bounds, from, to);
}
