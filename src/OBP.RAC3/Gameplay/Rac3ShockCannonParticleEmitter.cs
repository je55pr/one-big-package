using System.Globalization;
using System.Numerics;
using OBP.Core;

namespace OBP.RAC3.Gameplay;

/// <summary>OBP admission fence around the two recovered UYA recipes. Advances neither weapon logic nor damage.</summary>
public sealed class Rac3ShockCannonParticleEmitter
{
    private readonly Rac3ShockCannonSession _weapon;
    private long _lastUpdate = -1;
    private bool _stopped;
    public Rac3ParticleOwner Owner { get; }

    public Rac3ShockCannonParticleEmitter(Rac3ShockCannonSession weapon, Rac3ShockCannonFrame frame,
        ObpSourceRef levelSource, Rac3MobyRuntimeKey heldMoby)
    {
        if (!weapon.IsCurrentFrame(frame) || frame.State == Rac3ShockCannonState.Unequipped)
            throw new InvalidOperationException("Bind particles to the current equipped weapon generation.");
        if (levelSource.Game != ObpSourceGame.Rac3 || levelSource.BuildId != Rac3Authority.Primary.BuildId || levelSource.LevelId != "TABLE1" ||
            heldMoby.NativeClassId != Rac3ShockCannonProfile.HeldMobyClass || heldMoby.InstanceIndex < 0)
            throw new ArgumentException("Cannon particles require an admitted UYA held-Moby identity and TABLE1 source.");
        _weapon = weapon;
        Owner = new(levelSource with { AssetKind = "moby", OriginalId = heldMoby.InstanceIndex.ToString(CultureInfo.InvariantCulture) }, frame.Generation);
    }

    public Result Emit(Rac3ShockCannonFrame frame, Rac3ParticleSession particles, Vector4 nativeMuzzle, Vector4 nativeBeamEnd,
        bool movingFrameActive = false, bool directionalGravityActive = false)
    {
        if (_stopped) return new(Status.Stopped, 0, 0, false);
        if (!_weapon.IsCurrentFrame(frame) || frame.Generation != Owner.Generation) return new(Status.StaleOwner, 0, 0, false);
        if (frame.Update <= _lastUpdate) return new(Status.AlreadyConsumed, 0, 0, false);
        if (movingFrameActive || directionalGravityActive) throw new NotSupportedException("This Cannon particle slice requires a static native world frame.");
        particles.RequireCannonResources(Owner); // Fail before consuming the frame or RNG if resources are unavailable.
        if (!Finite(nativeMuzzle) || !Finite(nativeBeamEnd)) throw new ArgumentException("Cannon particle pose facts must be finite.");
        _lastUpdate = frame.Update;
        int attempted = 0, emitted = 0;
        if (frame.ChargeUpdates is > 0 and < 79)
        {
            var charge = particles.EmitShockCannonCharging(Owner, frame.ChargeUpdates, nativeMuzzle);
            attempted += charge.Attempted; emitted += charge.Emitted;
        }
        // Native particles precede counter decrement; unlike the mesh event, they also run on the last beam tick.
        if (frame.Queries.Any(q => q.Kind == Rac3ShockCannonQueryKind.BeamSphere))
        {
            var beam = particles.EmitShockCannonBeamGlow(Owner, nativeMuzzle, nativeBeamEnd);
            attempted += beam.Attempted; emitted += beam.Emitted;
        }
        return new(Status.Processed, attempted, emitted, frame.ChargeUpdates >= 79);
    }
    /// <summary>OBP policy: stop future admissions; already copied native particles drain independently.</summary>
    public void StopEmission() => _stopped = true;
    private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
    public enum Status { Processed, AlreadyConsumed, StaleOwner, Stopped }
    /// <summary>UnrecoveredReadyCharge explicitly reports the separate type-111/44 charged-ready composite.</summary>
    public readonly record struct Result(Status Admission, int Attempted, int Emitted, bool UnrecoveredReadyCharge);
}
