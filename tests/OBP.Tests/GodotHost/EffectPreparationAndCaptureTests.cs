using OBP.Core;
using OBP.Game;
using OBP.RAC1;
using OBP.RAC2;
using OBP.RAC3;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Effects;

namespace OBP.Tests;

public sealed class EffectPreparationAndCaptureTests
{
    [Fact]
    public void ACorruptBankIsUnavailableWithoutDiscardingTheOtherSources()
    {
        var data = new EffectSourceData();
        var a = new Registration(Rac1Authority.Primary); var b = new Registration(Rac2Authority.Primary); var c = new Registration(Rac3Authority.Primary);
        Assert.True(data.Prepare(Rac1Authority.Primary, () => [a]));
        Assert.False(data.Prepare(Rac2Authority.Primary, () => throw new InvalidDataException("Synthetic corrupt bank.")));
        Assert.True(data.Prepare(Rac3Authority.Primary, () => [c]));
        Assert.Equal(ObpSourceGame.Rac2, Assert.Single(data.Problems).Key);
        Assert.Equal(RuntimeEffectAvailability.MissingAssets, data.Registry.Resolve(b.Key, out _)!.Availability);
        Assert.Null(data.Registry.Resolve(a.Key, out var first)); Assert.Same(a, first);
        Assert.Null(data.Registry.Resolve(c.Key, out var third)); Assert.Same(c, third);
    }

    [Fact]
    public void OrdinaryAndEarlierRecordingsKeepTheirOriginalProvenanceWhileEffectsOptIn()
    {
        var args = new CommandLineArgs { RecordPlaytest = "synthetic-input-run" };
        Assert.False(EffectCaptureContract.Includes(args, null));
        Assert.True(EffectCaptureContract.Includes(args with { RecordEffects = true }, null));
        Assert.True(EffectCaptureContract.Includes(args with { EffectsSmoke = true }, null));
        Assert.False(EffectCaptureContract.Includes(args with { RecordEffects = true, RetailParity = true }, null));
        var header = new PlaytestRunHeader(2, "obp:fusion-lab", "synthetic-build", Guid.NewGuid(), "test", "test", "test", 60, 1, 960, 540,
            DateTimeOffset.UnixEpoch, default, default, new(new SortedDictionary<string, PlaytestValue>()));
        Assert.False(EffectCaptureContract.Includes(args with { EffectsSmoke = true }, header));
        Assert.True(EffectCaptureContract.Includes(args, header with { Adapters = [new(EffectCaptureContract.AdapterPrefix + "fixture", 1, 60)] }));
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(["--record-effects"]));
        Assert.True(CommandLineArgs.Parse(["--record-playtest", "synthetic-input-run", "--record-effects"]).RecordEffects);
    }

    private sealed class Registration(ObpBuildIdentity authority) : IRuntimeEffectFactory
    {
        public RuntimeEffectRecipeKey Key { get; } = new(new(authority.Game, authority.BuildId, "synthetic-bank", "caller", "0"), "synthetic-recipe/v1");
        public int NativeTicksPerSecond => 60;
        public IReadOnlyList<RuntimeEffectTexture> Textures => [];
        public IRuntimeEffectInstance Create(uint seed, RuntimeEffectPose pose) => throw new NotSupportedException("Preparation test does not spawn particles.");
    }
}
