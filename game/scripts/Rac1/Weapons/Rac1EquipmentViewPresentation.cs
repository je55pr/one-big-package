using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>
/// R&C1 source-native equipment view and attachment presentation. Model banks
/// are cached for the selected source; visible and staged nodes are bound to
/// one avatar world. Player inventory, native weapon cadence, audio playback
/// and world damage remain with their existing owners.
/// </summary>
// Playback, authored sound metadata and player position form one output port.
internal readonly record struct Rac1EquipmentAudioPorts(
    Rac1PickupAssets.Catalogue? Bank, PlayerHost Player,
    Action<int, int, Vector3> PlaySound);

internal sealed class Rac1EquipmentViewPresentation
{
    private Rac1EquipmentAssets.Catalogue? _assets;
    private string? _sourcePath;
    private WorldMaterialFactory? _materials;
    private readonly Dictionary<Rac1WeaponId, Rac1EquipmentView> _views = [];
    private readonly Dictionary<Rac1WeaponId, Rac1EquipmentView> _staged = [];
    private long _poseGeneration;

    internal Rac1EquipmentAssets.Catalogue? Assets => _assets;
    internal Rac1EquipmentView? View(Rac1WeaponId weapon) => _views.GetValueOrDefault(weapon);

    internal void ClearViews()
    {
        // The views are children of the avatar, but attaching a replacement
        // must also retire old views if the avatar itself remains alive.
        // Staged attachments are children of their owning view.
        foreach (var view in _views.Values)
            if (GodotObject.IsInstanceValid(view) && !view.IsQueuedForDeletion())
                view.QueueFree();
        _views.Clear();
        _staged.Clear();
    }

    internal void RetireWorld()
    {
        ClearViews();
        _materials = null;
    }

    internal void InvalidateSource()
    {
        RetireWorld();
        _assets = null;
        _sourcePath = null;
    }

    internal void Attach(string sourcePath, RuntimeWorld world, Node3D avatarView, PlayerAvatar? avatar)
    {
        if (_assets is null || _sourcePath != sourcePath)
        {
            _assets = Rac1EquipmentAssets.Load(sourcePath);
            _sourcePath = sourcePath;
        }
        _materials = new(world with { Textures = _assets.Textures });
        ClearViews();
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            var view = new Rac1EquipmentView(_assets.Models[profile.ModelClass], _materials, avatar);
            avatarView.AddChild(view); view.Visible = false; _views.Add(profile.Item, view);
            if (profile.Item is Rac1WeaponId.MineGlove or Rac1WeaponId.GloveOfDoom)
            {
                var staged = new Rac1EquipmentView(_assets.Models[profile.Item == Rac1WeaponId.MineGlove ? 74 : 230], _materials);
                view.AddChild(staged); staged.Rotation = new(0, MathF.PI, 0);
                if (profile.Item == Rac1WeaponId.GloveOfDoom) staged.Scale = Vector3.One * Rac1ArsenalRules.DoomCanisterHeldScale;
                if (profile.Item == Rac1WeaponId.MineGlove) staged.Play(0);
                _staged.Add(profile.Item, staged);
            }
        }
    }

    internal void PlaySelection(Rac1WeaponId weapon)
    {
        if (_views.TryGetValue(weapon, out var view))
            view.Play(0, false, ++_poseGeneration, 1);
    }

    internal void PlayPyrocitor(bool emitting)
    {
        if (_views.TryGetValue(Rac1WeaponId.Pyrocitor, out var view))
            view.Play(emitting ? 3 : 5, false, ++_poseGeneration, emitting ? 4 : 1);
    }

    internal void PlayShot(Rac1AdditionalWeaponSession.Shot shot)
    {
        if (shot.Weapon is not (Rac1WeaponId.Pyrocitor or Rac1WeaponId.Blaster) &&
            _views.TryGetValue(shot.Weapon, out var view) &&
            shot.Weapon is not (Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter or Rac1WeaponId.TeslaClaw))
            view.Play(Rac1ArsenalRules.HandFireSequence(shot.Weapon), false, shot.Generation);
    }

    internal void RefreshAttachment(Rac1WeaponId weapon, Rac1PlayerAnimationPresentationController controller)
    {
        if (!_views.TryGetValue(weapon, out var view)) return;
        // A hidden weapon has not been stepping. Refresh its attachment before
        // the first shot reads its muzzle, without advancing its clock.
        var profile = Rac1WeaponProfiles.Get(weapon);
        view.RefreshAttachment(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(profile.HandSemantic)));
    }

    internal void RefreshVisibility(Func<Rac1WeaponId, bool> visible)
    {
        foreach (var pair in _views)
            if (GodotObject.IsInstanceValid(pair.Value)) pair.Value.Visible = visible(pair.Key);
    }

    internal void Step(
        Rac1PlayerAnimationPresentationController controller,
        Func<Rac1WeaponId, bool> visible,
        int useTicks, bool arsenalHeld,
        Rac1AdditionalWeaponSession? native, Rac1WeaponInventory inventory,
        Rac1EquipmentAudioPorts audio)
    {
        foreach (var profile in Rac1WeaponProfiles.Additional)
        {
            if (!_views.TryGetValue(profile.Item, out var view) || !GodotObject.IsInstanceValid(view)) continue;
            bool isVisible = visible(profile.Item);
            view.Visible = isVisible;
            if (!isVisible) continue;
            if (profile.Item == Rac1WeaponId.Blaster)
                view.Play(useTicks > 0 ? 4 : 1);
            if (profile.Item is Rac1WeaponId.Taunter or Rac1WeaponId.MorphORay or Rac1WeaponId.TeslaClaw)
                view.Play(arsenalHeld ? 3 : 1);
            if (profile.Item == Rac1WeaponId.SuckCannon)
            {
                if (native?.Vacuuming == true) view.Play(3);
                else if (view.Sequence != 5)
                    view.Play(native?.Cargo.Count > 0 ? 6 : 1);
            }
            foreach (int sound in view.Step(controller.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(profile.HandSemantic))))
                if (audio.Bank?.Sounds?.TryGetValue((profile.ModelClass, sound), out var voice) != true || voice?.Loop != true)
                    audio.PlaySound(profile.ModelClass, sound, audio.Player.GlobalPosition);
            if (_staged.TryGetValue(profile.Item, out var staged))
            {
                staged.Position = view.MuzzleLocal; staged.Step();
                staged.Visible = inventory.GetAmmo((int)profile.Item) > 0 &&
                    (profile.Item != Rac1WeaponId.MineGlove || native?.MineRearmTicks == 0);
            }
        }
    }

    internal Vector3 Muzzle(Rac1WeaponId weapon, int socket, PlayerAvatarPresentationHost avatar, PlayerHost player, Vector3 forward)
    {
        if (_views.TryGetValue(weapon, out var view) &&
            avatar.TryEquipmentMuzzle(view, socket, out var muzzle))
        {
            var start = player.GlobalPosition + Vector3.Up * .8f;
            var ray = PhysicsRayQueryParameters3D.Create(start, muzzle); ray.Exclude = [player.GetRid()];
            var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            return hit.TryGetValue("position", out var point) ? point.AsVector3() - (muzzle - start).Normalized() * .02f : muzzle;
        }
        return player.GlobalPosition + Vector3.Up * .9f + forward * .7f;
    }

    internal Rac1EquipmentView CreateWorldView(int nativeClass, Node3D root)
    {
        var view = new Rac1EquipmentView(_assets!.Models[nativeClass], _materials!);
        root.AddChild(view); return view;
    }
}
