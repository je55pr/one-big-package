using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>Dedicated retail debris classes selected by 0x2d09b8; update 0x2afa48.</summary>
public sealed class Rac1CrateDebris
{
    private static readonly Vector3[] FaceOffsets = [new(-.251f, -.165f, .836f), new(-.335f, -.254f, .670f),
        new(-.171f, -.338f, .748f), new(.25f, .163f, .836f), new(-.335f, .249f, .335f), new(.166f, -.338f, .251f)];
    private static readonly Vector3[] FaceRotations = [new(3.141f, 3.141f, -1.57f), new(1.57f, 1.57f, -1.57f),
        new(1.57f, 0, 0), new(3.141f, 3.141f, 1.57f), new(1.57f, 1.57f, 1.57f), new(1.57f, 0, 3.141f)];
    public int NativeClassId { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Vector3 Rotation { get; private set; }
    public Quaternion Orientation => Quaternion.CreateFromRotationMatrix(
        Matrix4x4.CreateRotationX(Rotation.X) * Matrix4x4.CreateRotationY(Rotation.Y) * Matrix4x4.CreateRotationZ(Rotation.Z));
    public Vector3 Spin { get; }
    public float Scale { get; private set; }
    public int Alpha { get; private set; } = 128;
    public bool Expired => Alpha < 4;
    private readonly float _launchHeight;
    private int _contactDelay;
    private int _bounces;
    private bool _fading;

    public Rac1CrateDebris(int nativeClassId, Vector3 position, Vector3 velocity,
        Vector3 rotation, float scale, int bounces, Rac1PickupRandom random)
    {
        if (scale <= 0 || bounces < 0) throw new ArgumentOutOfRangeException(nameof(scale));
        NativeClassId = nativeClassId;
        Position = position; Velocity = velocity; Rotation = rotation; Scale = scale;
        _launchHeight = position.Z; _bounces = bounces; _contactDelay = random.Below(20);
        Spin = new(0, random.Signed(0, 360) * MathF.PI / 180 / 60, random.Signed(0, 360) * MathF.PI / 180 / 60);
    }

    public static int PrimaryClass(int crateClass) => crateClass switch
    {
        Rac1ClassIds.WoodenCrate or Rac1ClassIds.AmmoCrate => 357,
        Rac1ClassIds.NanotechCrate => 348,
        Rac1ClassIds.MetalCrate => 354,
        505 => 363, // Native break_crate variant 5 chooses source fragments 0x16b/0x16c.
        _ => throw new ArgumentOutOfRangeException(nameof(crateClass)),
    };

    /// <summary>
    /// 0x2d0cd8 budget uses free dynamic Moby slots (allocator decrements
    /// 0x15ffbc), not rendered frame rate. The host supplies pool availability.
    /// Camera-facing quadrant choice is an explicit omission: identity quadrant.
    /// </summary>
    public static IReadOnlyList<Rac1CrateDebris> Break(int crateClass, Matrix4x4 nativeTransform,
        Rac1PickupRandom random, int freeMobySlots = 256)
    {
        int primary = PrimaryClass(crateClass);
        int primaryCount = freeMobySlots > 200 ? 4 : freeMobySlots > 100 ? 2 : 1;
        int secondaryCount = freeMobySlots > 200 ? 4 : freeMobySlots > 150 ? 2 : freeMobySlots > 100 ? 1 : 0;
        var output = new List<Rac1CrateDebris>();
        for (int i = 0; i < primaryCount + secondaryCount; i++)
        {
            bool face = i < primaryCount;
            int faceIndex = i;
            Vector3 offset = face ? FaceOffsets[faceIndex] : new(random.Range(-.5f, .5f), random.Range(-.5f, .5f), random.Range(0, 1));
            Vector3 planar = new(offset.X, offset.Y, 0);
            planar = planar.LengthSquared() > 0 ? Vector3.Normalize(planar) : Vector3.UnitX;
            Vector3 velocity = face ? planar * (random.Range(1, 5) / 60) :
                new(random.Range(-6, 6) / 60, random.Range(-6, 6) / 60, 0);
            velocity.Z = random.Range(face ? 4 : 6, face ? 9 : 12) / 60;
            output.Add(new(primary + (face ? 0 : 1), Vector3.Transform(offset, nativeTransform),
                Vector3.TransformNormal(velocity, nativeTransform),
                face ? FaceRotations[faceIndex] : new(random.Angle(), random.Angle(), random.Angle()),
                face ? 1 : random.Range(.25f, .5f), face ? random.Below(2) : 1, random));
        }
        return output;
    }

    public void Step(Rac1PickupContact? contact = null)
    {
        Velocity -= new Vector3(0, 0, 20f / 3600f);
        Position += Velocity;
        Rotation += Spin;
        if (Position.Z < _launchHeight - 3) _fading = true;
        if (_contactDelay > 0) _contactDelay--;
        else if (!_fading && _bounces > 0 && contact is { } hit && Vector3.Dot(Velocity, hit.Normal) < 0)
        {
            Position = hit.Position;
            Velocity = Vector3.Reflect(Velocity, Vector3.Normalize(hit.Normal)) * .75f;
            _bounces--;
        }
        if (_fading) { Scale *= .9f; Alpha = Math.Max(0, Alpha - 4); }
    }
}
