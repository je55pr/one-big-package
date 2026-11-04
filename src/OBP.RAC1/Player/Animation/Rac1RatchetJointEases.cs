using System.Numerics;

namespace OBP.RAC1.Player;

/// <summary>The kind of native hero state that currently owns the procedural joint targets.</summary>
public enum Rac1EasePose
{
    /// <summary>No state sets targets: every ease relaxes to rest (crouch, traversal, swimming, ...).</summary>
    None,
    /// <summary>Ground walk/run (<c>0x215340</c> state 2): lean and sway from the yaw error.</summary>
    Run,
    /// <summary>Landing recovery (state 8).</summary>
    Land,
    /// <summary>Jump or fall control modes.</summary>
    Airborne,
    /// <summary>Standing idle or a neutral fidget: head wander and tail sway.</summary>
    Idle,
}

/// <summary>
/// Ratchet's procedural joint layer: 18 damped angular springs on the player
/// body joints, retargeted every update from the hero state and composed onto
/// the resolved animation pose before skinning (the native "hero eases").
/// Targets are cleared every update, so anything not set relaxes to rest.
/// Recovered from the NTSC-U overlay: target rules <c>0x215340</c> (run, land,
/// jump/fall), the idle head and tail wander helpers, spring <c>0x25bc98</c>
/// over <c>0x25b798</c>, Euler build <c>0x259f50</c>, and the additive
/// post-multiply the pose evaluator applies to each attached manipulator.
/// </summary>
public sealed class Rac1RatchetJointEases
{
    public const int Count = Rac1HeroEaseTable.BodyRecordCount;
    // Ease indices (the native table order).
    public const int Root = 0, LowerSpine = 1, Chest = 2, Neck = 3, RightShoulder = 9, HeadDerived = 12;
    public const int TailFirst = 13, TailLast = 16;

    private const float FrameTime = 1f / 60f;
    private const float SettledRotation = 0.005f;

    public readonly record struct Input(
        Rac1EasePose Pose,
        /// <summary>Remaining signed yaw to the stick target after this update's turn.</summary>
        float YawError = 0,
        /// <summary>Planar displacement this update, in native units.</summary>
        float FlatSpeed = 0,
        /// <summary>Native animation sequence currently playing (idle gating).</summary>
        int Sequence = 0,
        /// <summary>Native run state step 1 (the alternate run pose).</summary>
        bool RunStepOne = false,
        /// <summary>The held item has arm animations (item table slot count nonzero): the wall response applies.</summary>
        bool CarriesEquipment = false,
        /// <summary>Distance from the hero to the forward wall probe hit, or null when it found nothing.</summary>
        float? ForwardWallDistance = null);

    public readonly record struct Offset(int AttachmentId, Quaternion Rotation);

    private readonly Rac1HeroEaseTable _table;
    private readonly Random _random;
    private readonly float[] _accel = new float[Count], _max = new float[Count];
    private readonly float[,] _rot = new float[Count, 3], _vel = new float[Count, 3], _target = new float[Count, 3];

    // Idle head wander (native +0x20e0.. fields) and four tail segment timers.
    private int _headDelay;
    private bool _wasIdle;
    // Initial values from the hero initializer (0.007 rate, 0.3 limit).
    private float _idleYaw, _idlePitch, _idleAccel = 0.007f, _idleMax = 0.3f;
    private readonly int[] _tailTimer = new int[4];
    private readonly float[] _tailPitch = new float[4], _tailYaw = new float[4];

    public Rac1RatchetJointEases(Rac1HeroEaseTable table, int seed = 0x52414331)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _random = new Random(seed);
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_rot); Array.Clear(_vel); Array.Clear(_target);
        for (int i = 0; i < Count; i++)
        {
            _accel[i] = _table.Body[i].Acceleration;
            _max[i] = _table.Body[i].MaxSpeed;
        }
        // 180..300 updates before the first look at level start (hero initializer).
        _headDelay = _random.Next(180, 301); _idleYaw = _idlePitch = 0;
        _idleAccel = 0.007f; _idleMax = 0.3f; _wasIdle = false; _wallHit = false;
        Array.Clear(_tailTimer); Array.Clear(_tailPitch); Array.Clear(_tailYaw);
    }

    // Entering the idle state rolls a fresh 50..100 update delay and clears the
    // stored look and tail targets (the native idle entry and its target reset).
    private void EnterIdle()
    {
        _headDelay = _random.Next(50, 101);
        _idleYaw = _idlePitch = 0;
        Array.Clear(_tailPitch); Array.Clear(_tailYaw);
    }

    /// <summary>Current Euler angle of one ease, for diagnostics and tests.</summary>
    public float Angle(int ease, int axis) => _rot[ease, axis];

    /// <summary>Current target of the most recent update, for diagnostics and tests.</summary>
    public float Target(int ease, int axis) => _target[ease, axis];

    public (float Acceleration, float MaxSpeed) Parameters(int ease) => (_accel[ease], _max[ease]);

    private bool _wallHit;
    private const float WallCapsuleRadius = 0.45f;

    /// <summary>Height of the forward wall probe above the hero (0x20a990): higher while the item is in use.</summary>
    public static float WallProbeHeight(bool equipmentActive) => equipmentActive ? 0.7f : 0.57f;

    /// <summary>Length of the forward wall probe; a hit on the previous update lengthens it (hysteresis).</summary>
    public float WallProbeLength => _wallHit ? 1.6f : 1.3f;

    /// <summary>
    /// <c>0x20a990</c>: a held weapon with arm animations is raised by the right shoulder ease when a
    /// wall is within 1.3 units in front, so the gun does not clip. The raise grows from 30 to 70
    /// degrees as the wall nears.
    /// </summary>
    private void WallTargets(Input input)
    {
        _wallHit = false;
        if (!input.CarriesEquipment || input.ForwardWallDistance is not { } distance || !float.IsFinite(distance))
            return;
        SetParameters(RightShoulder, 0.05f, 0.3f);
        float closeness = (1.3f - (distance - WallCapsuleRadius)) / 1.3f;
        _target[RightShoulder, 1] = Math.Clamp(closeness * 1.25f * -1.22173047f, -1.22173047f, -0.52359879f);
        _wallHit = true;
    }

    /// <summary>Advance one 60 Hz update and return the non-settled joint offsets.</summary>
    public IReadOnlyList<Offset> Step(Input input)
    {
        if (!float.IsFinite(input.YawError) || !float.IsFinite(input.FlatSpeed))
            throw new ArgumentOutOfRangeException(nameof(input));
        Array.Clear(_target);
        switch (input.Pose)
        {
            case Rac1EasePose.Run: RunTargets(input); break;
            case Rac1EasePose.Land: LandTargets(input.YawError); break;
            case Rac1EasePose.Airborne: AirborneTargets(input.YawError); break;
        }
        bool idle = input.Pose == Rac1EasePose.Idle;
        if (idle && !_wasIdle) EnterIdle();
        _wasIdle = idle;
        if (idle)
        {
            IdleHeadTargets(input.Sequence);
            if (input.Sequence == 0) IdleTailTargets();
        }
        DeriveHeadTargets(input.Sequence);
        WallTargets(input);

        var result = new List<Offset>(Count);
        for (int i = 0; i < Count; i++)
        {
            bool relaxed = _target[i, 0] == 0 && _target[i, 1] == 0 && _target[i, 2] == 0;
            if (relaxed && Math.Abs(_rot[i, 0]) < SettledRotation && Math.Abs(_rot[i, 1]) < SettledRotation &&
                Math.Abs(_rot[i, 2]) < SettledRotation)
                continue; // Native detaches a settled manipulator.
            for (int axis = 0; axis < 3; axis++)
                SpringAngle(ref _rot[i, axis], ref _vel[i, axis], _target[i, axis], _accel[i], _max[i]);
            result.Add(new(_table.Body[i].AttachmentId, EulerQuaternion(_rot[i, 0], _rot[i, 1], _rot[i, 2])));
        }
        return result;
    }

    private void SetParameters(int ease, float acceleration, float maxSpeed)
    {
        _accel[ease] = acceleration;
        _max[ease] = maxSpeed;
    }

    // 0x215340, run branch.
    private void RunTargets(Input input)
    {
        SetParameters(Root, 0.04f, 0.2f);
        SetParameters(LowerSpine, 0.04f, 0.2f);
        SetParameters(Chest, 0.04f, 0.2f);
        SetParameters(Neck, 0.02f, 0.2f);
        if (input.RunStepOne)
        {
            float error = Math.Clamp(input.YawError, -1.4f, 1.4f);
            float negated = -error;
            float speedScale = Math.Clamp(input.FlatSpeed / (FrameTime * 5.7f), 0f, 1f);
            _target[Root, 0] = negated * 0.14f * speedScale;
            _target[LowerSpine, 2] = error * 0.16f;
            _target[Chest, 0] = error * -0.12f;
            _target[Neck, 2] = error * 0.65f;
            _target[Neck, 1] = Math.Abs(error) * -0.15f;
            _target[Neck, 0] = Math.Abs(error) * 0.16f;
            _target[TailFirst, 2] = negated * 0.35f;
            _target[TailFirst + 1, 2] = negated * 0.3f;
            _target[TailFirst + 2, 2] = negated * 0.3f;
            _target[TailLast, 2] = negated * 0.3f;
            _target[TailFirst, 1] = Math.Abs(error) * 0.35f;
            _target[TailFirst + 1, 1] = Math.Abs(error) * 0.2f;
            return;
        }
        float weight = Math.Clamp(input.YawError * 0.5f, -0.8f, 0.8f);
        weight = weight * Math.Abs(weight) * 1.25f;
        _target[LowerSpine, 2] = weight;
        _target[Neck, 2] = weight;
        _target[Neck, 1] = Math.Abs(weight) * 0.25f;
        float tail = -weight;
        _target[TailFirst, 2] = tail * 0.35f;
        _target[TailFirst + 1, 2] = tail * 0.3f;
        _target[TailFirst + 2, 2] = tail * 0.3f;
        _target[TailLast, 2] = tail * 0.3f;
    }

    // 0x215340, landing branch (state 8).
    private void LandTargets(float yawError)
    {
        SetParameters(Root, 0.008f, 0.08f);
        SetParameters(LowerSpine, 0.015f, 0.08f);
        SetParameters(Chest, 0.015f, 0.08f);
        SetParameters(Neck, 0.008f, 0.1f);
        float scaled = Math.Clamp(yawError * 1.6f, -1.25f, 1.25f);
        _target[Root, 0] = Math.Clamp(-scaled * 0.52f, -0.28f, 0.28f);
        _target[Neck, 2] = Math.Clamp(scaled * 1.2f, -0.6f, 0.6f);
        _target[LowerSpine, 2] = scaled * 0.58f;
        _target[Chest, 0] = scaled * -0.28f;
        _target[Neck, 1] = Math.Clamp(Math.Abs(scaled) * 0.2f, -0.2f, 0.2f);
        _target[Neck, 0] = Math.Abs(scaled) * 0.16f;
        _target[TailFirst, 2] = -scaled * 0.35f;
        _target[TailFirst + 1, 2] = -scaled * 0.3f;
        _target[TailFirst + 2, 2] = -scaled * 0.3f;
        _target[TailLast, 2] = -scaled * 0.3f;
    }

    // 0x215340, jump and fall control modes.
    private void AirborneTargets(float yawError)
    {
        SetParameters(Root, 0.04f, 0.2f);
        SetParameters(LowerSpine, 0.04f, 0.2f);
        SetParameters(Chest, 0.04f, 0.2f);
        SetParameters(Neck, 0.02f, 0.2f);
        float x = Math.Clamp(yawError, -1.4f, 1.4f);
        _target[LowerSpine, 2] = x * 0.29f;
        _target[Chest, 0] = x * -0.27f;
        _target[Neck, 2] = x * 0.87f;
        _target[TailFirst, 2] = -x * 0.35f;
        _target[TailFirst + 1, 2] = -x * 0.3f;
        _target[TailFirst + 2, 2] = -x * 0.3f;
        _target[TailLast, 2] = -x * 0.3f;
    }

    // Idle head wander: a new look target every 90..280 updates while standing,
    // held (and kept short) during fidget sequences 1 and 2.
    private void IdleHeadTargets(int sequence)
    {
        if (sequence is 1 or 2)
            _headDelay = _random.Next(40, 71);
        if (DecrementTimer(ref _headDelay))
        {
            _idlePitch = -RandomBetween(-0.15707964f, 0.43633232f);
            _idleYaw = RandomSigned(0.34906584f, 0.99483764f);
            float blend = (float)_random.NextDouble();
            _idleAccel = 0.015f + (0.007f - 0.015f) * blend;
            _idleMax = 0.29f + (0.3f - 0.29f) * blend;
            _headDelay = _random.Next(90, 201) + (int)(80 * blend);
        }
        if (sequence is 1 or 2)
        {
            _idleYaw = Math.Clamp(_idleYaw, -0.34906584f, 0.34906584f);
            _idlePitch = Math.Clamp(_idlePitch, 0f, 0.2617994f);
        }
        SetParameters(Neck, _idleAccel, _idleMax);
        SetParameters(LowerSpine, 0.008f, 0.3f);
        _target[Neck, 2] = _idleYaw;
        _target[Neck, 1] = _idlePitch;
        _target[LowerSpine, 2] = _target[Neck, 2] * 0.52f;
        _target[LowerSpine, 1] = _target[Neck, 1] * 0.55f;
    }

    // Four tail segments re-roll independent sway targets on their own timers.
    private void IdleTailTargets()
    {
        for (int i = 0; i < 4; i++)
        {
            if (DecrementTimer(ref _tailTimer[i]))
            {
                switch (i)
                {
                    case 0:
                        _tailYaw[0] = RandomSigned(0.17453292f, 0.5235988f);
                        _tailTimer[0] = _random.Next(70, 151);
                        break;
                    case 1:
                        _tailYaw[1] = RandomSigned(0.17453292f, 0.5235988f);
                        _tailTimer[1] = _random.Next(40, 91);
                        break;
                    case 2:
                        _tailYaw[2] = RandomSigned(0.2617994f, 0.87266463f);
                        _tailTimer[2] = _random.Next(40, 91);
                        break;
                    default:
                        _tailPitch[3] = RandomBetween(-0.08726646f, 0.5235988f);
                        float previous = _tailYaw[3];
                        float next = RandomSigned(0.34906584f, 0.95993108f);
                        // Consecutive swings alternate side.
                        _tailYaw[3] = (previous <= 0 && next <= 0) || (previous >= 0 && next >= 0) ? -next : next;
                        _tailTimer[3] = _random.Next(35, 71);
                        break;
                }
            }
            _target[TailFirst + i, 1] = _tailPitch[i];
            _target[TailFirst + i, 2] = _tailYaw[i];
        }
    }

    // 0x2056a8: the derived head ease follows the neck ease.
    private void DeriveHeadTargets(int sequence)
    {
        _target[HeadDerived, 2] = Math.Clamp(_target[Neck, 2] * 0.25f, -0.14835298f, 0.14835298f);
        float pitch = _target[Neck, 1] / 2.8f;
        if (pitch > 0.29670596f) pitch = 0.29670596f;
        if (pitch < -0.17453292f) pitch = -0.17453292f;
        _target[HeadDerived, 1] = pitch;
        if (sequence is 1 or 2)
        {
            _target[HeadDerived, 2] = Math.Clamp(_target[HeadDerived, 2], -0.034906585f, 0.034906585f);
            _target[HeadDerived, 1] = Math.Clamp(_target[HeadDerived, 1], -0.12217305f, 0.12217305f);
        }
    }

    private static bool DecrementTimer(ref int timer)
    {
        if (timer > 0) timer--;
        return timer <= 0;
    }

    private float RandomBetween(float low, float high) => low + (high - low) * (float)_random.NextDouble();

    private float RandomSigned(float low, float high) =>
        RandomBetween(low, high) * (_random.Next(2) == 0 ? -1f : 1f);

    /// <summary>
    /// One native angular spring step (<c>0x25bc98</c> with direction rule 2 over
    /// <c>0x25b798</c>): velocity gains <c>k * error - d * velocity</c>, is bounded by the
    /// remaining error, then advances the wrapped angle. The ease table's
    /// "maximum speed" is the damping term.
    /// </summary>
    public static void SpringAngle(ref float angle, ref float velocity, float target, float gain, float damping)
    {
        int direction = 0;
        if ((target > 0 && angle < 0) || (target < 0 && angle > 0)) direction = target < 0 ? -1 : 1;
        float difference = SignedDifference(direction, target, angle);
        velocity += gain * difference - damping * velocity;
        float limit = Math.Abs(difference);
        if (limit < velocity) velocity = limit;
        else if (velocity < -limit) velocity = -limit;
        angle = WrapPi(angle + velocity);
    }

    // 0x25bb38: signed angle difference, forced the long way when it opposes `direction`.
    private static float SignedDifference(int direction, float target, float angle)
    {
        float difference = WrapPi(target - angle);
        if (direction == 0 || difference * direction > 0) return difference;
        if (Math.Abs(difference) <= 0.0017453292f) return 0;
        return difference > 0 ? difference - MathF.Tau : difference + MathF.Tau;
    }

    private static float WrapPi(float angle)
    {
        float wrapped = (angle + MathF.PI) % MathF.Tau;
        if (wrapped < 0) wrapped += MathF.Tau;
        return wrapped - MathF.PI;
    }

    /// <summary>
    /// <c>0x259f50</c>: qx * qy * qz in the native quaternion convention, where
    /// each axis quaternion stores <c>-sin(a/2)</c> (the engine's channel convention
    /// is the conjugate of the usual one).
    /// </summary>
    public static Quaternion EulerQuaternion(float x, float y, float z)
    {
        static Quaternion Axis(int axis, float angle)
        {
            float s = -MathF.Sin(angle * 0.5f), c = MathF.Cos(angle * 0.5f);
            return axis switch { 0 => new(s, 0, 0, c), 1 => new(0, s, 0, c), _ => new(0, 0, s, c) };
        }
        return Quaternion.Multiply(Quaternion.Multiply(Axis(0, x), Axis(1, y)), Axis(2, z));
    }
}
