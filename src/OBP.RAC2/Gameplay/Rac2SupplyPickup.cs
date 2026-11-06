using System.Numerics;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Supply admission and one-grant ownership. The host supplies collision/visibility
/// and cloud particle travel; those presentation approximations are not PS2 splines.
/// </summary>
public sealed class Rac2NanotechCloud(Vector3 origin)
{
    public Vector3 Origin { get; } = origin;
    public bool Attracting { get; private set; }
    public bool Collected { get; private set; }
    public bool TryAttract(Vector3 player, int health, int capacity, int pending, bool visible)
    {
        if (Attracting || Collected || health <= 0 || health + pending >= capacity || !visible ||
            Vector3.Distance(player, Origin) >= Rac2CrateSupply.NanotechAttractionDistance) return false;
        return Attracting = true;
    }
    public bool Arrive()
    {
        if (!Attracting || Collected) return false;
        return Collected = true;
    }
}

/// <summary>
/// Class3291 scatter, ground contact and homing projection. Plane contacts and
/// stopped-on-contact settling are explicit OBP substitutes for the generic PS2
/// contact manifold; selection, amount and collection capacity remain native.
/// </summary>
public sealed class Rac2AmmoPickup
{
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public bool Claimed { get; private set; }
    public bool Arrived { get; private set; }
    public float Spin { get; private set; }
    private readonly float _gravity;
    private int _admissionDelay;
    private float _speed = .13333334f;
    public Rac2AmmoPickup(Vector3 origin, Func<float, float, float> sample, int admissionDelay = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(admissionDelay);
        _admissionDelay = admissionDelay;
        Position = origin;
        float angle = sample(-MathF.PI, MathF.PI), speed = sample(2, 4) / 60;
        Velocity = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, sample(4, 8) / 60);
        _gravity = sample(10, 15) / 3600;
    }
    public bool CanCollect(Vector3 player, float radius, float height) => !Claimed && _admissionDelay == 0 &&
        MathF.Abs(Position.Z - player.Z) <= height && Vector2.Distance(new(Position.X, Position.Y), new(player.X, player.Y)) <= radius;
    public void Claim() => Claimed = true;
    public void Step(Vector3 socket, Func<Vector3, Vector3, Vector3?> ground)
    {
        if (Arrived) return;
        Spin += .05f; // Host display rotation, not the native orientation solver.
        if (Claimed)
        {
            _speed = Math.Min(1.0666667f, _speed + .006666667f);
            var to = socket - Position;
            if (to.Length() <= _speed) { Position = socket; Arrived = true; }
            else Position += Vector3.Normalize(to) * _speed;
            return;
        }
        if (_admissionDelay > 0) _admissionDelay--;
        Velocity -= Vector3.UnitZ * _gravity;
        var next = Position + Velocity;
        if (ground(Position, next) is { } contact) { Position = contact; Velocity = Vector3.Zero; }
        else Position = next;
    }
}
