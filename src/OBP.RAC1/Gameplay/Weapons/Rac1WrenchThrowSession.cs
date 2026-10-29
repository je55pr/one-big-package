using OBP.RAC1.Player;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class71 states10/11 (0x217778..217948, 0x2a9134..2a9404).
/// World contact and the animated hand catch point are supplied by the host.
/// </summary>
public sealed class Rac1WrenchThrowSession
{
    public const float InitialStep = 23f * (1f / 60f);
    public const float OutwardJerk = 170f * (1f / 216000f);
    public const float ReturnJerk = .9f * (1f / 3600f);
    public const float AngularStep = 19.314491f * (1f / 60f);
    public const float BoltCollectionRadius = 2.7f;
    public const float CollisionRadius = .4f;

    private Rac1NativeVector3 _direction;
    private float _acceleration;
    public Rac1NativeVector3 Position { get; private set; }
    public float StepLength { get; private set; }
    public float Spin { get; private set; }
    public int NativeState { get; private set; }
    public bool Active => NativeState is 10 or 11;

    public void Begin(Rac1NativeVector3 position, Rac1NativeVector3 direction)
    {
        if (!double.IsFinite(position.Length) || !double.IsFinite(direction.Length) || direction.Length <= 0)
            throw new ArgumentOutOfRangeException(nameof(direction));
        Position = position;
        _direction = Scale(direction, 1 / direction.Length);
        StepLength = InitialStep;
        _acceleration = Spin = 0;
        NativeState = 10;
    }

    public Rac1NativeVector3 Step(Rac1NativeVector3 catchPoint, bool hitWorld = false)
    {
        if (!double.IsFinite(catchPoint.Length))
            throw new ArgumentOutOfRangeException(nameof(catchPoint));
        if (!Active)
            return Position;
        Spin += AngularStep;
        if (NativeState == 10)
        {
            Position = Add(Position, Scale(_direction, StepLength));
            _acceleration += OutwardJerk;
            StepLength = MathF.Max(0, StepLength - _acceleration);
            if (StepLength == 0)
                NativeState = 11;
            if (hitWorld)
            {
                StepLength *= .5f;
                NativeState = 11;
            }
        }
        else
        {
            var difference = Subtract(catchPoint, Position);
            _acceleration += ReturnJerk;
            StepLength += _acceleration;
            if (difference.Length <= StepLength)
            {
                Position = catchPoint;
                NativeState = 0;
            }
            else
                Position = Add(Position, Scale(difference, StepLength / difference.Length));
        }
        return Position;
    }

    public void Reset() { NativeState = 0; StepLength = Spin = _acceleration = 0; }

    private static Rac1NativeVector3 Scale(Rac1NativeVector3 value, double amount) =>
        new(value.X * amount, value.Y * amount, value.Z * amount);
    private static Rac1NativeVector3 Add(Rac1NativeVector3 a, Rac1NativeVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static Rac1NativeVector3 Subtract(Rac1NativeVector3 a, Rac1NativeVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}
