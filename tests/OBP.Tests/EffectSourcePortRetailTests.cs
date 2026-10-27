using System.Numerics;
using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime.Effects;

namespace OBP.Tests;

public sealed class EffectSourcePortRetailTests
{
    [SkippableFact]
    public void SixRealRecipesAreDestinationIndependentAndRepeatAcrossTenResetsAndReentry()
    {
        using var r1 = Open("OBP_RAC1_ISO"); using var r2 = Open("OBP_GC_ISO"); using var r3 = Open("OBP_UYA_ISO");
        var factories = Rac1ParticleEffectFactory.ReadNovalis(r1).Cast<IRuntimeEffectFactory>()
            .Concat(Rac2ParticleEffectFactory.ReadMuseum(r2)).Concat(Rac3ParticleEffectFactory.ReadVeldin(r3)).ToArray();
        var registry = new EffectRecipeRegistry();
        foreach (var factory in factories) { registry.Source(factory.Key.Source.Game, factory.Key.Source.BuildId); registry.Register(factory); }
        var definition = EffectSourcePortTests.ReadDefinition(); string? expected = null;
        var view = new RuntimeEffectView(new(0, 3, 7), Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 1, 1, 1, 1);
        foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
        {
            using var world = new EffectWorldSession(destination, 5, registry);
            for (int reset = 0; reset < 10; reset++)
            {
                var handles = definition.Requests(world).Select(request => world.Start(request)).ToArray();
                Assert.All(handles, result => Assert.Equal(EffectAdmission.Ready, result.Admission));
                for (int tick = 0; tick < 20; tick++) world.Advance(1d / 60, view);
                var observations = world.Capture(); Assert.Equal(6, observations.Length); Assert.All(observations, e => Assert.True(e.State.ParticleCount > 0));
                Assert.All(observations.GroupBy(e => e.Recipe.Source.Game), group => Assert.Equal(2, group.Count()));
                Assert.Equal(new long[] { 152, 152, 41, 41, 160, 580 }, observations.Select(e => e.State.RandomSamples));
                var output = world.CaptureDraw(view); Assert.All(output, entry => Assert.NotEmpty(entry.Frame.Sprites));
                Assert.All(output.SelectMany(e => e.Frame.Sprites), s => Assert.Contains(world.Textures, t => t.Source == s.Texture));
                string actual = JsonSerializer.Serialize(output.Select(e => e.Frame), new JsonSerializerOptions { IncludeFields = true });
                if (expected is null) expected = actual; else Assert.Equal(expected, actual);
                world.Reset(); Assert.Equal(0, world.Count); Assert.Empty(world.Textures);
                Assert.All(handles, h => Assert.False(world.Stop(h.Handle!.Value)));
            }
        }
    }
    private static FileRandomAccessReader Open(string variable)
    {
        string? path = Environment.GetEnvironmentVariable(variable); Skip.If(string.IsNullOrWhiteSpace(path), variable + " not set");
        return new(path!);
    }
}
