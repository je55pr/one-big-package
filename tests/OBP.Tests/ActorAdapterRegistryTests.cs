using OBP.Core;
using OBP.Fusion;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class ActorAdapterRegistryTests
{
    private static ActorAdapterDescriptor Recipe => new(ObpSourceGame.Rac2, "build", 2827, "adapter", "context", 0, 12);
    private static ActorPlacement Placement => new("enemy", new(ObpSourceGame.Rac2, "build", "native-instance"), 0, 2827,
        null, new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]), "adapter", "context", 12, "encounter");

    [Fact]
    public void ANewFamilyRegistersWithoutChangingTheWorldHost()
    {
        var registry = new ActorAdapterRegistry<object>();
        var first = new object(); var another = new object();
        registry.Register(Recipe, first);
        registry.Register(Recipe with { Source = ObpSourceGame.Rac3, NativeClassId = 500 }, another);
        Assert.Same(first, registry.Resolve(Placement));
        Assert.Same(another, registry.Resolve(Placement with
        { Source = Placement.Source with { Game = ObpSourceGame.Rac3 }, NativeClassId = 500 }));
        Assert.Throws<ArgumentException>(() => registry.Register(Recipe, new object()));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("build")]
    [InlineData("class")]
    [InlineData("adapter")]
    [InlineData("context")]
    [InlineData("level")]
    [InlineData("state")]
    public void NoFieldOfTheNativeRecipeCanBeAliased(string field)
    {
        var registry = new ActorAdapterRegistry<object>(); registry.Register(Recipe, new object());
        var placement = field switch
        {
            "source" => Placement with { Source = Placement.Source with { Game = ObpSourceGame.Rac1 } },
            "build" => Placement with { Source = Placement.Source with { Build = "other" } },
            "class" => Placement with { NativeClassId = 500 },
            "adapter" => Placement with { AdapterId = "other" },
            "context" => Placement with { NativeContextId = "other" },
            "level" => Placement with { LevelId = 1 },
            _ => Placement with { InitialNativeState = 5 },
        };
        Assert.Throws<NotSupportedException>(() => registry.Resolve(placement));
    }

    [Fact]
    public void InvalidIdentitiesFailBeforeAnyFactoryIsSelected()
    {
        var registry = new ActorAdapterRegistry<object>();
        Assert.Throws<ArgumentException>(() => registry.Register(Recipe with { NativeClassId = -1 }, new object()));
        Assert.Throws<ArgumentException>(() => registry.Register(Recipe with { Build = "" }, new object()));
        Assert.Throws<ArgumentException>(() => registry.Register(Recipe with { Source = (ObpSourceGame)999 }, new object()));
        Assert.Throws<ArgumentException>(() => registry.Resolve(Placement with { NativeClassId = -1 }));
        Assert.Throws<ArgumentException>(() => registry.Resolve(Placement with { SourceInstanceIndex = -1 }));
    }
}
