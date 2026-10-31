using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Borrowed player-pose/action and audio adapters for the per-world wrench.
/// Wrench motion, swing and victim state are not stored in the engine root.
/// </summary>
internal sealed record Rac1WrenchFramePorts(
    Func<PlayerHost?> Player,
    Func<(Vector3 Hand, Vector3 Tip)?> ContactPose,
    Func<bool> Alive,
    Action ResetAudio,
    Action HoldWrench,
    Action<int> ApplyNativeAttack,
    Action EndNativeAttack,
    Action<Vector3, float> UpdateThrownVisual,
    Action<global::Godot.Collections.Dictionary> WorldContactAudio,
    Func<bool> ImpactAudioAdmitted,
    Action<Rac1WrenchAttackSession.Frame, Rac1WrenchThrowSession, Vector3, Vector3> StepAudio,
    Action RefreshVisibility);

/// <summary>
/// Borrowed world observations and damage authority. The wrench owns its sweep
/// geometry, not enemy/crate gameplay or their reward/health state.
/// </summary>
internal sealed record Rac1WrenchContactPorts(
    Action<Vector3, Vector3, Vector3, Vector3, float, float, Rac1WrenchDamageResult> OtherContacts,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> Targets,
    Func<RuntimeWorldScene.DynamicObjectNode, bool> Eligible,
    Func<int, Rac1EquipmentView?> MorphView,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1WrenchDamageResult, bool> Strike,
    Action<bool> ContactSound);

/// <summary>
/// World-lease R&C1 wrench: native throw flight, attack generation, historical
/// hand/tip samples and bounded per-swing victim admission share a lifetime.
/// The recovered four-point sweep retains the original mesh-bound host policy.
/// </summary>
internal sealed class Rac1WrenchHost(Rac1WrenchFramePorts framePorts, Rac1WrenchContactPorts contactPorts) : IDisposable
{
    private readonly Rac1WrenchThrowSession _throw = new();
    private readonly HashSet<Rac1MobyRuntimeKey> _victims = [];
    private long _generation;
    private Vector3? _previousHand;
    private Vector3? _previousTip;
    private bool _disposed;

    // World retirement can precede a late player death or cross-source avatar reset.
    // Preserve the player/audio fallback when no R&C1 lease is bound.
    internal static void ResetOrFallback(Rac1WrenchHost? wrench, Rac1PlayerAudioPresenter? audio, PlayerHost? player)
    {
        if (wrench is not null) wrench.Reset();
        else
        {
            GodotRac1PlayerAudioPlayback.ResetWrench(audio);
            if (player is not null) player.Rac1WrenchThrown = false;
        }
    }

    internal bool ThrowActive => !_disposed && _throw.Active;
    internal long Generation => _disposed ? 0 : _generation;
    internal Vector3 WorldPosition => new((float)_throw.Position.X, (float)_throw.Position.Z, (float)_throw.Position.Y);
    internal bool HasVictim(Rac1MobyRuntimeKey key) => !_disposed && _victims.Contains(key);
    internal void AdmitVictim(Rac1MobyRuntimeKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _victims.Add(key);
    }
    internal void ForgetVictim(Rac1MobyRuntimeKey key) => _victims.Remove(key);

    internal void Reset()
    {
        if (_disposed) return;
        framePorts.ResetAudio();
        _throw.Reset();
        _victims.Clear();
        _generation = 0;
        _previousHand = _previousTip = null;
        if (framePorts.Player() is { } player && GodotObject.IsInstanceValid(player))
            player.Rac1WrenchThrown = false;
    }

    // Runs after fixed-update avatar sampling so contacts, throw return and
    // visible wrench share the same attachment pose and action generation.
    internal void OnPhysicsUpdated(Rac1WrenchAttackSession.Frame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (framePorts.Player() is not { } player || framePorts.ContactPose() is not { } wrenchPose)
            return;
        if (!framePorts.Alive())
        {
            Reset();
            return;
        }
        if (frame.Active && frame.Generation != _generation)
        {
            framePorts.HoldWrench();
            _generation = frame.Generation;
            _victims.Clear();
            framePorts.ApplyNativeAttack(frame.ProfileId);
        }
        else if (!frame.Active)
            framePorts.EndNativeAttack();

        Vector3 hand = wrenchPose.Hand;
        Vector3 tip = wrenchPose.Tip;
        if (frame.ReleaseWrench)
        {
            float yaw = (float)player.Rac1CurrentYaw;
            _throw.Begin(ToWrenchNative(hand), new(-Mathf.Cos(yaw), Mathf.Sin(yaw), 0));
        }
        if (_throw.Active)
        {
            Vector3 previous = WorldPosition;
            bool obstruction = false;
            if (_throw.NativeState == 10)
            {
                float yaw = (float)player.Rac1CurrentYaw;
                Vector3 end = previous + new Vector3(-Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * _throw.StepLength;
                var query = PhysicsRayQueryParameters3D.Create(previous, end);
                query.Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() };
                var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
                obstruction = hit.Count != 0;
                framePorts.WorldContactAudio(hit);
            }
            _throw.Step(ToWrenchNative(hand), obstruction);
            Vector3 next = WorldPosition;
            ResolveContacts(previous, next, previous, next, Rac1WrenchThrowSession.CollisionRadius, damage: 1);
            if (_throw.Active)
                framePorts.UpdateThrownVisual(next, _throw.Spin);
        }
        else if (frame.HitActive)
        {
            // The native four-point sweep is recovered; the rendered mesh bound
            // candidate remains a host approximation until PS2 narrow phase is recovered.
            ResolveContacts(_previousHand ?? hand, _previousTip ?? tip, hand, tip, .05f, frame.Damage);
            if (framePorts.ImpactAudioAdmitted())
            {
                var query = PhysicsRayQueryParameters3D.Create(hand, tip);
                query.Exclude = new global::Godot.Collections.Array<Rid> { player.GetRid() };
                framePorts.WorldContactAudio(player.GetWorld3D().DirectSpaceState.IntersectRay(query));
            }
        }
        player.Rac1WrenchThrown = _throw.Active;
        framePorts.StepAudio(frame, _throw, hand, WorldPosition);
        _previousHand = hand;
        _previousTip = tip;
        framePorts.RefreshVisibility();
    }

    private static Rac1NativeVector3 ToWrenchNative(Vector3 point) => new(point.X, point.Z, point.Y);

    internal void ResolveContacts(Vector3 previousHand, Vector3 previousTip,
        Vector3 hand, Vector3 tip, float radius, float damage)
    {
        // A retained diagnostic/actor callback must not strike a replacement world.
        if (_disposed) return;
        var result = new Rac1WrenchDamageResult(Rac1WrenchContactPath.AnimatedSweepHostGeometry,
            damage, Rac1WrenchAttackProfile.NativeDamageFlags);
        contactPorts.OtherContacts(previousHand, previousTip, hand, tip, radius, damage, result);
        foreach (var target in contactPorts.Targets())
        {
            var key = new Rac1MobyRuntimeKey(target.Source.NativeClassId, target.Source.InstanceIndex);
            if (_victims.Contains(key) || !GodotObject.IsInstanceValid(target.Root) ||
                !contactPorts.Eligible(target)) continue;

            var meshes = contactPorts.MorphView(target.Source.InstanceIndex) is { } morphView
                ? morphView.GetChildren().OfType<MeshInstance3D>()
                : target.Root.GetChildren().OfType<MeshInstance3D>();
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
            bool applied = contactPorts.Strike(target, result);
            // Explicit bounded host policy; retail shared hit-record producer
            // is still unknown and must not be replaced by a new native claim.
            if (applied)
            {
                _victims.Add(key);
                bool soft = Rac1PlayerAudioSession.WrenchVictimSoftness(target.Source.NativeClassId) ?? false;
                contactPorts.ContactSound(soft);
            }
        }
    }

    private static bool WrenchSegmentIntersects(Aabb bounds, Vector3 from, Vector3 to)
        => ActorContactGeometry.WrenchSegmentIntersects(bounds, from, to);

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
