using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Borrowed native player and source-equipment state, resolved only when used.</summary>
internal sealed record Rac1WeaponUseStatePorts(
    Func<PlayerHost?> Player,
    Func<Rac1PlayerAudioPresenter?> Audio,
    Func<Rac1WeaponInventory> Weapons,
    Func<Rac1WeaponInventory> CampaignWeapons,
    Func<FusionWeaponHost> Fusion,
    Func<RuntimeWorld?> CurrentWorld,
    Func<Node3D> SceneRoot,
    Func<string> SourcePath);

internal sealed record Rac1WeaponUseEnvironmentPorts(
    Func<bool> Ordinary, Func<bool> Loading, Func<bool> Alive,
    Func<bool> Smoke, Func<int?> Seed);

/// <summary>App/UI outputs, no independent projectile or damage authority.</summary>
internal sealed record Rac1WeaponUseViewPorts(
    Action<string> Report, Action<string> Persist,
    Action RefreshHud, Action HoldSelected,
    Action RefreshWrenchVisibility, Func<Rac1WeaponId, bool> Visible);

internal sealed record Rac1WeaponUsePorts(
    PlayerSessionHost Session,
    PlayerEquipmentPresentationBinder Equipment,
    PlayerAvatarPresentationHost Avatar,
    Rac1WeaponUseStatePorts State,
    Rac1WeaponUseEnvironmentPorts Environment,
    Rac1WeaponUseViewPorts Views);

/// <summary>
/// One world-bound equipment-use/presentation adapter. The persistent player
/// continues to own native inventory, ammo, request admission and RNG; current
/// weapon meshes, collisions and visual effects remain scoped to the world.
/// </summary>
internal sealed class Rac1WeaponUsePresentationHost(Rac1WorldModule world, Rac1WeaponUsePorts ports)
{
    private PlayerSessionHost Session => ports.Session;
    private Rac1WeaponUseStatePorts State => ports.State;
    private Rac1WeaponUseEnvironmentPorts Environment => ports.Environment;
    private Rac1WeaponUseViewPorts Views => ports.Views;
    private PlayerEquipmentPresentationBinder Equipment => ports.Equipment;

    internal static Vector3 EquipmentForward(PlayerHost? player) =>
        player is null ? Vector3.Forward :
            new(-MathF.Cos((float)player.Rac1CurrentYaw), 0, MathF.Sin((float)player.Rac1CurrentYaw));

    internal static Vector3 EquipmentMuzzle(Rac1WeaponId weapon, int socket,
        PlayerEquipmentPresentationBinder equipment, PlayerAvatarPresentationHost avatar, PlayerHost? player) =>
        equipment.NativeViews.Muzzle(weapon, socket, avatar, player!, EquipmentForward(player));

    internal Vector3 Muzzle(Rac1WeaponId weapon, int socket = 0) =>
        EquipmentMuzzle(weapon, socket, Equipment, ports.Avatar, State.Player());

    internal Rac1EquipmentView CreateEquipmentWorldView(int nativeClass) =>
        Equipment.NativeViews.CreateWorldView(nativeClass, State.SceneRoot());

    internal static void SetPlayerAudioPaused(bool paused, Rac1PlayerAudioPresenter? audio,
        PlayerEquipmentPresentationBinder equipment, FusionWeaponHost fusion)
    {
        audio?.SetPaused(paused);
        equipment.WeaponAudio.SetPaused(paused);
        fusion.SetPaused(paused);
    }

    internal void StepAudio()
    {
        var player = State.Player();
        var audio = State.Audio();
        var weapons = State.Weapons();
        var use = Session.Rac1Use;
        Equipment.WeaponAudio.Step(new(
            player is not null && audio?.Ready == true && Environment.Alive() && !Environment.Loading() &&
                !player.Rac1WrenchFrame.Active && !world.Wrench.ThrowActive,
            weapons.Equipped, use.UseTicks, use.PyroEmitting,
            use.Session?.Vacuuming == true, world.Weapons.Arsenal.Held, weapons.GetAmmo(19) > 0,
            world.Weapons.WorldTick, Environment.Smoke()), player, audio?.Random,
            Equipment.Assets(Environment.Ordinary(), world.Loot.PickupAssets)?.Sounds);
    }

    internal void ResetAdditional()
    {
        Equipment.WeaponAudio.Reset();
        world.Weapons.Arsenal.Reset();
        if (State.Player() is { } player)
        {
            player.Rac1EquipmentActionActive = false;
            player.Rac1EquipmentLungeVelocity = Vector3.Zero;
        }
        world.Weapons.Projectiles.RetireShotsAgentsMotes();
        world.Weapons.SuckCannon.Reset();
        world.Weapons.Flames.ClearCooldowns();
        world.Weapons.Flames.ClearTrackedFlames();
        Equipment.Explosions.Particles?.RemoveType(12);
        Equipment.Explosions.Particles?.RemoveType(2);
        Session.Rac1Use.ResetSession(State.Weapons());
        world.Weapons.Projectiles.ResetFlashes();
        world.Weapons.ResetWorldTick();
        Session.Rac1Use.ResetInputState();
        // Target meshes restored by preview/reset invalidate this world's bounds.
        world.Weapons.Contacts.Invalidate();
    }

    // The preview callback is also used by Fusion menus outside an R&C1 world.
    // Keep its original Fusion-test rejection before resolving a world lease.
    internal static void RequestPreview(bool disable, Func<FusionWeaponHost> fusion,
        Func<Rac1WorldModule?> currentWorld, Action<string> report)
    {
        if (fusion().TestActive)
        {
            report("Leave the fusion test before changing the R&C1 arsenal preview.");
            return;
        }

        if (currentWorld() is { } world) world.UsePresentation.SetPreview(disable);
        else report("Enter an R&C1 world before changing the arsenal preview.");
    }

    internal void TogglePreview() => SetPreview(Input.IsKeyPressed(Key.Shift));

    internal void SetPreview(bool disable)
    {
        var fusion = State.Fusion();
        if (fusion.TestActive)
        {
            Views.Report("Leave the fusion test before changing the R&C1 arsenal preview.");
            return;
        }
        bool resetWorld = Session.UpdateRac1WeaponPreview(disable, State.Weapons, State.CampaignWeapons());
        if (resetWorld)
        {
            world.Context.BombGlove = new(State.Weapons());
            ResetAdditional();
        }
        Views.Report(disable
            ? "Weapon preview disabled; campaign equipment restored."
            : "Full arsenal preview: hold Triangle to select, Circle to use. Refill or restore campaign equipment from Pause → Testing Equipment.");
        Session.Rac1Use.BindInventory(State.Weapons());
        fusion.SynchronizeNativeSelection();
        if (State.CurrentWorld() is { Game.SourceGame: ObpSourceGame.Rac1 } current)
            world.Weapons.Detector.Refresh(current);
        if (Session.Rac1Held.NativeItemId != (int)Rac1WeaponId.Wrench)
            Views.HoldSelected();
        Views.RefreshHud();
        Views.RefreshWrenchVisibility();
    }

    internal void AttachViews(Node3D avatarView) =>
        Equipment.NativeViews.Attach(State.SourcePath(), State.CurrentWorld()!, avatarView, ports.Avatar.Avatar);

    internal void StepAdditional()
    {
        var use = Session.Rac1Use;
        var player = State.Player();
        var weapons = State.Weapons();
        if (Environment.Loading() || Equipment.Assets(Environment.Ordinary(), world.Loot.PickupAssets) is null ||
            player is null || use.Session is null) return;
        bool eligible = Environment.Alive() && !player.Rac1WrenchFrame.Active && !world.Wrench.ThrowActive;
        var frame = use.BeginFrame(eligible, () => world.Weapons.Detector.Equipped, weapons,
            world.Weapons.Arsenal.SetHeld, world.Weapons.Arsenal.AdmitUse);
        if (frame.PyroChanged) Equipment.NativeViews.PlayPyrocitor(frame.Pyro);
        if (frame.Shot is { } shot)
        {
            // Hidden equipment skips the view step; refresh its socket before firing.
            if (ports.Avatar.Controller is Rac1PlayerAnimationPresentationController controller)
                Equipment.NativeViews.RefreshAttachment(shot.Weapon, controller);
            use.RecordShot(shot);
            Equipment.NativeViews.PlayShot(shot);
            if (shot.Weapon is Rac1WeaponId.MineGlove or Rac1WeaponId.GloveOfDoom)
            {
                world.Weapons.ThrownAnimation.Begin();
                world.Weapons.Projectiles.Throw(shot);
            }
            else if (shot.Weapon == Rac1WeaponId.Blaster) world.Weapons.Projectiles.FireBlaster(shot);
            else if (shot.Weapon == Rac1WeaponId.SuckCannon) world.Weapons.SuckCannon.Fire(shot);
            else if (Rac1ArsenalRules.IsNew(shot.Weapon)) world.Weapons.Arsenal.Fire(shot.Weapon);
            if (shot.Weapon is Rac1WeaponId.DecoyGlove or Rac1WeaponId.DroneDevice)
                world.Weapons.ThrownAnimation.Begin();
            if (Rac1ItemAmmoDescriptors.Get((int)shot.Weapon).UsesAmmo)
                Views.Persist($"{shot.Weapon} ammo consumption");
            Views.RefreshHud();
        }
        use.CompleteFrame(frame, weapons);
        StepAudio();
    }

    internal void StepViews(Rac1PlayerAnimationPresentationController controller) =>
        Equipment.NativeViews.Step(controller, Views.Visible, Session.Rac1Use.UseTicks,
            world.Weapons.Arsenal.Held, Session.Rac1Use.Session, State.Weapons(),
            new(Equipment.Assets(Environment.Ordinary(), world.Loot.PickupAssets),
                State.Player()!, State.Fusion().PlayNativeSound));
}
