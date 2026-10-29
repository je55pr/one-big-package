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
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac1Enemies.cs")));
        Assert.Contains("internal void TickRac1Hostiles(",
            File.ReadAllText(Path.Combine(scripts, "Rac1EnemyHost.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac1Pickups.cs")));
        Assert.Contains("internal void TickRac1Pickups(",
            File.ReadAllText(Path.Combine(scripts, "Rac1LootPresentation.Pickups.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Rac2GameplayAndAranosAreLeaseOwnedAndDebugFixturesStayExternal()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string corePath = Path.Combine(scripts, "Rac2WorldModule.cs");
        string core = File.ReadAllText(corePath);
        string aranos = File.ReadAllText(Path.Combine(scripts, "Rac2AranosOpeningSessionAdapter.cs"));
        string resources = File.ReadAllText(Path.Combine(scripts, "GodotWorldResources.cs"));
        string actors = File.ReadAllText(Path.Combine(scripts, "ActorNativeAdapters.cs"));
        string app = File.ReadAllText(Path.Combine(scripts, "OBPGame.cs"));
        string worldEntry = File.ReadAllText(Path.Combine(scripts, "OBPGame.WorldEntry.cs"));

        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac2Gameplay.cs")));
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac2AranosOpeningHost.cs")));
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Crates.cs")));
        Assert.False(File.Exists(Path.Combine(scripts, "OBPGame.Rac2CrateDebugHarness.cs")));
        Assert.DoesNotContain("partial class OBPGame", core, StringComparison.Ordinal);
        Assert.Contains("internal void Configure()", core, StringComparison.Ordinal);
        Assert.Contains("internal void Arm(PlayerHost player)", core, StringComparison.Ordinal);
        Assert.Contains("public void Dispose()", core, StringComparison.Ordinal);
        Assert.Contains("if (!Current)", aranos, StringComparison.Ordinal);
        Assert.Contains("private void TickRac2OpeningMsr1NativeTick(", aranos, StringComparison.Ordinal);
        Assert.Contains("public Rac2WorldModule? Rac2Gameplay", resources, StringComparison.Ordinal);
        Assert.Contains("Rac2Gameplay?.Dispose()", resources, StringComparison.Ordinal);
        Assert.Contains("if (_player is { } player) resources.Rac2Gameplay?.Arm(player);", worldEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("_worldScenes?.Current?.Resources.Rac2Gameplay?.Arm(player)", app, StringComparison.Ordinal);
        Assert.Contains("internal bool Current => _clock.Current;", core, StringComparison.Ordinal);
        Assert.Contains("if (!_clock.SourceReadyForActivation) return;", core, StringComparison.Ordinal);
        Assert.Contains("_clock.Retire();", core, StringComparison.Ordinal);
        Assert.Contains("_player.CrateStrikeRequested -= OnRac2PrimaryAttackRequested;", core, StringComparison.Ordinal);
        Assert.Contains("_clock.Advance(delta, _ports.Ordinary, TickRac2OpeningLiftNativeTick,", core, StringComparison.Ordinal);
        Assert.Contains("TickRac2OpeningDoorNativeTick, TickRac2Hostiles);", core, StringComparison.Ordinal);
        Assert.Contains("new Rac2Msr1ActorFactory(scenes, sources, Rac2Bindings())", actors, StringComparison.Ordinal);
        Assert.Contains("world.TickOpeningMsr1FromActor();", actors, StringComparison.Ordinal);
        Assert.Contains("if (!world.Current) return;", actors, StringComparison.Ordinal);
        Assert.DoesNotContain("TickRac2Gameplay(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac2Class2827Hostiles", app, StringComparison.Ordinal);

        string probe = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/Rac2CrateDiagnosticProbe.cs"));
        Assert.Contains("Rac2CrateDebugSnapshot? Capture(", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac2FreshBoltSession", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("CrateDebugHarness", core, StringComparison.Ordinal);
    }
}
