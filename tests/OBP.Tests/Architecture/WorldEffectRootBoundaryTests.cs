using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>Keep world-effect resources and their explicit acceptance off the Godot root.</summary>
public sealed class WorldEffectRootBoundaryTests
{
    [Fact]
    public void EffectsAreConstructedByWorldLeaseResourcesAndRetiredWithThem()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        string entry = File.ReadAllText(Path.Combine(scripts, "OBPGame.WorldEntry.cs"));
        string resources = File.ReadAllText(Path.Combine(scripts, "GodotWorldResources.cs"));

        Assert.Contains("resources.BindEffects(new GodotWorldResources.EffectBindings(", entry, StringComparison.Ordinal);
        // Verify the resolved owner and created type, not just the spelling of
        // an implicit constructor. This also catches explicit constructors,
        // aliases, and direct writes through another local resource name.
        var compilation = HostOwnershipAudit.Host.Value;
        var entryTree = Assert.Single(compilation.SyntaxTrees, tree =>
            tree.FilePath == "game/scripts/OBPGame.WorldEntry.cs");
        var semantic = compilation.GetSemanticModel(entryTree);
        var nodes = entryTree.GetRoot().DescendantNodes();
        Assert.DoesNotContain(nodes.OfType<AssignmentExpressionSyntax>(), assignment =>
            semantic.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol property &&
            property.Name == "Effects" &&
            property.ContainingType.ToDisplayString() == "OBP.Game.GodotWorldResources");
        Assert.DoesNotContain(nodes.OfType<BaseObjectCreationExpressionSyntax>(), creation =>
            semantic.GetTypeInfo(creation).Type?.ToDisplayString() == "OBP.Game.WorldEffectHost");
        Assert.Contains("var scope = EffectScope ?? throw", resources, StringComparison.Ordinal);
        Assert.Contains("Effects = new WorldEffectHost(Scene.Root, scope.WorldInstanceId, scope.Generation,", resources, StringComparison.Ordinal);
        Assert.Contains("EffectsFailure = \"Native effects setup is unavailable (\"", resources, StringComparison.Ordinal);
        Assert.Contains("finally { Effects?.Dispose(); }", resources, StringComparison.Ordinal);
        Assert.Contains("ObjectDisposedException.ThrowIf(_disposed, this);", resources, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", resources, StringComparison.Ordinal);

        int configured = entry.IndexOf("ActorHost.Configure(", StringComparison.Ordinal);
        int bound = entry.IndexOf("resources.BindEffects(", StringComparison.Ordinal);
        int hud = entry.IndexOf("EnsurePlayerHud(); EnsureWorldHud();", StringComparison.Ordinal);
        Assert.True(configured >= 0 && configured < bound && bound < hud,
            "Effects must bind after actors and before HUD activation.");
    }

    [Fact]
    public void EffectsSmokeStartsLazilyFromItsDiagnosticOwnerOnlyOnFirstExplicitWorld()
    {
        string entry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", "OBPGame.WorldEntry.cs"));
        string launcher = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "diagnostics", "GodotEffectsSmoke.cs"));

        Assert.Contains("GodotEffectsSmoke.StartIfRequested(_args, _worldSwitches, () => new(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("new EffectsExperimentScenario(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("new GodotEffectsFixture(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("new DiagnosticScenarioDefinition(\"effects-room/v1\"", entry, StringComparison.Ordinal);
        Assert.Contains("args.EffectsSmoke && !args.PlaytestRequested && worldSwitches == 1", launcher, StringComparison.Ordinal);
        Assert.Contains("if (!IsRequested(args, worldSwitches)) return;", launcher, StringComparison.Ordinal);
        Assert.Contains("var ports = bind();", launcher, StringComparison.Ordinal);
        Assert.Contains("new EffectsExperimentScenario(definition, fixture,", launcher, StringComparison.Ordinal);
        Assert.Contains("new DiagnosticSourceAttachmentFixture(ports.Sources, () => { })", launcher, StringComparison.Ordinal);
        Assert.Contains("new ExistingDiagnosticWorld(ports.ReadWorld, ports.Operations.Token)", launcher, StringComparison.Ordinal);
        Assert.Contains("DiagnosticScenarioDispatch.Start([scenario], ports.Operations, runner, ports.Quit, ports.Error);", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class OBPGame", launcher, StringComparison.Ordinal);
    }
}
