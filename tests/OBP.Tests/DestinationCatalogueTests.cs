using OBP.Core;
using OBP.RAC1;
using OBP.RAC2;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class DestinationCatalogueTests
{
    [Fact]
    public void GcProjectionPreservesAllNativeDestinationsWithoutCollapsingPlanets()
    {
        IObpDestinationCatalogue catalogue = GcDestinationCatalogue.Instance;

        Assert.Equal(ObpSourceGame.Rac2, catalogue.Game);
        Assert.Equal(Rac2Authority.Primary.BuildId, catalogue.BuildId);
        Assert.Equal(27, catalogue.Destinations.Count);
        Assert.Equal(27, catalogue.Destinations.Select(d => d.DestinationId).Distinct().Count());
        Assert.All(catalogue.Destinations, d => Assert.Equal(ObpSourceGame.Rac2, d.Game));

        var aranos = catalogue.Destinations.Where(d => d.PlanetLabel == "Aranos").ToArray();
        Assert.Equal(3, aranos.Length);
        Assert.Equal(new[] { "rac2:LEVEL0", "rac2:LEVEL14", "rac2:LEVEL21" },
            aranos.Select(d => d.DestinationId).ToArray());
    }

    [Fact]
    public void GcProjectionKeepsFileLevelAndEngineLevelIdentitySeparate()
    {
        var level21 = GcDestinationCatalogue.Instance.FindByNativeLevel(21)!;

        Assert.Equal("rac2:LEVEL21", level21.DestinationId);
        Assert.Equal("LEVEL21", level21.NativeDestinationId);
        Assert.Equal("30", level21.NativeEngineId);
        Assert.Equal("/G/LEVEL21.WAD", level21.NativeContainer);
        Assert.Equal("Aranos — Floating Prison", level21.DisplayName);
        Assert.True(level21.Walkable);
    }

    [Fact]
    public void GcProjectionMapsNativeKindsWithoutChangingTheirMeaning()
    {
        var entries = GcDestinationCatalogue.Instance.Destinations;

        Assert.Equal(ObpDestinationKind.Planet, entries.Single(d => d.DestinationId == "rac2:LEVEL1").Kind);
        Assert.Equal(ObpDestinationKind.SpaceCombat, entries.Single(d => d.DestinationId == "rac2:LEVEL5").Kind);
        Assert.Equal(ObpDestinationKind.Unresolved, entries.Single(d => d.DestinationId == "rac2:LEVEL14").Kind);
        Assert.Equal(ObpDestinationKind.Vendor, entries.Single(d => d.DestinationId == "rac2:LEVEL24").Kind);
        Assert.Equal(ObpDestinationKind.Scene, entries.Single(d => d.DestinationId == "rac2:LEVEL25").Kind);
    }

    [Fact]
    public void GcWorldProviderRoutesByNativeFileIdentityNotEngineId()
    {
        IObpWorldProvider provider = GcWorldProvider.Instance;
        var level21 = GcDestinationCatalogue.Instance.FindByNativeLevel(21)!;

        Assert.Equal(ObpSourceGame.Rac2, provider.Game);
        Assert.Same(GcDestinationCatalogue.Instance, provider.Catalogue);
        Assert.True(provider.CanLoad(level21));
        Assert.Equal(21, GcWorldProvider.Instance.ResolveNativeLevel(level21));
        Assert.Equal("30", level21.NativeEngineId);
    }

    [Fact]
    public void GcWorldProviderRejectsForeignOrInventedDestinations()
    {
        var gc = GcWorldProvider.Instance;
        var oozla = GcDestinationCatalogue.Instance.FindByNativeLevel(1)!;
        Assert.True(gc.CanLoad(oozla));

        var foreign = oozla with { Game = ObpSourceGame.Rac1, DestinationId = "rac1:LEVEL1" };
        Assert.False(gc.CanLoad(foreign));

        var invented = oozla with { DestinationId = "rac2:LEVEL999", NativeDestinationId = "LEVEL999" };
        Assert.False(gc.CanLoad(invented));
    }

    [Fact]
    public void ProviderRegistryResolvesGlobalDestinationIdsCaseInsensitively()
    {
        var registry = new ObpWorldProviderRegistry([GcWorldProvider.Instance]);

        Assert.Single(registry.Providers);
        Assert.Contains(ObpSourceGame.Rac2, registry.Games);
        Assert.Same(GcWorldProvider.Instance, registry.Find(ObpSourceGame.Rac2));
        Assert.Equal("rac2:LEVEL21", registry.Resolve("RAC2:level21")!.DestinationId);
        Assert.Null(registry.Resolve("rac1:LEVEL1"));
    }

    [Fact]
    public void Rac1ProjectionExposesOnlyRetailValidatedNativeIdentity()
    {
        IObpDestinationCatalogue catalogue = Rac1DestinationCatalogue.Instance;
        Assert.Equal(ObpSourceGame.Rac1, catalogue.Game);
        Assert.Equal(Rac1Authority.Primary.BuildId, catalogue.BuildId);
        Assert.Equal(19, catalogue.Destinations.Count);
        Assert.Equal(Enumerable.Range(0, 19).Select(level => $"rac1:LEVEL{level}"),
            catalogue.Destinations.Select(destination => destination.DestinationId));
        Assert.All(catalogue.Destinations, destination =>
        {
            Assert.Equal(ObpDestinationKind.Unresolved, destination.Kind);
            Assert.Null(destination.NativeEngineId);
            Assert.Null(destination.NativeContainer);
        });
    }

    [Fact]
    public void Rac1WorldProviderRoutesCanonicalNativeLevelIds()
    {
        var provider = Rac1WorldProvider.Instance;
        var level0 = Rac1DestinationCatalogue.Instance.FindByNativeLevel(0)!;
        var level18 = Rac1DestinationCatalogue.Instance.FindByNativeLevel(18)!;
        Assert.True(provider.CanLoad(level0));
        Assert.True(provider.CanLoad(level18));
        Assert.Equal(0, provider.ResolveNativeLevel(level0));
        Assert.Equal(18, provider.ResolveNativeLevel(level18));
        Assert.False(provider.CanLoad(level0 with { DestinationId = "rac1:LEVEL999", NativeDestinationId = "LEVEL999" }));
        Assert.False(provider.CanLoad(level0 with { Game = ObpSourceGame.Rac2, DestinationId = "rac2:LEVEL0" }));
    }
}
