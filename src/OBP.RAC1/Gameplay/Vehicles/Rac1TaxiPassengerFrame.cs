namespace OBP.RAC1.Gameplay;

/// <summary>
/// Orthonormal scene-space cab axes. This is an engine-neutral host
/// presentation basis, not a recovered PS2 hero-parenting matrix.
/// Godot's +Y is up and a positive yaw rotates +X toward -Z.
/// </summary>
public readonly record struct Rac1TaxiCabBasis(
    Rac1TaxiPoint Right, Rac1TaxiPoint Up, Rac1TaxiPoint Back)
{
    public static Rac1TaxiCabBasis FromYaw(double yaw)
    {
        if (!double.IsFinite(yaw))
            throw new ArgumentOutOfRangeException(nameof(yaw));
        double sine = Math.Sin(yaw);
        double cosine = Math.Cos(yaw);
        return new Rac1TaxiCabBasis(
            new(cosine, 0, -sine),
            new(0, 1, 0),
            new(sine, 0, cosine));
    }

    public bool IsOrthonormal()
    {
        return Finite(Right) && Finite(Up) && Finite(Back) &&
            Close(Dot(Right, Right), 1) &&
            Close(Dot(Up, Up), 1) &&
            Close(Dot(Back, Back), 1) &&
            Close(Dot(Right, Up), 0) &&
            Close(Dot(Right, Back), 0) &&
            Close(Dot(Up, Back), 0);
    }

    public Rac1TaxiPoint ToLocal(Rac1TaxiPoint world) => new(
        Dot(Right, world), Dot(Up, world), Dot(Back, world));

    public Rac1TaxiPoint ToWorld(Rac1TaxiPoint local) => new(
        Right.X * local.X + Up.X * local.Y + Back.X * local.Z,
        Right.Y * local.X + Up.Y * local.Y + Back.Y * local.Z,
        Right.Z * local.X + Up.Z * local.Y + Back.Z * local.Z);

    private static bool Close(double value, double expected) =>
        Math.Abs(value - expected) <= 0.0001d;

    private static bool Finite(Rac1TaxiPoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        double.IsFinite(p.Z);

    private static double Dot(Rac1TaxiPoint a, Rac1TaxiPoint b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>
/// Bounded, full-orientation host passenger pose while native parenting is
/// not wired. Retail class-816/1012 calls attach_frame_set (0x00260738)
/// with the Ratchet-minus-cab displacement and relative 3D orientation.
/// Keeping that displacement in the cab's boarding frame follows yaw,
/// pitch and roll, without claiming to execute native attachment or camera.
/// Coordinates are the consumer's Y-up scene frame.
/// </summary>
public sealed class Rac1TaxiPassengerFrame
{
    private readonly Rac1TaxiPoint _boardingLocalOffset;

    public Rac1TaxiPassengerFrame(Rac1TaxiPoint boardingWorldOffset, double boardingYaw)
        : this(boardingWorldOffset, Rac1TaxiCabBasis.FromYaw(boardingYaw))
    {
    }

    public Rac1TaxiPassengerFrame(
        Rac1TaxiPoint boardingWorldOffset, Rac1TaxiCabBasis boardingBasis)
    {
        if (!Finite(boardingWorldOffset) || !boardingBasis.IsOrthonormal())
            throw new ArgumentOutOfRangeException(nameof(boardingWorldOffset));
        _boardingLocalOffset = boardingBasis.ToLocal(boardingWorldOffset);
    }

    public Rac1TaxiPoint OffsetAt(double cabYaw) =>
        OffsetAt(Rac1TaxiCabBasis.FromYaw(cabYaw));

    public Rac1TaxiPoint OffsetAt(Rac1TaxiCabBasis cabBasis)
    {
        if (!cabBasis.IsOrthonormal())
            throw new ArgumentOutOfRangeException(nameof(cabBasis));
        return cabBasis.ToWorld(_boardingLocalOffset);
    }

    public Rac1TaxiPoint PositionAt(Rac1TaxiPoint cabPosition, double cabYaw) =>
        PositionAt(cabPosition, Rac1TaxiCabBasis.FromYaw(cabYaw));

    public Rac1TaxiPoint PositionAt(Rac1TaxiPoint cabPosition, Rac1TaxiCabBasis cabBasis)
    {
        if (!Finite(cabPosition))
            throw new ArgumentOutOfRangeException(nameof(cabPosition));
        Rac1TaxiPoint offset = OffsetAt(cabBasis);
        return new Rac1TaxiPoint(
            cabPosition.X + offset.X,
            cabPosition.Y + offset.Y,
            cabPosition.Z + offset.Z);
    }

    private static bool Finite(Rac1TaxiPoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        double.IsFinite(p.Z);
}
