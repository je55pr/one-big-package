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

    // Loaded Oozla 0x310580 / 0x3105E0. Symmetric uses the same sample's
    // low twelve fraction bits and bit zero for sign. The sign bit deliberately
    // overlaps the fraction, matching 0x310604/0x31061C; it is not an independent draw.
    internal float Range(float minimum, float maximum) => Rac2ParticleMath.Add(minimum,
        Rac2ParticleMath.Multiply(Rac2ParticleMath.Multiply(Next(), Rac2ParticleMath.Subtract(maximum, minimum)), 1f / 32768));
    internal float Symmetric(float minimum, float maximum)
    {
        int sample = Next();
        float value = Rac2ParticleMath.Add(minimum, Rac2ParticleMath.Multiply(
            Rac2ParticleMath.Multiply(sample & 4095, Rac2ParticleMath.Subtract(maximum, minimum)), 1f / 4096));
        return (sample & 1) == 0 ? value : -value;
    }
}
