using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class Rac1TaxiHostOwnershipTests
{
    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));
    private static CompilationUnitSyntax Parse(string file) => CSharpSyntaxTree.ParseText(Source(file)).GetCompilationUnitRoot();
    private static MethodDeclarationSyntax Method(string file, string name) =>
        Parse(file).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name);

    [Fact]
    public void WorldLeaseOwnsTaxiMountResetTickAndRetirement()
    {
        var module = Parse("Rac1WorldModule.cs");
        var taxi = Assert.Single(module.DescendantNodes().OfType<PropertyDeclarationSyntax>(),
            property => property.Identifier.ValueText == "Taxis");
        Assert.Equal("Rac1MetropolisTaxiHost", taxi.Type.ToString());
        Assert.Contains("Taxis = new(taxis)", Source("Rac1WorldModule.cs"), StringComparison.Ordinal);
        Assert.Contains("Taxis.Configure(world, result)", Method("Rac1WorldModule.cs", "ConfigureActors").ToString(), StringComparison.Ordinal);
        Assert.Contains("Taxis.Tick(delta)", Method("Rac1WorldModule.cs", "Tick").ToString(), StringComparison.Ordinal);
        Assert.Contains("Taxis.Reset()", Method("Rac1WorldModule.cs", "ResetBindings").ToString(), StringComparison.Ordinal);
        var disposal = Method("Rac1WorldModule.cs", "Dispose").ToString();
        Assert.Contains("Taxis.AbortRide()", disposal, StringComparison.Ordinal);
        Assert.Contains("Taxis.Dispose()", disposal, StringComparison.Ordinal);
        Assert.True(disposal.IndexOf("Taxis.AbortRide()", StringComparison.Ordinal) <
            disposal.IndexOf("_binding.Dispose()", StringComparison.Ordinal),
            "The taxi must release its captured passenger before the world unbinds it.");
    }

    [Fact]
    public void TaxiCabAndPassengerStateCannotLeakIntoTheRootPartial()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.Rac1MetropolisTaxis.cs")));
        var root = Source("OBPGame.cs");
        Assert.DoesNotContain("_rac1MetropolisTaxis", root, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1TaxiNodes", root, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1TaxiCollisionBodies", root, StringComparison.Ordinal);
        Assert.Contains("new Rac1TaxiHostPorts(", root, StringComparison.Ordinal);
        var host = Parse("Rac1MetropolisTaxiHost.cs");
        Assert.DoesNotContain(host.DescendantNodes().OfType<IdentifierNameSyntax>(),
            identifier => identifier.Identifier.ValueText == "PlayerHost");
        Assert.Contains(host.DescendantNodes().OfType<FieldDeclarationSyntax>(),
            field => field.Declaration.Type.ToString().Contains("IRac1TaxiPassengerController", StringComparison.Ordinal));
        Assert.Contains("Rac1HostedTaxiCab", Source("Rac1MetropolisTaxiHost.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsUseImmutableTaxiCabSnapshotsInsteadOfHostCollections()
    {
        var diagnostic = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "diagnostics",
            "GodotRac1TaxiDiagnosticFixture.cs"));
        Assert.Contains("Host.ReadSnapshot()", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Host.NativeSession", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<int, StaticBody3D>", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<int, RuntimeWorldScene.DynamicObjectNode>", diagnostic, StringComparison.Ordinal);
        var host = Source("Rac1MetropolisTaxiHost.cs");
        Assert.Contains("ImmutableArray<Rac1TaxiCabSnapshot>", host, StringComparison.Ordinal);
        Assert.Contains("ImmutableArray<StaticBody3D> CaptureHulls()", host, StringComparison.Ordinal);
    }
}
