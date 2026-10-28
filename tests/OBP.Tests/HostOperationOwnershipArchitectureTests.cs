using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class HostOperationOwnershipArchitectureTests
{
    private static readonly HashSet<string> CriticalOperations = new(StringComparer.Ordinal)
    {
        "RunCaptureAsync", "RunShotsAsync", "RunStressSwitchAsync", "LoadMovementStateAsync",
        "ExportSelectedBugAsync", "ReplaySelectedBugAsync", "CheckMovementCancellationAsync"
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
        // The movement flow is a typed scenario now; the independent engine proof still awaits its cancellation boundary.
        var movementBindings = HostOwnershipAudit.Host.Value.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(call => Name(call.Expression) == "CheckMovementCancellationAsync").ToArray();
        Assert.Single(movementBindings);
        var proof = HostOwnershipAudit.Host.Value.SyntaxTrees.Single(tree => Path.GetFileName(tree.FilePath) == "GodotSessionOperationChecks.cs");
        var delegated = Assert.Single(proof.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>(), call => Name(call.Expression) == "movementStateCheck");
        Assert.True(Owned(delegated), "The retained movement cancellation proof must await the bound critical operation.");
        Assert.True(CriticalOperations.SetEquals(seen), "The guard must encounter every retained critical operation.");
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        int stop = root.IndexOf("await hostStopped;", StringComparison.Ordinal);
        Assert.True(stop > 0);
        Assert.True(root.IndexOf("ReleaseAudioForExit();", stop, StringComparison.Ordinal) > stop);
    }

    [Theory]
    [InlineData("Fixture", "_=LoadMovementStateAsync(path);", false)]
    [InlineData("Fixture", "LoadMovementStateAsync(path);", false)]
    [InlineData("Fixture", "var lost=ReplaySelectedBugAsync();", false)]
    [InlineData("Fixture", "await LoadMovementStateAsync(path);", true)]
    [InlineData("Fixture", "_hostOperations.Start(\"capture\", token=>RunCaptureAsync(1, token));", true)]
    [InlineData("Fixture", "StartBugOverlayWork(\"replay\", token=>ReplaySelectedBugAsync(token));", false)]
    [InlineData("Fixture", "_work.Start(\"bug-replay\", token=>ReplaySelectedBugAsync(token), result=>Show(result), error=>Fail(error));", false)]
    [InlineData("BugReportUiCoordinator", "_work.Start(\"bug-replay\", token=>ReplaySelectedBugAsync(token), result=>Show(result), error=>Fail(error));", true)]
    [InlineData("Fixture", "await Task.Run(()=>{_=RunCaptureAsync(1, token);});", false)]
    [InlineData("Fixture", "_=Task.Run(async()=>await RunCaptureAsync(1, token));", false)]
    [InlineData("Fixture", "_hostOperations.Start(\"capture\", async token=>await RunCaptureAsync(1, token));", true)]
    [InlineData("Fixture", "_hostOperations.Start(\"capture\", token=>{_=RunCaptureAsync(1, token); return Task.CompletedTask;});", false)]
    [InlineData("Fixture", "var checks = new GodotSessionOperationChecks(token=>CheckMovementCancellationAsync(token));", true)]
    [InlineData("Fixture", "var checks = new GodotSessionOperationChecks(token=>{_=CheckMovementCancellationAsync(token); return Task.CompletedTask;});", false)]
    [InlineData("Fixture", "var checks = new OtherChecks(token=>CheckMovementCancellationAsync(token));", false)]
    [InlineData("Fixture", "var fixture = new GodotMovementStateDiagnosticFixture(new Bindings { Load = async (path, frames, token) => { await LoadMovementStateAsync(path); } });", true)]
    [InlineData("Fixture", "var fixture = new GodotMovementStateDiagnosticFixture(new Bindings { Load = async (path, frames, token) => { _=LoadMovementStateAsync(path); } });", false)]
    [InlineData("Fixture", "var fixture = new OtherFixture(new Bindings { Load = async (path, frames, token) => { await LoadMovementStateAsync(path); } });", false)]
    public void GuardRecognizesAwaitedOrOwnedCallsRatherThanDiscardSpelling(string enclosingType, string body, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("class " + enclosingType + " { async Task Run() { " + body + " } }");
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
        if (Name(call.Expression) == "CheckMovementCancellationAsync" &&
            call.Parent is SimpleLambdaExpressionSyntax binding && ReferenceEquals(binding.Body, call) &&
            binding.Parent is ArgumentSyntax { Parent.Parent: ObjectCreationExpressionSyntax creation } && creation.Type.ToString() == "GodotSessionOperationChecks")
            return true;
        SyntaxNode expression = call;
        SyntaxNode? parent = call.Parent;
        while (parent is ParenthesizedExpressionSyntax) { expression = parent; parent = parent.Parent; }
        var function = call.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        if (function is null) return parent is AwaitExpressionSyntax;
        if (Name(call.Expression) == "LoadMovementStateAsync" && parent is AwaitExpressionSyntax &&
            function.Parent is AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax { Identifier.ValueText: "Load" },
                Parent: InitializerExpressionSyntax
                {
                    Parent: BaseObjectCreationExpressionSyntax
                    {
                        Parent: ArgumentSyntax { Parent.Parent: ObjectCreationExpressionSyntax fixture }
                    }
                }
            } && fixture.Type.ToString() == "GodotMovementStateDiagnosticFixture")
            return true;
        bool consumed = parent is AwaitExpressionSyntax or ReturnStatementSyntax ||
            function is LambdaExpressionSyntax lambda && lambda.Body == expression;
        if (!consumed || function.Parent is not ArgumentSyntax argument || argument.Parent?.Parent is not InvocationExpressionSyntax owner)
            return false;
        return owner.Expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText == "Start" && (member.Expression.ToString().EndsWith("_hostOperations", StringComparison.Ordinal) ||
                member.Expression.ToString() == "_work" && call.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText == "BugReportUiCoordinator");
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
