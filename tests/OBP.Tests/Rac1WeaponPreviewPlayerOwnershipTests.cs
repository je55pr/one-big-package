using Microsoft.CodeAnalysis;
using OBP.Game;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

/// <summary>
/// #367/#368: native preview rules and inventory grants belong to the player,
/// while root still coordinates world reset, HUD and audio. These tests protect
/// parity mode from granting preview-only items to the campaign inventory.
/// </summary>
public sealed class Rac1WeaponPreviewPlayerOwnershipTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    private static void AssertIncreasing(string text, params string[] steps)
    {
        int previous = -1;
        foreach (var step in steps)
        {
            int next = text.IndexOf(step, StringComparison.Ordinal);
            Assert.True(next > previous, "Player preview order changed: " + step);
            previous = next;
        }
    }

    [Fact]
    public void ParityPreviewGrantsOnlyIntoPreviewCopyAndReturnsToUntouchedCampaign()
    {
        var campaign = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        var original = Rac1WeaponProfiles.Additional
            .Select(p => (Item: p.Item, Owned: campaign.OwnsNativeItem((int)p.Item),
                Ammo: campaign.GetAmmo((int)p.Item)))
            .ToArray();
        var player = new PlayerSessionHost();
        player.Rac1Use.ResetSession(campaign);
        Rac1WeaponInventory Current() => player.Rac1Weapons(null, false, campaign);

        bool reset = player.UpdateRac1WeaponPreview(disable: false, Current, campaign);
        Assert.True(reset);
        var preview = Assert.IsType<Rac1WeaponInventory>(player.WeaponPreview);
        Assert.NotSame(campaign, preview);
        Assert.Same(preview, Current());
        Assert.True(preview.OwnsNativeItem(Rac1MetalDetectorSession.NativeGadgetItem));
        foreach (var snapshot in original)
        {
            Assert.Equal(snapshot.Owned, campaign.OwnsNativeItem((int)snapshot.Item));
            Assert.Equal(snapshot.Ammo, campaign.GetAmmo((int)snapshot.Item));
            Assert.True(preview.OwnsNativeItem((int)snapshot.Item));
            Assert.Equal(Rac1ItemAmmoDescriptors.Get((int)snapshot.Item).MaxAmmo,
                preview.GetAmmo((int)snapshot.Item));
        }
        Assert.True(preview.OwnsNativeItem((int)Rac1WeaponId.FirstRanged));

        Assert.True(player.UpdateRac1WeaponPreview(disable: true, Current, campaign));
        Assert.Null(player.WeaponPreview);
        Assert.Same(campaign, Current());
        foreach (var snapshot in original)
        {
            Assert.Equal(snapshot.Owned, campaign.OwnsNativeItem((int)snapshot.Item));
            Assert.Equal(snapshot.Ammo, campaign.GetAmmo((int)snapshot.Item));
        }
    }

    [Fact]
    public void RefillingSamePreviewDoesNotResetExistingNativeUseSession()
    {
        var campaign = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        var player = new PlayerSessionHost();
        player.Rac1Use.ResetSession(campaign);
        Rac1WeaponInventory Current() => player.Rac1Weapons(null, false, campaign);

        Assert.True(player.UpdateRac1WeaponPreview(disable: false, Current, campaign));
        var preview = Assert.IsType<Rac1WeaponInventory>(player.WeaponPreview);
        player.Rac1Use.ResetSession(preview);
        var activeUse = Assert.IsType<Rac1AdditionalWeaponSession>(player.Rac1Use.Session);
        Assert.True(preview.TryEquip(Rac1WeaponId.Blaster));
        var fired = activeUse.Step(true, true);
        var shot = Assert.IsType<Rac1AdditionalWeaponSession.Shot>(fired);
        Assert.Equal(Rac1WeaponId.Blaster, shot.Weapon);
        Assert.True(preview.GetAmmo((int)Rac1WeaponId.Blaster) <
            Rac1ItemAmmoDescriptors.Get((int)Rac1WeaponId.Blaster).MaxAmmo);

        bool reset = player.UpdateRac1WeaponPreview(disable: false, Current, campaign);
        Assert.False(reset);
        Assert.Same(preview, player.WeaponPreview);
        Assert.Same(activeUse, player.Rac1Use.Session);
        Assert.Equal(Rac1ItemAmmoDescriptors.Get((int)Rac1WeaponId.Blaster).MaxAmmo,
            preview.GetAmmo((int)Rac1WeaponId.Blaster));
    }

    [Fact]
    public void PlayerPreviewPolicyRunsBeforeWorldLeaseResetAndSyncInRoot()
    {
        string player = Script("PlayerSessionHost.cs");
        string preview = player[player.IndexOf("internal bool UpdateRac1WeaponPreview(", StringComparison.Ordinal)..player.IndexOf("internal Rac1WrenchCombatController Wrench", StringComparison.Ordinal)];
        AssertIncreasing(preview,
            "Native?.SetEquipmentPreview(!disable)",
            "if (disable)",
            "WeaponPreview = null",
            "WeaponPreview ??= Native?.Inventory ?? new(campaignInventory.CapturePersistentState(), currentInventory().CurrentItemId)",
            "var inventory = currentInventory()",
            "inventory.AcquireNativeItem(Rac1MetalDetectorSession.NativeGadgetItem)",
            "foreach (var weapon in Rac1WeaponProfiles.Additional",
            "inventory.AcquireNativeItem((int)weapon)",
            "inventory.GrantAmmoClamped((int)weapon, Rac1ItemAmmoDescriptors.Get((int)weapon).MaxAmmo)",
            "return Rac1Use.BoundInventory != currentInventory()");
        Assert.DoesNotContain("Godot", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", preview, StringComparison.Ordinal);

        string root = Script("OBPGame.Rac1Weapons.cs");
        string orchestrate = root[root.IndexOf("private void SetRac1WeaponPreview(", StringComparison.Ordinal)..root.IndexOf("private void AttachRac1EquipmentViews(", StringComparison.Ordinal)];
        AssertIncreasing(orchestrate,
            "if (FusionWeapons.TestActive)",
            "_persistentPlayer.UpdateRac1WeaponPreview(disable, () => _rac1Weapons, _rac1Campaign.Session.Weapons)",
            "if (resetWorld)",
            "Rac1World.Context.BombGlove = new(_rac1Weapons); ResetRac1AdditionalWeapons()",
            "MovementReproduction.Report(disable",
            "_persistentPlayer.Rac1Use.BindInventory(_rac1Weapons)",
            "FusionWeapons.SynchronizeNativeSelection()",
            "RefreshSourceEquipmentContext()",
            "HoldSelectedRac1HandItem()",
            "RefreshRac1HudState(); RefreshRac1WrenchPresentationVisibility()");
        Assert.DoesNotContain("GrantAmmoClamped", orchestrate, StringComparison.Ordinal);
        Assert.DoesNotContain("SetEquipmentPreview(!disable)", orchestrate, StringComparison.Ordinal);
    }

    [Fact]
    public void RootRetiresSoundForwardersAndBorrowsExistingAudioPresenterDirectly()
    {
        var root = HostOwnershipAudit.Host.Value.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (var name in new[]
        {
            "ResetRac1WeaponAudio", "RecordRac1WeaponSound",
        })
            Assert.Empty(root.GetMembers(name));

        string weapons = Script("OBPGame.Rac1Weapons.cs");
        string combat = Script("OBPGame.Rac1Combat.cs");
        string composition = Script("OBPGame.cs");
        Assert.Contains("_equipmentPresentation.WeaponAudio.Reset()", weapons, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.WeaponAudio.Reset()", combat, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.WeaponAudio.Reset(freeImmediately: true)", composition, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.WeaponAudio.Record(nativeClass, localSound, (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), _args.Rac1WeaponsSmoke)", composition, StringComparison.Ordinal);
        Assert.Equal(2, composition.Split("_equipmentPresentation.WeaponAudio.Record(nativeClass, localSound, (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), _args.Rac1WeaponsSmoke)").Length - 1);
        Assert.Contains("_equipmentPresentation.WeaponAudio.Step(new(", weapons, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.WeaponAudio.SetPaused(paused)", weapons, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetRac1WeaponAudio(", weapons, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordRac1WeaponSound(", weapons, StringComparison.Ordinal);
    }
}
