namespace OBP.RAC1.Gameplay;

/// <summary>
/// The distributions in 0x257b50/0x257be8/0x257cb0. The caller supplies the
/// native rand() high 15 bits; the host's seeded stream is not a retail replay.
/// </summary>
public sealed class Rac1PickupRandom(Func<int> next15)
{
    /// <summary>0x1160d8 LCG. A host seed does not reproduce the game's global call order.</summary>
    public static Rac1PickupRandom FromSeed(uint seed)
    {
        uint origin = seed, state = seed;
        return new(() =>
        {
            state = unchecked(state * 1103515245u + 12345u) & 0x7fffffffu;
            return (int)(state >> 16);
        })
        { _origin = origin };
    }

    private uint? _origin;
    private uint _forks;
    private int Next() => next15() is >= 0 and <= 32767 and var value
        ? value : throw new InvalidOperationException("Expected a 15-bit retail random sample.");
    internal int Next15() => Next();
    /// <summary>
    /// An independent presentation-only stream. A seeded stream forks from its original seed and a fork counter, drawing nothing,
    /// so creating cosmetic effects cannot shift later gameplay rolls (and stays deterministic for a fixed seed). A stream
    /// built from an arbitrary sample source has no seed to derive from, so it falls back to two draws.
    /// </summary>
    public Rac1PickupRandom Fork()
    {
        if (_origin is not uint origin) return FromSeed((uint)(Below(32768) << 15 | Below(32768)));
        return FromSeed(unchecked((origin + 0x9E3779B9u * ++_forks) * 2654435761u));
    }
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
