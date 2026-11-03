using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using static OBP.Tests.Rac2RetailLevelFixtures;

namespace OBP.Tests;

/// <summary>
/// Oozla (LEVEL1) gameplay lump (lump 2) checks: instance counts, the class-500 Bolt Crates, the lighting tables and the
/// environment sample/transition tables, plus the per-level class-0 player seed. Retail-gated on <c>OBP_GC_ISO</c>.
/// </summary>
public sealed class Rac2LevelGameplayTests : IClassFixture<Rac2OozlaGameplayFixture>
{
    private readonly Rac2OozlaGameplayFixture _oozla;

    public Rac2LevelGameplayTests(Rac2OozlaGameplayFixture oozla)
    {
        _oozla = oozla;
    }

    [SkippableFact]
    public void Level1_GameplayInstanceCountsMatchTheBaseline()
    {
        var gameplay = OozlaGameplay();

        Assert.Equal(1617, gameplay.TieInstances.Count);
        Assert.Equal(2825, gameplay.ShrubInstances.Count);
        Assert.Equal(748, gameplay.MobyInstances.Count);
    }

    [SkippableFact]
    public void Level1_BoltCratesKeepNativeInstanceRecordsAndPVars()
    {
        var gameplay = OozlaGameplay();

        // Gameplay identity/state needed by the dynamic-Moby bridge. Oozla has
        // 190 authored class-500 Bolt Crates; each retains its native 0x88
        // instance record and independently resolved 0x110-byte PVar.
        var boltCrates = gameplay.MobyInstances.Where(m => m.OClass == 500).ToArray();
        Assert.Equal(190, boltCrates.Length);
        Assert.All(boltCrates, m =>
        {
            Assert.Equal(0x88, m.RawInstance.Length);
            Assert.Equal(0x20, m.ModeBits);
            Assert.True(m.PVarIndex >= 0);
            Assert.NotNull(m.PVarData);
            Assert.Equal(0x110, m.PVarData!.Length);
        });
        Assert.Equal(boltCrates.Length, boltCrates.Select(m => m.PVarIndex).Distinct().Count());
        Assert.Equal(189, boltCrates.Count(m => m.Bolts == 13));
        Assert.Single(boltCrates, m => m.Bolts == 14);
        Assert.All(boltCrates, m => Assert.InRange(m.Bolts, 13, 14));
    }

    [SkippableFact]
    public void Level1_ShowcaseBoltCrateIdentityAndPayoutMatchTheBaseline()
    {
        var gameplay = OozlaGameplay();
        var boltCrates = gameplay.MobyInstances.Where(m => m.OClass == 500).ToArray();

        // The deterministic Oozla showcase deliberately targets the first authored
        // class-500 instance. Pin its retail identity and the neutral fresh-session
        // payout contract so the visible crate -> pickups loop cannot drift.
        var showcaseCrate = boltCrates.OrderBy(m => m.Index).First();
        Assert.Equal(31, showcaseCrate.Index);
        Assert.Equal(73, showcaseCrate.Uid);
        Assert.Equal(13, showcaseCrate.Bolts);
        Assert.Equal(0, showcaseCrate.PVarData![0xC8]);
        var showcasePayout = new Rac2FreshBoltSession().PlanClass500Payout(
            showcaseCrate.Uid, showcaseCrate.Bolts, rewardMultiplierByte: 0,
            progressionLikeInput: 0, rngMod2: 1);
        Assert.Equal(13, showcasePayout.RewardCentreValue);
        Assert.Equal(new[] { 5, 5, 1 }, showcasePayout.PhysicalPickups.Select(p => p.Denomination));
        Assert.Equal(2, showcasePayout.DeferredValue);
    }

    [SkippableFact]
    public void Level1_DirectionalLightsMatchTheBaseline()
    {
        var gameplay = OozlaGameplay();

        // Directional lights (gameplay ptr 0x04) — the main GC light type.
        Assert.Equal(3, gameplay.DirLights.Count);
        var dl0 = gameplay.DirLights[0];
        Assert.Equal(0.37254903f, dl0.ColourA.X, 5);
        Assert.Equal(0.46666667f, dl0.ColourA.Y, 5);
        Assert.Equal(0.83867055f, dl0.DirectionA.Y, 5);
        Assert.Equal(-0.54463905f, dl0.DirectionA.Z, 5);
    }

    [SkippableFact]
    public void Level1_MobyBakedLightingMatchesTheBaseline()
    {
        var gameplay = OozlaGameplay();

        // Per-moby baked ambient (0x74) + directional-light index (0x80).
        Assert.Equal(new[] { 28 / 255f, 45 / 255f, 51 / 255f }, new[] { gameplay.MobyInstances[0].LightColour.R, gameplay.MobyInstances[0].LightColour.G, gameplay.MobyInstances[0].LightColour.B });
        var idxHistogram = gameplay.MobyInstances.GroupBy(mi => mi.LightIndex).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(506, idxHistogram[0]);
        Assert.Equal(168, idxHistogram[1]);
        Assert.Equal(61, idxHistogram[2]);
        Assert.Equal(13, idxHistogram[3855]); // out-of-range sentinel -> ambient only
    }

    [SkippableFact]
    public void Level1_PointLightsMatchTheBaseline()
    {
        var gameplay = OozlaGameplay();

        // Point lights (gameplay ptr 0x80) — only affect mobies. Oozla has 2.
        Assert.Equal(2, gameplay.PointLights.Count);
        Assert.Equal(353.703125f, gameplay.PointLights[0].Position.X, 3);
        Assert.InRange(gameplay.PointLights[0].Radius, 15f, 45f);
        Assert.All(gameplay.PointLights, p => Assert.True(p.Radius > 0));
    }

    [SkippableFact]
    public void Level1_EnvSamplePointsMatchTheBaseline()
    {
        var gameplay = OozlaGameplay();

        // Env sample points (gameplay ptr 0x8c) — the per-region atmosphere probes.
        Assert.Equal(10, gameplay.EnvSamples.Count);
        Assert.Equal(
            new short[] { 0, 0, 0, 4, 0, 0, 0, 4, 4, 0 },
            gameplay.EnvSamples.Select(es => es.MusicTrack).ToArray());
        Assert.Equal(0, gameplay.EnvSamples[0].HeroLightIndex);
        Assert.Equal(new[] { 28 / 255f, 45 / 255f, 51 / 255f }, new[] { gameplay.EnvSamples[0].HeroColour.R, gameplay.EnvSamples[0].HeroColour.G, gameplay.EnvSamples[0].HeroColour.B });
        Assert.Null(gameplay.EnvSamples[0].Fog); // Oozla samples carry no fog override
        Assert.All(gameplay.EnvSamples, es => Assert.Null(es.Fog));
    }

    [SkippableFact]
    public void EnvTransitionsMatchTheBaselineOnOozlaEndakoAndGrelbin()
    {
        var iso = RequireIso();
        var gameplay = OozlaGameplay();

        // Env transitions (gameplay ptr 0x84) — doorway lighting/fog blend volumes.
        Assert.Empty(gameplay.EnvTransitions); // Oozla has none

        // Endako has 12 (hero-only); Grelbin has 2 with a fog blend.
        var (endakoWad, endakoHeader) = OpenLevelWad(iso, 3);
        var endako = Rac2Instances.Read(Rac2LevelWad.RequireLump(endakoWad, endakoHeader, 2));
        Assert.Equal(12, endako.EnvTransitions.Count);
        Assert.True(endako.EnvTransitions[0].EnableHero);
        Assert.False(endako.EnvTransitions[0].EnableFog);
        Assert.Equal(16, endako.EnvTransitions[0].InverseMatrix.Length);

        var (grelbinWad, grelbinHeader) = OpenLevelWad(iso, 19);
        var grelbin = Rac2Instances.Read(Rac2LevelWad.RequireLump(grelbinWad, grelbinHeader, 2));
        Assert.Equal(2, grelbin.EnvTransitions.Count);
        Assert.True(grelbin.EnvTransitions[0].EnableFog);
        Assert.Equal(150f, grelbin.EnvTransitions[0].StateA.FogFarDistance, 1);
        Assert.Equal(235f, grelbin.EnvTransitions[0].StateB.FogFarDistance, 1);
    }

    [SkippableFact]
    public void EveryRetailLevelHasOneAuthoredClassZeroPlayerSeedAtInstanceZero()
    {
        var iso = RequireIso();

        using var reader = new FileRandomAccessReader(iso);
        var fs = Iso9660Filesystem.Open(reader);
        for (int level = 0; level <= 26; level++)
        {
            var wad = fs.OpenFile($"/G/LEVEL{level}.WAD")
                ?? throw new FileNotFoundException($"/G/LEVEL{level}.WAD");
            var header = Rac2LevelWad.ReadHeader(wad);
            var gameplay = Rac2Instances.Read(Rac2LevelWad.RequireLump(wad, header, 2));
            var start = Assert.Single(gameplay.MobyInstances, moby => moby.OClass == 0);
            Assert.Equal(0, start.Index);
        }
    }

    private Rac2Instances.Gameplay OozlaGameplay()
    {
        Skip.If(_oozla.Gameplay is null, "OBP_GC_ISO not set");
        return _oozla.Gameplay!;
    }
}
