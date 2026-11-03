using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class ActorReplayAdmissionTests
{
    private static ActorRegistration Registration(ObpSourceGame game, int nativeClass, string adapter, string context = "fixture/context") =>
        new(new("world", 7, new(game, nativeClass, 1)), new(game, "build", "asset"), adapter, context, []);

    [Theory]
    [InlineData(ObpSourceGame.Rac1, Rac1ClassIds.WoodenCrate, "source-world/rac1")]
    [InlineData(ObpSourceGame.Rac1, Rac1ClassIds.NanotechCrate, "source-world/rac1")]
    [InlineData(ObpSourceGame.Rac1, Rac1ClassIds.AmmoCrate, "source-world/rac1")]
    [InlineData(ObpSourceGame.Rac1, Rac1ClassIds.VeldinToad, "source-world/rac1")]
    [InlineData(ObpSourceGame.Rac1, Rac1ClassIds.VeldinFlameRobot, "source-world/rac1")]
    [InlineData(ObpSourceGame.Rac2, 2827, "source-world/rac2")]
    [InlineData(ObpSourceGame.Rac3, 500, "source-world/rac3")]
    public void ExistingOriginalContextsRetainTheirReplayIds(ObpSourceGame game, int nativeClass, string adapter) =>
        Assert.Equal(adapter, OBP.Game.ActorReplayAdmission.Require(Registration(game, nativeClass, adapter), []));

    [Fact]
    public void ANewRegisteredFamilyNeedsOnlyItsQualifiedFactoryWitness()
    {
        const string adapter = "fixture/new-family/v1";
        var actor = Registration(ObpSourceGame.Rac3, 9001, adapter);
        var descriptor = new ActorAdapterDescriptor(ObpSourceGame.Rac3, "build", 9001, adapter, "fixture/context", 0, 0);
        Assert.Equal(adapter, OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor, true)]));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor, false)]));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor with { Source = ObpSourceGame.Rac2 }, true)]));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor with { Build = "changed" }, true)]));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor with { NativeClassId = 9002 }, true)]));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor with { AdapterId = "fixture/other-family/v1" }, true)]));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(actor, [new(descriptor with { NativeContextId = "other" }, true)]));
    }

    [Fact]
    public void EqualNativeNumbersAndSpoofedAdapterNamesNeverAliasSources()
    {
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(Registration(ObpSourceGame.Rac2, 500, "source-world/rac3"), []));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(Registration(ObpSourceGame.Rac1, 999, "source-world/rac1"), []));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(Registration(ObpSourceGame.Rac1, Rac1ClassIds.MetalCrate, "source-world/rac1"), []));
        Assert.Throws<InvalidOperationException>(() => OBP.Game.ActorReplayAdmission.Require(Registration(ObpSourceGame.Rac1, Rac1ClassIds.VeldinToad, "rac1/class749/v1"), []));
    }
}
