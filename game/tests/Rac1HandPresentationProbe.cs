using System;
using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>Source-free Godot regression for cross-game held-item presentation gating.</summary>
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

            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged, state),
                "An undrawn first-ranged item must stay hidden.");
            binder.HoldSelectedRac1HandItem(state, avatar);
            Require(held.NativeItemId == (int)Rac1WeaponId.FirstRanged,
                "HoldSelected did not retain the native selected R&C1 item.");
            Require(PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged, state),
                "The newly held R&C1 item must become visible.");
            binder.RefreshRac1HeldItem((int)Rac1WeaponId.Wrench, state, avatar);
            binder.RefreshRac1HandVisibility(state, avatar);

            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged,
                state with { WaterHandItemHidden = true }), "Water must hide R&C1 hand equipment.");
            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged,
                state with { WrenchFrameActive = true }), "An active wrench frame must hide R&C1 hand equipment.");
            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged,
                state with { WrenchThrowActive = true }), "A world-scoped wrench throw must hide R&C1 hand equipment.");

            // Foreign selection is an independent source qualification, not a native
            // held-item mutation. Keep the R&C1 held session unchanged for this check.
            var fusion = new FusionWeaponSession(seed: 27, inventory: inventory);
            Require(fusion.Select(FusionWeaponSession.Lancer), "Could not select R&C2 Lancer.");
            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged,
                state with { Fusion = fusion }), "R&C2 selection must hide the R&C1 weapon view.");
            Require(fusion.Select(FusionWeaponSession.ShockCannon), "Could not select R&C3 Shock Cannon.");
            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged,
                state with { Fusion = fusion }), "R&C3 selection must hide the R&C1 weapon view.");
            Require(fusion.Select(FusionWeaponSession.BombGlove), "Could not restore the Bomb Glove.");
            Require(PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged,
                state with { Fusion = fusion }), "Re-selecting R&C1 must restore the held R&C1 view.");

            held.HoldWrench();
            binder.RefreshRac1HeldItem((int)Rac1WeaponId.FirstRanged, state, avatar);
            Require(!PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(Rac1WeaponId.FirstRanged, state),
                "Returning to wrench must hide the R&C1 ranged attachment.");

            GD.Print("[rac1-hand-presentation-smoke] PASS: native hold, visibility, water, wrench, mixed-source selection");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[rac1-hand-presentation-smoke] FAIL: " + error);
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
