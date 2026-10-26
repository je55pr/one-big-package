using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class HostOperationOwnershipArchitectureTests
{
    private static readonly HashSet<string> CriticalOperations = new(StringComparer.Ordinal)
    {
        "RunCaptureAsync", "RunShotsAsync", "RunStressSwitchAsync", "LoadMovementStateAsync",
        "ExportSelectedBugAsync", "ReplaySelectedBugAsync", "RunMovementStateSmokeAsync"
    };

    [Fact]
    public void CriticalLaunchesAreOwnedAndShutdownDrainsThemBeforeResourceRelease()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in HostOwnershipAudit.Host.Value.SyntaxTrees)
        {
            foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string name = Name(call.Expression);
                if (!CriticalOperations.Contains(name)) continue;
                seen.Add(name);
                Assert.True(Owned(call), $"Unowned critical operation {name} in {tree.FilePath}:{call.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
            }
        }
        // The retired player-session partial bound this existing entrypoint to a focused engine proof.
        // Keep auditing both constructor bindings and the awaited delegate, rather than dropping the critical operation.
        var movementBindings = HostOwnershipAudit.Host.Value.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ArgumentSyntax>())
            .Where(argument => argument.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "RunMovementStateSmokeAsync").ToArray();
        Assert.Equal(2, movementBindings.Length);
        Assert.All(movementBindings, argument => Assert.Contains(
            Assert.IsType<ObjectCreationExpressionSyntax>(argument.Parent!.Parent).Type.ToString(),
            new[] { "WorldSmokeHarness.Entrypoints", "GodotSessionOperationChecks" }));
        var proof = HostOwnershipAudit.Host.Value.SyntaxTrees.Single(tree => Path.GetFileName(tree.FilePath) == "GodotSessionOperationChecks.cs");
        var delegated = Assert.Single(proof.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>(), call => Name(call.Expression) == "movementStateCheck");
        Assert.True(Owned(delegated), "The retained movement cancellation proof must await the bound critical operation.");
        seen.Add("RunMovementStateSmokeAsync");
        Assert.True(CriticalOperations.SetEquals(seen), "The guard must encounter every retained critical operation.");
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        int stop = root.IndexOf("await hostStopped;", StringComparison.Ordinal);
        Assert.True(stop > 0);
        Assert.True(root.IndexOf("ReleaseAudioForExit();", stop, StringComparison.Ordinal) > stop);
    }

    [Theory]
    [InlineData("_=LoadMovementStateAsync(path);", false)]
    [InlineData("LoadMovementStateAsync(path);", false)]
    [InlineData("var lost=ReplaySelectedBugAsync();", false)]
    [InlineData("await LoadMovementStateAsync(path);", true)]
    [InlineData("_hostOperations.Start(\"capture\", token=>RunCaptureAsync(1, token));", true)]
    [InlineData("StartBugOverlayWork(\"replay\", token=>ReplaySelectedBugAsync(token));", true)]
    [InlineData("await Task.Run(()=>{_=RunCaptureAsync(1, token);});", false)]
    [InlineData("_=Task.Run(async()=>await RunCaptureAsync(1, token));", false)]
    [InlineData("_hostOperations.Start(\"capture\", async token=>await RunCaptureAsync(1, token));", true)]
    [InlineData("_hostOperations.Start(\"capture\", token=>{_=RunCaptureAsync(1, token); return Task.CompletedTask;});", false)]
    public void GuardRecognizesAwaitedOrOwnedCallsRatherThanDiscardSpelling(string body, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("class Fixture { async Task Run() { " + body + " } }");
        var call = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>(),
            call => CriticalOperations.Contains(Name(call.Expression)));
        Assert.Equal(expected, Owned(call));
    }

    private static string Name(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => string.Empty
    };

    private static bool Owned(InvocationExpressionSyntax call)
    {
        SyntaxNode expression = call;
        SyntaxNode? parent = call.Parent;
        while (parent is ParenthesizedExpressionSyntax) { expression = parent; parent = parent.Parent; }
        var function = call.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        if (function is null) return parent is AwaitExpressionSyntax;
        bool consumed = parent is AwaitExpressionSyntax or ReturnStatementSyntax ||
            function is LambdaExpressionSyntax lambda && lambda.Body == expression;
        if (!consumed || function.Parent is not ArgumentSyntax argument || argument.Parent?.Parent is not InvocationExpressionSyntax owner)
            return false;
        return Name(owner.Expression) == "StartBugOverlayWork" || owner.Expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText == "Start" && member.Expression.ToString().EndsWith("_hostOperations", StringComparison.Ordinal);
    }

    [Fact]
    public void SourceVerificationDoesNotPublishIntoSharedRootProgressFields()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_verifyOutcome"));
        Assert.Empty(root.GetMembers("_verifyProgressBits"));
        Assert.Single(root.GetMembers("_verification").OfType<IFieldSymbol>());
    }
}
