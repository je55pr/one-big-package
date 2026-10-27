using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime;
using OBP.Runtime.Effects;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class EffectWorldSessionTests
{
    internal static readonly RuntimeEffectView View = new(new(0, 3, -10), Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 1, 1, 1, 1);
    private static RuntimeEffectRecipeKey Key(ObpSourceGame game) => new(new(game, "test-build", "BANK", "caller", "0"), "existing-recipe/v1");
    private static EffectRequest Request(EffectWorldSession world, RuntimeEffectRecipeKey key, long sequence = 1, int ticks = 4, ActorInstanceKey? actor = null) =>
        new(world.WorldInstanceId, world.LeaseGeneration, world.ResetGeneration, sequence, key, 1234, new(new(2, 1, 3), new(2, 1, 5)), ticks, actor);
    private static EffectRecipeRegistry Registry(params FakeFactory[] factories)
    {
        var registry = new EffectRecipeRegistry();
        foreach (var factory in factories) { registry.Source(factory.Key.Source.Game, factory.Key.Source.BuildId); registry.Register(factory); }
        return registry;
    }
    [Theory]
    [InlineData("rac1:LEVEL0")]
    [InlineData("rac3:TABLE1")]
    [InlineData("obp:fusion-lab")]
    public void ThreeSourcesRunTogetherWithoutDestinationOrNativeIdAliasing(string destination)
    {
        var factories = new[] { new FakeFactory(Key(ObpSourceGame.Rac1)), new FakeFactory(Key(ObpSourceGame.Rac2)), new FakeFactory(Key(ObpSourceGame.Rac3)) };
        using var world = new EffectWorldSession(destination, 7, Registry(factories));
        for (int i = 0; i < factories.Length; i++) Assert.Equal(EffectAdmission.Ready, world.Start(Request(world, factories[i].Key, i + 1)).Admission);
        world.Advance(1d / 60, View);
        Assert.Equal(3, world.Capture().Length); Assert.Equal(3, world.Textures.Select(t => t.Source).Distinct().Count());
        Assert.All(world.Capture(), e => Assert.Equal(1, e.State.NativeTick));
        Assert.Equal(3, world.CaptureDraw(View).SelectMany(e => e.Frame.Sprites).Select(s => s.Texture.Game).Distinct().Count());
    }
    [Fact]
    public void ProviderClocksPauseAndDrawSamplingDoNotAdvanceOneAnother()
    {
        var a = new FakeFactory(Key(ObpSourceGame.Rac1)); var b = new FakeFactory(Key(ObpSourceGame.Rac2), 30);
        using var world = new EffectWorldSession("world", 1, Registry(a, b));
        world.Start(Request(world, a.Key)); world.Start(Request(world, b.Key, 2));
        world.Advance(1d / 30, View); Assert.Equal(new long[] { 2, 1 }, world.Capture().Select(e => e.State.NativeTick));
        var frozen = world.Capture(); world.Advance(.5, View, paused: true); world.CaptureDraw(View); world.CaptureDraw(View);
        Assert.Equal(frozen.ToArray(), world.Capture().ToArray());
    }
    [Fact]
    public void FailedSourceAdmissionIsExplicitAndRetryDoesNotConsumeTheRequest()
    {
        var factory = new FakeFactory(Key(ObpSourceGame.Rac2)); var registry = new EffectRecipeRegistry(); registry.Register(factory);
        using var world = new EffectWorldSession("world", 1, registry); var request = Request(world, factory.Key);
        Assert.Equal(RuntimeEffectAvailability.MissingSource, world.Start(request).Problem!.Availability);
        registry.Source(ObpSourceGame.Rac2, "other-build"); Assert.Equal(RuntimeEffectAvailability.WrongBuild, world.Start(request).Problem!.Availability);
        registry.Source(ObpSourceGame.Rac2, "test-build", new(RuntimeEffectAvailability.MissingAssets, "Bank unavailable."));
        Assert.Equal(RuntimeEffectAvailability.MissingAssets, world.Start(request).Problem!.Availability); Assert.Equal(0, factory.Created);
        registry.Source(ObpSourceGame.Rac2, "test-build"); Assert.Equal(EffectAdmission.Ready, world.Start(request).Admission);
        Assert.Equal(EffectAdmission.AlreadyConsumed, world.Start(request).Admission); Assert.Equal(1, factory.Created);
        Assert.Equal(RuntimeEffectAvailability.UnsupportedRecipe, world.Start(request with { Sequence = 2, Recipe = factory.Key with { Recipe = "unrecovered-type" } }).Problem!.Availability);
        registry.Source(ObpSourceGame.Rac2, "test-build", new(RuntimeEffectAvailability.MissingSource, "Source detached."));
        world.Advance(1d / 60, View); Assert.Equal(0, world.Count); Assert.True(factory.Last!.Disposed); Assert.NotNull(world.LastProblem);
    }
    [Fact]
    public void ResetEpochAndWorldLeaseFenceOldHandlesWhileSeedsAndRequestsCanRepeat()
    {
        var factory = new FakeFactory(Key(ObpSourceGame.Rac1)); using var world = new EffectWorldSession("world", 1, Registry(factory));
        var request = Request(world, factory.Key); var old = world.Start(request).Handle!.Value; world.Advance(1d / 60, View);
        var state = Assert.Single(world.Capture()).State;
        world.Reset(); Assert.False(world.Stop(old)); Assert.False(world.SetPose(old, request.Pose));
        Assert.Equal(EffectAdmission.StaleLease, world.Start(request).Admission); Assert.Equal(0, world.Count);
        var next = world.Start(Request(world, factory.Key)).Handle!.Value; Assert.NotEqual(old, next); world.Advance(1d / 60, View);
        Assert.Equal(state, Assert.Single(world.Capture()).State);
        Assert.Equal(EffectAdmission.StaleLease, world.Start(Request(world, factory.Key, 2) with { LeaseGeneration = 0 }).Admission);
        world.Dispose(); Assert.Equal(EffectAdmission.Disposed, world.Start(Request(world, factory.Key, 3)).Admission);
    }
    [Fact]
    public void ActorRetirementDelegatesDrainAndOwnerRulesAndRejectsForeignLeases()
    {
        var factory = new FakeFactory(Key(ObpSourceGame.Rac2)); bool alive = true;
        using var world = new EffectWorldSession("world", 2, Registry(factory), _ => alive);
        var actor = new ActorInstanceKey("world", 2, new WeaponTargetKey(ObpSourceGame.Rac1, 749, 0));
        Assert.Equal(EffectAdmission.RetiredOwner, world.Start(Request(world, factory.Key, actor: actor with { Generation = 1 })).Admission);
        world.Start(Request(world, factory.Key, actor: actor)); world.Advance(1d / 60, View); alive = false; world.Advance(1d / 60, View);
        Assert.Equal(1, factory.Last!.OwnerRetirements); Assert.False(factory.Last.Emitting); Assert.Equal(1, world.Count);
        for (int i = 0; i < 4; i++) world.Advance(1d / 60, View);
        Assert.Equal(0, world.Count); Assert.True(factory.Last.Disposed);
    }
    [Fact]
    public void CapacityAndExpiredReceiptsCannotAllocateOrResurrectAnOldInstance()
    {
        var factory = new FakeFactory(Key(ObpSourceGame.Rac1)); using var world = new EffectWorldSession("world", 1, Registry(factory), maximumInstances: 1);
        var first = world.Start(Request(world, factory.Key, ticks: 1)).Handle!.Value;
        Assert.Equal(EffectAdmission.CapacityExceeded, world.Start(Request(world, factory.Key, 2)).Admission); Assert.Equal(1, factory.Created);
        world.Stop(first); for (int i = 0; i < 5; i++) world.Advance(1d / 60, View);
        var duplicate = world.Start(Request(world, factory.Key)); Assert.Equal(EffectAdmission.AlreadyConsumed, duplicate.Admission); Assert.Null(duplicate.Handle);
        Assert.Equal(EffectAdmission.Ready, world.Start(Request(world, factory.Key, 2)).Admission);
    }
    private sealed class FakeFactory(RuntimeEffectRecipeKey key, int rate = 60) : IRuntimeEffectFactory
    {
        public RuntimeEffectRecipeKey Key => key;
        public int NativeTicksPerSecond => rate;
        public int Created;
        public FakeInstance? Last;
        public IReadOnlyList<RuntimeEffectTexture> Textures { get; } = [new(key.Source with { AssetKind = "part", OriginalId = "0" }, new("part", 0, 1, 1, [255, 255, 255, 255]))];
        public IRuntimeEffectInstance Create(uint seed, RuntimeEffectPose pose) { Created++; return Last = new(Textures[0].Source, pose); }
    }
    private sealed class FakeInstance(ObpSourceRef texture, RuntimeEffectPose pose) : IRuntimeEffectInstance
    {
        public bool Emitting = true, Disposed;
        public int OwnerRetirements;
        private long _tick, _emitted;
        private int _life;
        public RuntimeEffectState State => new(_tick, _life > 0 ? 1 : 0, Emitting, _emitted, _emitted, _emitted);
        public void Step(RuntimeEffectView view) { _tick++; if (Emitting) { _life = 4; _emitted++; } else _life--; }
        public void SetPose(RuntimeEffectPose value) => pose = value;
        public void StopEmission() => Emitting = false;
        public void RetireOwner() { OwnerRetirements++; StopEmission(); }
        public RuntimeEffectFrame Capture(RuntimeEffectView view) => new([new(1, 0, texture, pose.Position, 1, Vector4.One, 0, new(RuntimeBlendEquation.SourceAlpha))], [], []);
        public void Dispose() { Disposed = true; _life = 0; }
    }
}
