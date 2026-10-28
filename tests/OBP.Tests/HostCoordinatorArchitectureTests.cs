using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public class HostCoordinatorArchitectureTests
{
    [Fact]
    public void ActorCompositionDefersWorldContextUntilALeaseExists()
    {
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs")));
        var factory = source.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "CreateActorHost");
        var reads = factory.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == "Rac1World").ToArray();
        Assert.NotEmpty(reads);
        Assert.All(reads, read => Assert.Contains(read.Ancestors(), node => node is AnonymousFunctionExpressionSyntax));
    }

    [Fact]
    public void Rac1GameplayCoordinatorStaysSplitByHostResponsibility()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string corePath = Path.Combine(scripts, "Rac1WorldModule.cs");
        string core = File.ReadAllText(corePath);

        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac1Gameplay.cs")));
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac1Presentation.cs")));
        Assert.DoesNotContain("partial class OBPGame", core, StringComparison.Ordinal);
        Assert.True(File.ReadLines(corePath).Count() < 400);
        Assert.DoesNotContain("private void TickRac1Swing(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void StepRac1BombProjectilesPhysics(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Hostiles(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private void TickRac1Pickups(", core, StringComparison.Ordinal);

        Assert.Contains("private void TickRac1Swing(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Combat.cs")), StringComparison.Ordinal);
        Assert.Contains("private void StepRac1BombProjectilesPhysics(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Projectiles.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Hostiles(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Enemies.cs")), StringComparison.Ordinal);
        Assert.Contains("private void TickRac1Pickups(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac1Pickups.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Rac2GameplayCoordinatorKeepsAranosAndDebugHarnessSeparate()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string corePath = Path.Combine(scripts, "OBPGame.Rac2Gameplay.cs");
        string core = File.ReadAllText(corePath);

        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Crates.cs")));
        Assert.True(File.ReadLines(corePath).Count() < 400);
        Assert.DoesNotContain("private void TickRac2OpeningMsr1NativeTick(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("private CrateDebugSnapshot? GetCrateDebugSnapshot(", core, StringComparison.Ordinal);

        Assert.Contains("private void TickRac2OpeningMsr1NativeTick(",
            File.ReadAllText(Path.Combine(scripts, "OBPGame.Rac2AranosOpeningHost.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac2CrateDebugHarness.cs")));
        string probe = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/Rac2CrateDiagnosticProbe.cs"));
        Assert.Contains("Rac2CrateDebugSnapshot? Capture(", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac2FreshBoltSession", probe, StringComparison.Ordinal);

        Assert.Contains("private void ResetRac2GameplayHost(", core, StringComparison.Ordinal);
        Assert.Contains("private void ConfigureRac2GameplayHost(", core, StringComparison.Ordinal);
        Assert.Contains("private void ArmRac2GameplayHost(", core, StringComparison.Ordinal);
        Assert.DoesNotContain("CrateDebugHarness", core, StringComparison.Ordinal);
    }
}
