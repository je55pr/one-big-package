using OBP.Core;
using OBP.Game;
using OBP.RAC1;
using OBP.RAC2;
using OBP.RAC3;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class DestinationCatalogueTests
{
    [Fact]
    public void Rac2ProjectionPreservesAllNativeDestinationsWithoutCollapsingPlanets()
    {
        IObpDestinationCatalogue catalogue = Rac2DestinationCatalogue.Instance;

        Assert.Equal(ObpSourceGame.Rac2, catalogue.Game);
        Assert.Equal(Rac2Authority.Primary.BuildId, catalogue.BuildId);
        Assert.Equal(27, catalogue.Destinations.Count);
        Assert.Equal(27, catalogue.Destinations.Select(d => d.DestinationId).Distinct().Count());
        Assert.All(catalogue.Destinations, d => Assert.Equal(ObpSourceGame.Rac2, d.Game));

        var aranos = catalogue.Destinations.Where(d => d.PlanetLabel == "Aranos").ToArray();
        Assert.Equal(2, aranos.Length);
        Assert.Equal(new[] { "rac2:LEVEL0", "rac2:LEVEL14" },
            aranos.Select(d => d.DestinationId).ToArray());
    }

    [Fact]
    public void AllGoingCommandoDestinationsHavePlayerFacingNames()
    {
        string[] expectedNames =
        [
            "Aranos — Floating Prison", "Oozla — The Megacorp Outlet", "Maktar Nebula — Maktar Resort", "Endako — Megapolis",
            "Barlow — Vukovar Canyon", "Feltzin System — Thug Rendezvous", "Notak — Canal City", "Siberius — Frozen Lab",
            "Tabora — Mining Area", "Dobbo — Testing Facility", "Hrugis Cloud — Deep Space Disposal", "Joba — Megacorp Games",
            "Todano — Megacorp Armory", "Boldan — Silver City", "Aranos", "Gorn — Thug Fleet", "Snivelak — Thug Headquarters",
            "Smolg — Distribution Center", "Damosel — Allgon City", "Grelbin — Tundor Wastes", "Yeedil — Protopet Factory",
            "Insomniac Museum", "Feltzin System — Space Arena", "Hrugis Cloud — Space Arena", "Slim Cognito's Ship Shack",
            "Starfield", "Gorn — Space Arena",
        ];

        Assert.Equal(expectedNames, Rac2DestinationCatalogue.Instance.Destinations.Select(destination => destination.DisplayName));
        Assert.Equal(Enumerable.Range(0, expectedNames.Length).Select(level => $"rac2:LEVEL{level}"),
            Rac2DestinationCatalogue.Instance.Destinations.Select(destination => destination.DestinationId));
        Assert.DoesNotContain(Rac2DestinationCatalogue.Instance.Destinations,
            destination => destination.DisplayName.Contains("unresolved", StringComparison.OrdinalIgnoreCase)
                || destination.DisplayName.Contains("stub", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rac2ProjectionKeepsFileLevelAndEngineLevelIdentitySeparate()
    {
        var level21 = Rac2DestinationCatalogue.Instance.FindByNativeLevel(21)!;

        Assert.Equal("rac2:LEVEL21", level21.DestinationId);
        Assert.Equal("LEVEL21", level21.NativeDestinationId);
        Assert.Equal("30", level21.NativeEngineId);
        Assert.Equal("/G/LEVEL21.WAD", level21.NativeContainer);
        Assert.Equal("Insomniac Museum", level21.DisplayName);
        Assert.True(level21.Walkable);
    }

    [Fact]
    public void Rac2ProjectionMapsNativeKindsWithoutChangingTheirMeaning()
    {
        var entries = Rac2DestinationCatalogue.Instance.Destinations;

        Assert.Equal(ObpDestinationKind.Planet, entries.Single(d => d.DestinationId == "rac2:LEVEL1").Kind);
        Assert.Equal(ObpDestinationKind.SpaceCombat, entries.Single(d => d.DestinationId == "rac2:LEVEL5").Kind);
        Assert.Equal(ObpDestinationKind.Unresolved, entries.Single(d => d.DestinationId == "rac2:LEVEL14").Kind);
        Assert.Equal(ObpDestinationKind.Vendor, entries.Single(d => d.DestinationId == "rac2:LEVEL24").Kind);
        Assert.Equal(ObpDestinationKind.Scene, entries.Single(d => d.DestinationId == "rac2:LEVEL25").Kind);
    }

    [Fact]
    public void Rac2WorldProviderRoutesByNativeFileIdentityNotEngineId()
    {
        IObpWorldProvider provider = Rac2WorldProvider.Instance;
        var level21 = Rac2DestinationCatalogue.Instance.FindByNativeLevel(21)!;

        Assert.Equal(ObpSourceGame.Rac2, provider.Game);
        Assert.Same(Rac2DestinationCatalogue.Instance, provider.Catalogue);
        Assert.True(provider.CanLoad(level21));
        Assert.Equal(21, Rac2WorldProvider.Instance.ResolveNativeLevel(level21));
        Assert.Equal("30", level21.NativeEngineId);
    }

    [Fact]
    public void Rac2WorldProviderRejectsForeignOrInventedDestinations()
    {
        var rac2 = Rac2WorldProvider.Instance;
        var oozla = Rac2DestinationCatalogue.Instance.FindByNativeLevel(1)!;
        Assert.True(rac2.CanLoad(oozla));

        var foreign = oozla with { Game = ObpSourceGame.Rac1, DestinationId = "rac1:LEVEL1" };
        Assert.False(rac2.CanLoad(foreign));

        var invented = oozla with { DestinationId = "rac2:LEVEL999", NativeDestinationId = "LEVEL999" };
        Assert.False(rac2.CanLoad(invented));
    }

    [Fact]
    public void ProviderRegistryResolvesGlobalDestinationIdsCaseInsensitively()
    {
        var registry = new ObpWorldProviderRegistry([Rac2WorldProvider.Instance]);

        Assert.Single(registry.Providers);
        Assert.Contains(ObpSourceGame.Rac2, registry.Games);
        Assert.Same(Rac2WorldProvider.Instance, registry.Find(ObpSourceGame.Rac2));
        Assert.Equal("rac2:LEVEL21", registry.Resolve("RAC2:level21")!.DestinationId);
        Assert.Null(registry.Resolve("rac1:LEVEL1"));
    }

    [Fact]
    public void Rac3ProjectionExposesAllObservedNamesWithoutChangingTableIdentity()
    {
        string[] expectedNames =
        [
            "Veldin", "Florana", "Starship Phoenix", "Marcadia", "Daxx", "Starship Phoenix (Under Attack)",
            "Annihilation Nation", "Aquatos", "Tyhrranosis", "Zeldrin Starport", "Obani Moons", "Rilgar",
            "Holostar Studios Ratchet", "Koros", "Kerwan", "Crash Site", "Aridia", "Thran Asteroid Belt", "Final Boss",
            "Obani Draco", "Mylon", "Holostar Studios Clank", "Insomniac Museum", "Kerwan Ranger Missions", "Aquatos Base",
            "Aquatos Sewers", "Tyhrranosis Ranger Missions", "Vid-Comic", "Vid-Comic 1", "Vid-Comic 4", "Vid-Comic 2",
            "Vid-Comic 3", "Vid-Comic 5", "Vid-Comic 1 Special Edition", "Multiplayer Menu", "Bakisi Isles", "Hoven Gorge",
            "Outpost X12", "Korgon Outpost", "Metropolis", "Blackwater City", "Command Center", "Blackwater Docks",
            "Aquatos Sewers", "Marcadia Palace", "Bakisi Isles (Split-screen)", "Hoven Gorge (Split-screen)",
            "Outpost X12 (Split-screen)", "Korgon Outpost (Split-screen)", "Metropolis (Split-screen)",
            "Blackwater City (Split-screen)",
        ];

        var catalogue = Rac3DestinationCatalogue.Instance;
        Assert.Equal(expectedNames, catalogue.Destinations.Select(destination => destination.DisplayName));
        Assert.Equal(Rac3DestinationCatalogue.ObservedMainTableIndices.Select(table => $"rac3:TABLE{table}"),
            catalogue.Destinations.Select(destination => destination.DestinationId));
        Assert.Equal(Rac3DestinationCatalogue.ObservedMainTableIndices.Select(table => $"TABLE{table}"),
            catalogue.Destinations.Select(destination => destination.NativeDestinationId));
        Assert.DoesNotContain(catalogue.Destinations,
            destination => destination.DisplayName.Contains("TABLE", StringComparison.OrdinalIgnoreCase)
                || destination.DisplayName.Contains("Unnamed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldSelectorShowsLoadableUnclassifiedRac3DestinationsWithoutDebugMode()
    {
        var provider = Rac3WorldProvider.Instance;
        Assert.Equal(51, provider.Catalogue.Destinations.Count);
        Assert.All(provider.Catalogue.Destinations, destination =>
        {
            Assert.Equal(ObpDestinationKind.Unresolved, destination.Kind);
            Assert.True(provider.CanLoad(destination));
            Assert.True(DestinationSelectorVisibility.IsVisible(destination, provider, debugOutputEnabled: false));
        });

        var unknown = provider.Catalogue.Destinations[0] with
        {
            DestinationId = "rac3:TABLE999",
            NativeDestinationId = "TABLE999",
        };
        Assert.False(provider.CanLoad(unknown));
        Assert.False(DestinationSelectorVisibility.IsVisible(unknown, provider, debugOutputEnabled: false));
        Assert.True(DestinationSelectorVisibility.IsVisible(unknown, provider, debugOutputEnabled: true));
    }

    [Fact]
    public void WorldSelectorDoesNotAdvertiseExtraDestinationsWhenAllRac3RowsAreLoadable()
    {
        var provider = Rac3WorldProvider.Instance;
        Assert.False(DestinationSelectorVisibility.HasHiddenDestinations(provider.Catalogue.Destinations, provider));
    }

    [Fact]
    public void WorldSelectorAdvertisesAdditionalDestinationsWhenUnresolvedRowCannotLoad()
    {
        var provider = Rac3WorldProvider.Instance;
        var unknown = provider.Catalogue.Destinations[0] with
        {
            DestinationId = "rac3:TABLE999",
            NativeDestinationId = "TABLE999",
        };
        Assert.True(DestinationSelectorVisibility.HasHiddenDestinations(
            provider.Catalogue.Destinations.Append(unknown), provider));
    }

    [Fact]
    public void WorldSelectorDistinguishesDuplicateRac3LocationNamesWithoutRenamingCatalogueEntries()
    {
        var destinations = Rac3WorldProvider.Instance.Catalogue.Destinations;
        var firstSewers = destinations.Single(destination => destination.NativeDestinationId == "TABLE28");
        var secondSewers = destinations.Single(destination => destination.NativeDestinationId == "TABLE48");
        var veldin = destinations.Single(destination => destination.NativeDestinationId == "TABLE1");

        Assert.Equal("Aquatos Sewers", firstSewers.DisplayName);
        Assert.Equal("Aquatos Sewers", secondSewers.DisplayName);
        Assert.Equal("Aquatos Sewers (TABLE28)", DestinationSelectorVisibility.DisambiguatedName(firstSewers, destinations));
        Assert.Equal("Aquatos Sewers (TABLE48)", DestinationSelectorVisibility.DisambiguatedName(secondSewers, destinations));
        Assert.Equal("Veldin", DestinationSelectorVisibility.DisambiguatedName(veldin, destinations));
    }

    [Fact]
    public void Rac1ProjectionExposesAllDecodedNamesWithoutChangingNativeIdentity()
    {
        IObpDestinationCatalogue catalogue = Rac1DestinationCatalogue.Instance;
        string[] expectedNames =
        [
            "Veldin", "Novalis", "Aridia", "Kerwan", "Eudora", "Rilgar", "Nebula G34", "Umbris", "Batalia", "Gaspar",
            "Orxon", "Pokitaru", "Hoven", "Oltanis Orbit", "Oltanis", "Quartu", "Kalebo III", "Veldin Orbit", "Veldin",
        ];

        Assert.Equal(ObpSourceGame.Rac1, catalogue.Game);
        Assert.Equal(Rac1Authority.Primary.BuildId, catalogue.BuildId);
        Assert.Equal(expectedNames, catalogue.Destinations.Select(destination => destination.DisplayName));
        Assert.Equal(Enumerable.Range(0, expectedNames.Length).Select(level => $"rac1:LEVEL{level}"),
            catalogue.Destinations.Select(destination => destination.DestinationId));
        Assert.Equal(Enumerable.Range(0, expectedNames.Length).Select(level => $"LEVEL{level}"),
            catalogue.Destinations.Select(destination => destination.NativeDestinationId));
        Assert.All(catalogue.Destinations, destination =>
        {
            Assert.Equal(ObpDestinationKind.Planet, destination.Kind);
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
