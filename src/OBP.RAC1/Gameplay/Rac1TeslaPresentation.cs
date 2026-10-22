using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Tesla draw parameters from 0x2ba970/2baae0 and wave clocks from 0x2b94d0.
/// Target discovery, the native moving-point solver and world collision are
/// separate from this bounded presentation projection of a supplied target line.
/// </summary>
public sealed class Rac1TeslaPresentation
{
    public const int MaximumPoints = 20;
    public const float NominalStep = 15f / 20;
    public readonly record struct Ribbon(IReadOnlyList<Vector3> Points, int Texture, float HalfWidth, uint Color, float EndExtension);
    public float TexturePhase { get; private set; }
    public float VerticalPhase { get; private set; }
    public float CurlPhase { get; private set; }
    public float RadiusPhase { get; private set; }

    public void Advance()
    {
        VerticalPhase = Wrap(VerticalPhase + Float(0x3eb2b8c2));
        CurlPhase = Wrap(CurlPhase - Float(0x3f4d87ac));
        RadiusPhase = Wrap(RadiusPhase + Float(0x3d567770));
        // 0x2ba970 subtracts .3 per draw and wraps at -8, independently of
        // the angular clocks. Every segment uses the same phase..phase+1 UVs.
        TexturePhase -= .3f;
        if (TexturePhase <= -8) TexturePhase += 8;
    }

    public IReadOnlyList<Ribbon> ProjectTargetLine(Vector3 from, Vector3 to, Rac1PickupRandom random)
    {
        float distance = Vector3.Distance(from, to);
        if (distance < .0001f) return [];
        // Five samples keep the host's short fallback arc visible. Native
        // four auxiliary five-point arcs and their attachment remain separate.
        int count = Math.Clamp((int)MathF.Ceiling(distance / NominalStep) + 1, 5, MaximumPoints);
        var core = new Vector3[count]; var curl = new Vector3[count];
        Vector3 forward = Vector3.Normalize(to - from);
        Vector3 across = Vector3.Cross(forward, Vector3.UnitZ);
        if (across.LengthSquared() < .00001f) across = Vector3.UnitX;
        across = Vector3.Normalize(across);
        Vector3 up = Vector3.Normalize(Vector3.Cross(across, forward));
        core[0] = curl[0] = from; core[^1] = curl[^1] = to;
        for (int i = 1; i < count - 1; i++)
        {
            // The source's wave constants and clocks are retained; projecting
            // them onto a host-selected endpoint line is still an adapter, not
            // a claim to reproduce the full stateful native 20-point solver.
            float bob = .03f * MathF.Sin(VerticalPhase + i * Float(0x3c8efa35)) + random.Signed(0, .1f);
            core[i] = Vector3.Lerp(from, to, i / (float)(count - 1)) + Vector3.UnitZ * bob;
            float angle = CurlPhase + i * Float(0x3f7eadae);
            float radius = .8f * MathF.Sin(RadiusPhase + i * Float(0x3e567750));
            curl[i] = core[i] + (across * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius;
        }
        // The draw routine always reloads the .05 white-core width, including
        // for the curl path. .9/.1 extend the blue segment ends; they are not
        // texture repetition rates or white-core widths.
        return [new(core, 14, .05f, 0x807f7f7f, 0), new(core, 16, .4f, 0x407f2020, .9f),
            new(curl, 14, .05f, 0x107f7f7f, 0), new(curl, 16, .3f, 0x207f2020, .1f)];
    }

    public void Reset() => TexturePhase = VerticalPhase = CurlPhase = RadiusPhase = 0;
    private static float Float(uint bits) => BitConverter.Int32BitsToSingle((int)bits);
    private static float Wrap(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
}
