using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1BeamSlotTests
{
    private static readonly Vector3 Muzzle = new(10, 20, 3), Target = new(15, 20, 3), Eye = new(12, 12, 6);

    private static Rac1BeamSlot Started(uint seed = 7)
    {
        var slot = new Rac1BeamSlot();
        slot.Step(Muzzle, Target, Rac1PickupRandom.FromSeed(seed));
        return slot;
    }

    [Fact]
    public void FirstTickLaysTwentyPointStrandsAlongTheSpanAndSineAmplitudeGrowsTowardTheTip()
    {
        var slot = Started();
        float step = (Target - Muzzle).Length() / 20;
        Assert.All(slot.Strands, strand =>
        {
            Assert.Equal(20, strand.Length);
            for (int i = 0; i < 19; i++) Assert.Equal(step * i, strand[i].X, 4);
        });
        // 0x2e2250: amplitude(j) = j/20 * .3 + .1 scaled by a sine, so it never exceeds .4 and strand 0 stays planar (y = 0).
        Assert.All(slot.Strands[0].Take(19), point => { Assert.Equal(0, point.Y); Assert.InRange(MathF.Abs(point.Z), 0, .4f); });
        Assert.All(slot.Strands[1].Take(19), point => Assert.InRange(MathF.Abs(point.Y), 0, .3f + 1e-5f));
        // Tip is strand-0 point 18 mapped into the beam frame, so it lies near the muzzle->target line, short of the endpoint.
        Assert.InRange(Vector3.Distance(slot.Tip, Muzzle), step * 17, step * 19.5f);
    }

    [Fact]
    public void StrandsAnimateEveryTickAndStayDeterministicForASeed()
    {
        var a = Started(); var b = Started();
        var first = a.Strands[1].Select(p => p).ToArray();
        a.Step(Muzzle, Target, Rac1PickupRandom.FromSeed(8)); b.Step(Muzzle, Target, Rac1PickupRandom.FromSeed(8));
        Assert.NotEqual(first, a.Strands[1]);
        Assert.Equal(a.Strands[0], b.Strands[0]);
        Assert.Equal(a.Strands[1], b.Strands[1]);
    }

    [Fact]
    public void TextureScrollsPointTwoPerTickAndWrapsAtEight()
    {
        var slot = Started();
        Assert.Equal(-.2f, slot.Scroll, 5);
        int wraps = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            float before = slot.Scroll;
            slot.Step(Muzzle, Target, Rac1PickupRandom.FromSeed(1));
            Assert.InRange(slot.Scroll, -8f, 0.0001f);
            if (slot.Scroll > before) { wraps++; Assert.Equal(7.8f, slot.Scroll - before, 3); }
            else Assert.Equal(-.2f, slot.Scroll - before, 4);
        }
        Assert.InRange(wraps, 2, 3); // one wrap per ~40 ticks.
    }

    [Fact]
    public void QuadsFollowTheRecoveredDrawOrderAndBlendRegisters()
    {
        var slot = Started();
        var quads = slot.Build(Eye, Rac1PickupRandom.FromSeed(3));
        // 2 end-quad pairs + 2 strands x 18 segments x (beam + glow).
        Assert.Equal(4 + 2 * 18 * 2, quads.Count);
        Assert.Equal([Rac1FxBlend.Alpha, Rac1FxBlend.Additive, Rac1FxBlend.Alpha, Rac1FxBlend.Additive],
            quads.Take(4).Select(q => q.Blend));
        Assert.All(quads.Take(4), q => Assert.Equal(Rac1BeamSlot.EndpointTexture, q.Texture));
        Assert.All(quads.Skip(4).Where((_, i) => i % 2 == 0), q => Assert.Equal(Rac1BeamSlot.BeamTexture, q.Texture));
        Assert.All(quads.Skip(4).Where((_, i) => (i & 1) == 1), q => Assert.Equal(Rac1BeamSlot.GlowTexture, q.Texture));
        Assert.All(quads.Skip(4), q => Assert.Equal(Rac1FxBlend.Additive, q.Blend));
        Assert.All(quads, q => Assert.All(q.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z))));
        Assert.Equal(0xFFFFFFFFu, quads[0].Colors[0]);   // white core, alpha 0xFF
        Assert.Equal(0x607F4040u, quads[1].Colors[0]);   // blue-violet halo
    }

    [Fact]
    public void BeamRibbonsFadeInAtTheMuzzleAndNarrowTowardTheTip()
    {
        var slot = Started();
        var quads = slot.Build(Eye, Rac1PickupRandom.FromSeed(3));
        var firstBeam = quads[4]; var firstGlow = quads[5];
        Assert.Equal([0x00FFFFFFu, 0x00FFFFFFu, 0x80_7F7F7Fu, 0x80_7F7F7Fu], firstBeam.Colors);
        Assert.Equal([0x007F4040u, 0x007F4040u, 0x407F4040u, 0x407F4040u], firstGlow.Colors);
        // The noisy second strand uses the dimmer 0x30 / 0x10 alphas.
        Assert.Equal(0x30_7F7F7Fu, quads[4 + 36].Colors[3]);
        Assert.Equal(0x10_7F4040u, quads[4 + 37].Colors[3]);
        float Width(Rac1FxSurface q) => Vector3.Distance(q.Positions[2], q.Positions[3]);
        float near = Width(quads[4 + 2 * 1]), middle = Width(quads[4 + 2 * 9]), far = Width(quads[4 + 2 * 17]);
        Assert.True(near > middle && middle > far, $"{near} {middle} {far}");
        Assert.InRange(far, .05f, .6f);
        // Glow ribbons are twice the beam ribbon width.
        Assert.Equal(Width(quads[4 + 2 * 5]) * 2, Width(quads[4 + 2 * 5 + 1]), 3);
        Assert.Equal(new Vector2(1, 0), firstGlow.Uvs[0]);
        Assert.Equal(new Vector2(0, 1), firstGlow.Uvs[3]);
        Assert.Equal(new Vector2(slot.Scroll, 0), firstBeam.Uvs[0]);
        Assert.Equal(new Vector2(slot.Scroll + 1, 1), firstBeam.Uvs[3]);
    }

    [Fact]
    public void RobotGlowIsASingleAdditiveBillboardLoweredByOneThirdFacingTheCamera()
    {
        var socket = new Vector3(1, 2, 5);
        var quad = Rac1BeamSlot.RobotGlow(socket, Eye);
        Assert.Equal(Rac1FxBlend.Additive, quad.Blend);
        Assert.Equal(Rac1BeamSlot.EndpointTexture, quad.Texture);
        var centre = (quad.Positions[0] + quad.Positions[3]) / 2;
        Assert.Equal(0, Vector3.Distance(centre, socket - Vector3.UnitZ * .333f), 4);
        Assert.Equal(.4f, Vector3.Distance(quad.Positions[0], quad.Positions[2]), 4);
        // Face-on: the quad plane is perpendicular to the view direction.
        var normal = Vector3.Cross(quad.Positions[2] - quad.Positions[0], quad.Positions[1] - quad.Positions[0]);
        Assert.Equal(1, MathF.Abs(Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(Eye - centre))), 3);
        Assert.All(quad.Colors, color => Assert.Equal(0x80FFFFFFu, color));
    }

    [Fact]
    public void DegenerateGeometryStaysFinite()
    {
        var slot = new Rac1BeamSlot();
        slot.Step(Muzzle, Muzzle, Rac1PickupRandom.FromSeed(1)); // fully blocked: zero length
        foreach (var eye in new[] { Muzzle, Target + Vector3.UnitX * 100 })
            Assert.All(slot.Build(eye, Rac1PickupRandom.FromSeed(2)),
                q => Assert.All(q.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z))));
    }

    [SkippableFact]
    public void RobotClaimsItsBeamSlotWithEndpointFlagsAndSparkTagModeZero()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OBP_RAC1_ISO")), "OBP_RAC1_ISO not set");
        var read = Rac1AirborneNativeSourceTests.ReadOverlay(); // authenticates the Veldin overlay (SHA-256) first
        uint Word(int address) => BinaryPrimitives.ReadUInt32LittleEndian(read(address, 4));
        const int claim = 0x2e1f28, site = 0x2e10d4; // fx1471_claim, and its call inside the robot update 0x2e0b88
        uint jal = 0x0c000000u | (uint)(claim >> 2);
        Assert.Equal(jal, Word(site));
        Assert.Equal(0x0260202du, Word(site - 8)); // a0 = the robot moby
        Assert.Equal(0x0000282du, Word(site - 4)); // a1 = 0: endpoint effect flags (no point lights)
        Assert.Equal(0x0000302du, Word(site + 4)); // a2 = 0, in the delay slot: spark tag mode (sparks just drift)
        // It is the robot update's only claim of a beam slot.
        var sites = Enumerable.Range(0x2e0b88 / 4, (0x2e1678 - 0x2e0b88) / 4).Select(i => i * 4).Where(address => Word(address) == jal).ToArray();
        Assert.Equal([site], sites);
    }

    [SkippableFact]
    public void VeldinBankDecodesTheBeamGlowAndEndpointTexturesTheManagerPvarsSelect()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 0);
        // Class 1471's authored 16-byte PVar is {0, 1}: Fx textures 0x28 + 0 (glow) and 0x28 + 1 (beam).
        var manager = Assert.Single(Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances, m => m.OClass == 1471);
        Assert.Equal(0, BitConverter.ToInt32(manager.PVar!, 0));
        Assert.Equal(1, BitConverter.ToInt32(manager.PVar!, 4));
        Assert.Equal(Rac1BeamSlot.GlowTexture, 0x28 + BitConverter.ToInt32(manager.PVar!, 0));
        Assert.Equal(Rac1BeamSlot.BeamTexture, 0x28 + BitConverter.ToInt32(manager.PVar!, 4));
        var textures = Rac1FxTextures.Read(Rac1LevelCore.Open(disc, level), new HashSet<int>
            { Rac1BeamSlot.GlowTexture, Rac1BeamSlot.BeamTexture, Rac1BeamSlot.EndpointTexture });
        Assert.Equal(3, textures.Count);
        Assert.All(textures.Values, texture =>
        {
            Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length);
            Assert.Contains(texture.Rgba.Where((_, i) => (i & 3) == 3), alpha => alpha != 0);
        });
    }
}
