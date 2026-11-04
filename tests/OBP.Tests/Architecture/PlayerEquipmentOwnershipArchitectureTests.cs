using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class PlayerEquipmentOwnershipArchitectureTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts", name));

    [Fact]
    public void EquipmentPartialRetiresAndThePlayerOwnerKeepsTheOnlyFacade()
    {
        string path = Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.PlayerEquipment.cs");
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".uid"));
        var root = CSharpSyntaxTree.ParseText(Read("OBPGame.cs")).GetRoot();
        var wrappers = new Dictionary<string, string>
        {
            ["ReadPlayerQuickSlots"] = "ReadQuickSlots",
            ["ReadPlayerEquipment"] = "Read",
            ["SelectPlayerEquipment"] = "Select",
            ["AssignPlayerQuickSelect"] = "AssignQuickSelect",
            ["UnequipPlayerEquipment"] = "Unequip",
            ["SetPlayerGadgetEnabled"] = "SetGadgetEnabled",
        };
        foreach (var (entrypoint, action) in wrappers)
        {
            var method = Assert.Single(root.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == entrypoint);
            Assert.Null(method.Body);
            var call = Assert.IsType<InvocationExpressionSyntax>(method.ExpressionBody!.Expression);
            Assert.Equal("_persistentPlayer.Equipment." + action, call.Expression.ToString());
        }
        Assert.DoesNotContain(root.DescendantNodes().OfType<FieldDeclarationSyntax>(), field => field.Declaration.Type.ToString() == "PlayerEquipmentFacade");
        string owner = Read("Player/PlayerSessionHost.cs");
        Assert.Contains("private PlayerEquipmentFacade? _equipment", owner, StringComparison.Ordinal);
        Assert.Contains("_equipment?.Dispose()", owner, StringComparison.Ordinal);
        Assert.Contains("persistent.RetireEquipment()", Read("Application/GodotApplicationShutdown.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationExitRetiresEquipmentEvenIfAudioCleanupThrows()
    {
        var root = CSharpSyntaxTree.ParseText(Read("OBPGame.cs")).GetRoot();
        var exit = Assert.Single(root.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == "_ExitTree");
        Assert.Null(exit.Body);
        string forwarding = exit.ExpressionBody!.ToString();
        Assert.Contains("GodotApplicationShutdown.Exit(ReleaseAudioForExit", forwarding, StringComparison.Ordinal);
        Assert.Contains("() => PlayerServices.LifeIfCreated, () => PlayerServices.FusionIfCreated", forwarding, StringComparison.Ordinal);
        Assert.Contains("ApplicationLifecycle.ReportRootExit(Name)", forwarding, StringComparison.Ordinal);
        var shutdown = CSharpSyntaxTree.ParseText(Read("Application/GodotApplicationShutdown.cs")).GetRoot();
        var helper = Assert.Single(shutdown.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == "Exit");
        var cleanup = Assert.Single(helper.Body!.Statements.OfType<TryStatementSyntax>());
        Assert.Contains("releaseAudio()", cleanup.Block.ToString(), StringComparison.Ordinal);
        var retirement = Assert.IsType<FinallyClauseSyntax>(cleanup.Finally);
        // Resolve lazily created services only after releaseAudio has completed or thrown.
        Assert.DoesNotContain("player()", cleanup.Block.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("weapons()", cleanup.Block.ToString(), StringComparison.Ordinal);
        Assert.Contains("player()?.Dispose()", retirement.Block.ToString(), StringComparison.Ordinal);
        Assert.Contains("weapons()?.Dispose()", retirement.Block.ToString(), StringComparison.Ordinal);
        Assert.Contains("persistent.RetireEquipment()", retirement.Block.ToString(), StringComparison.Ordinal);
        Assert.Contains("ApplicationLifecycle.SetQuitPreparation(null)", retirement.Block.ToString(), StringComparison.Ordinal);
        Assert.Contains("reportExit()", retirement.Block.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FacadeUsesExistingNativeContractsAndCopiedSceneFactsWithoutPrivilegedRootAccess()
    {
        string facade = Read("Player/PlayerEquipmentFacade.cs");
        Assert.DoesNotContain("OBPGame", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("using Godot", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldContentIdentity", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("ObpDestination", facade, StringComparison.Ordinal);
        foreach (string replacement in new[] { "new PlayerSessionNativeState", "new Rac1WeaponInventory", "new FusionWeaponSession", "new PlayerEquipmentAssignments" })
            Assert.DoesNotContain(replacement, facade, StringComparison.Ordinal);
        Assert.Contains("native.Assignments.Assign(slot", facade, StringComparison.Ordinal);
        Assert.Contains("native.Assignments.Enable(entry.Definition", facade, StringComparison.Ordinal);
        Assert.Contains("native.Held.HoldWrench()", facade, StringComparison.Ordinal);
        string capture = Read("CaptureReplay/CaptureReplayCoordinator.cs");
        Assert.Contains("actions.Equip(command.Value)", capture, StringComparison.Ordinal);
        Assert.Contains("actions.QuickSlot(command.Slot", capture, StringComparison.Ordinal);
        string composition = CompositionSources.Application;
        Assert.Contains("item => SelectPlayerEquipment(item).Accepted", composition, StringComparison.Ordinal);
        Assert.Contains("(slot, item) => AssignPlayerQuickSelect(slot, item).Accepted", composition, StringComparison.Ordinal);
        Assert.Contains("Record: command => Capture.Replay.RecordCommand(command)", composition, StringComparison.Ordinal);
        string modal = Read("UI/UiModalCoordinator.cs");
        Assert.DoesNotContain("PlayerSessionNativeState", modal, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerEquipmentPresentationBinder", modal, StringComparison.Ordinal);
    }
}
