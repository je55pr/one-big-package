using System.Numerics;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Original detachable-point geometry and the generic free/point/edge/settled
/// states (Oozla 0x2D1798). World sweeps and contact selection are host adapters.
/// </summary>
public sealed class Rac2CrateFragment
{
    public sealed record Contact(int Vertex, Vector3 Point, Vector3 Normal);
    private sealed record Support(int Vertex, Vector3 Point, Vector3 Normal);
    public int Part { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Quaternion Rotation { get; private set; } = Quaternion.Identity;
    public float Alpha { get; private set; } = 1;
    public bool Expired => Alpha <= 0;
    public bool Settled => _supports.Count == 3;
    public int ContactCount => _supports.Count;
    private readonly Vector3[] _vertices;
    private readonly List<Support> _supports = [];
    private Quaternion _angularStep;
    private readonly float _gravity;
    private int _remaining = 180, _age;
    private bool _fading;
    private const float MinimumSpeedLoss = .033333335f;
    private const float GravityTurn = .0017453295f;
    private const float MaximumTurn = MathF.PI / 60; // 3 degrees/tick; 0x2D2558..0x2D25BC.

    /// <param name="vertices">Original contact points relative to the part centre, in its initial world orientation and scale.</param>
    public Rac2CrateFragment(int part, Vector3 origin, IReadOnlyList<Vector3> vertices, Func<float, float, float> sample)
    {
        if (vertices.Count < 3) throw new ArgumentException("A fragment needs its original contact points.", nameof(vertices));
        Part = part;
        Position = origin;
        _vertices = vertices.ToArray();
        float angle = sample(-MathF.PI, MathF.PI), speed = sample(3, 6) / 60;
        Velocity = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, sample(3, 6) / 60);
        var axis = new Vector3(sample(-1, 1), sample(-1, 1), sample(-1, 1));
        axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitZ;
        _angularStep = Quaternion.CreateFromAxisAngle(axis, sample(90, 270) * MathF.PI / 180 / 60);
        _gravity = sample(10, 20) / 3600;
    }

    public void Step(Func<int, Vector3, Vector3, Contact?> probe)
    {
        if (Expired) return;
        bool wasSettled = Settled, wasFading = _fading;
        if (!wasSettled) Advance(probe);
        _age++;
        // Native +0x5C is the support count, not "has ever bounced".
        if (_supports.Count != 0 || (_age & 3) == 0) _remaining--;
        if (wasSettled || _remaining <= 0) _fading = true;
        if (wasFading) Alpha = Math.Max(0, Alpha - 2f / 128);
    }

    private void Advance(Func<int, Vector3, Vector3, Contact?> probe)
    {
        Vector3 before = Position;
        Quaternion previous = Rotation;
        if (_supports.Count == 0)
        {
            Velocity -= Vector3.UnitZ * _gravity;
            Position += Velocity;
            Rotation = Quaternion.Normalize(_angularStep * Rotation);
        }
        else AdvanceOnSupport();
        Contact? contact = null;
        float deepest = float.NegativeInfinity;
        for (int index = 0; index < _vertices.Length; index++)
        {
            if (_supports.Any(support => support.Vertex == index)) continue;
            Vector3 from = before + Vector3.Transform(_vertices[index], previous);
            Vector3 to = Vertex(index);
            if (probe(index, from, to) is not { } hit) continue;
            float depth = Vector3.Dot(hit.Point - to, hit.Normal);
            if (depth < -.001f || depth <= deepest) continue;
            contact = hit;
            deepest = depth;
        }
        if (contact is not { } collision) return;
        Position += collision.Normal * Math.Max(0, deepest);
        Resolve(collision);
    }

    private void AdvanceOnSupport()
    {
        Vector3 localPivot = _vertices[_supports[0].Vertex];
        Vector3 anchor = _supports[0].Point;
        if (_supports.Count == 2)
        {
            localPivot = (localPivot + _vertices[_supports[1].Vertex]) * .5f;
            anchor = (anchor + _supports[1].Point) * .5f;
        }
        // 0x2D1E78..0x2D1ED8: rotate about support, rather than translating
        // the centre across the floor after the linear bounce has stopped.
        Vector3 lever = Vector3.Transform(localPivot, Rotation);
        Turn(Vector3.Cross(lever, Vector3.UnitZ), GravityTurn);
        if (_supports.Count == 2)
        {
            Vector3 edge = Vector3.Normalize(Vector3.Transform(_vertices[_supports[1].Vertex] - _vertices[_supports[0].Vertex], Rotation));
            var vector = new Vector3(_angularStep.X, _angularStep.Y, _angularStep.Z);
            vector = edge * Vector3.Dot(vector, edge); // 0x2D25E0: constrain spin to the supporting edge.
            _angularStep = new(vector, MathF.Sqrt(Math.Max(0, 1 - vector.LengthSquared())));
        }
        Rotation = Quaternion.Normalize(_angularStep * Rotation);
        Position = anchor - Vector3.Transform(localPivot, Rotation);
    }

    private void Resolve(Contact contact)
    {
        if (_supports.Count == 0)
        {
            Vector3 torque = Vector3.Cross(contact.Normal, Velocity);
            Turn(torque, Math.Min(torque.Length(), MaximumTurn * .5f));
            Velocity = Vector3.Reflect(Velocity, contact.Normal);
            float speed = Velocity.Length();
            if (Vector3.Dot(contact.Normal, Vector3.UnitZ) > .7f && speed > 0)
            {
                // 0x2D2630 removes max(45% of speed, 2/60). It does not
                // multiply every reflected component by 0.45 indefinitely.
                float next = Math.Max(0, speed - Math.Max(speed * .45f, MinimumSpeedLoss));
                Velocity *= next / speed;
            }
            if (Velocity.LengthSquared() > 0) return;
        }
        Velocity = Vector3.Zero;
        Vector3 point = Vertex(contact.Vertex);
        if (_supports.Any(support => Vector3.DistanceSquared(point, support.Point) < 1e-8f)) return;
        _supports.Add(new(contact.Vertex, point, contact.Normal));
        PruneSupports();
    }

    private void PruneSupports()
    {
        if (_supports.Count < 3) return;
        Vector3 a = _supports[0].Point, b = _supports[1].Point, c = _supports[2].Point;
        Vector3 cross = Vector3.Cross(b - a, c - a);
        float area = cross.LengthSquared();
        if (area > 1e-10f)
        {
            float u = Vector3.Dot(Vector3.Cross(b - Position, c - Position), cross) / area;
            float v = Vector3.Dot(Vector3.Cross(c - Position, a - Position), cross) / area;
            float w = 1 - u - v;
            if (u >= -1e-4f && v >= -1e-4f && w >= -1e-4f) return;
        }
        // Bounded manifold adapter: retain the edge nearest the projected
        // centre. Native 0x2D2298 uses its own contact-plane predicates.
        Vector3 normal = _supports[0].Normal + _supports[1].Normal + _supports[2].Normal;
        normal = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        Vector3 projected = Position - normal * Vector3.Dot(Position - a, normal);
        int remove = Enumerable.Range(0, 3).MinBy(index =>
        {
            Vector3 start = _supports[(index + 1) % 3].Point, end = _supports[(index + 2) % 3].Point;
            Vector3 edge = end - start;
            float t = Math.Clamp(Vector3.Dot(projected - start, edge) / Math.Max(1e-10f, edge.LengthSquared()), 0, 1);
            return Vector3.DistanceSquared(projected, start + edge * t);
        });
        _supports.RemoveAt(remove);
    }

    private void Turn(Vector3 axis, float angle)
    {
#pragma warning disable S1244 // Exact zero is a no-op rotation; an epsilon would discard intentional small turns.
        if (axis.LengthSquared() < 1e-12f || angle == 0) return;
#pragma warning restore S1244
        _angularStep = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle) * _angularStep);
        float halfAngle = MathF.Acos(Math.Clamp(_angularStep.W, -1, 1));
        if (halfAngle <= MaximumTurn * .5f) return;
        var vector = new Vector3(_angularStep.X, _angularStep.Y, _angularStep.Z);
        _angularStep = Quaternion.CreateFromAxisAngle(Vector3.Normalize(vector), MaximumTurn);
    }

    private Vector3 Vertex(int index) => Position + Vector3.Transform(_vertices[index], Rotation);
}
