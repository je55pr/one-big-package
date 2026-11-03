using OBP.Core;
using OBP.Core.Math;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class WorldProviderRegistryTests
{
    private static ObpDestination Destination => new("rac1:fixture", ObpSourceGame.Rac1, "synthetic-v1",
        "fixture", null, "Fixture", null, null, ObpDestinationKind.Planet);

    [Fact]
    public void MissingOrRejectedProviderCannotStartSourceIo()
    {
        var destination = Destination;
        var empty = new ObpWorldProviderRegistry([]);
        Assert.Throws<InvalidOperationException>(() => empty.Load("unused", destination));
        var provider = new Provider(new Catalogue(destination.Game, destination.BuildId, [destination])) { Admit = false };
        var registry = new ObpWorldProviderRegistry([provider]);
        Assert.Throws<InvalidOperationException>(() => registry.Load("unused", destination));
        Assert.Equal(0, provider.Loads);
    }

    [Fact]
    public void RegisteredProviderRetainsDestinationAndPropagatesImportFailure()
    {
        var destination = Destination;
        var provider = new Provider(new Catalogue(destination.Game, destination.BuildId, [destination]));
        var registry = new ObpWorldProviderRegistry([provider]);
        Assert.Same(provider.World, registry.Load("synthetic-source", destination));
        Assert.Same(destination, provider.LoadedDestination);
        var failure = new InvalidDataException("Injected malformed source.");
        provider.Failure = failure;
        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => registry.Load("synthetic-source", destination)));
        Assert.Equal(2, provider.Loads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogueMustMatchItsProviderSourceAndBuild(bool wrongSource)
    {
        var destination = Destination;
        var catalogue = new Catalogue(wrongSource ? ObpSourceGame.Rac2 : destination.Game,
            wrongSource ? destination.BuildId : "other-build", [destination]);
        var provider = new Provider(catalogue);
        Assert.Throws<ArgumentException>(() => new ObpWorldProviderRegistry([provider]));
        Assert.Equal(0, provider.Loads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DestinationCannotSmuggleAnotherSourceOrBuildIntoAProvider(bool wrongSource)
    {
        var destination = wrongSource ? Destination with { Game = ObpSourceGame.Rac3 } : Destination with { BuildId = "other-build" };
        var provider = new Provider(new Catalogue(Destination.Game, Destination.BuildId, [destination]));
        Assert.Throws<ArgumentException>(() => new ObpWorldProviderRegistry([provider]));
        Assert.Equal(0, provider.Loads);
    }

    [Fact]
    public void DuplicateProviderOrCaseVariantDestinationCannotReplaceTheFirstRegistration()
    {
        var destination = Destination;
        var provider = new Provider(new Catalogue(destination.Game, destination.BuildId, [destination]));
        Assert.Throws<ArgumentException>(() => new ObpWorldProviderRegistry([provider, provider]));
        var duplicate = new Provider(new Catalogue(destination.Game, destination.BuildId,
            [destination, destination with { DestinationId = destination.DestinationId.ToUpperInvariant() }]));
        Assert.Throws<ArgumentException>(() => new ObpWorldProviderRegistry([duplicate]));
        Assert.Equal(0, provider.Loads + duplicate.Loads);
    }

    private sealed record Catalogue(ObpSourceGame Game, string BuildId, IReadOnlyList<ObpDestination> Destinations) : IObpDestinationCatalogue;

    private sealed class Provider(IObpDestinationCatalogue catalogue) : IObpWorldProvider
    {
        public ObpSourceGame Game => ObpSourceGame.Rac1;
        public string BuildId => "synthetic-v1";
        public IObpDestinationCatalogue Catalogue => catalogue;
        public bool Admit { get; init; } = true;
        public int Loads { get; private set; }
        public ObpDestination? LoadedDestination { get; private set; }
        public Exception? Failure { get; set; }
        public RuntimeWorld World { get; } = new(ObpSourceGame.Rac1, "synthetic-v1", 0, "Fixture", null,
            [], [], 0, [], new ObpBounds(Vec3.Zero, new(1, 1, 1)), null, null);
        public bool CanLoad(ObpDestination destination) => Admit;
        public RuntimeWorld Load(string sourcePath, ObpDestination destination)
        {
            Loads++;
            LoadedDestination = destination;
            if (Failure is { } failure) throw failure;
            return World;
        }
    }
}
