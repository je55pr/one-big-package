namespace OBP.RAC3.Gameplay;

/// <summary>UYA boot rand 0x13d3e0, helpers 0x440848/0x440878/0x4408c0. Independent of GC's stream.</summary>
public sealed class Rac3ParticleRandom(Func<int> next15)
{
    public long SamplesConsumed { get; private set; }
    public static Rac3ParticleRandom FromSeed(uint seed) => new(() =>
    {
        seed = unchecked(seed * 0x343fd + 0x269ec3);
        return (int)(seed >> 16 & 0x7fff);
    });

    private int Next()
    {
        int value = next15();
        if (value is < 0 or > 32767) throw new InvalidOperationException("UYA particles require a native 15-bit sample.");
        SamplesConsumed++; return value;
    }
    internal int Below(int limit) => Next() % limit;
    // Reversed endpoints remain reversed. Native remainder for (-1,-3) is zero: spin is -1, with a draw consumed.
    internal int Integer(int first, int last) => first + Next() % (last - first + 1);
    internal float Float(float first, float last) => Rac3ParticleMath.Add(first, Rac3ParticleMath.Multiply(
        Rac3ParticleMath.Multiply(Next(), Rac3ParticleMath.Subtract(last, first)), 1f / 32768));
}
