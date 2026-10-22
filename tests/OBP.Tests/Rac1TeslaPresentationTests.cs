using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TeslaPresentationTests
{
    [Fact]
    public void TeslaUsesDistinctCoreAndCurlLayersWithTheirNativeTexturesAndWidths()
    {
        var tesla = new Rac1TeslaPresentation(); tesla.Advance();
        Vector3 from = new(100), to = new(108, 100, 100);
        var layers = tesla.ProjectTargetLine(from, to, Rac1PickupRandom.FromSeed(121));
        Assert.Equal(new[] { 14, 16, 14, 16 }, layers.Select(p => p.Texture));
        Assert.Equal(new[] { .05f, .4f, .05f, .3f }, layers.Select(p => p.HalfWidth));
        Assert.Equal(new[] { 0f, .9f, 0f, .1f }, layers.Select(p => p.EndExtension));
        Assert.All(layers, p => { Assert.Equal(from, p.Points[0]); Assert.Equal(to, p.Points[^1]); });
        Assert.Same(layers[0].Points, layers[1].Points);
        Assert.NotEqual(layers[0].Points[1], layers[2].Points[1]);
        Assert.Equal(-.3f, tesla.TexturePhase);
        for (int i = 0; i < 1000; i++) tesla.Advance();
        Assert.InRange(tesla.TexturePhase, -8, 0);
        Assert.InRange(tesla.VerticalPhase, -MathF.PI, MathF.PI);
        tesla.Reset(); Assert.Equal(0, tesla.TexturePhase); Assert.Equal(0, tesla.CurlPhase);
    }

    [Fact]
    public void VerticalAndLongEndpointProjectionsAreFiniteBoundedAndCoincidentPointsEmitNothing()
    {
        var tesla = new Rac1TeslaPresentation();
        Assert.Empty(tesla.ProjectTargetLine(new(100), new(100), Rac1PickupRandom.FromSeed(1)));
        var vertical = tesla.ProjectTargetLine(new(100), new(100, 100, 300), Rac1PickupRandom.FromSeed(1));
        Assert.All(vertical, layer =>
        {
            Assert.Equal(20, layer.Points.Count);
            Assert.All(layer.Points, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
        });
    }
}
