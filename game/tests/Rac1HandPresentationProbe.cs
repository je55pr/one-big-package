using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Source-free Godot regression for native equipment and avatar-node visibility.</summary>
public partial class Rac1HandPresentationProbe : Node
{
    public override void _Ready()
    {
        try
        {
            var inventory = new Rac1WeaponInventory(ownsFirstRanged: true,
                equipped: Rac1WeaponId.FirstRanged, firstRangedAmmo: 6);
            var held = new Rac1HeldItemSession();
            var binder = new PlayerEquipmentPresentationBinder();
            var avatar = new PlayerAvatarPresentationHost();
            var state = new Rac1HandPresentationContext(held, inventory, null, false, false, false);

            // Install a real native view without loading retail assets. Keep the
            // view registry private in production; this is a probe-only fixture.
            var view = new Rac1EquipmentView(SyntheticModel(), new WorldMaterialFactory(SyntheticWorld()));
            AddChild(view);
            var registry = typeof(Rac1EquipmentViewPresentation)
                .GetField("_views", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(binder.NativeViews) as Dictionary<Rac1WeaponId, Rac1EquipmentView>
                ?? throw new InvalidOperationException("Native equipment view registry was not found.");
            registry.Add(Rac1WeaponId.Blaster, view);

            var glove = new Node3D { Name = "FixtureGlove" };
            var stagedBomb = new Node3D { Name = "FixtureStagedBomb" };
            var wrench = new Node3D { Name = "FixtureWrench" };
            AddChild(glove);
            AddChild(stagedBomb);
            AddChild(wrench);

            void Refresh(Rac1HandPresentationContext current)
            {
                // The binder's production method updates registered native views
                // and calls the same node implementation for avatar-owned views.
                binder.RefreshRac1HandVisibility(current, avatar);
                PlayerEquipmentPresentationBinder.RefreshRac1HandNodes(current, glove, stagedBomb, wrench);
            }

            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged, state),
                "An undrawn first-ranged item must stay hidden.");
            Refresh(state);
            Require(!glove.Visible && !stagedBomb.Visible && wrench.Visible && !view.Visible,
                "Initial wrench state did not hide the staged/native weapon views.");

            binder.HoldSelectedRac1HandItem(state, avatar);
            Require(held.NativeItemId == (int)Rac1WeaponId.FirstRanged,
                "HoldSelected did not retain the native selected R&C1 item.");
            Refresh(state);
            Require(glove.Visible && stagedBomb.Visible && !wrench.Visible && !view.Visible,
                "Holding first-ranged item did not show the glove and staged bomb.");
            Require(PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged, state),
                "The newly held R&C1 item must become visible.");

            Require(inventory.TryConsumeAmmo((int)Rac1WeaponId.FirstRanged, inventory.FirstRangedAmmo),
                "Fixture first-ranged ammo could not be consumed.");
            Refresh(state);
            Require(glove.Visible && !stagedBomb.Visible,
                "Zero first-ranged ammo must hide the staged bomb without hiding the glove.");

            Refresh(state with { WaterHandItemHidden = true });
            Require(!glove.Visible && !stagedBomb.Visible && !wrench.Visible && !view.Visible,
                "Water must hide both equipment attachments and wrench.");
            Refresh(state with { WrenchFrameActive = true });
            Require(!glove.Visible && !stagedBomb.Visible && wrench.Visible,
                "Active wrench frame must replace the glove with the wrench.");
            Refresh(state with { WrenchThrowActive = true });
            Require(!glove.Visible && !stagedBomb.Visible && wrench.Visible,
                "World-scoped wrench throw must show the wrench and hide the hand weapon.");

            inventory.AcquireNativeItem((int)Rac1WeaponId.Blaster);
            Require(inventory.TryEquip(Rac1WeaponId.Blaster), "Fixture could not equip Blaster.");
            binder.HoldSelectedRac1HandItem(state, avatar);
            Refresh(state);
            Require(view.Visible && !glove.Visible && !stagedBomb.Visible && !wrench.Visible,
                "Native Blaster view must be visible immediately after binder refresh.");

            Refresh(state with { WaterHandItemHidden = true });
            Require(!view.Visible, "Water must hide the native Blaster view immediately.");
            Refresh(state with { WrenchFrameActive = true });
            Require(!view.Visible && wrench.Visible,
                "Wrench frame must hide the native Blaster and display the wrench.");
            Refresh(state with { WrenchThrowActive = true });
            Require(!view.Visible && wrench.Visible,
                "Wrench throw must hide the native Blaster.");

            var fusion = new FusionWeaponSession(seed: 27, inventory: inventory);
            Require(fusion.Select(FusionWeaponSession.Lancer), "Could not select R&C2 Lancer.");
            Refresh(state with { Fusion = fusion });
            Require(!view.Visible, "R&C2 selection must hide the native R&C1 Blaster view.");
            Require(fusion.Select(FusionWeaponSession.ShockCannon), "Could not select R&C3 Shock Cannon.");
            Refresh(state with { Fusion = fusion });
            Require(!view.Visible, "R&C3 selection must hide the native R&C1 Blaster view.");
            Require(fusion.Select(FusionWeaponSession.BombGlove), "Could not restore the R&C1 selected source.");
            Refresh(state with { Fusion = fusion });
            Require(view.Visible, "Restoring R&C1 source must show the held R&C1 Blaster view.");

            held.HoldWrench();
            binder.RefreshRac1HeldItem((int)Rac1WeaponId.Blaster, state, avatar);
            Refresh(state);
            Require(!view.Visible && wrench.Visible && !glove.Visible,
                "Holding the wrench must hide native weapon views and display wrench.");
            Refresh(state with { WaterHandItemHidden = true });
            Require(!wrench.Visible, "Water must hide the held wrench.");

            // Both absent and freed nodes must be skipped without reading them.
            PlayerEquipmentPresentationBinder.RefreshRac1HandNodes(state, null, null, null);
            glove.Free(); stagedBomb.Free(); wrench.Free();
            PlayerEquipmentPresentationBinder.RefreshRac1HandNodes(state, glove, stagedBomb, wrench);
            view.Free();
            binder.RefreshRac1HandVisibility(state, avatar);

            GD.Print("[rac1-hand-presentation-smoke] PASS: native view, glove, bomb/ammo, wrench, water, invalid nodes, mixed-source selection");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[rac1-hand-presentation-smoke] FAIL: " + error);
            GetTree().Quit(1);
        }
    }

    private static Rac1EquipmentAssets.Model SyntheticModel()
    {
        var clip = new Rac1EquipmentAssets.Clip(0,
            [[0d, 0d, 0d], [0d, 0d, 0d]], [1d / 60, 1d / 60],
            [System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero],
            [0f, 1f], null, []);
        return new(13, [], new Dictionary<int, Rac1EquipmentAssets.Clip> { [0] = clip },
            1, 0, "source-free-probe");
    }

    private static RuntimeWorld SyntheticWorld() => new(
        Game: ObpSourceGame.Rac1,
        BuildId: "source-free-probe",
        LevelId: 0,
        PlanetName: "Equipment Fixture",
        LocationName: null,
        Meshes: [],
        Textures: [],
        MaterialCount: 0,
        CollisionMeshes: [],
        Bounds: new ObpBounds(Vec3.Zero, new Vec3(2, 2, 2)),
        Environment: null,
        Ship: null,
        AnimatedMeshes: []);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
