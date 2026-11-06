using System.Numerics;

namespace OBP.RAC2.Gameplay;

/// <summary>Class-500 part-mask emission (0x392678..0x392720) and generic fragment parameters (0x2D1400). Static-world contact is supplied by the host.</summary>
public sealed class Rac2CrateFragment
{
    public int Part { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Quaternion Rotation { get; private set; } = Quaternion.Identity;
    public float Alpha { get; private set; } = 1;
    public bool Expired => Alpha <= 0;
    private readonly Vector3 _axis;
    private readonly float _spin, _gravity;
    private int _remaining = 180;
    private int _age;
    private bool _contacted;
    public Rac2CrateFragment(int part, Vector3 origin, Func<float, float, float> sample)
    {
        Part = part;
        Position = origin;
        float angle = sample(-MathF.PI, MathF.PI), speed = sample(3, 6) / 60;
        Velocity = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, sample(3, 6) / 60);
        var axis = new Vector3(sample(-1, 1), sample(-1, 1), sample(-1, 1));
        _axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitZ;
        _spin = sample(90, 270) * MathF.PI / 180 / 60;
        _gravity = sample(10, 20) / 3600;
    }
    public void Step(Rac2BoltContact? contact)
    {
        if (Expired) return;
        Velocity -= Vector3.UnitZ * _gravity;
        Position += Velocity;
        Rotation = Quaternion.Normalize(Quaternion.Concatenate(Rotation, Quaternion.CreateFromAxisAngle(_axis, _spin)));
        if (contact is { } hit && Vector3.Dot(Velocity, hit.Normal) < 0)
        {
            _contacted = true;
            Position = hit.Position;
            Velocity = Vector3.Reflect(Velocity, hit.Normal) * .45f;
            if (Velocity.LengthSquared() < .033333335f * .033333335f) Velocity = Vector3.Zero;
        }
        _age++;
        if (_contacted || (_age & 3) == 0) _remaining--;
        if (_remaining <= 0) Alpha = Math.Max(0, Alpha - 2f / 128);
    }
}
