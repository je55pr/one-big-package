namespace OBP.RAC1.Player;

/// <summary>
/// Results of the native water/solid queries, supplied by the collision host.
/// Heights are native Z, not camera or model coordinates. Query membership
/// (masks 2 and 0x24), currents and moving water remain outside this controller.
/// </summary>
public readonly record struct Rac1WaterContact(
    bool Present, float SurfaceHeight, float SolidFloorHeight,
    bool SurfaceCeilingBlocked = false, bool SurfaceUnderfoot = true)
{
    public float Depth => MathF.Max(0, SurfaceHeight - SolidFloorHeight);
    public bool Wading => Depth is > .25f and < .85f;
}

/// <summary>P+aa8/aac and animation loop flag, from the physics-owned clip clock.</summary>
public readonly record struct Rac1WaterAnimationFacts(
    int Sequence, float AuthoredTime, float AuthoredAdvance, bool Wrapped = false,
    bool CycleCompleted = false);

public readonly record struct Rac1WaterMovementInput(
    Rac1WaterContact Contact, float PositionHeight,
    Rac1NativeVector3 ContactMotion, float RequestedVerticalStep,
    float FacingYaw, float DesiredYaw, float Magnitude,
    bool CrossHeld = false, bool CrossPressed = false,
    bool SquareHeld = false, bool SquarePressed = false,
    bool CrouchHeld = false, bool CrouchPressed = false,
    bool OwnsHydroPack = false, bool OwnsOxygenMask = false,
    int MovementDomain = 2, int MovementAction = 0, bool HasApexed = true,
    Rac1WaterAnimationFacts Animation = default);

public enum Rac1WaterExit
{
    None,
    Ground,
    Fall,
    Jump,
}

public readonly record struct Rac1WaterMovementResult(
    bool Active, Rac1SwimSnapshot State, Rac1NativeVector3 Request,
    float Yaw, float Pitch, float Roll,
    Rac1MovementAnimationSelection? Animation,
    Rac1WaterExit Exit = Rac1WaterExit.None, bool DeathAnimationCompleted = false,
    int? Sound = null);

/// <summary>
/// Regular-axis water actions from Novalis 2378bc/238a24, 2406b0/2408e8,
/// 240c78, 2435b8..243b64 and the shared entry dispatcher 23cf98.
/// Advances exactly one NTSC update. Geometry, collision response, animation
/// evaluation and the eventual checkpoint reload are external facts/owners.
/// </summary>
public sealed class Rac1WaterMovementSession
{
    private const float Dt = 1f / 60f;
    private static readonly float DtSquared = BitConverter.Int32BitsToSingle(0x3991a2b4);
    private static readonly float[] Stroke =
    [
        .4f, .4f, .45f, .52f, .6f, .68f, .78f, .89f, 1, 1,
        1, 1, .95f, .9f, .85f, .8f, .75f, .7f, .65f, .6f,
        .55f, .5f, .45f, .4f, .4f, .4f, .4f, .4f, .4f, .4f,
    ];

    private int _action, _previousAction, _previousDomain, _earlierDomain;
    private int _age, _diveAge, _strokeTimer, _ticks;
    private int _crossAge = 1000, _squareAge = 1000, _crouchAge = 1000;
    private long _generation;
    private bool _diving;
    private float _yaw, _yawVelocity, _yawRest, _pitch, _pitchVelocity, _roll, _rollVelocity;
    private float _uprightPitchVelocity, _uprightRollVelocity;
    private float _scalar, _bob, _bobVelocity, _bobSpringVelocity, _water, _waterVelocity, _sink;
    private Rac1NativeVector3 _carry;
    private int _blend = 22;
    private int? _sound;

    public int Oxygen { get; private set; } = Rac1TraversalEquipmentWater.MaximumOxygen;
    public bool Active => _action is >= 0x33 and <= 0x37 or 0x6a;
    public bool Diving => _diving;
    public int ActionAge => _age;
    public float Scalar => _scalar;

    public void Reset()
    {
        _action = _previousAction = _previousDomain = _earlierDomain = 0;
        _age = _diveAge = _strokeTimer = _ticks = 0;
        _crossAge = _squareAge = _crouchAge = 1000;
        _diving = false;
        _yaw = _yawVelocity = _yawRest = _pitch = _pitchVelocity = _roll = _rollVelocity = 0;
        _scalar = _bob = _bobVelocity = _bobSpringVelocity = _water = _waterVelocity = _sink = 0;
        _uprightPitchVelocity = _uprightRollVelocity = 0;
        _carry = default;
        Oxygen = Rac1TraversalEquipmentWater.MaximumOxygen;
        // Keep generation monotonic across placement so an old clip cannot
        // accidentally resume on a later entry to the same action.
        _generation++;
        _sound = null;
    }

    public Rac1WaterMovementResult Step(Rac1WaterMovementInput input)
    {
        Validate(input);
        _sound = null;
        _ticks++;
        _crossAge = input.CrossPressed ? 0 : Math.Min(1000, _crossAge + 1);
        _squareAge = input.SquarePressed ? 0 : Math.Min(1000, _squareAge + 1);
        _crouchAge = input.CrouchPressed ? 0 : Math.Min(1000, _crouchAge + 1);
        var contact = input.Contact;
        if (!Active)
        {
            Oxygen = Rac1TraversalEquipmentWater.MaximumOxygen;
            int entry = SelectEntry(input);
            if (entry == 0) return Result(default);
            _yaw = input.FacingYaw;
            _pitch = _roll = _yawVelocity = _pitchVelocity = _rollVelocity = _scalar = 0;
            Enter(entry, input, input.MovementDomain);
        }
        else
            _age++;

        if (_action == 0x6a)
        {
            _carry = Shorten(_carry, 4 * DtSquared);
            _sink = Approach(_sink, -1.5f * Dt, 1.5f * DtSquared);
            return Result(new(_carry.X, _carry.Y, _sink),
                deathFinished: input.Animation.Sequence == 95 && input.Animation.CycleCompleted);
        }
        if (!contact.Present)
            return Leave(Rac1WaterExit.Fall);

        bool underwater = _action is 0x33 or 0x34 or 0x35;
        if (underwater && !_diving && input.ContactMotion.Z > 0 &&
            input.PositionHeight > contact.SurfaceHeight - .4f)
            Enter(0x37, input, 0x11);
        underwater = _action is 0x33 or 0x34 or 0x35;
        Oxygen = Rac1TraversalEquipmentWater.StepOxygen(Oxygen,
            underwater ? 0x11 : 0x12, _action, input.OwnsOxygenMask);
        if (underwater)
        {
            bool escaping = input.PositionHeight > contact.SurfaceHeight - 2 &&
                _pitch < BitConverter.Int32BitsToSingle(unchecked((int)0xbf5f66f3)) &&
                input.ContactMotion.Z > Dt;
            if (Oxygen == 0 && !escaping)
            {
                Enter(0x6a, input, 0x11);
                return Result(default);
            }
            if (input.PositionHeight > contact.SurfaceHeight + .4f)
                return Leave(Rac1WaterExit.Fall);
        }
        else if (contact.SurfaceCeilingBlocked)
            Dive(input);

        if (_action is 0x36 or 0x37)
        {
            bool still = _action == 0x37;
            if (_crossAge < 8 && (!still || _age > 10))
                return Leave(Rac1WaterExit.Jump);
            if (_squareAge < 7 || still && input.OwnsHydroPack && _crouchAge < 7)
                Dive(input);
            else if (contact.Wading && contact.Depth < (still ? .4f : .7f))
                return Leave(Rac1WaterExit.Ground);
            else if (still && input.Magnitude > .2f && _age > 10)
                Enter(0x36, input, 0x12);
            else if (!still && input.Magnitude < .1f && _scalar == 0 && _strokeTimer == 0)
                Enter(0x37, input, 0x12);
        }
        else
        {
            if (_action == 0x34)
            {
                if (input.Magnitude > .2f || input.CrossHeld || input.SquareHeld ||
                    input.OwnsHydroPack && input.CrouchHeld)
                    Dive(input);
            }
            else if (_action == 0x33)
            {
                float t = input.Animation.Sequence == 59 ? input.Animation.AuthoredTime : 0;
                if (!input.CrossHeld && !input.SquareHeld && t is > 9 and < 19 &&
                    !_diving && input.Magnitude < .1f && _scalar < 2 * Dt)
                    Enter(0x34, input, 0x11);
                else if (input.OwnsHydroPack && input.CrouchHeld && _age > 20)
                    Enter(0x35, input, 0x11);
            }
            else if (_action == 0x35)
            {
                if (!(input.OwnsHydroPack && input.CrouchHeld) && input.Magnitude > .5f)
                    Enter(0x33, input, 0x11);
                else if (!input.CrossHeld && !input.SquareHeld && !_diving &&
                    input.Magnitude < .7f && !input.CrouchHeld)
                    Enter(0x34, input, 0x11);
            }
        }
        return _action is 0x36 or 0x37 ? Surface(input) : Underwater(input);
    }

    /// <summary>
    /// P+12e4/22a4 water entry. The periodic submerged check is admitted only
    /// when the supplied solid floor lies below its ray end, 1.3 units ABOVE
    /// the feet. The helper adds both offsets; this is not a downward ray.
    /// </summary>
    private int SelectEntry(Rac1WaterMovementInput input)
    {
        var c = input.Contact;
        if (!c.Present || input.MovementDomain is 3 or 7 or 20) return 0;
        if (input.MovementAction == 0x12 && !input.HasApexed) return 0;
        bool landing = input.MovementDomain != 4 || input.HasApexed;
        float reach = MathF.Max(.2f, MathF.Abs(input.RequestedVerticalStep) + .07f);
        if (c.SurfaceUnderfoot && MathF.Abs(c.SurfaceHeight - (input.PositionHeight + .45f)) < reach &&
            landing && c.Depth > .8f && input.ContactMotion.Z < 0)
            return 0x37;
        if (c.SurfaceUnderfoot && landing && input.PositionHeight < c.SurfaceHeight - .8f)
            return 0x37;
        if (input.MovementAction == 0x12 && input.HasApexed && input.PositionHeight < c.SurfaceHeight - .7f)
            return 0x37;
        // The host supplies static-water membership instead of executing the
        // native mask-2 segment query. No arbitrary depth box is introduced.
        float submergedRayEnd = MathF.Max(0, input.PositionHeight + 1.3f);
        return input.MovementAction != 0x12 && (_ticks & 15) == 0 &&
            c.SurfaceHeight >= submergedRayEnd &&
            c.SurfaceHeight <= input.PositionHeight + ((_ticks & 63) == 0 ? 16 : 4) &&
            c.SolidFloorHeight < submergedRayEnd ? 0x34 : 0;
    }

    private void Dive(Rac1WaterMovementInput input) =>
        Enter(input.OwnsHydroPack && input.CrouchHeld ? 0x35 : 0x33,
            input, _action is 0x36 or 0x37 ? 0x12 : 0x11);

    private void Enter(int action, Rac1WaterMovementInput input, int previousDomain)
    {
        _previousAction = _action;
        _earlierDomain = _previousDomain;
        _previousDomain = previousDomain;
        _action = action;
        if (action == 0x37 && previousDomain != 0x12 || action is 0x33 or 0x35 && previousDomain == 0x12)
            _sound = 3;
        _age = 0;
        _generation++;
        _blend = action switch
        {
            0x33 => 12,
            0x34 => _previousAction == 0x35 ? -2 : 35,
            0x35 => _previousAction == 0x33 ? 15 : 11,
            0x36 => 17,
            0x37 => 22,
            _ => 10
        };
        if (action is 0x33 or 0x35)
        {
            _diving = previousDomain == 0x12;
            _diveAge = 0;
        }
        else _diving = false;
        if (action is 0x34 or 0x37 or 0x6a) _carry = input.ContactMotion;
        if (action == 0x37)
        {
            _strokeTimer = 0;
            _bobVelocity = MathF.Max((float)input.ContactMotion.Z * .37f, -7 * Dt);
            _water = input.Contact.SurfaceHeight;
            _waterVelocity = 0;
            _bob = (input.PositionHeight - _water) - (-.12f);
            _carry = ClampLength(new(_carry.X, _carry.Y, 0), 5.5f * Dt);
        }
        if (action == 0x36) { _strokeTimer = 45; _bobSpringVelocity = 0; }
        if (action == 0x6a) _sink = 0;
    }

    private Rac1WaterMovementResult Surface(Rac1WaterMovementInput input)
    {
        // Shared hero_tilt_update (Veldin2165b8) admits surface domain18,
        // restoring an ascending/diving body's pitch and roll to upright.
        Spring(ref _pitch, ref _uprightPitchVelocity, 0, .015f, .3f, 5.2359877f * Dt, angle: true);
        Spring(ref _roll, ref _uprightRollVelocity, 0, .015f, .3f, 5.2359877f * Dt, angle: true);
        Turn(input);
        if (_strokeTimer > 0) _strokeTimer--;
        float x = 0, y = 0;
        if (_action == 0x36)
        {
            float target = _strokeTimer != 0 ? 3 * Dt : input.Magnitude * 3 * Dt;
            if (MathF.Abs(_yawRest) > MathF.PI / 4) target = 0;
            _scalar = Approach(_scalar, target, (_scalar < target ? 4 : 5) * DtSquared);
            float v = _scalar * SampleStroke(input.Animation, 101) * 5.5f;
            x = MathF.Cos(_yaw) * v;
            y = MathF.Sin(_yaw) * v;
        }
        else _scalar = 0;
        _carry = _carry.Length <= .001f ? default :
            Shorten(new(_carry.X, _carry.Y, 0), 4.2f * DtSquared);
        if (_age > 10 || _bob < 0 || _previousDomain == 0x11 || _earlierDomain == 0x11)
        {
            if (_action == 0x36)
            {
                Spring(ref _bob, ref _bobSpringVelocity, 0, .03f, .3f, 1.5f * Dt);
                _bobVelocity = 0;
            }
            else
            {
                _bobVelocity -= .005f * _bob + .045f * _bobVelocity;
                if (MathF.Abs(_bob) < .001f && _bobVelocity < .0001f) _bobVelocity = 0;
            }
        }
        _bob += _bobVelocity;
        if (_action == 0x36 && _bob > 0)
        {
            if (_bobVelocity > 0) _bobVelocity = 0;
            _bob = Approach(_bob, 0, .7f * Dt);
        }
        Spring(ref _water, ref _waterVelocity, input.Contact.SurfaceHeight, .027f, .3f, 0);
        float z = (_water + (-.12f)) + _bob;
        // Source writes the new height directly; the host runs this requested
        // correction through its contact solver so an obstruction remains solid.
        return Result(new(x + _carry.X, y + _carry.Y, z - input.PositionHeight));
    }

    private Rac1WaterMovementResult Underwater(Rac1WaterMovementInput input)
    {
        if (_diving && ++_diveAge > 40) _diving = false;
        float pitchTarget = 0;
        if (input.SquareHeld && !input.CrossHeld || _diving)
            pitchTarget = 1.36135685f * (_diving && !input.SquareHeld ? .35f : 1);
        else if (input.CrossHeld) pitchTarget = -1.36135685f;
        if (_action == 0x34) pitchTarget = 0;
        float pitchSpeed = (input.CrossHeld || input.SquareHeld ? 1.2217305f : .69813168f) * Dt;
        // OBP's digital/normalized input supplies full button pressure. The
        // DS2's analog pressure and non-pressure-pad ramp are not fabricated.
        Spring(ref _pitch, ref _pitchVelocity, pitchTarget, .015f, .2f, pitchSpeed, angle: true);
        Turn(input);
        float speed = _diving ? 6 * Dt : _action == 0x35
            ? (input.CrouchHeld ? 7 * Dt : 7 * Dt * .4f) : 3 * Dt;
        float stick = input.Magnitude;
        if (input.CrossHeld || input.SquareHeld || _diving) stick = MathF.Max(stick, 1);
        if (input.Magnitude > .15f) stick = MathF.Max(stick, _action == 0x35 ? .4f : .35f);
        float target = _action == 0x35 && input.CrouchHeld ? speed : speed * stick;
        _scalar = Approach(_scalar, target,
            (_action == 0x35 ? _scalar < target ? 13 : 8 : _scalar < target ? 7 : 4) * DtSquared);
        Rac1NativeVector3 request;
        if (_action == 0x34)
        {
            _carry = Shorten(_carry, (_previousAction == 0x35 ? 8 : 4) * DtSquared);
            request = _carry;
        }
        else
        {
            float v = _action == 0x35 ? _scalar : _scalar * SampleStroke(input.Animation, 59) * 5;
            if (_action == 0x33 && _previousAction == 0x35 && _age < 15)
                v = MathF.Max(v, 4.5f * Dt);
            float planar = v * MathF.Cos(-_pitch);
            request = new(MathF.Cos(_yaw) * planar, MathF.Sin(_yaw) * planar, MathF.Sin(-_pitch) * v);
        }
        float rollTarget = _yawVelocity * MathF.Cos(-_pitch) * -7;
        Spring(ref _roll, ref _rollVelocity, rollTarget, .007f, .17f, 1.2217305f * Dt, angle: true);
        return Result(request);
    }

    private void Turn(Rac1WaterMovementInput input)
    {
        float target = input.Magnitude > 0 ? input.DesiredYaw : _yaw;
        Spring(ref _yaw, ref _yawVelocity, target, .007f, .08f, 5.2359877f * Dt, angle: true);
        _yawRest = (float)Rac1RatchetYawController.WrapPi(target - _yaw);
    }

    public static float SampleStroke(Rac1WaterAnimationFacts animation, int sequence)
    {
        if (animation.Sequence != sequence) return 0; // New action still blending from another owner.
        float t = animation.AuthoredTime;
        if (!float.IsFinite(t) || t < 0 || t >= Stroke.Length ||
            !float.IsFinite(animation.AuthoredAdvance)) return 0;
        int i = (int)t;
        int previous = animation.Wrapped ? Stroke.Length - 1 : (i + Stroke.Length - 1) % Stroke.Length;
        float fraction = t - i;
        return fraction * Stroke[i] + (animation.AuthoredAdvance - fraction) * Stroke[previous];
    }

    private Rac1WaterMovementResult Leave(Rac1WaterExit exit)
    {
        _action = 0;
        _pitch = _roll = 0;
        return Result(default, exit);
    }

    private Rac1WaterMovementResult Result(Rac1NativeVector3 request,
        Rac1WaterExit exit = Rac1WaterExit.None, bool deathFinished = false)
    {
        int sequence = _action switch
        {
            0x33 => 59,
            0x34 => 60,
            0x35 => 61,
            0x36 => 101,
            0x37 => 58,
            0x6a => 95,
            _ => 0
        };
        int domain = _action == 0x6a ? 20 : _action is 0x36 or 0x37 ? 18 : Active ? 17 : 0;
        var water = domain == 18 ? Rac1WaterDomain.Surface : domain is 17 or 20 ? Rac1WaterDomain.Underwater : Rac1WaterDomain.Dry;
        float playback = domain == 18 ? .6f : _action == 0x33 ? Math.Clamp(_scalar * 15, .25f, .75f) : 1;
        Rac1MovementAnimationSelection? animation = Active
            ? new(_action, sequence, _generation, 0, _blend, _action != 0x6a, playback) : null;
        return new(Active, new(water, domain, _action, Active ? sequence : null, Oxygen, _action == 0x6a),
            request, _yaw, _pitch, _roll, animation, exit, deathFinished, _sound);
    }

    private static float Approach(float value, float target, float amount) =>
        value + Math.Clamp(target - value, -amount, amount);

    private static Rac1NativeVector3 Shorten(Rac1NativeVector3 v, float amount) =>
        ClampLength(v, MathF.Max(0, (float)v.Length - amount));

    private static Rac1NativeVector3 ClampLength(Rac1NativeVector3 v, float maximum)
    {
        if (v.Length <= maximum || v.Length == 0) return v;
        double scale = maximum / v.Length;
        return new(v.X * scale, v.Y * scale, v.Z * scale);
    }

    private static void Spring(ref float value, ref float velocity, float target,
        float k, float damping, float maximum, bool angle = false)
    {
        float error = angle ? (float)Rac1RatchetYawController.WrapPi(target - value) : target - value;
        velocity += k * error - damping * velocity;
        if (maximum > 0) velocity = Math.Clamp(velocity, -maximum, maximum);
        velocity = Math.Clamp(velocity, -MathF.Abs(error), MathF.Abs(error));
        value += velocity;
        float rest = angle ? (float)Rac1RatchetYawController.WrapPi(target - value) : target - value;
        if (MathF.Abs(rest) < maximum * .01f) { value = target; velocity = 0; }
        if (angle) value = (float)Rac1RatchetYawController.WrapPi(value);
    }

    private static void Validate(Rac1WaterMovementInput input)
    {
        if (!float.IsFinite(input.PositionHeight) || !float.IsFinite(input.FacingYaw) ||
            !float.IsFinite(input.DesiredYaw) || !float.IsFinite(input.Magnitude) ||
            input.Magnitude is < 0 or > 1 || !float.IsFinite(input.RequestedVerticalStep) ||
            !double.IsFinite(input.ContactMotion.Length) || input.Contact.Present &&
            (!float.IsFinite(input.Contact.SurfaceHeight) || !float.IsFinite(input.Contact.SolidFloorHeight)))
            throw new ArgumentOutOfRangeException(nameof(input));
    }
}
