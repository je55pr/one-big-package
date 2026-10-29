namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original R&C1 packed buried-bolt progress at resident address 0x0014bf10:
/// twenty planet rows, sixteen bytes per row, two saturating 4-bit counters
/// per byte. A native class-605 collection increments one nibble per five
/// extracted bolts. The boot ELF initializes all 320 bytes to zero.
/// </summary>
public sealed class Rac1BuriedBoltProgress
{
    public const int LevelCount = 20;
    public const int BytesPerLevel = 16;
    public const int TargetsPerLevel = 32;
    private readonly byte[] _nibbles = new byte[LevelCount * BytesPerLevel];

    public int Read(int levelId, int ordinal)
    {
        int at = Offset(levelId, ordinal);
        byte current = _nibbles[at];
        return (ordinal & 1) == 0 ? current >> 4 : current & 0xf;
    }

    public int Increment(int levelId, int ordinal)
    {
        int at = Offset(levelId, ordinal);
        byte current = _nibbles[at];
        int next = Math.Min(15, Read(levelId, ordinal) + 1);
        _nibbles[at] = (byte)((ordinal & 1) == 0
            ? (current & 0xf) | (next << 4)
            : (current & 0xf0) | next);
        return next;
    }

    public byte[] Snapshot() => (byte[])_nibbles.Clone();

    public static Rac1BuriedBoltProgress Restore(byte[]? saved)
    {
        var history = new Rac1BuriedBoltProgress();
        if (saved is null || saved.Length == 0) return history;
        if (saved.Length != LevelCount * BytesPerLevel)
            throw new InvalidDataException("Native R&C1 buried-bolt history must contain 320 bytes.");
        saved.CopyTo(history._nibbles, 0);
        return history;
    }

    private static int Offset(int levelId, int ordinal)
    {
        if (levelId is < 0 or >= LevelCount) throw new ArgumentOutOfRangeException(nameof(levelId));
        if (ordinal is < 0 or >= TargetsPerLevel) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return levelId * BytesPerLevel + ordinal / 2;
    }
}
