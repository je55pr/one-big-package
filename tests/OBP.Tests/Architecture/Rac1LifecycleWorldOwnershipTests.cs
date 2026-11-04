using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>Structural checks for source-scoped retry/environment callback ownership.</summary>
public sealed class Rac1LifecycleWorldOwnershipTests
{
    private static string Script(string filename) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", filename));

    private static string Method(string file, string name) =>
        HostOwnershipAudit.Host.Value.SyntaxTrees
            .Single(tree => tree.FilePath == "game/scripts/" + file).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == name).ToString();

    [Fact]
    public void RetryAndEnvironmentalContactLiveInsideTheCurrentWorldLease()
    {
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.Rac1Lifecycle.cs")));
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        Assert.Empty(root.GetMembers("_rac1CombatRetry"));
        var module = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.Rac1WorldModule")!;
        Assert.Equal("Rac1LifecycleWorldHost",
            Assert.Single(module.GetMembers("Lifecycle").OfType<IPropertySymbol>()).Type.Name);
        Assert.Contains("Lifecycle = new(world, Context, Presentation, lifecyclePorts)",
            Script("Rac1/World/Rac1WorldModule.cs"), StringComparison.Ordinal);
        Assert.Contains("Lifecycle.Reset()", Method("Rac1/World/Rac1WorldModule.cs", "ResetBindings"), StringComparison.Ordinal);
        Assert.Contains("Lifecycle.Dispose()", Method("Rac1/World/Rac1WorldModule.cs", "Dispose"), StringComparison.Ordinal);
        Assert.Contains("Lifecycle.TryCompleteEnvironmentalRestart(automatic)",
            CompositionSources.Application, StringComparison.Ordinal);
        Assert.Contains("Lifecycle.TickEnvironmentalDeath()", CompositionSources.Application, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartRequiresParityAndCurrentWorldBeforeMutatingNativeHealth()
    {
        string restart = Method("Rac1/Campaign/Rac1LifecycleWorldHost.cs", "TryCompleteEnvironmentalRestart");
        int guard = restart.IndexOf("_disposed || !ports.WorldStillCurrent() || ports.Ordinary()", StringComparison.Ordinal);
        int mutation = restart.IndexOf("ports.Health().Respawn()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && mutation > guard);
        Assert.Contains("ResolveEnvironmentalRestart()", restart, StringComparison.Ordinal);
        Assert.Contains("ports.ResetHeldItem()", restart, StringComparison.Ordinal);
        Assert.Contains("player.ApplyRecoveredRac1Restart(restart.Placement)", restart, StringComparison.Ordinal);

        string admission = Method("Rac1/Campaign/Rac1LifecycleWorldHost.cs", "ResolveEnvironmentalRestart");
        Assert.Contains("world.Game != ObpSourceGame.Rac1", admission, StringComparison.Ordinal);
        Assert.Contains("checkpoint.NativeLevelId != world.LevelId", admission, StringComparison.Ordinal);
        Assert.Contains("death.HasRecoveredEnvironmentalRespawn", admission, StringComparison.Ordinal);
        Assert.Contains("return checkpoint.ResolveEnvironmentalRestart()", admission, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentalDeathAndContactPreserveOriginalSourceInputsAndThresholdRay()
    {
        string death = Method("Rac1/Campaign/Rac1LifecycleWorldHost.cs", "TickEnvironmentalDeath");
        Assert.Contains("world.LevelId != 0", death, StringComparison.Ordinal);
        Assert.Contains("world.Environment is not { } environment", death, StringComparison.Ordinal);
        Assert.Contains("NativeVerticalPosition: player.GlobalPosition.Y", death, StringComparison.Ordinal);
        Assert.Contains("DeathHeight: environment.DeathHeight", death, StringComparison.Ordinal);
        Assert.Contains("NativeSpecialPlayerState20A4: context.NativePlayerState20A4", death,
            StringComparison.Ordinal);
        Assert.Contains("context.LastEnvironmentalDeathBoundary = dead", death, StringComparison.Ordinal);

        string ray = Method("Rac1/Campaign/Rac1LifecycleWorldHost.cs", "MeasureContactSeparation");
        Assert.Contains("Vector3.Up * 0.1f", ray, StringComparison.Ordinal);
        Assert.Contains("Vector3.Down * 1024f", ray, StringComparison.Ordinal);
        Assert.Contains("player.GetRid()", ray, StringComparison.Ordinal);
        Assert.Contains("double.PositiveInfinity", ray, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredRetryButtonCannotNavigateAfterWorldSwap()
    {
        string retry = Method("Rac1/Campaign/Rac1LifecycleWorldHost.cs", "ShowCombatRetry");
        Assert.Contains("ports.ActiveDestination() is not { } destination", retry, StringComparison.Ordinal);
        int guard = retry.LastIndexOf("if (_disposed || !ports.WorldStillCurrent()) return;", StringComparison.Ordinal);
        int navigation = retry.IndexOf("ports.Navigate(destination)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && navigation > guard);
        Assert.Contains("ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible", retry,
            StringComparison.Ordinal);
        Assert.Contains("ports.ResetPlayerRetry()", Method("Rac1/Campaign/Rac1LifecycleWorldHost.cs", "Reset"),
            StringComparison.Ordinal);
    }
}
