using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class WorldSceneOwnershipArchitectureTests
{
    [Fact]
    public void WorldFactsAreReadOnlyViewsOfTheSceneOwnerRatherThanSharedMutableFields()
    {
        var host = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string name in new[] { "_world", "_sceneResult", "_worldHost", "_activeDestination",
            "_currentDestinationId", "_worldPlayerEntry", "_worldEntrySnapReach" })
        {
            var member = Assert.Single(host.GetMembers(name));
            var property = Assert.IsAssignableFrom<IPropertySymbol>(member);
            Assert.Null(property.SetMethod);
        }
        Assert.Single(host.GetMembers("_worldScenes").OfType<IFieldSymbol>());
        Assert.Empty(host.GetTypeMembers("SceneLease"));
    }

    [Fact]
    public void DeferredEntryHasAnOwnedCompletionAndShutdownAwaitsIt()
    {
        string destinations = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/ApplicationNavigationCoordinator.cs"));
        Assert.DoesNotContain("_ = EnterWorldAfterSharedPreparationAsync", destinations, StringComparison.Ordinal);
        Assert.Contains("_ports.PrepareLater(", destinations, StringComparison.Ordinal);
        var deferred = NavigationMethod(destinations, "EnterAfterSharedPreparationAsync");
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", deferred.ToFullString(), StringComparison.Ordinal);
        Assert.True(HasGenerationComparison(deferred, SyntaxKind.NotEqualsExpression),
            "Deferred preparation must reject a stale navigation generation.");
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        Assert.Contains("await scenes.PendingPreparation;", root, StringComparison.Ordinal);
        Assert.Contains("_worldScenes?.Dispose();", root, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedDestinationOrLabEntryKeepsBrowserCallbacksAliveForRetry()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string source = File.ReadAllText(Path.Combine(scripts, "ApplicationNavigationCoordinator.cs"));
        foreach (string name in new[] { "Choose", "EnterLab", "OpenFromBootstrap" })
        {
            var method = NavigationMethod(source, name);
            var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(call => call.Expression.ToString()).ToArray();
            Assert.Contains("_ports.CancelPreparation", calls);
            Assert.DoesNotContain("CancelNavigation", calls);
            Assert.DoesNotContain(method.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>(),
                increment => increment.IsKind(SyntaxKind.PreIncrementExpression) &&
                    increment.Operand.ToString() == "_intentGeneration");
        }

        // A rejected bootstrap destination must retain the generation captured by
        // every existing selector action. Assert each callback's own guard, not
        // a global substring that could match some unrelated deferred callback.
        var selector = NavigationMethod(source, "ShowSelector");
        foreach (string eventName in new[] { "DestinationChosen", "FusionLabRequested", "BackRequested" })
        {
            var registration = Assert.Single(selector.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
                assignment => assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                    assignment.Left is MemberAccessExpressionSyntax member &&
                    member.Name.Identifier.ValueText == eventName);
            var callback = Assert.IsAssignableFrom<LambdaExpressionSyntax>(registration.Right);
            Assert.True(HasGenerationComparison(callback, SyntaxKind.EqualsExpression),
                $"{eventName} must ignore callbacks from a retired selector.");
        }

        var deferred = NavigationMethod(source, "EnterAfterSharedPreparationAsync");
        Assert.True(HasGenerationComparison(deferred, SyntaxKind.NotEqualsExpression));
        var travel = NavigationMethod(source, "TravelRac1CampaignTo");
        Assert.Contains(travel.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            call => call.Expression.ToString() == "_ports.AbandonFailedTravel");

        foreach (string retired in new[] { "OBPGame.Navigation.cs", "OBPGame.Destinations.cs", "OBPGame.FusionLab.cs" })
            Assert.False(File.Exists(Path.Combine(scripts, retired)), retired);
    }

    private static MethodDeclarationSyntax NavigationMethod(string source, string name) =>
        Assert.Single(CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == name);

    private static bool HasGenerationComparison(SyntaxNode scope, SyntaxKind comparison) =>
        scope.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Any(binary =>
            binary.IsKind(comparison) &&
            binary.Left is IdentifierNameSyntax left && left.Identifier.ValueText == "generation" &&
            binary.Right is IdentifierNameSyntax right && right.Identifier.ValueText == "_intentGeneration");
}
