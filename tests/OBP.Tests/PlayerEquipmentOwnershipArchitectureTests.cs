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
        string owner = Read("PlayerSessionHost.cs");
        Assert.Contains("private PlayerEquipmentFacade? _equipment", owner, StringComparison.Ordinal);
        Assert.Contains("_equipment?.Dispose()", owner, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.RetireEquipment()", Read("OBPGame.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void FacadeUsesExistingNativeContractsAndCopiedSceneFactsWithoutPrivilegedRootAccess()
    {
        string facade = Read("PlayerEquipmentFacade.cs");
        Assert.DoesNotContain("OBPGame", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("using Godot", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldContentIdentity", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("ObpDestination", facade, StringComparison.Ordinal);
        foreach (string replacement in new[] { "new PlayerSessionNativeState", "new Rac1WeaponInventory", "new FusionWeaponSession", "new PlayerEquipmentAssignments" })
            Assert.DoesNotContain(replacement, facade, StringComparison.Ordinal);
        Assert.Contains("native.Assignments.Assign(slot", facade, StringComparison.Ordinal);
        Assert.Contains("native.Assignments.Enable(entry.Definition", facade, StringComparison.Ordinal);
        Assert.Contains("native.Held.HoldWrench()", facade, StringComparison.Ordinal);
        Assert.Contains("_persistentPlayer.Equipment.ApplyCommand(command).Accepted", Read("OBPGame.CaptureSession.cs"), StringComparison.Ordinal);
        string modal = Read("UiModalCoordinator.cs");
        Assert.DoesNotContain("PlayerSessionNativeState", modal, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerEquipmentPresentationBinder", modal, StringComparison.Ordinal);
    }
}
