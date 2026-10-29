using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>Syntax-aware ownership and lease-retirement guards for the extracted Aridia encounter.</summary>
public sealed class Rac1AridiaEncounterSemanticsTests
{
    [Fact]
    public void HostCallsEncounterDirectlyWithoutStateSharingThroughNeighborPorts()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var encounter = compilation.GetTypeByMetadataName("OBP.Game.Rac1AridiaEncounter")!;
        var host = compilation.GetTypeByMetadataName("OBP.Game.Rac1EnemyHost")!;
        var neighbor = compilation.GetTypeByMetadataName("OBP.Game.Rac1EnemyNeighborPorts")!;
        var ports = compilation.GetTypeByMetadataName("OBP.Game.Rac1EnemyPorts")!;
        var constructor = Assert.Single(neighbor.InstanceConstructors.Where(method =>
            method.Parameters.Length == 1 && method.Parameters[0].Name == "Current"));
        Assert.Equal("Func<bool>", constructor.Parameters[0].Type.ToDisplayString());
        Assert.DoesNotContain(ports.GetMembers().OfType<IPropertySymbol>(),
            member => member.Type.ToDisplayString().Contains("Rac1Aridia", StringComparison.Ordinal));
        Assert.DoesNotContain(neighbor.GetMembers().OfType<IPropertySymbol>(),
            member => member.Type.ToDisplayString().Contains("Rac1Aridia", StringComparison.Ordinal));

        // Validate the host's typed encounter calls as AST nodes. This guard
        // tolerates whitespace and formatting changes without string matching.
        AssertCalls(host, "ConfigureRac1Enemies", "Aridia", "Configure");
        AssertCalls(host, "TickNativeActors", "Aridia", "Tick");
        AssertCalls(host, "ApplyRac1EnemyWeaponDamage", "Aridia", "ApplyDamage");
        Assert.Contains(MethodSyntax(host, "ResetOwned").DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
            access => access.Expression is IdentifierNameSyntax { Identifier.ValueText: "Aridia" }
                && access.Name.Identifier.ValueText == "Reset");
        AssertCalls(host, "Dispose", "Aridia", "Dispose");
        AssertCalls(encounter, "Reset", null, "ResetRac1ConstructorbotStartups");
        AssertCalls(encounter, "Dispose", null, "Reset");
    }

    [Fact]
    public void ResetClearsEveryReadonlyDictionaryCacheAndDisposalPreventsReconfigure()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var encounter = compilation.GetTypeByMetadataName("OBP.Game.Rac1AridiaEncounter")!;
        var caches = encounter.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .SelectMany(node => node.DescendantNodes().OfType<FieldDeclarationSyntax>())
            .Where(field => field.Modifiers.Any(token => token.IsKind(SyntaxKind.ReadOnlyKeyword))
                && field.Declaration.Type is GenericNameSyntax { Identifier.ValueText: "Dictionary" })
            .SelectMany(field => field.Declaration.Variables)
            .Select(variable => variable.Identifier.ValueText).ToArray();
        Assert.NotEmpty(caches);

        var reset = MethodSyntax(encounter, "ResetRac1ConstructorbotStartups");
        var cleared = reset.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression).OfType<MemberAccessExpressionSyntax>()
            .Where(access => access.Name.Identifier.ValueText == "Clear"
                && access.Expression is IdentifierNameSyntax)
            .Select(access => ((IdentifierNameSyntax)access.Expression).Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string field in caches)
            Assert.Contains(field, cleared);

        var configure = MethodSyntax(encounter, "Configure");
        Assert.Contains(configure.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            call => call.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "ObjectDisposedException" },
                Name.Identifier.ValueText: "ThrowIf"
            });

        // Clearing this cache is essential because a subsequent lease may use
        // the same native clock frame but different Godot physics RIDs.
        Assert.Contains(reset.DescendantNodes().OfType<AssignmentExpressionSyntax>(), assignment =>
            assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "_actorExclusions" }
            && assignment.Right.IsKind(SyntaxKind.NullLiteralExpression));
    }

    [Fact]
    public void LethalRewardIsTrackedAndTerrainProbesExcludeTheEncounterRoster()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var encounter = compilation.GetTypeByMetadataName("OBP.Game.Rac1AridiaEncounter")!;
        var damage = MethodSyntax(encounter, "ApplyDamage");
        Assert.Contains(damage.DescendantNodes().OfType<AssignmentExpressionSyntax>(), assignment =>
            assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "RewardEmitted" }
            && assignment.Right.IsKind(SyntaxKind.TrueLiteralExpression));
        Assert.Contains(damage.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
            call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "TrackPickups" });

        var exclusions = MethodSyntax(encounter, "ActorExclusions");
        Assert.Contains(exclusions.DescendantNodes().OfType<MemberAccessExpressionSyntax>(), access =>
            access.Name.Identifier.ValueText == "Roster");
        Assert.Contains(exclusions.DescendantNodes().OfType<MemberAccessExpressionSyntax>(), access =>
            access.Name.Identifier.ValueText == "Player");
        foreach (string methodName in new[]
        {
            "ProbeRac1ConstructorbotStateThreeFloor", "ProbeRac1AridiaWalkerSupport",
            "ProbeRac1AridiaWalkerSpherePushouts", "ProbeRac1AridiaWalkerGroundLift",
            "MoveRac1AridiaWalkerContact"
        })
        {
            var probe = MethodSyntax(encounter, methodName);
            Assert.Contains(probe.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
                call.Expression is IdentifierNameSyntax { Identifier.ValueText: "ActorExclusions" });
        }
    }
    private static MethodDeclarationSyntax MethodSyntax(INamedTypeSymbol type, string methodName)
    {
        var method = Assert.Single(type.GetMembers(methodName).OfType<IMethodSymbol>());
        return Assert.IsType<MethodDeclarationSyntax>(Assert.Single(method.DeclaringSyntaxReferences).GetSyntax());
    }

    private static void AssertCalls(INamedTypeSymbol owner, string callerMethod, string? receiver, string calleeMethod)
    {
        var method = MethodSyntax(owner, callerMethod);
        Assert.Contains(method.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
            receiver is null
                ? call.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == calleeMethod
                : call.Expression is MemberAccessExpressionSyntax access
                  && access.Expression is IdentifierNameSyntax receiverIdentifier && receiverIdentifier.Identifier.ValueText == receiver
                  && access.Name.Identifier.ValueText == calleeMethod);
    }
}
