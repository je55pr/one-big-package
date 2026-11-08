using System.Numerics;

namespace OBP.Fusion;

/// <summary>
/// OBP-authored physical supply piece. This is a common presentation trajectory,
/// not a recovered PS2 pickup controller. One host tick is 1/60 second, in Y-up world units.
/// No inventory, native resource selection or grant amount is owned by this motion.
/// </summary>
public sealed class PlayerAmmoPickupMotion
{
    private const float TickSeconds = 1f / 60;
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public float Spin { get; private set; }
    public bool Claimed { get; private set; }
    public bool Finished { get; private set; }
    private readonly float _originHeight;
    private int _delay = PlayerAmmoSupplyPolicy.DropGraceTicks;
    private float _speed = 8;

    public PlayerAmmoPickupMotion(Vector3 origin, Func<float, float, float> sample)
    {
        Position = origin;
        _originHeight = origin.Y;
        float angle = sample(-MathF.PI, MathF.PI), speed = sample(2, 4);
        Velocity = new(MathF.Cos(angle) * speed, sample(4, 8), MathF.Sin(angle) * speed);
    }

    public bool CanCollect(Vector3 player) => !Claimed && !Finished && _delay == 0 &&
        MathF.Abs(Position.Y - player.Y) <= 2.25f &&
        Vector2.Distance(new(Position.X, Position.Z), new(player.X, player.Z)) <= 4.25f;

    public void Claim() => Claimed = true;

    public void Step(Vector3 socket, Func<Vector3, Vector3, Vector3?> ground)
    {
        if (Finished) return;
        Spin += 3 * TickSeconds;
        if (Claimed)
        {
            _speed = Math.Min(24, _speed + 24 * TickSeconds);
            var delta = socket - Position;
            if (delta.Length() <= _speed * TickSeconds) { Position = socket; Finished = true; }
            else Position += Vector3.Normalize(delta) * (_speed * TickSeconds);
            return;
        }
        if (_delay > 0) _delay--;
        Velocity -= Vector3.UnitY * (12 * TickSeconds);
        var next = Position + Velocity * TickSeconds;
        if (ground(Position, next) is { } contact) { Position = contact; Velocity = Vector3.Zero; }
        else Position = next;
        // Dropping out of the world never awards ammunition remotely.
        if (Position.Y < _originHeight - 30) Finished = true;
    }
}
