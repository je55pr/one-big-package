namespace OBP.RAC1.Gameplay;

/// <summary>
/// Bounded host passenger pose while native Ratchet-to-cab frame ownership
/// is not wired. The original class-816 / class-1012 updater calls
/// attach_frame_set (0x00260738) with player-minus-cab displacement and
/// cab orientation relative to its start-of-tick frame. This session
/// retains a boarding-space displacement and follows taxi yaw; it does
/// NOT claim to reproduce native animation parenting or camera mode.
/// Coordinates are the consumer's Y-up scene frame.
/// </summary>
public sealed class Rac1TaxiPassengerFrame
{
    private readonly Rac1TaxiPoint _boardingOffset;
    private readonly double _boardingYaw;

    public Rac1TaxiPassengerFrame(Rac1TaxiPoint boardingWorldOffset, double boardingYaw)
    {
        if (!Finite(boardingWorldOffset) || !double.IsFinite(boardingYaw))
            throw new ArgumentOutOfRangeException(nameof(boardingWorldOffset));
        _boardingOffset = boardingWorldOffset;
        _boardingYaw = boardingYaw;
    }

    public Rac1TaxiPoint OffsetAt(double cabYaw)
    {
        if (!double.IsFinite(cabYaw))
            throw new ArgumentOutOfRangeException(nameof(cabYaw));

        // Godot's positive yaw rotates +X toward -Z in its Y-up scene.
        // Relative rotation preserves the boarding pose exactly when the
        // cab has not yet turned, including after a full 2*pi revolution.
        double rotation = Math.IEEERemainder(cabYaw - _boardingYaw, Math.Tau);
        double sine = Math.Sin(rotation);
        double cosine = Math.Cos(rotation);
        return new Rac1TaxiPoint(
            _boardingOffset.X * cosine + _boardingOffset.Z * sine,
            _boardingOffset.Y,
            _boardingOffset.Z * cosine - _boardingOffset.X * sine);
    }

    public Rac1TaxiPoint PositionAt(Rac1TaxiPoint cabPosition, double cabYaw)
    {
        if (!Finite(cabPosition))
            throw new ArgumentOutOfRangeException(nameof(cabPosition));

        Rac1TaxiPoint offset = OffsetAt(cabYaw);
        return new Rac1TaxiPoint(
            cabPosition.X + offset.X,
            cabPosition.Y + offset.Y,
            cabPosition.Z + offset.Z);
    }

    private static bool Finite(Rac1TaxiPoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
