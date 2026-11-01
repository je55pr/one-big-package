namespace OBP.Tests;

/// <summary>Guards #376's removal of hand presentation policy from the Godot root.</summary>
public sealed class Rac1HandPresentationOwnershipArchitectureTests
{
    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", file));

    [Fact]
    public void HandVisibilityAndAttachmentMutationBelongToTheExistingEquipmentBinder()
    {
        string entry = Script("OBPGame.WorldEntry.cs");
        string root = Script("OBPGame.cs");
        string binder = Script("PlayerEquipmentPresentationBinder.cs");

        Assert.DoesNotContain("private void HoldSelectedRac1HandItem(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private void RefreshRac1HeldItem(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool IsRac1HandEquipmentVisible(", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("private void RefreshRac1WrenchPresentationVisibility(", entry, StringComparison.Ordinal);

        Assert.Contains("void HoldSelectedRac1HandItem(", binder);
        Assert.Contains("void RefreshRac1HeldItem(", binder);
        Assert.Contains("static bool IsRac1HandEquipmentVisible(", binder);
        Assert.Contains("void RefreshRac1HandVisibility(", binder);
        Assert.Contains("state.Held.HoldSelected(state.Inventory)", binder);
        Assert.Contains("NativeViews.RefreshAttachment(state.Inventory.Equipped, controller)", binder);
        Assert.Contains("NativeViews.RefreshVisibility(", binder);
        Assert.Contains("avatar.GloveView.Visible = bombVisible", binder);
        Assert.Contains("avatar.StagedBombView.Visible = bombVisible && state.Inventory.FirstRangedAmmo > 0", binder);

        Assert.Contains("_equipmentPresentation.HoldSelectedRac1HandItem(Rac1HandPresentation, _avatarPresentation)", root);
        Assert.Contains("_equipmentPresentation.RefreshRac1HeldItem(previous, Rac1HandPresentation, _avatarPresentation)", root);
        Assert.Contains("_equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation)", root);
        Assert.Contains("PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(", entry);
    }

    [Fact]
    public void WorldFlagsAreBorrowedWithoutMovingInventoriesOrNativeWorldOwnership()
    {
        string root = Script("OBPGame.cs");
        string binder = Script("PlayerEquipmentPresentationBinder.cs");

        Assert.Contains("private Rac1HandPresentationContext Rac1HandPresentation => new(", root);
        Assert.Contains("_rac1HeldItem, _rac1Weapons, FusionWeapons.Session", root);
        Assert.Contains("_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true", root);
        Assert.Contains("state.Fusion?.Selected.SourceGame is not (ObpSourceGame.Rac2 or ObpSourceGame.Rac3)", binder);
        Assert.Contains("!state.WaterHandItemHidden && !state.WrenchFrameActive && !state.WrenchThrowActive", binder);
        Assert.DoesNotContain("new Rac1WeaponInventory(", binder, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1HeldItemSession(", binder, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", binder, StringComparison.Ordinal);
    }
}
