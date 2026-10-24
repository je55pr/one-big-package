using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source-backed airborne phase and camera-convergence request for Nebula G34
/// Alien Queen class1051: FUN_L06_002f9a28 state2 and FUN_L06_002fb2a8.
/// Produces camera requests; it does NOT seize the host camera, issue native
/// hero-state callbacks or declare a landing without committed camera output.
/// </summary>
public sealed class Rac1AlienQueenFlight
{
    public const float SourceHeight = 17.5f;
    public const float GravityPerTick = 30f / 3600f;
    public const float NearLandingTimeTicks = 6f;
    public const float CameraBlendAcceleration = 3f / 3600f;
    public const float CameraBlendVelocityLimit = 2f / 60f;
    private readonly Rac1MobySteering _cameraBlend = new();
    private readonly float _baseZ;
    private bool _ready;

    public Rac1AlienQueenFlight(float authoredZ)
    {
        if (!float.IsFinite(authoredZ)) throw new ArgumentOutOfRangeException(nameof(authoredZ));
        _baseZ = authoredZ;
        Z = authoredZ + SourceHeight;
    }

    public float Z { get; private set; }
    public float VerticalVelocity { get; private set; }
    public int Sequence { get; private set; } = 8;
    public int NativeState { get; private set; } = 2;
    public float CameraBlend => _cameraBlend.Value;
    public bool CameraCompletionCandidate => _ready && CameraBlend >= 1f && Sequence == 1;
    public int NativeTicks { get; private set; }

    /// <summary>Native first integrates height and gravity; the current
    /// sequence8 switches to9 if abs((Z-baseZ)/velocity) <= 6. After sequence9
    /// completion sequence6 plays, followed by sequence1. Ground Z is clamped
    /// only in the non8 source branch, never used to fake camera completion.</summary>
    public Rac1AlienQueenFlightFrame StepDescent(bool animationCompleted)
    {
        if (NativeState != 2) return Snapshot(false);
        Z += VerticalVelocity;
        VerticalVelocity -= GravityPerTick;
        NativeTicks++;
        bool changed = false;
        if (Sequence == 8)
        {
            if (VerticalVelocity != 0 &&
                MathF.Abs((Z - _baseZ) / VerticalVelocity) <= NearLandingTimeTicks)
            {
                Sequence = 9;
                changed = true;
            }
        }
        else
        {
            if (Z < _baseZ) Z = _baseZ;
            if (animationCompleted)
            {
                int next = Sequence switch { 9 => 6, 6 => 1, _ => Sequence };
                changed = next != Sequence;
                Sequence = next;
            }
        }
        return Snapshot(changed);
    }

    /// <summary>
    /// Computes the native transition camera request. Original sequence8
    /// leaves camera at the overlay's fixed anchor. Later sequences blend
    /// from an 8-unit Queen-forward offset (at hero height+2) to 7 units
    /// behind Ratchet (at hero height+4), then aim back toward the Queen.
    /// The camera output must be consumed by a real host before state7.
    /// </summary>
    public Rac1AlienQueenFlightCamera SampleCamera(Vector3 queenPosition,
        Vector3 queenForward, Vector3 heroPosition, Vector3 heroForward,
        Vector3 sourceCameraAnchor)
    {
        if (!Finite(queenPosition) || !Finite(queenForward) ||
            !Finite(heroPosition) || !Finite(heroForward) || !Finite(sourceCameraAnchor))
            throw new ArgumentOutOfRangeException(nameof(queenPosition));
        Vector3 output;
        if (Sequence == 8)
        {
            output = sourceCameraAnchor;
        }
        else
        {
            _cameraBlend.Step(1f, CameraBlendAcceleration,
                CameraBlendAcceleration, CameraBlendVelocityLimit);
            var from = queenPosition + NormalizedOrZero(queenForward) * 8f;
            from.Z = heroPosition.Z + 2f;
            var to = heroPosition - NormalizedOrZero(heroForward) * 7f;
            to.Z += 4f;
            output = Vector3.Lerp(from, to, CameraBlend);
        }
        _ready = true;
        return new(output, queenPosition, CameraBlend, CameraCompletionCandidate);
    }

    /// <summary>Native helper returns 1 only once its blend has reached
    /// one and the Moby is in sequence1. Caller must confirm the camera
    /// request reached its owning host; terrain contact alone never suffices.</summary>
    public bool CommitCameraAndComplete(bool cameraApplied)
    {
        if (NativeState != 2 || !cameraApplied || !CameraCompletionCandidate) return false;
        NativeState = 7;
        return true;
    }

    private Rac1AlienQueenFlightFrame Snapshot(bool changed) =>
        new(NativeState, Sequence, Z, VerticalVelocity, CameraBlend, changed,
            CameraCompletionCandidate);

    private static Vector3 NormalizedOrZero(Vector3 v) =>
        v.LengthSquared() > 1e-9f ? Vector3.Normalize(v) : Vector3.Zero;

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public sealed record Rac1AlienQueenFlightFrame(int NativeState, int Sequence,
    float NativeZ, float VerticalVelocity, float CameraBlend,
    bool SequenceChanged, bool CameraCompletionCandidate);

public sealed record Rac1AlienQueenFlightCamera(Vector3 NativeCameraPosition,
    Vector3 NativeLookAt, float Blend, bool CanCompleteWithAppliedCamera);
