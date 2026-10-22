using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1RedPlantSessionTests
{
    [Fact]
    public void RetailThresholdsActivateAndOneShotCompletionReturnsToRest()
    {
        var source = Source(10);
        var session = new Rac1RedPlantSession();
        Assert.True(session.Register(source));
        Assert.Equal(1, session.RegisteredCount);

        var state = RuntimeEntityState.FromAuthored(source);
        state = session.Step(source, state, 1, 0.1);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, state.Presentation.AnimationRole);
        state = session.Step(source, state, 0.5, 1d / 30);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, state.Presentation.AnimationRole);
        state = session.Step(source, state, 0.5, 0.1);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, state.Presentation.AnimationRole);

        for (int tick = 1; tick < Rac1RedPlantSession.ReactionTicks; tick++)
        {
            state = session.Step(source, state, 2, 0);
            Assert.Equal(RuntimeObjectAnimationRole.Reaction, state.Presentation.AnimationRole);
        }
        state = session.Step(source, state, 2, 0);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, state.Presentation.AnimationRole);
        state = session.Step(source, state, 0.5, 0.1);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, state.Presentation.AnimationRole);
    }

    [Fact]
    public void MultiplePlantsHaveIndependentNativeSequenceTimers()
    {
        var session = new Rac1RedPlantSession();
        var first = Source(12);
        var second = Source(14);
        Assert.True(session.Register(first));
        Assert.True(session.Register(second));
        var a = session.Step(first, RuntimeEntityState.FromAuthored(first), 0.2, 0.1);
        var b = RuntimeEntityState.FromAuthored(second);
        for (int tick = 1; tick <= Rac1RedPlantSession.ReactionTicks; tick++)
            a = session.Step(first, a, 2, 0);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, a.Presentation.AnimationRole);
        Assert.Equal(RuntimeObjectAnimationRole.Rest, b.Presentation.AnimationRole);
        b = session.Step(second, b, 0.2, 0.1);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, b.Presentation.AnimationRole);
    }

    [Fact]
    public void CannotRegisterArbitraryClassesOrMissingRetailClips()
    {
        var session = new Rac1RedPlantSession();
        Assert.False(session.Register(Source(1) with { NativeClassId = 1782 }));
        Assert.Throws<InvalidDataException>(() =>
            session.Register(Source(2) with { Animations = null }));
    }

    [SkippableFact]
    public void VeldinAllThirtyThreeRedPlantsCarryDecodedReaction()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new OBP.IO.FileRandomAccessReader(iso!);
        var world = OBP.RAC1.Rac1WorldImport.Build(reader, 0);
        var plants = world.DynamicObjects!.Where(obj =>
            obj.NativeClassId == Rac1MobyAnimationProvider.RedPlantClassId).ToArray();
        Assert.Equal(33, plants.Length);
        var session = new Rac1RedPlantSession();
        foreach (var plant in plants) Assert.True(session.Register(plant));
        Assert.Equal(33, session.RegisteredCount);
    }

    private static RuntimeDynamicObject Source(int index)
    {
        var clip = new RuntimeObjectAnimationClip(
            "reaction", RuntimeObjectAnimationRole.Reaction,
            [new RuntimeObjectAnimationSurface(0,
                Enumerable.Range(0, 20).Select(_ => new double[3]).ToArray())],
            Enumerable.Repeat(1d / 30, 20).ToArray());
        return new RuntimeDynamicObject(
            "rac1", Rac1MobyAnimationProvider.RedPlantClassId, index, null,
            "plant", "plant", new RuntimeObjectTransform(new double[16]),
            [], Animations: new RuntimeObjectAnimationSet([clip]));
    }
}
