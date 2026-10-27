using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

public sealed class UiModalOwnershipArchitectureTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts", file));

    private static MethodDeclarationSyntax Method(string source, string name) =>
        Assert.Single(CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == name);

    [Fact]
    public void RootModalPartialsRetireAndAllMutableModalStateBelongsToOneCoordinator()
    {
        string scripts = Path.Combine(RepoPaths.Root, "game", "scripts");
        foreach (string retired in new[] { "OBPGame.ControllerSettings.cs", "OBPGame.QuickSelect.cs" })
        {
            Assert.False(File.Exists(Path.Combine(scripts, retired)), retired);
            Assert.False(File.Exists(Path.Combine(scripts, retired + ".uid")), retired + ".uid");
        }

        var rootFields = Directory.EnumerateFiles(scripts, "OBPGame*.cs")
            .SelectMany(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot().DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)))
            .ToArray();
        Assert.Equal(1, rootFields.Count(field => field == "_modals"));
        foreach (string retired in new[] { "_pauseMenu", "_controllerSettings", "_pauseOwnsTreePause",
                     "_controllerSettingsFromPause", "_prePauseMouseMode", "_prePauseHeldButtons", "_settingsReturnFocus",
                     "_quickSelect", "_preQuickSelectMouse", "_preQuickSelectHeldButtons",
                     "_lastQuickSelectTap", "_previousSelectedWeapon", "_debugOutputEnabled" })
            Assert.DoesNotContain(retired, rootFields);

        string owner = Read("UiModalCoordinator.cs");
        var ownerRoot = CSharpSyntaxTree.ParseText(owner).GetRoot();
        var ownerFields = ownerRoot.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables.Select(variable => variable.Identifier.ValueText))
            .ToArray();
        foreach (string state in new[] { "_pause", "_settings", "_wheel", "_ownsPause",
                     "_beforePauseButtons", "_beforeWheelButtons", "_settingsReturnFocus", "_lastWheelTap",
                     "_pausePointerOpened" })
            Assert.Contains(state, ownerFields);
        var ownerProperties = ownerRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Select(property => property.Identifier.ValueText).ToArray();
        Assert.Contains("PreviousSelectedWeapon", ownerProperties);
        Assert.DoesNotContain("_playerNative", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1Weapons", owner, StringComparison.Ordinal);
    }

    [Fact]
    public void ModalBackPriorityAndWorldRetirementShareOneOwner()
    {
        string owner = Read("UiModalCoordinator.cs");
        string root = Read("OBPGame.cs");
        string back = Method(owner, "HandleBack").ToFullString();
        int wheel = back.IndexOf("if (QuickSelectOpen)", StringComparison.Ordinal);
        int settings = back.IndexOf("if (SettingsOpen)", StringComparison.Ordinal);
        int pause = back.IndexOf("if (PauseOpen)", StringComparison.Ordinal);
        Assert.True(wheel >= 0 && wheel < settings && settings < pause);
        Assert.Contains("Modals.HandleBack()", Method(root, "TryHandleInteractiveBack").ToFullString(), StringComparison.Ordinal);

        string retirement = Method(owner, "RetireAll").ToFullString();
        Assert.Contains("CloseQuickSelect(retiring: true)", retirement, StringComparison.Ordinal);
        Assert.Contains("DiscardSettings(restoreParent: false)", retirement, StringComparison.Ordinal);
        Assert.Contains("ReleasePauseLease()", retirement, StringComparison.Ordinal);
        Assert.Contains("TearDownInteractiveOverlays()", Method(root, "RetireWorldBindings").ToFullString(), StringComparison.Ordinal);
        Assert.Contains("_modals?.RetireAll()", Method(root, "ReleaseAudioForExit").ToFullString(), StringComparison.Ordinal);
        Assert.Contains("_modals?.RetireAll()", Method(root, "PrepareForQuitAsync").ToFullString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PauseLeaseAndQuickSelectOwnershipRemainDistinct()
    {
        string owner = Read("UiModalCoordinator.cs");
        string pause = Method(owner, "OpenPause").ToFullString();
        string release = Method(owner, "ReleasePauseLease").ToFullString();
        string wheel = Method(owner, "OpenQuickSelect").ToFullString();
        string closeWheel = Method(owner, "CloseQuickSelect").ToFullString();

        Assert.Contains("_ports.Host.GetTree().Paused = true", pause, StringComparison.Ordinal);
        Assert.Contains("if (_ownsPause)", release, StringComparison.Ordinal);
        Assert.Contains("_ports.Host.GetTree().Paused = false", release, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTree().Paused =", wheel, StringComparison.Ordinal);
        Assert.Contains("player.QuickSelectOwnsGameplayInput = true", wheel, StringComparison.Ordinal);
        Assert.Contains("player.QuickSelectOwnsGameplayInput = false", closeWheel, StringComparison.Ordinal);
        Assert.Contains("SuppressButtonsUntilRelease(_beforeWheelButtons)", closeWheel, StringComparison.Ordinal);
        Assert.Contains("UiPointerModality.MenuClosed(_beforePauseMouse)", release, StringComparison.Ordinal);
        Assert.Contains("_pausePointerOpened = true", pause, StringComparison.Ordinal);
        Assert.Contains("_pausePointerOpened", release, StringComparison.Ordinal);
        Assert.True(pause.IndexOf("UiPointerModality.MenuOpened()", StringComparison.Ordinal) <
            pause.IndexOf("_pausePointerOpened = true", StringComparison.Ordinal));
        Assert.Contains("MenuFocus.Restore(_settingsReturnFocus", owner, StringComparison.Ordinal);
        string failedSettings = Method(owner, "OpenSettings").ToFullString();
        Assert.Contains("RestoreSettingsParent()", failedSettings, StringComparison.Ordinal);
        Assert.Contains("MenuFocus.Restore(_settingsReturnFocus", failedSettings, StringComparison.Ordinal);
        Assert.Contains("ApplicationPointer.MouseMode = _beforeSettingsMouse", failedSettings, StringComparison.Ordinal);
        Assert.True(wheel.IndexOf("_beforeWheelMouse = ApplicationPointer.MouseMode", StringComparison.Ordinal) <
            wheel.IndexOf("equipment.WheelEntries()", StringComparison.Ordinal));
        Assert.True(wheel.IndexOf("_beforeWheelButtons = player.MenuHeldButtons", StringComparison.Ordinal) <
            wheel.IndexOf("equipment.WheelEntries()", StringComparison.Ordinal));
        Assert.True(wheel.IndexOf("equipment.WheelEntries()", StringComparison.Ordinal) <
            wheel.IndexOf("new WeaponQuickSelectUi", StringComparison.Ordinal));
    }

    [Fact]
    public void ModalEquipmentChangesUseTypedPortsAndKeepDistinctRetailAndUniversalPresenters()
    {
        string ports = Read("UiModalPorts.cs");
        string owner = Read("UiModalCoordinator.cs");
        string composition = Read("OBPGame.cs");
        Assert.Contains("record ModalEquipmentPorts(", ports, StringComparison.Ordinal);
        Assert.Contains("equipment.SelectWeapon(weapon)", owner, StringComparison.Ordinal);
        Assert.Contains("equipment.WheelEntries()", owner, StringComparison.Ordinal);
        Assert.Contains("equip.AssignNativeSlot", composition, StringComparison.Ordinal);
        Assert.Contains("CatalogEquip = equip.Equip", composition, StringComparison.Ordinal);
        Assert.Contains("RetailEnabled = !OrdinaryPlayerSession", composition, StringComparison.Ordinal);
        Assert.Contains("CatalogReader = OrdinaryPlayerSession ? equip.Catalog", composition, StringComparison.Ordinal);
    }
}
