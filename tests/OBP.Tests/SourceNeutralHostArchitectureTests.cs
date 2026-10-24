using Microsoft.CodeAnalysis.CSharp;

namespace OBP.Tests;

public sealed class SourceNeutralHostArchitectureTests
{
    [Fact]
    public void HostPlayerDependenciesHaveNarrowRationalesAndFrozenMigrationOwners()
    {
        var usages = HostOwnershipAudit.Scan(HostOwnershipAudit.Host.Value);
        Assert.NotEmpty(usages);
        var errors = HostOwnershipAudit.Violations(usages, HostOwnershipAudit.Policy());
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void InjectingDestinationDependentAvatarSelectionIntoTheRealHostFails()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.PlayerAvatar.cs");
        const string mistake = """
            private void ChooseAvatarFromPlanet(OBP.Core.ObpDestination next) {
                var origin = next;
                var provider = PlayerAvatarProviders.Get(origin.Game);
            }
            """;
        string source = original.GetText().ToString();
        var changed = CSharpSyntaxTree.ParseText(source.Insert(source.LastIndexOf('}'), mistake), path: original.FilePath);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original, changed)), HostOwnershipAudit.Policy());
        Assert.Contains(errors, error => error.Contains("ChooseAvatarFromPlanet", StringComparison.Ordinal) && error.Contains("OBP001", StringComparison.Ordinal));
    }

    [Fact]
    public void AnApprovedWaterAdapterCannotStartSelectingThePlayerAvatar()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.Rac1NovalisWater.cs");
        string source = original.GetText().ToString().Replace(
            "_rac1NovalisWater = Rac1NovalisWaterSurfaces.FromWorld(world);",
            "var wrongProvider = PlayerAvatarProviders.Get(OBP.Core.ObpSourceGame.Rac1);\n" +
            "_rac1NovalisWater = Rac1NovalisWaterSurfaces.FromWorld(world);", StringComparison.Ordinal);
        Assert.NotEqual(original.GetText().ToString(), source);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("ConfigureRac1NovalisWater", errors[0]);
        Assert.Contains("source-world adapter selects a player service", errors[0]);
    }

    [Fact]
    public void ExistingPlayerCouplingCannotGrowUnderItsMigrationException()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.PlayerAvatar.cs");
        string source = original.GetText().ToString().Replace("IPlayerAvatarProvider? provider =",
            "var anotherWorldGate = _activeDestination?.Game;\nIPlayerAvatarProvider? provider =", StringComparison.Ordinal);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("AttachPlayerAvatarVisual", errors[0]);
        Assert.Contains("migration owner #268", errors[0]);
    }

    [Fact]
    public void WorldEntryAdapterCannotConditionallySpawnThePlayerByOrigin()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.Destinations.cs");
        string source = original.GetText().ToString().Replace("SpawnPlayer(world);",
            "if (world.Game == \"rac1\") SpawnPlayer(world);", StringComparison.Ordinal);
        Assert.NotEqual(original.GetText().ToString(), source);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("AdoptRuntimeWorld", errors[0]);
        Assert.Contains("source-world adapter selects a player service", errors[0]);
    }

    [Fact]
    public void FrozenBaselinesIgnoreCommentsAndCheckoutLineEndings()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.PlayerAvatar.cs");
        string source = original.GetText().ToString().Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("IPlayerAvatarProvider? provider =", "/* presentation comment */ IPlayerAvatarProvider? provider =", StringComparison.Ordinal);
        Assert.Empty(HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy()));
    }

    [Theory]
    [InlineData("var alias = world; if (alias.Game == \"rac1\") SelectAvatar();")]
    [InlineData("if (world?.Game == \"rac1\") SelectMenu();")]
    [InlineData("if (world is { Game: \"rac1\" }) SelectAttackRoute();")]
    [InlineData("if (host.UseRac1Gameplay) EnableTraversal();")]
    public void NewPlayerGatesFailIncludingAliasesNullAccessAndPatterns(string body)
    {
        string source = """
            using OBP.Runtime;
            class PlayerHost { public bool UseRac1Gameplay { get; set; } }
            class Host {
                void Choose(RuntimeWorld world, PlayerHost host) { BODY }
                void SelectAvatar() {} void SelectMenu() {} void SelectAttackRoute() {} void EnableTraversal() {}
            }
            """.Replace("BODY", body, StringComparison.Ordinal);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(HostOwnershipAudit.Compile(
            [CSharpSyntaxTree.ParseText(source, path: "game/scripts/NewHost.cs")])), [], checkStale: false);
        Assert.Single(errors);
        Assert.Contains("Host.Choose", errors[0]);
    }

    [Fact]
    public void AssetProvenanceCommentsAndStringsAreNotWorldGates()
    {
        const string source = """
            using OBP.Core;
            class Host {
                void Inspect(ObpSourceRef actor) {
                    // world.Game and UseRac1Gameplay are just commentary here.
                    string label = "destination.Game";
                    var game = actor.Game;
                }
            }
            """;
        Assert.Empty(HostOwnershipAudit.Scan(HostOwnershipAudit.Compile([CSharpSyntaxTree.ParseText(source)])));
    }
}
