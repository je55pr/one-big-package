using Microsoft.CodeAnalysis;

namespace OBP.Tests;

public sealed class ReplayDriverArchitectureTests
{
    [Theory]
    [InlineData("PlaytestDriver")]
    [InlineData("PlaytestEndDriver")]
    public void ReplayCallbacksAreStandaloneScripts(string name)
    {
        var type = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game." + name)!;
        Assert.Null(type.ContainingType);
        Assert.Contains(type.DeclaringSyntaxReferences, reference => Path.GetFileName(reference.SyntaxTree.FilePath) == name + ".cs");
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.PlaytestDriver.cs")));
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.PlaytestBugReports.cs")));
    }

    [Fact]
    public void ReplayPortsExposeFocusedActionsAndObservationsWithoutRootOrSceneNodes()
    {
        var host = HostOwnershipAudit.Host.Value;
        foreach (var name in new[] { "PlaytestDriverPorts", "PlaytestDiagnosticActions", "PlaytestReportActions" })
        {
            var type = host.GetTypeByMetadataName("OBP.Game." + name)!;
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>()) Check(property.Type);
        }
        var adapter = host.GetTypeByMetadataName("OBP.Game.GodotPlaytestHost")!;
        foreach (var member in adapter.GetMembers().Where(member => member.DeclaredAccessibility == Accessibility.Internal))
        {
            if (member is IPropertySymbol property) Check(property.Type);
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                Check(method.ReturnType);
                foreach (var parameter in method.Parameters) Check(parameter.Type);
            }
        }
    }

    private static void Check(ITypeSymbol type)
    {
        Assert.DoesNotContain(type.Name, new[] { "OBPGame", "PlayerHost", "CaptureDiagnosticHarness", "BugReportUiCoordinator", "PauseMenuUi" });
        for (var current = type; current is not null; current = current.BaseType)
            Assert.NotEqual("Godot.Node", current.ToDisplayString());
        if (type is INamedTypeSymbol named)
            foreach (var argument in named.TypeArguments) Check(argument);
    }
}
