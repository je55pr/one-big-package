using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private void StepRac1BombWeaponPhysics()
    {
        if (_equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is null || Rac1World.Context.BombGlove is null || _player is null || !PlayerLife.Alive) return;
        bool fire = Rac1World.Context.BombFireRequested && !Rac1World.Weapons.Detector.Equipped &&
            !_player.Rac1WrenchFrame.Active && !(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true);
        Rac1World.Context.BombFireRequested = false;
        var probe = Rac1World.Context.BombGlove.Step(fire);
        if (probe.Shot is { } shot)
        {
            Rac1World.Weapons.ThrownAnimation.Begin();
            Rac1World.Bombs.Spawn(shot);
            FusionWeapons.PlayNativeSound(0, 26, _player.GlobalPosition); // 0x2c2.. hero throw, not the glove's equip cue.
            PersistRac1CampaignState("Bomb Glove ammo consumption");
            Rac1World.Context.CombatStatus = $"Bomb Glove fired: ammo {shot.AmmoBefore}->{shot.AmmoAfter}";
            RefreshRac1HudState();
            GD.Print($"[rac1-gameplay] {Rac1World.Context.CombatStatus}");
        }
        else if (fire && probe.UseAdmission is { Accepted: false } rejected)
            Rac1World.Context.CombatStatus = $"Bomb Glove use rejected: {rejected.Rejection}";
    }

    private Rac1LocomotionAnimationInput SelectRac1BombThrowAnimation(
        Rac1PlayerAnimationPresentationController controller, Rac1LocomotionAnimationInput input)
        => _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.ThrownAnimation.Select(input,
            PlayerLife.Alive, _player?.Rac1WrenchFrame.Active == true,
            CurrentPlayerAvatarSourceSequence(), controller.TraversalAuthoredAnimationTime) ?? input;

    // Player/campaign use admission remains in the existing persistent owner.
    // The R&C1 world lease owns launched projectiles and damage routing.
    private void StepRac1BombProjectilesPhysics()
        => _worldScenes?.Current?.Resources.Rac1Gameplay?.Bombs.StepPhysics();

    private static void SetBombOrientation(Node3D node, NVector3 angles) =>
        node.Quaternion = Rac1LootPresentation.FromEffectOrientation(System.Numerics.Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z));

}
