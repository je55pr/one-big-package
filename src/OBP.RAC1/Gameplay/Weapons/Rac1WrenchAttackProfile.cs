namespace OBP.RAC1.Gameplay;

/// <summary>First five 44-byte rows at LEVEL0 0x17bc28; times are authored animation coordinates.</summary>
public sealed record Rac1WrenchAttackProfile(
    int Id, int Family, int ComboIndex, int ChainBefore, int BufferReference,
    int ChainFrom, int JumpFrom, int FinishAfter, int HitFrom, int HitThrough,
    int TurnFrom, int TurnThrough)
{
    public const uint NativeDamageFlags = 0x10000;
    public float NativeDamage => Family == 1 ? 2 : 1;
    public static IReadOnlyList<Rac1WrenchAttackProfile> Profiles { get; } =
    [
        new(0, 0, 0, 33, 19, 26, 24, 31, 17, 23, 17, 24),
        new(1, 0, 1, 25, 7, 12, 13, 23, 8, 13, 6, 12),
        new(2, 0, 2, 0, 16, 23, 19, 21, 12, 16, 9, 23),
        new(3, 2, 0, 99, 83, 88, 87, 91, 99, 99, 99, 99),
        new(4, 1, 0, 99, 32, 28, 28, 30, 1, 28, 22, 25),
    ];

    public int NativeAction => Family switch { 0 => 0x13, 1 => 0x14, _ => 0x15 };
    public int BodySequence => Family switch { 0 => 23 + ComboIndex, 1 => 43, _ => 26 };
    public int WrenchSequence => Family switch { 0 => 3 + ComboIndex, 1 => 10, _ => 6 };
    public int EntryFrame => Family == 0 ? ComboIndex : Family == 1 ? 4 : 0;
    public int BlendTicks => Family == 0 ? (ComboIndex == 0 ? 5 : 7) : Family == 1 ? 11 : 10;

    public bool CanHit(float authoredTime, bool blending) =>
        !blending && authoredTime >= HitFrom && authoredTime <= HitThrough;

    public int BufferedPressWindow(float authoredTime) =>
        Math.Clamp(2 * ((int)MathF.Floor(authoredTime) - BufferReference), 1, 15);
}
