using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Effects;

namespace OBP.Tests;

public sealed class EffectSourcePortTests
{
    [Fact]
    public void SourcePortsPreserveOwnerRetirementAndCopiedParticleDrain()
    {
        var assets = Rac1ParticleEmitterTests.Assets() with
        { PartTextures = new Dictionary<int, RuntimeTexture> { [42] = new("rac1-part", 42, 1, 1, [255, 255, 255, 255]) } };
        var rac1 = new Rac1ParticleEffectFactory(assets, Rac1ParticleEmitterTests.Owner(Rac1ParticleEmitterTests.SyntheticParameters()));
        var rac2 = new Rac2ParticleEffectFactory(Rac2ParticleTests.Assets(), Rac2ParticleTests.Owner(), new(Enumerable.Repeat(1f, 256).ToArray(), 0));
        var rac3 = new Rac3ParticleEffectFactory(Rac3ParticleTests.Assets(), new(Enumerable.Repeat(1f, 256).ToArray(), 0), false, 30);
        var pose = new RuntimeEffectPose(new(2, 1, 3), new(2, 1, 5));
        using var r1 = rac1.Create(1234, pose); using var r2 = rac2.Create(1234, pose); using var r3 = rac3.Create(1234, pose);
        r1.Step(EffectWorldSessionTests.View); Assert.Equal(0, r1.State.RandomSamples);
        r1.Step(EffectWorldSessionTests.View); r2.Step(EffectWorldSessionTests.View); r3.Step(EffectWorldSessionTests.View);
        Assert.Equal(8, r1.State.RandomSamples); Assert.Equal(8, r3.State.RandomSamples);
        Assert.Equal(2, r3.State.Emitted); Assert.Equal(1, r1.State.ParticleCount); Assert.Equal(1, r2.State.ParticleCount);
        var frame = r1.Capture(EffectWorldSessionTests.View); Assert.Equal(new Vector3(2, 1, 3), Assert.Single(frame.Sprites).Position);
        r1.RetireOwner(); r2.RetireOwner(); r3.RetireOwner();
        r1.Step(EffectWorldSessionTests.View); r2.Step(EffectWorldSessionTests.View); r3.Step(EffectWorldSessionTests.View);
        Assert.Equal(0, r1.State.ParticleCount); Assert.Equal(1, r2.State.ParticleCount); Assert.Equal(2, r3.State.ParticleCount);
        Assert.Single(frame.Sprites); // A frozen presentation observation survives native retirement.
        for (int i = 0; i < 20; i++) { r2.Step(EffectWorldSessionTests.View); r3.Step(EffectWorldSessionTests.View); }
        Assert.Equal(0, r2.State.ParticleCount); Assert.Equal(0, r3.State.ParticleCount);
    }
    [Fact]
    public void ChargeCallerBoundAndRecordedOperationsRejectUnsupportedInstructions()
    {
        var factory = new Rac3ParticleEffectFactory(Rac3ParticleTests.Assets(), new(Enumerable.Repeat(1f, 256).ToArray(), 0), false, 30);
        var registry = new EffectRecipeRegistry(); registry.Source(factory.Key.Source.Game, factory.Key.Source.BuildId); registry.Register(factory);
        using var world = new EffectWorldSession("world", 1, registry);
        var request = new EffectRequest("world", 1, 0, 1, factory.Key, 1234, new(Vector3.Zero, Vector3.UnitZ), 50);
        Assert.Equal(RuntimeEffectAvailability.UnsupportedContext, world.Start(request).Problem!.Availability); Assert.Equal(0, world.Count);
        Assert.Equal(EffectAdmission.Ready, world.Start(request with { EmissionTicks = 49 }).Admission);
        new PlaytestCommand("effects", "start").Validate(); new PlaytestCommand("effects", "reset").Validate();
        Assert.Throws<InvalidDataException>(() => new PlaytestCommand("effects", "arbitrary-native-type").Validate());
    }
    [Fact]
    public void MissingOriginalImagesFailAdmissionBeforeANativeClockOrAllocationStarts()
    {
        var rac1 = new Rac1ParticleEffectFactory(Rac1ParticleEmitterTests.Assets(), Rac1ParticleEmitterTests.Owner(Rac1ParticleEmitterTests.SyntheticParameters()));
        var assets2 = Rac2ParticleTests.Assets();
        var rac2 = new Rac2ParticleEffectFactory(assets2 with { Textures = assets2.Textures.Where(p => p.Key != 44).ToDictionary() },
            Rac2ParticleTests.Owner(), new(Enumerable.Repeat(1f, 256).ToArray(), 0));
        var assets3 = Rac3ParticleTests.Assets();
        var rac3 = new Rac3ParticleEffectFactory(assets3 with { Textures = new Dictionary<int, RuntimeTexture>() },
            new(Enumerable.Repeat(1f, 256).ToArray(), 0), false);
        foreach (var factory in new IRuntimeEffectFactory[] { rac1, rac2, rac3 })
        {
            var registry = new EffectRecipeRegistry(); registry.Source(factory.Key.Source.Game, factory.Key.Source.BuildId); registry.Register(factory);
            using var world = new EffectWorldSession("world", 1, registry);
            var result = world.Start(new("world", 1, 0, 1, factory.Key, 1234, new(Vector3.Zero, Vector3.UnitZ), 10));
            Assert.Equal(EffectAdmission.Unavailable, result.Admission); Assert.Equal(RuntimeEffectAvailability.MissingAssets, result.Problem!.Availability);
            Assert.Empty(world.Capture()); Assert.Empty(world.Textures);
        }
    }
    [Fact]
    public void PresetIsPayloadFreeQualifiedAndHasTwoInstancesPerSource()
    {
        var definition = ReadDefinition(); Assert.Equal(6, definition.Effects.Count);
        Assert.All(definition.Effects.GroupBy(e => e.Game), group => Assert.Equal(2, group.Count()));
        Assert.Equal(new[] { "285", "286" }, definition.Effects.Where(e => e.Game == ObpSourceGame.Rac1).Select(e => e.OriginalId));
        Assert.Equal(new[] { "262", "263" }, definition.Effects.Where(e => e.Game == ObpSourceGame.Rac2).Select(e => e.OriginalId));
        Assert.Equal(64, definition.Fingerprint.Length); Assert.Equal(definition.Fingerprint, ReadDefinition().Fingerprint);
        Assert.Throws<ArgumentException>(() => (definition with { Version = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (definition with { Effects = [definition.Effects[0], definition.Effects[0]] }).Validate());
    }
    internal static EffectExperimentDefinition ReadDefinition()
    {
        return EffectExperimentDefinition.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "data", "effects_experiment.json")));
    }
}
