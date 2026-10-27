using OBP.Core;
using OBP.Fusion;
using OBP.Game.Diagnostics;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1EncounterDiagnosticTests
{
    [Fact]
    public void LiveRac1ActorIsAdmittedInItsOwnWorldGeneration()
    {
        var key = new ActorInstanceKey("live", 7, new(ObpSourceGame.Rac1, 749, 143));
        Rac1DiagnosticActorScope.RequireCurrent(key, new(null, null, "live", 7));
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac2)]
    [InlineData(ObpSourceGame.Rac3)]
    public void EqualNativeNumbersCannotAliasAnotherSource(ObpSourceGame source)
    {
        var key = new ActorInstanceKey("live", 7, new(source, 749, 143));
        Assert.Throws<DiagnosticUnavailableException>(() =>
            Rac1DiagnosticActorScope.RequireCurrent(key, new(null, null, "live", 7)));
    }

    [Theory]
    [InlineData("live", 8)]
    [InlineData("previous", 7)]
    [InlineData(null, 7)]
    [InlineData("", 7)]
    public void RetiredOrUnavailableWorldCannotRebindAnActor(string? world, long generation)
    {
        var key = new ActorInstanceKey("live", 7, new(ObpSourceGame.Rac1, 749, 143));
        Assert.Throws<DiagnosticUnavailableException>(() =>
            Rac1DiagnosticActorScope.RequireCurrent(key, new(null, null, world, generation)));
    }
}
