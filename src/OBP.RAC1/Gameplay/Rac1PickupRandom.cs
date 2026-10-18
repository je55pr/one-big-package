namespace OBP.RAC1.Gameplay;

/// <summary>
/// The distributions in 0x257b50/0x257be8/0x257cb0. The caller supplies the
/// native rand() high 15 bits; the host's seeded stream is not a retail replay.
/// </summary>
public sealed class Rac1PickupRandom(Func<int> next15)
{
    /// <summary>0x1160d8 LCG. A host seed does not reproduce the game's global call order.</summary>
    public static Rac1PickupRandom FromSeed(uint seed) => new(() =>
    {
        seed = unchecked(seed * 1103515245u + 12345u) & 0x7fffffffu;
        return (int)(seed >> 16);
    });
    private int Next() => next15() is >= 0 and <= 32767 and var value
        ? value : throw new InvalidOperationException("Expected a 15-bit retail random sample.");
    public int Below(int limit) => limit > 0 ? Next() % limit : throw new ArgumentOutOfRangeException(nameof(limit));
    public int Inclusive(int minimum, int maximum) => minimum + Below(checked(maximum - minimum + 1));
    public float Range(float minimum, float maximum) => minimum + Next() * (maximum - minimum) / 32768f;
    public float Angle() => ((Next() & 0xfff) - 0x800) * MathF.PI / 2048f;
    public float Signed(float minimum, float maximum)
    {
        int sample = Next();
        float value = minimum + (sample & 0xfff) * (maximum - minimum) / 4096f;
        return (sample & 1) != 0 ? -value : value;
    }
}
