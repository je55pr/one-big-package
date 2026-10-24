using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/PlayerHost.Animation.cs");
        string source = original.GetText().ToString().Replace("bool recoveredSupportForPresentation = UseRac1Gameplay",
            "bool recoveredSupportForPresentation = UseRac1Gameplay && UseRac1Gameplay", StringComparison.Ordinal);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("UpdateAnimationState", errors[0]);
        Assert.Contains("migration owner #268", errors[0]);
    }

    [Fact]
    public void WorldEntryAdapterCannotConditionallySpawnThePlayerByOrigin()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.WorldEntry.cs");
        string source = original.GetText().ToString().Replace("else SpawnPlayer(world);",
            "else if (world.Game == \"rac1\") SpawnPlayer(world);", StringComparison.Ordinal);
        Assert.NotEqual(original.GetText().ToString(), source);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("ActivateWorld", errors[0]);
        Assert.Contains("unapproved world-origin", errors[0]);
    }

    [Theory]
    [InlineData("game/scripts/PlayerHost.Animation.cs", "bool recoveredSupportForPresentation = UseRac1Gameplay")]
    [InlineData("game/scripts/OBPGame.Rac1NovalisWater.cs", "_rac1NovalisWater = Rac1NovalisWaterSurfaces.FromWorld(world);")]
    public void FrozenBaselinesIgnoreCommentsAndCheckoutLineEndings(string path, string anchor)
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        string source = original.GetText().ToString().Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace(anchor, "/* ownership comment */ " + anchor, StringComparison.Ordinal);
        Assert.Empty(HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy()));
    }

    [Fact]
    public void MigratedPlayerSpawnUsesTheSelectedProfileWithoutWorldOriginReads()
    {
        var usage = HostOwnershipAudit.Scan(HostOwnershipAudit.Host.Value)
            .Single(usage => usage.Key.EndsWith("::OBPGame.SpawnPlayer(RuntimeWorld)", StringComparison.Ordinal));
        Assert.Equal(0, usage.WorldReads);
        Assert.Equal(1, usage.LegacyReads); // Explicit profile compatibility bridge until #269.
        var avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.PlayerAvatar.cs"));
        Assert.DoesNotContain("Get(destination.Game)", avatar, StringComparison.Ordinal);
        var lab = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.FusionLab.cs"));
        Assert.DoesNotContain("rac1:LEVEL0", lab, StringComparison.Ordinal);
        Assert.DoesNotContain("_activeDestination =", lab, StringComparison.Ordinal);
        Assert.Contains("EnterWorld(definition.Id", lab, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovedWorldAdapterCannotAcquireTheReportedEquipmentSelectionCall()
    {
        var (compilation, tree) = InsertAdapterStatement("game/scripts/OBPGame.Destinations.cs", "WorldOptions", """
            if (world.Game == "rac1")
                OnRac1WeaponSelectionRequested(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster);
            """);
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "OnRac1WeaponSelectionRequested");
        var bound = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(call).Symbol);
        Assert.Equal("OBPGame", bound.ContainingType.Name);
        Assert.NotEmpty(bound.DeclaringSyntaxReferences); // The real host method, not an unresolved or test-only stub.

        var usages = HostOwnershipAudit.Scan(compilation);
        var adapter = usages.Single(usage => usage.Key.EndsWith("::OBPGame.WorldOptions(RuntimeWorld)", StringComparison.Ordinal));
        Assert.False(adapter.PlayerSelection); // Fingerprinting closes gaps beyond the known-sink heuristic.
        var errors = HostOwnershipAudit.Violations(usages, HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("WorldOptions", errors[0]);
        Assert.Contains("approved source-world adapter changed", errors[0]);
    }

    [Fact]
    public void LegitimateWorldAdapterUpdatesRequireAReviewedFingerprint()
    {
        var (compilation, _) = InsertAdapterStatement("game/scripts/OBPGame.Rac1NovalisWater.cs", "ConfigureRac1NovalisWater",
            "Godot.GD.Print(\"Novalis source-water diagnostic\");");
        var usages = HostOwnershipAudit.Scan(compilation);
        var adapter = usages.Single(usage => usage.Key.EndsWith("::OBPGame.ConfigureRac1NovalisWater(RuntimeWorld)", StringComparison.Ordinal));
        Assert.False(adapter.PlayerSelection);
        var policy = HostOwnershipAudit.Policy();
        var errors = HostOwnershipAudit.Violations(usages, policy);
        Assert.Single(errors);
        Assert.Contains("approved source-world adapter changed", errors[0]);

        // Model the explicit ledger update in a reviewed source-world change;
        // retain its adapter kind/rationale and all other exception constraints.
        var reviewed = policy.Select(entry => entry.Key == adapter.Key ? entry with { Hash = adapter.Hash } : entry).ToArray();
        Assert.Empty(HostOwnershipAudit.Violations(usages, reviewed));
    }

    private static (CSharpCompilation Compilation, SyntaxTree Tree) InsertAdapterStatement(string path, string methodName, string statement)
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        var root = original.GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(member => member.Identifier.ValueText == methodName);
        var body = method.Body!;
        var changed = original.WithRootAndOptions(root.ReplaceNode(method,
            method.WithBody(body.WithStatements(body.Statements.Insert(0, SyntaxFactory.ParseStatement(statement))))), original.Options);
        return (compilation.ReplaceSyntaxTree(original, changed), changed);
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
