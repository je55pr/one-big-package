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
        foreach (var tree in HostOwnershipAudit.Host.Value.SyntaxTrees.Where(tree =>
            Path.GetFileName(tree.FilePath).StartsWith("OBPGame", StringComparison.Ordinal)))
        {
            foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string name = Name(call.Expression);
                if (!CriticalOperations.Contains(name)) continue;
                seen.Add(name);
                Assert.True(Owned(call), $"Unowned critical operation {name} in {tree.FilePath}:{call.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
            }
        }
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
        SyntaxNode? parent = call.Parent;
        while (parent is ParenthesizedExpressionSyntax) parent = parent.Parent;
        if (parent is AwaitExpressionSyntax) return true;
        var lambda = call.Ancestors().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        if (lambda?.Parent is not ArgumentSyntax argument || argument.Parent?.Parent is not InvocationExpressionSyntax owner)
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
