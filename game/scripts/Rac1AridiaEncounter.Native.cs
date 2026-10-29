using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using static OBP.Game.Rac1EnemyCoordinates;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed partial class Rac1AridiaEncounter
{
    /// <summary>
    /// Narrow native class612 support. The original state10 and state11
    /// transitions use Moby+0x70 animation-complete; the host supplies a
    /// clip-wrap approximation, never a proximity timer. State12's full
    /// authored path/target solver is not yet implemented and must NOT be
    /// auto-advanced. Mode8/9 still use provisional movement/host combat.
    /// </summary>
    private Rac1AridiaConstructorbotNative.Frame? StepRac1NativeConstructorbot(Rac1HostedEnemy enemy)
    {
        var instance = _host.Context.MobyRuntime.Require(enemy.Node.Source);
        int state = instance.State.NativeState;
        if (state is Rac1AridiaConstructorbotNative.ParticleModeState or
            Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            int volumeIndex = Rac1AridiaConstructorbotNative.ReadActivationVolume(instance.PVar.Span);
            if (volumeIndex >= 0)
            {
                if (volumeIndex >= _rac1AridiaClipVolumes.Count)
                    throw new InvalidDataException("Constructorbot references a missing retail activation volume.");
                if (_host.Player is not { } player) return null;
                bool inside = _rac1AridiaClipVolumes[volumeIndex].Contains(
                    ToEffectNative(player.GlobalPosition));
                var active = Rac1AridiaConstructorbotNative.StepActivation(state, inside);
                if (active.NativeState != state)
                    _host.Context.MobyRuntime.SetNativeState(instance, active.NativeState);
                return active;
            }
            // L2:0x2d7fac uses 3D distance to the SAME native clip-list
            // record's translation +0x30. No oriented-box check for -1.
            if (_host.Player is not { } target || !_rac1ConstructorbotNativeWaypoints.TryGetValue(
                    instance.InstanceIndex, out var sourceWaypoint))
                return null;
            bool close = Rac1AridiaConstructorbotNative.NativeWaypointActivation(
                ToEffectNative(target.GlobalPosition),
                ToEffectNative(sourceWaypoint),
                Rac1AridiaConstructorbotNative.ReadActivationDistance(instance.PVar.Span));
            var activated = Rac1AridiaConstructorbotNative.StepActivation(state, close);
            if (activated.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, activated.NativeState);
            return activated;
        }
        if (state == 2 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var startup) &&
            startup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            int entrySequence = startup.NativeState == Rac1AridiaConstructorbotNative.ParticleModeState
                ? 4 : 16;
            var next = Rac1AridiaConstructorbotNative.StepActivatedAnimation(
                enemy.Animation.Sequence == entrySequence && enemy.Animation.Wrapped, entrySequence);
            if (next.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == 3 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var activatedStartup) &&
            activatedStartup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            // Original 0x1600ec movement waypoint is now decoded.
            // Horizontal displacement and host yaw are applied by the
            // presentation layer, after the engine-neutral state gate.
            // Source floor-height convergence is checked after physics.
            return Rac1AridiaConstructorbotNative.StepStateThreeLanding(null);
        }
        if (state == 4 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var landingStartup) &&
            landingStartup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            var next = Rac1AridiaConstructorbotNative.StepStateFour(
                enemy.Animation.Sequence == 5 && enemy.Animation.Wrapped);
            if (next.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == 5 &&
            _rac1ConstructorbotStartups.TryGetValue(instance.InstanceIndex, out var movementStartup) &&
            movementStartup.NativeState is Rac1AridiaConstructorbotNative.ParticleModeState or
                Rac1AridiaConstructorbotNative.ParticleAlternateState)
        {
            // Source 0x25b710 walker flags and speed are applied after
            // the real Godot collision movement. Only source return ==4
            // permits transition to rest; no timed host melee pulse.
            return new(state, 6);
        }
        if (state == Rac1AridiaConstructorbotNative.RoamingState)
        {
            // Native 0x2d7bb4 feeds PVar+0x1e0, which is generated
            // ONLY by state7 after source animation completion. If the
            // target has not been observed do not invent a destination.
            return new(state, 6);
        }
        if (state == Rac1AridiaConstructorbotNative.RestState)
        {
            if (_host.Player is { } player &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance.InstanceIndex, out var anchor))
            {
                // L2:0x2d7c98 selects the actual gameplay +0x60 record at
                // PVar+0x188 and tests its original +0x30 translation.
                // Unlike the previous fallback, this is a retail waypoint.
                Vector3 position = enemy.Node.Root.GlobalPosition;
                Vector3 target = player.GlobalPosition;
                Vector3 planar = target - position;
                planar.Y = 0;
                Vector3 facing = Rac1EnemyHost.EnemyForward(enemy.Node);
                facing.Y = 0;
                float facingError = planar.LengthSquared() > .00001f &&
                    facing.LengthSquared() > .00001f
                    ? facing.Normalized().SignedAngleTo(planar.Normalized(), Vector3.Up)
                    : float.PositiveInfinity;
                if (Rac1AridiaConstructorbotNative.State7AdmitsJointContact(
                    target.DistanceTo(anchor), target.Y - position.Y,
                    position.DistanceTo(anchor), facingError,
                    Rac1AridiaConstructorbotNative.ReadEngagementRadius(instance.PVar.Span)))
                {
                    _host.Context.MobyRuntime.SetNativeState(instance,
                        Rac1AridiaConstructorbotNative.JointContactState);
                    // Native state7 selects attack animation8, then state11
                    // runs its per-tick joint query on the following update.
                    return new(Rac1AridiaConstructorbotNative.JointContactState, 8);
                }
            }
            if (_host.Player is { } nearby &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance.InstanceIndex,
                    out var home) &&
                nearby.GlobalPosition.DistanceTo(home) <
                    Rac1AridiaConstructorbotNative.ReadEngagementRadius(instance.PVar.Span) &&
                MathF.Abs(nearby.GlobalPosition.Y - enemy.Node.Root.GlobalPosition.Y) < 4f &&
                enemy.Node.Root.GlobalPosition.DistanceTo(home) >= 1f)
            {
                // L2:0x2d7de4: in-range player, actor still too far from
                // source waypoint for joint attack; approach home first.
                _host.Context.MobyRuntime.SetNativeState(instance, 5);
                return new(5, 6);
            }
            var roam = Rac1AridiaConstructorbotNative.StepStateSevenRoam(
                enemy.Animation.Sequence == 0 && enemy.Animation.Wrapped);
            if (roam.NativeState == Rac1AridiaConstructorbotNative.RoamingState &&
                _rac1ConstructorbotNativeWaypoints.TryGetValue(instance.InstanceIndex,
                    out var roamAnchor))
            {
                var target = Rac1AridiaConstructorbotNative.CreateRoamingDestination(
                    ToEffectNative(roamAnchor), _host.Random.Angle(),
                    _host.Random.Range(1f, 2f), _host.Random.Range(1f, 2f));
                _rac1ConstructorbotRoamingTargets[instance.InstanceIndex] =
                    FromEffectNative(target);
                _rac1ConstructorbotRoamExitFlags.Remove(instance.InstanceIndex);
                _rac1ConstructorbotWalkerSpeeds.Remove(instance.InstanceIndex);
                _rac1ConstructorbotWalkerYawVelocities.Remove(instance.InstanceIndex);
                _host.Context.MobyRuntime.SetNativeState(instance, roam.NativeState);
            }
            return roam;
        }
        if (state == Rac1AridiaConstructorbotNative.AimState)
        {
            // Do not mistake a wrap from a provisional host-selected clip
            // for completion of the source's required animation 0.
            if (enemy.Animation.Sequence != 0) return new(state, 0);
            var next = Rac1AridiaConstructorbotNative.StepAim(enemy.Animation.Wrapped);
            if (next.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == Rac1AridiaConstructorbotNative.JointContactState)
        {
            // Native state11 executes its joint-0 contact query every tick.
            bool clipCompleted = enemy.Animation.Sequence == 8 && enemy.Animation.Wrapped;
            var next = Rac1AridiaConstructorbotNative.StepJointContact(
                clipCompleted, clipCompleted && _host.Random.Below(2) != 0);
            if (next.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == Rac1AridiaConstructorbotNative.FollowupState)
        {
            if (_host.Player is null) return new(state, 9);
            Vector3 delta = _host.Player.GlobalPosition - enemy.Node.Root.GlobalPosition;
            float planarDistance = new Vector2(delta.X, delta.Z).Length();
            bool completed = enemy.Animation.Sequence == 9 && enemy.Animation.Wrapped;
            var next = Rac1AridiaConstructorbotNative.StepFollowup(
                completed, planarDistance, delta.Y,
                Rac1AridiaConstructorbotNative.ReadEngagementRadius(instance.PVar.Span));
            if (next.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        if (state == Rac1AridiaConstructorbotNative.ContactRecoveryState)
        {
            // Source enemy_physics checks contact bit 0x40. Our enemy bodies
            // use MoveAndCollide, not MoveAndSlide, so IsOnFloor() is never
            // populated. Observe terrain support with the SAME shape sweep
            // used by this host's movement/grounding path, excluding actors.
            // This grounded signal is still a provisional mapping to native
            // bit 0x40, not an unconditional or elapsed-time recovery.
            bool floorContact = _host.TryGroundRac1Enemy(
                enemy, upwardReach: 0, downwardReach: .3f);
            enemy.Grounded = floorContact;
            var next = Rac1AridiaConstructorbotNative.StepRecovery(
                floorContact, enemy.Animation.Sequence);
            if (next.NativeState != state)
                _host.Context.MobyRuntime.SetNativeState(instance, next.NativeState);
            return next;
        }
        return null;
    }
}
