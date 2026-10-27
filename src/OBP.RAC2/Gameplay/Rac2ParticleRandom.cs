namespace OBP.RAC2.Gameplay;

/// <summary>Museum helpers 0x3210d0/0x321110 consume the high 15 bits of GC's newlib rand (boot 0x1163b0).</summary>
public sealed class Rac2ParticleRandom(Func<int> next15)
{
    public long SamplesConsumed { get; private set; }

    /// <summary>Conditioned stream only; it does not replay other retail consumers of the global RNG.</summary>
    public static Rac2ParticleRandom FromSeed(uint seed) => new(() =>
    {
        seed = unchecked(seed * 0x41c64e6d + 0x3039);
        return (int)(seed >> 16 & 0x7fff);
    });

    private int Next()
    {
        int value = next15();
        if (value is < 0 or > 32767) throw new InvalidOperationException("GC particles require a native high-15-bit sample.");
        SamplesConsumed++;
        return value;
    }

    internal int Below(int limit) => Next() % limit;
    internal int Inclusive(int minimum, int maximum) => minimum + Next() % (maximum - minimum + 1);
}
