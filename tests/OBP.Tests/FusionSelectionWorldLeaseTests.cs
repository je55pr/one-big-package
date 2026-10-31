using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// Source-neutral guard for selection callbacks that may survive R&C1 world retirement.
/// </summary>
public sealed class FusionSelectionWorldLeaseTests
{
    [Fact]
    public void PreviewMenuRejectsFusionBeforeResolvingAnyWorldLease()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "Rac1WeaponUsePresentationHost.cs"));
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        var request = Assert.Single(syntax.DescendantNodes().OfType<MethodDeclarationSyntax>(),
            m => m.Identifier.ValueText == "RequestPreview");
        var body = Assert.IsType<BlockSyntax>(request.Body);
        Assert.Equal(2, body.Statements.Count);
        var fusionGuard = Assert.IsType<IfStatementSyntax>(body.Statements[0]);
        Assert.Contains("fusion().TestActive", fusionGuard.Condition.ToString(), StringComparison.Ordinal);
        var leaseGuard = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        Assert.Contains("currentWorld() is { } world", leaseGuard.Condition.ToString(), StringComparison.Ordinal);
        Assert.Contains("world.UsePresentation.SetPreview(disable)", leaseGuard.Statement.ToString(), StringComparison.Ordinal);
        Assert.Contains("report(", leaseGuard.Else!.Statement.ToString(), StringComparison.Ordinal);

        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        Assert.Equal(5, root.Split("Rac1WeaponUsePresentationHost.RequestPreview(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Rac1World.UsePresentation.SetPreview", root, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionCancellationDoesNotDereferenceAnUnboundRac1World()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.cs"));
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        var argument = Assert.Single(syntax.DescendantNodes().OfType<ArgumentSyntax>(),
            argument => argument.NameColon?.Name.Identifier.ValueText == "SelectionChanged");
        var lambda = Assert.IsType<SimpleLambdaExpressionSyntax>(argument.Expression);
        var body = Assert.IsType<BlockSyntax>(lambda.Body);

        Assert.Equal(5, body.Statements.Count);
        var guard = Assert.IsType<IfStatementSyntax>(body.Statements[2]);
        Assert.Contains("_worldScenes?.Current?.Resources.Rac1Gameplay is { } gameplay",
            guard.Condition.ToString(), StringComparison.Ordinal);
        var guarded = Assert.IsType<BlockSyntax>(guard.Statement);
        Assert.Contains("gameplay.Context.BombFireRequested = gameplay.Context.EquipmentUsePressed = false",
            Assert.Single(guarded.Statements).ToString(), StringComparison.Ordinal);

        // Player input cancellation and native throwing cleanup remain after the guard.
        // They must execute when no world lease is bound.
        Assert.Contains("_persistentPlayer.Rac1Use.CancelFire()", body.Statements[3].ToString(), StringComparison.Ordinal);
        Assert.Contains("ThrownAnimation.Stop()", body.Statements[4].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.Context", body.ToString(), StringComparison.Ordinal);
    }
}
