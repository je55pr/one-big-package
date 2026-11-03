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

        string root = CompositionSources.Application;
        Assert.Equal(4, root.Split("Rac1WeaponUsePresentationHost.RequestPreview(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Rac1World.UsePresentation.SetPreview", root, StringComparison.Ordinal);
        string menu = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/PauseMenuUi.cs"));
        Assert.Equal(2, menu.Split("bindings.RequestPreview(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void OptionalDiagnosticGuidedCamerasSurviveRac1WorldRetirement()
    {
        // These diagnostic delegates can outlive their world, and the original
        // camera fields were nullable before the weapon owner extraction.
        string source = CompositionSources.Diagnostics;
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (string name in new[]
        {
            "CreateRac1AdditionalWeaponDiagnosticFixture",
            "CreateRac1QueenDiagnosticBindings",
        })
        {
            var method = Assert.Single(syntax.DescendantNodes().OfType<MethodDeclarationSyntax>(),
                method => method.Identifier.ValueText == name).ToString();
            Assert.Contains("_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.GuidedCamera.Camera",
                method, StringComparison.Ordinal);
            Assert.DoesNotContain("Rac1World.Weapons.GuidedCamera.Camera", method, StringComparison.Ordinal);
        }
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

        Assert.Equal(2, body.Statements.Count);
        Assert.Contains("Rac1WeaponUsePresentationHost.ForeignSelectionChanged(", body.ToString(), StringComparison.Ordinal);
        Assert.Contains("_worldScenes?.Current?.Resources.Rac1Gameplay", body.ToString(), StringComparison.Ordinal);
        string owner = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/Rac1WeaponUsePresentationHost.cs"));
        var handler = CSharpSyntaxTree.ParseText(owner).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ForeignSelectionChanged");
        var cleanup = Assert.IsType<BlockSyntax>(handler.Body);
        Assert.Equal(4, cleanup.Statements.Count);
        Assert.Contains("equipment.WeaponAudio.Reset()", cleanup.Statements[0].ToString(), StringComparison.Ordinal);
        var guard = Assert.IsType<IfStatementSyntax>(cleanup.Statements[1]);
        Assert.Contains("world is not null",
            guard.Condition.ToString(), StringComparison.Ordinal);
        Assert.Contains("world.Context.BombFireRequested = world.Context.EquipmentUsePressed = false",
            guard.Statement.ToString(), StringComparison.Ordinal);

        // Player input cancellation and native throwing cleanup remain after the guard.
        // They must execute when no world lease is bound.
        Assert.Contains("use.CancelFire()", cleanup.Statements[2].ToString(), StringComparison.Ordinal);
        Assert.Contains("world?.Weapons.ThrownAnimation.Stop()", cleanup.Statements[3].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Rac1World.Context", body.ToString(), StringComparison.Ordinal);
    }
}
