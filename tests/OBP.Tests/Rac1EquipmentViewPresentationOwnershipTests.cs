using Microsoft.CodeAnalysis;

namespace OBP.Tests;

/// <summary>
/// #367/#368: R&C1 view/model lifetimes are owned by the existing player
/// equipment presentation binder, rather than by OBPGame's root partial.
/// Native weapon inventory, animation pose clock, and world damage are borrowed.
/// </summary>
public sealed class Rac1EquipmentViewPresentationOwnershipTests
{
    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "scripts", name));

    private static void AssertIncreasing(string text, params string[] members)
    {
        int previous = -1;
        foreach (var member in members)
        {
            int current = text.IndexOf(member, StringComparison.Ordinal);
            Assert.True(current > previous, "Equipment-view presentation order changed: " + member);
            previous = current;
        }
    }

    [Fact]
    public void EquipmentViewsAndPoseGenerationsRetireFromRootIntoExistingPresentationOwner()
    {
        var compilation = HostOwnershipAudit.Host.Value;
        var root = compilation.GetTypeByMetadataName("OBP.Game.OBPGame")!;
        foreach (string member in new[]
        {
            "_rac1EquipmentAssets", "_rac1EquipmentSource", "_rac1EquipmentMaterials",
            "_rac1EquipmentViews", "_rac1StagedEquipment", "_rac1WeaponPoseGeneration",
        })
            Assert.Empty(root.GetMembers(member));

        var binder = compilation.GetTypeByMetadataName("OBP.Game.PlayerEquipmentPresentationBinder")!;
        Assert.Equal("Rac1EquipmentViewPresentation",
            Assert.Single(binder.GetMembers("NativeViews").OfType<IPropertySymbol>()).Type.Name);
        string composition = Script("PlayerEquipmentPresentationBinder.cs");
        string views = Script("Rac1EquipmentViewPresentation.cs");
        Assert.Contains("internal Rac1EquipmentViewPresentation NativeViews { get; } = new()", composition, StringComparison.Ordinal);
        Assert.Contains("NativeViews.RetireWorld()", composition, StringComparison.Ordinal);
        Assert.Contains("NativeViews.InvalidateSource()", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("OBPGame", views, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1WeaponInventory(", views, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1AdditionalWeaponSession(", views, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "game/scripts/Rac1EquipmentViewPresentation.cs.uid")));
    }

    [Fact]
    public void ModelsAndStagedEquipmentPreserveSourceCacheAndWorldAttachmentOrder()
    {
        string views = Script("Rac1EquipmentViewPresentation.cs");
        string attach = views[views.IndexOf("internal void Attach(", StringComparison.Ordinal)..views.IndexOf("internal void PlaySelection(", StringComparison.Ordinal)];

        AssertIncreasing(attach,
            "if (_assets is null || _sourcePath != sourcePath)",
            "_assets = Rac1EquipmentAssets.Load(sourcePath)",
            "_sourcePath = sourcePath",
            "_materials = new(world with { Textures = _assets.Textures })",
            "ClearViews()",
            "foreach (var profile in Rac1WeaponProfiles.Additional)",
            "new Rac1EquipmentView(_assets.Models[profile.ModelClass], _materials, avatar)",
            "avatarView.AddChild(view); view.Visible = false; _views.Add(profile.Item, view)",
            "new Rac1EquipmentView(_assets.Models[profile.Item == Rac1WeaponId.MineGlove ? 74 : 230], _materials)",
            "view.AddChild(staged); staged.Rotation = new(0, MathF.PI, 0)",
            "staged.Play(0)",
            "_staged.Add(profile.Item, staged)");

        Assert.Contains("Rac1ArsenalRules.DoomCanisterHeldScale", views, StringComparison.Ordinal);
        Assert.Contains("internal void ClearViews()", views, StringComparison.Ordinal);
        Assert.Contains("_views.Clear()", views, StringComparison.Ordinal);
        Assert.Contains("_staged.Clear()", views, StringComparison.Ordinal);
        Assert.Contains("_assets = null", views, StringComparison.Ordinal);
        Assert.Contains("_sourcePath = null", views, StringComparison.Ordinal);
        Assert.Contains("_materials = null", views, StringComparison.Ordinal);
        string root = Script("OBPGame.Rac1Weapons.cs");
        Assert.Contains("NativeViews.Attach(Sources.Setup.Library.Get(ObpSourceGame.Rac1)!.Path", root, StringComparison.Ordinal);
    }

    [Fact]
    public void SourcePosePlaybacksAndNativeSoundRoutingRetainHandSamplingClock()
    {
        string views = Script("Rac1EquipmentViewPresentation.cs");
        Assert.Contains("view.Play(0, false, ++_poseGeneration, 1)", views, StringComparison.Ordinal);
        Assert.Contains("view.Play(emitting ? 3 : 5, false, ++_poseGeneration, emitting ? 4 : 1)", views, StringComparison.Ordinal);
        Assert.Contains("view.Play(Rac1ArsenalRules.HandFireSequence(shot.Weapon), false, shot.Generation)", views, StringComparison.Ordinal);
        Assert.Contains("Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter or Rac1WeaponId.TeslaClaw", views, StringComparison.Ordinal);
        Assert.Contains("view.RefreshAttachment(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(profile.HandSemantic)))", views, StringComparison.Ordinal);

        string step = views[views.IndexOf("internal void Step(", StringComparison.Ordinal)..views.IndexOf("internal Vector3 Muzzle(", StringComparison.Ordinal)];
        AssertIncreasing(step,
            "_views.TryGetValue(profile.Item, out var view)",
            "bool isVisible = visible(profile.Item)",
            "view.Visible = isVisible",
            "if (!isVisible) continue",
            "view.Play(useTicks > 0 ? 4 : 1)",
            "view.Play(arsenalHeld ? 3 : 1)",
            "if (native?.Vacuuming == true) view.Play(3)",
            "view.Step(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(profile.HandSemantic)))",
            "bank?.Sounds?.TryGetValue((profile.ModelClass, sound), out var voice)",
            "playNativeSound(profile.ModelClass, sound, player.GlobalPosition)",
            "staged.Position = view.MuzzleLocal; staged.Step()",
            "staged.Visible = inventory.GetAmmo((int)profile.Item) > 0");
        Assert.Contains("native?.MineRearmTicks == 0", step, StringComparison.Ordinal);

        string root = Script("OBPGame.Rac1Weapons.cs");
        Assert.Contains("_player!, FusionWeapons.PlayNativeSound", root, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets)", root, StringComparison.Ordinal);
        Assert.Contains("Rac1World.Loot.PickupAssets", root, StringComparison.Ordinal);
    }

    [Fact]
    public void EquipmentMuzzleKeepsAvatarSocketRayAndNativeFallbackWithNoWorldOwnership()
    {
        string views = Script("Rac1EquipmentViewPresentation.cs");
        string muzzle = views[views.IndexOf("internal Vector3 Muzzle(", StringComparison.Ordinal)..views.IndexOf("internal Rac1EquipmentView CreateWorldView(", StringComparison.Ordinal)];

        AssertIncreasing(muzzle,
            "_views.TryGetValue(weapon, out var view)",
            "avatar.TryEquipmentMuzzle(view, socket, out var muzzle)",
            "var start = player.GlobalPosition + Vector3.Up * .8f",
            "PhysicsRayQueryParameters3D.Create(start, muzzle)",
            "ray.Exclude = [player.GetRid()]",
            "player.GetWorld3D().DirectSpaceState.IntersectRay(ray)",
            "point.AsVector3() - (muzzle - start).Normalized() * .02f",
            "return player.GlobalPosition + Vector3.Up * .9f + forward * .7f");
        Assert.Contains("new Rac1EquipmentView(_assets!.Models[nativeClass], _materials!)", views, StringComparison.Ordinal);
        Assert.Contains("root.AddChild(view); return view", views, StringComparison.Ordinal);
        string root = Script("OBPGame.Rac1Weapons.cs");
        Assert.Contains("NativeViews.Muzzle(weapon, socket, _avatarPresentation, _player!, EquipmentForward)", root, StringComparison.Ordinal);
        Assert.Contains("NativeViews.CreateWorldView(nativeClass, _sceneResult!.Root)", root, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicsRayQueryParameters3D.Create(start, muzzle)", root, StringComparison.Ordinal);
    }

    [Fact]
    public void RootKeepsWorldDispatchOrderAndAvatarViewRetirementAtExistingBoundary()
    {
        string weapons = Script("OBPGame.Rac1Weapons.cs");
        string step = weapons[weapons.IndexOf("private void StepRac1AdditionalWeapons()", StringComparison.Ordinal)..weapons.IndexOf("private void StepRac1EquipmentViews(", StringComparison.Ordinal)];
        AssertIncreasing(step,
            "var frame = use.BeginFrame(",
            "if (frame.PyroChanged) _equipmentPresentation.NativeViews.PlayPyrocitor(frame.Pyro)",
            "if (frame.Shot is { } shot)",
            "use.RecordShot(shot)",
            "_equipmentPresentation.NativeViews.PlayShot(shot)",
            "Rac1World.Weapons.Projectiles.Throw(shot)",
            "Rac1World.Weapons.Projectiles.FireBlaster(shot)",
            "Rac1World.Weapons.SuckCannon.Fire(shot)",
            "Rac1World.Weapons.Arsenal.Fire(shot.Weapon)",
            "PersistRac1CampaignState(",
            "RefreshRac1HudState()",
            "use.CompleteFrame(frame, _rac1Weapons)",
            "StepRac1WeaponAudio()");

        string combat = Script("OBPGame.Rac1Combat.cs");
        Assert.Contains("_equipmentPresentation.NativeViews.PlaySelection(weapon)", combat, StringComparison.Ordinal);
        string entry = Script("OBPGame.WorldEntry.cs");
        AssertIncreasing(entry,
            "_avatarPresentation.Retire()",
            "_equipmentPresentation.NativeViews.ClearViews()",
            "_equipmentPresentation.ClearBindings()");
        Assert.Contains("NativeViews.RefreshAttachment(_rac1Weapons.Equipped, controller)", entry, StringComparison.Ordinal);
        Assert.Contains("NativeViews.RefreshVisibility(IsRac1HandEquipmentVisible)", entry, StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.NativeViews.Assets", Script("OBPGame.cs"), StringComparison.Ordinal);
        Assert.Contains("_equipmentPresentation.NativeViews.View(weapon)", Script("OBPGame.cs"), StringComparison.Ordinal);
    }
}
