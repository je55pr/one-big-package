using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private readonly Rac1WrenchThrowSession _rac1WrenchThrow = new();
    private readonly HashSet<Rac1MobyRuntimeKey> _rac1WrenchHitTargets = [];
    private long _rac1WrenchHostGeneration;
    private Vector3? _rac1PreviousWrenchHand;
    private Vector3? _rac1PreviousWrenchTip;

    private void ResetRac1WrenchHost()
    {
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
        if (_player is null || _playerAvatarView is null || _rac1WrenchView is null)
            return;
        if (_rac1Nanotech.Probe().IsDead)
        {
            ResetRac1WrenchHost();
            return;
        }
        if (frame.Active && frame.Generation != _rac1WrenchHostGeneration)
        {
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

        Vector3 hand = _playerAvatarView.ToGlobal(_rac1WrenchView.HandLocal);
        Vector3 tip = _playerAvatarView.ToGlobal(_rac1WrenchView.TipLocal);
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
                obstruction = _player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count != 0;
            }
            _rac1WrenchThrow.Step(ToWrenchNative(hand), obstruction);
            Vector3 next = WrenchWorldPosition();
            ResolveRac1AnimatedWrenchContacts(previous, next, previous, next,
                Rac1WrenchThrowSession.CollisionRadius, damage: 1);
            if (_rac1WrenchThrow.Active)
                _rac1WrenchView.StepThrown(next, _rac1WrenchThrow.Spin);
        }
        else if (frame.HitActive)
        {
            // The native four-point hand/tip sweep is recovered. Victim geometry
            // is still a host adapter: rendered mesh bounds plus a small margin,
            // not the unrecovered PS2 narrow-phase volume/query implementation.
            ResolveRac1AnimatedWrenchContacts(_rac1PreviousWrenchHand ?? hand,
                _rac1PreviousWrenchTip ?? tip, hand, tip, .05f, frame.Damage);
        }
        _player.Rac1WrenchThrown = _rac1WrenchThrow.Active;
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
        foreach (var target in _rac1CrateNodes.Concat(_rac1HostileNodes.Values))
        {
            var key = new Rac1MobyRuntimeKey(target.Source.NativeClassId, target.Source.InstanceIndex);
            if (_rac1WrenchHitTargets.Contains(key) || !IsInstanceValid(target.Root) || !IsRac1GameplayActive(target))
                continue;
            bool contact = target.Root.GetChildren().OfType<MeshInstance3D>().Any(mesh =>
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
                int nativeClass when Rac1BoltCrate.IsCrateClass(nativeClass) => TryStrikeRac1Crate(target, result),
                Rac1Class749Hostile.NativeClassId => TryStrikeRac1Hostile(target, result),
                _ => false,
            };
            // Per-target coalescing is an explicit bounded host policy. Retail's
            // shared damage-record identity/cooldown producer remains unresolved.
            if (applied) _rac1WrenchHitTargets.Add(key);
        }
    }

    private static bool WrenchSegmentIntersects(Aabb bounds, Vector3 from, Vector3 to)
    {
        float first = 0, last = 1;
        Vector3 delta = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            float low = bounds.Position[axis], high = bounds.End[axis];
            if (Mathf.Abs(delta[axis]) < 1e-7f)
            {
                if (from[axis] < low || from[axis] > high) return false;
                continue;
            }
            float a = (low - from[axis]) / delta[axis], b = (high - from[axis]) / delta[axis];
            first = Math.Max(first, Math.Min(a, b));
            last = Math.Min(last, Math.Max(a, b));
            if (first > last) return false;
        }
        return true;
    }
}
