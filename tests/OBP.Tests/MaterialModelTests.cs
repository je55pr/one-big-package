using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Engine-independent material decisions for the unshaded PS2 world render —
/// alpha handling, culling, the tfrag bake curve, emission. Baked lighting stays
/// authoritative.
/// </summary>
public class MaterialModelTests
{
    private static byte[] Rgba(params (byte r, byte g, byte b, byte a)[] px)
    {
        var buf = new byte[px.Length * 4];
        for (int i = 0; i < px.Length; i++)
        {
            buf[i * 4] = px[i].r;
            buf[i * 4 + 1] = px[i].g;
            buf[i * 4 + 2] = px[i].b;
            buf[i * 4 + 3] = px[i].a;
        }

        return buf;
    }

    [Fact]
    public void AlphaProfile_ClassifiesTexelsAndFlagsBelowHalf()
    {
        var opaque = AlphaProfile.Analyse(Rgba((10, 10, 10, 255), (20, 20, 20, 255)));
        Assert.Equal(1.0, opaque.OpaqueFraction);
        Assert.False(opaque.AnyBelowHalf);
        Assert.False(opaque.HasTransparency);

        var mixed = AlphaProfile.Analyse(Rgba(
            (0, 0, 0, 255), (0, 0, 0, 255), (0, 0, 0, 0), (0, 0, 0, 120)));
        Assert.Equal(0.5, mixed.OpaqueFraction);
        Assert.Equal(0.25, mixed.CutoutFraction);
        Assert.Equal(0.25, mixed.TranslucentFraction);
        Assert.True(mixed.AnyBelowHalf);
    }

    [Theory]
    // opaque -> Opaque
    [InlineData(1.0, 0.0, 0.0, AlphaMode.Opaque)]
    // a real hard cut-out band -> Scissor
    [InlineData(0.7, 0.30, 0.0, AlphaMode.Scissor)]
    // a couple of stray sub-threshold texels -> still Opaque (no speckle)
    [InlineData(0.999, 0.001, 0.0, AlphaMode.Opaque)]
    // meaningful partial alpha -> Blend
    [InlineData(0.8, 0.0, 0.20, AlphaMode.Blend)]
    public void ResolveAlpha_PicksModeFromWhatTheTextureActuallyContains(
        double op, double cut, double trans, AlphaMode expected)
    {
        var (mode, threshold) = MaterialModel.ResolveAlpha(new AlphaProfile(op, cut, trans, cut > 0 || trans > 0));
        Assert.Equal(expected, mode);
        Assert.Equal(0.5f, threshold);
    }

    [Fact]
    public void Culling_And_Tiling_And_VertexColour_ByKind()
    {
        Assert.True(MaterialModel.BackFaceCull("tfrag"));
        Assert.True(MaterialModel.BackFaceCull("tie"));
        Assert.False(MaterialModel.BackFaceCull("moby"));
        Assert.False(MaterialModel.BackFaceCull("shrub"));

        Assert.True(MaterialModel.TileTexture("sky"));
        Assert.False(MaterialModel.TileTexture("moby"));

        Assert.True(MaterialModel.VertexColourAsAlbedo("moby", hasColour: true));
        Assert.True(MaterialModel.VertexColourAsAlbedo("tfrag", hasColour: true));
        Assert.False(MaterialModel.VertexColourAsAlbedo("moby", hasColour: false));
        Assert.False(MaterialModel.VertexColourAsAlbedo("tie", hasColour: true));
    }

    [Fact]
    public void BakeCurve_LiftsDarksAndClampsHighs_Monotonic()
    {
        Assert.Equal(0.6f, MaterialModel.BakeCurve(0f), precision: 5);
        Assert.Equal(1f, MaterialModel.BakeCurve(1f), precision: 5);
        Assert.True(MaterialModel.BakeCurve(0.2f) > 0.6f);

        float prev = -1;
        for (float c = 0; c <= 1f; c += 0.05f)
        {
            float v = MaterialModel.BakeCurve(c);
            Assert.InRange(v, 0f, 1f);
            Assert.True(v >= prev, $"not monotonic at {c}");
            prev = v;
        }

        Assert.True(MaterialModel.UsesBakeCurve("tfrag"));
        Assert.False(MaterialModel.UsesBakeCurve("tie"));
    }

    [Fact]
    public void Emission_OnlyForVeryBrightTextures_Clamped()
    {
        Assert.Equal(0.0, MaterialModel.EmissionEnergy(0.5));
        Assert.Equal(0.0, MaterialModel.EmissionEnergy(0.82));
        Assert.True(MaterialModel.EmissionEnergy(0.9) > 0);
        Assert.Equal((1.0 - 0.82) * 1.6, MaterialModel.EmissionEnergy(1.0), precision: 6);
        Assert.InRange(MaterialModel.EmissionEnergy(2.0), 0.0, 0.35); // clamp ceiling

        // white texels -> ~1.0 luminance
        Assert.Equal(1.0, MaterialModel.MeanLuminance(Rgba((255, 255, 255, 255))), precision: 3);
        Assert.Equal(0.0, MaterialModel.MeanLuminance(Rgba((0, 0, 0, 255))), precision: 3);
    }
}
