using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class SourceNeutralHostArchitectureTests
{
    [Fact]
    public void NativeWaterContactsCanPlayAudioButCannotBindItsSelectedSource()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/Rac1WorldModule.cs");
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "BindSourcePlayerContacts");
        var moduleModel = compilation.GetSemanticModel(tree);
        Assert.DoesNotContain(method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => moduleModel.GetSymbolInfo(call).Symbol).OfType<IMethodSymbol>(),
            symbol => symbol.ContainingType.Name is "Rac1PlayerAudioPresenter" or "PlayerAvatarPresentationHost");
        var subscriptions = method.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.IsKind(SyntaxKind.AddAssignmentExpression))
            .Select(assignment => (
                Event: moduleModel.GetSymbolInfo(assignment.Left).Symbol,
                Callback: moduleModel.GetSymbolInfo(assignment.Right).Symbol));
        Assert.Contains(subscriptions, binding =>
            binding.Event is IEventSymbol { Name: "Rac1WaterSoundRequested", ContainingType.Name: "PlayerHost" } &&
            binding.Callback is IPropertySymbol { Name: "WaterSound", ContainingType.Name: "Rac1WorldContactPorts" });
        var composition = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.cs");
        var factory = composition.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "CreateRac1WorldModule");
        var model = compilation.GetSemanticModel(composition);
        var audioCalls = factory.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => model.GetSymbolInfo(call).Symbol).OfType<IMethodSymbol>()
            .Where(symbol => symbol.ContainingType.Name == "Rac1PlayerAudioPresenter").ToArray();
        Assert.Single(audioCalls, symbol => symbol.Name == "Play");
        Assert.All(audioCalls, symbol => Assert.Contains(symbol.Name, new[] { "Play", "RetireWorld" }));
        var usage = Assert.Single(HostOwnershipAudit.Scan(compilation), usage => usage.Key.EndsWith(
            "::Rac1WorldModule.BindSourcePlayerContacts(PlayerHost)", StringComparison.Ordinal));
        Assert.False(usage.PlayerSelection);
    }

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
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.WorldEntry.cs");
        const string mistake = """
            private void ChooseAvatarFromPlanet(OBP.Core.ObpDestination next) {
                var origin = next;
                var provider = OBP.Game.PlayerAvatarPresentationHost.Provider(origin.Game);
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
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/Rac1NovalisWaterMechanism.cs");
        string source = original.GetText().ToString().Replace(
            "_water = Rac1NovalisWaterSurfaces.FromWorld(world);",
            "var wrongProvider = OBP.Game.PlayerAvatarPresentationHost.Provider(OBP.Core.ObpSourceGame.Rac1);\n" +
            "_water = Rac1NovalisWaterSurfaces.FromWorld(world);", StringComparison.Ordinal);
        Assert.NotEqual(original.GetText().ToString(), source);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("Rac1NovalisWaterMechanism.Configure", errors[0]);
        Assert.Contains("source-world adapter selects a player service", errors[0]);
    }

    [Fact]
    public void GadgetToggleCannotRegainRootOrWorldOriginSelection()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/Rac1MetalDetectorHost.cs");
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Toggle");
        Assert.Contains("ports.Ordinary()", method.ToString(), StringComparison.Ordinal);
        Assert.Contains("ports.OwnsGadget()", method.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(method.DescendantNodes().OfType<IdentifierNameSyntax>(),
            name => name.Identifier.ValueText is "OBPGame" or "UseRac1Gameplay");
        Assert.DoesNotContain(method.DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
            access => access.Name.Identifier.ValueText == "Game");
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1MetalDetector.cs")));
    }

    [Fact]
    public void CheckpointSourceAdmissionCannotAcquirePlayerSelection()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.Rac1Lifecycle.cs");
        var method = original.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.Text == "ResolveRac1EnvironmentalRestart");
        Assert.Empty(method.DescendantNodes().OfType<AssignmentExpressionSyntax>());
        Assert.All(method.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
            Assert.Contains(call.Expression.ToString(), new[] { "_rac1Nanotech.Probe", "checkpoint.ResolveEnvironmentalRestart" }));
        string source = original.GetText().ToString().Replace("return checkpoint.ResolveEnvironmentalRestart();",
            "var wrongProvider = OBP.Game.PlayerAvatarPresentationHost.Provider(OBP.Core.ObpSourceGame.Rac1);\nreturn checkpoint.ResolveEnvironmentalRestart();", StringComparison.Ordinal);
        Assert.NotEqual(original.GetText().ToString(), source);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            CSharpSyntaxTree.ParseText(source, path: original.FilePath))), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("source-world adapter selects a player service", errors[0]);
    }

    [Fact]
    public void WorldEntryAdapterCannotConditionallySpawnThePlayerByOrigin()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var original = compilation.SyntaxTrees.Single(tree => tree.FilePath == "game/scripts/OBPGame.WorldEntry.cs");
        var invocation = original.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression is IdentifierNameSyntax { Identifier.ValueText: "SpawnPlayer" });
        var statement = Assert.IsType<ExpressionStatementSyntax>(invocation.Parent);
        var mutation = SyntaxFactory.ParseStatement("if (world.Game == ObpSourceGame.Rac1) SpawnPlayer(world);")
            .WithTriviaFrom(statement);
        var changed = CSharpSyntaxTree.Create((CSharpSyntaxNode)original.GetRoot().ReplaceNode(statement, mutation),
            (CSharpParseOptions)original.Options, original.FilePath);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation.ReplaceSyntaxTree(original,
            changed)), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("ActivateWorld", errors[0]);
        Assert.Contains("unapproved world-origin", errors[0]);
    }

    [Theory]
    [InlineData("game/scripts/Rac1MetalDetectorHost.cs", "if (ports.Ordinary())")]
    [InlineData("game/scripts/Rac1NovalisWaterMechanism.cs", "_water = Rac1NovalisWaterSurfaces.FromWorld(world);")]
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
        var usages = HostOwnershipAudit.Scan(HostOwnershipAudit.Host.Value);
        Assert.DoesNotContain(usages, usage => usage.Key.EndsWith("::OBPGame.SpawnPlayer(RuntimeWorld)", StringComparison.Ordinal));
        Assert.DoesNotContain(usages, usage => usage.LegacyReads > 0);
        foreach (var tree in HostOwnershipAudit.Host.Value.SyntaxTrees.Where(tree => tree.FilePath.StartsWith("game/scripts/", StringComparison.Ordinal)))
            Assert.DoesNotContain("UseRac1Gameplay", tree.GetText().ToString(), StringComparison.Ordinal);
        var avatar = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.WorldEntry.cs"));
        Assert.DoesNotContain("Get(destination.Game)", avatar, StringComparison.Ordinal);
        var lab = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/ApplicationNavigationCoordinator.cs"));
        Assert.DoesNotContain("rac1:LEVEL0", lab, StringComparison.Ordinal);
        Assert.DoesNotContain("_activeDestination =", lab, StringComparison.Ordinal);
        Assert.Contains("EnterWorld(definition.Id", lab, StringComparison.Ordinal);
    }

    [Fact]
    public void EquipmentAndPlayerAudioNoLongerUseDestinationGates()
    {
        var usages = HostOwnershipAudit.Scan(HostOwnershipAudit.Host.Value);
        foreach (string member in new[] { "StepRac1PlayerAudio(", "StepRac1AdditionalWeaponWorld(", "ArmRac3Gameplay(" })
            Assert.DoesNotContain(usages, usage => usage.Key.Contains("::OBPGame." + member, StringComparison.Ordinal));
    }

    [Fact]
    public void ActorAssetProvenanceAdapterCannotSelectPlayerEquipment()
    {
        var (compilation, tree) = InsertAdapterStatement("game/scripts/ActorSceneFactory.cs", "Create",
            "((OBP.Game.OBPGame)parent.GetTree().Root.GetChild(0)).SelectPlayerEquipment(\"rac1/item/12\");");
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString().EndsWith(".SelectPlayerEquipment", StringComparison.Ordinal));
        var bound = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(call).Symbol);
        Assert.Equal("OBPGame", bound.ContainingType.Name);
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains("ActorSceneFactory.Create", errors[0]);
        Assert.Contains("approved source-world adapter changed", errors[0]);
    }

    [Theory]
    [InlineData("game/scripts/Rac1EnemyHost.cs", "TickNativeActors")]
    [InlineData("game/scripts/Rac1EnemyHost.Queries.cs", "TryGetRac1Hostile")]
    public void EnemyHostAdaptersCannotAcquirePlayerProviderSelection(string path, string method)
    {
        var (compilation, tree) = InsertAdapterStatement(path, method,
            "PlayerAvatarPresentationHost.Provider(OBP.Core.ObpSourceGame.Rac1);");
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "PlayerAvatarPresentationHost.Provider");
        var bound = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(call).Symbol);
        Assert.Equal("OBP.Game.PlayerAvatarPresentationHost", bound.ContainingType.ToDisplayString());
        var errors = HostOwnershipAudit.Violations(HostOwnershipAudit.Scan(compilation), HostOwnershipAudit.Policy());
        Assert.Single(errors);
        Assert.Contains(method, errors[0]);
        Assert.Contains("source-world adapter selects a player service", errors[0]);
    }

    [Fact]
    public void ApprovedWorldAdapterCannotAcquireTheReportedEquipmentSelectionCall()
    {
        var (compilation, tree) = InsertAdapterStatement("game/scripts/OBPGame.WorldEntry.cs", "WorldOptions", """
            if (world.Game == ObpSourceGame.Rac1)
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
        var (compilation, _) = InsertAdapterStatement("game/scripts/Rac1NovalisWaterMechanism.cs", "Configure",
            "Godot.GD.Print(\"Novalis source-water diagnostic\");");
        var usages = HostOwnershipAudit.Scan(compilation);
        var adapter = usages.Single(usage => usage.Key.EndsWith("::Rac1NovalisWaterMechanism.Configure(RuntimeWorld,RuntimeWorldScene.Result)", StringComparison.Ordinal));
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
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(member => member.Identifier.ValueText == methodName && member.Body is not null);
        var body = method.Body!;
        var changed = original.WithRootAndOptions(root.ReplaceNode(method,
            method.WithBody(body.WithStatements(body.Statements.Insert(0, SyntaxFactory.ParseStatement(statement))))), original.Options);
        return (compilation.ReplaceSyntaxTree(original, changed), changed);
    }

    [Theory]
    [InlineData("var alias = world; if (alias.Game == ObpSourceGame.Rac1) SelectAvatar();")]
    [InlineData("if (world?.Game == ObpSourceGame.Rac1) SelectMenu();")]
    [InlineData("if (world is { Game.SourceGame: ObpSourceGame.Rac1 }) SelectAttackRoute();")]
    [InlineData("var identity = world.Game; if (identity.SourceGame == ObpSourceGame.Rac1) SelectAvatar();")]
    [InlineData("if (host.UseRac1Gameplay) EnableTraversal();")]
    public void NewPlayerGatesFailIncludingAliasesNullAccessAndPatterns(string body)
    {
        string source = """
            using OBP.Runtime;
            using OBP.Core;
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
