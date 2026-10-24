using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private static readonly PlayerAvatarProviderRegistry PlayerAvatarProviders = new(
        [Rac1PlayerAvatarProvider.Instance, DiagnosticAvatarProvider.Instance]);

    private PlayerAvatar? _playerAvatar;
    private string? _playerAvatarSourcePath;
    private ObpSourceGame? _playerAvatarSourceGame;
    private PlayerAvatarView? _playerAvatarView;
    private IPlayerAnimationPresentationSink? _playerAvatarAnimationSink;
    private IPlayerAnimationPresentationController? _playerAvatarAnimationController;
    private double _playerAvatarClock;
    private PlayerHost? _avatarPhysicsPlayer;
    private Rac1WrenchAsset.Asset? _rac1WrenchAsset;
    private string? _rac1WrenchSourcePath;
    private Rac1WrenchView? _rac1WrenchView;
    private OBP.RAC1.Gameplay.Rac1BombGloveAssets.Asset? _rac1BombGloveAsset;
    private string? _rac1BombGloveSourcePath;
    private Rac1BombGloveView? _rac1BombGloveView;
    private Node3D? _rac1StagedBombView;

    /// <summary>
    /// Bind the staged, session-selected presentation independently of destination.
    /// Missing required assets fail entry; the capsule is an explicit diagnostic.
    /// </summary>
    private void AttachPlayerAvatarVisual(PlayerHost player)
    {
        if (_playerSession is null) return; // Explicit standalone asset-free fixtures.
        var selected = SelectedPlayerConfiguration.Avatar;
        if (selected.SourceGame == ObpSourceGame.Synthetic)
        {
            GD.Print("[player-avatar] EXPLICIT DIAGNOSTIC CAPSULE");
            return;
        }
        var provider = PlayerAvatarProviders.Get(selected.SourceGame)
            ?? throw new InvalidOperationException("The selected player avatar provider is unavailable.");
        var source = _sources.Get(selected.SourceGame)
            ?? throw new InvalidOperationException("Attach the selected player avatar source.");
        var avatar = _playerAvatar ?? throw new InvalidOperationException("Selected avatar was not staged before world entry.");
        var controller = CreatePlayerAnimationController(provider, avatar);
        controller.SetAnimationState(player.AnimationState);
        var view = new PlayerAvatarView(avatar, controller.Current, alignGeometricBase: true) { Name = "PlayerAvatar" };
        ClearPlayerAvatarView();
        // Optional source-backed equipment visuals must not select or hide the
        // player. World-specific effect/pickup support remains a #271 adapter.
        if (selected.SourceGame == ObpSourceGame.Rac1)
        {
            _rac1WrenchView = new(_rac1WrenchAsset ?? throw new InvalidOperationException("Selected wrench asset was not staged."), avatar);
            view.AddChild(_rac1WrenchView);
            if (_rac1PickupAssets is not null)
            {
                if (_rac1BombGloveAsset is null || _rac1BombGloveSourcePath != source.Path)
                {
                    _rac1BombGloveAsset = OBP.RAC1.Gameplay.Rac1BombGloveAssets.Load(source.Path);
                    _rac1BombGloveSourcePath = source.Path;
                }
                _rac1BombGloveView = new(_rac1BombGloveAsset, avatar,
                    new WorldMaterialFactory(_world! with { Textures = _rac1BombGloveAsset.Textures }));
                view.AddChild(_rac1BombGloveView);
                _rac1StagedBombView = CreateRac1EffectNode(121, "Rac1RetailStagedBomb_Class121");
                _rac1StagedBombView.Reparent(_rac1BombGloveView, keepGlobalTransform: false);
                _rac1StagedBombView.Rotation = new(0, MathF.PI, 0);
                AttachRac1EquipmentViews(view);
            }
            else AttachFusionWeaponViews(view);
        }
        player.VisualRoot.ReplaceVisual(view);
        player.ConfigureAvatarPresentation((float)avatar.AnimationBounds.Height);
        _playerAvatarView = view; _playerAvatarAnimationSink = view; _playerAvatarAnimationController = controller;
        _playerAvatarClock = 0;
        if (controller is Rac1PlayerAnimationPresentationController && Engine.PhysicsTicksPerSecond == SelectedPlayerConfiguration.NativeTicksPerSecond)
        {
            _avatarPhysicsPlayer = player;
            player.AnimationPhysicsUpdated += StepPlayerAvatarPhysics;
            player.AnimationPhysicsReset += ResetPlayerAvatarPhysics;
        }
        RefreshRac1WrenchPresentationVisibility();
        HideAuthoredWorldRatchetPresentation();
        GD.Print($"[player-avatar] attached selected {avatar.Identity.ModelId}: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
    }
    private static IPlayerAnimationPresentationController CreatePlayerAnimationController(
        IPlayerAvatarProvider provider,
        PlayerAvatar avatar)
    {
        if (provider is IPlayerAnimationControllerProvider sourceAware)
            return sourceAware.CreateAnimationController(avatar);

        GD.Print($"[player-avatar] {provider.SourceGame} has no native animation selector; using generic presentation fallback");
        return new GenericPlayerAnimationPresentationController(avatar);
    }

    private void TickPlayerAvatar(double delta)
    {
        if (_playerAvatarView is null || !GodotObject.IsInstanceValid(_playerAvatarView))
            return;

        // A bound R&C1 avatar is sampled by movement updates. Render frequency
        // cannot select a sequence, advance a frame, or change its multiplier.
        if (_avatarPhysicsPlayer is not null)
            return;

        _playerAvatarAnimationController?.SetAnimationState(
            _player?.AnimationState ?? PlayerAnimationState.Idle);
        _playerAvatarClock += Math.Max(0, delta);
        if (_playerAvatarAnimationController is not null)
        {
            var presentation = _playerAvatarAnimationController.SetClock(_playerAvatarClock);
            _playerAvatarAnimationSink?.SetAnimationPresentation(presentation);
        }

        _playerAvatarView.SetClock(_playerAvatarClock);
    }

    private void StepPlayerAvatarPhysics(
        double delta, PlayerAnimationState state, Rac1LocomotionAnimationInput input)
    {
        if (_playerAvatarAnimationController is not Rac1PlayerAnimationPresentationController controller)
            return;
        StepRac1EquippedUseInput();
        StepRac1BombWeaponPhysics();
        StepRac1AdditionalWeapons();
        StepFusionWeapons();
        input = SelectRac1BombThrowAnimation(controller, input);
        _playerAvatarClock += Math.Max(0, delta);
        controller.StepOrdinaryAnimation(state, _playerAvatarClock, input);
        bool holdGun = _rac1EquipmentUseTicks > 0 && OBP.RAC1.Gameplay.Rac1WeaponProfiles.IsAdditional(_rac1Weapons.Equipped) &&
            OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped).BodySequence is > 0 and not 44 &&
            IsRac1HandEquipmentVisible(_rac1Weapons.Equipped) && input.Traversal is null &&
            !_rac1Nanotech.Probe().IsDead;
        bool walloper = IsRac1HandEquipmentVisible(OBP.RAC1.Gameplay.Rac1WeaponId.Walloper) && _rac1WalloperAge > 0;
        bool moving = input.Active || state is PlayerAnimationState.Walk or PlayerAnimationState.Run or PlayerAnimationState.JumpRise or PlayerAnimationState.Fall;
        int? equipmentPose = walloper ? OBP.RAC1.Gameplay.Rac1ArsenalRules.WalloperSequence :
            holdGun ? (moving ? OBP.RAC1.Gameplay.Rac1ArsenalRules.MovingBodySequence(_rac1Weapons.Equipped) :
                OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped).BodySequence) : null;
        // Jump/traversal clocks still own the body. Evaluate equipment from
        // that clock too, including the attachment when no arm bank applies.
        bool fusionPose = _fusion is { Selected.SourceGame: not "rac1" } && _fusion.Hand.IsHolding(_fusion.Selected) &&
            !_rac1Nanotech.Probe().IsDead && _player?.Rac1WrenchFrame.Active != true && _player?.Rac1WaterHandItemHidden != true;
        if (equipmentPose < 0) equipmentPose = null;
        if (fusionPose && _fusionAssets?.AvatarAnimations.TryGetValue(_fusion!.Selected, out var sourceAnimation) == true)
            _playerAvatarAnimationSink?.SetAnimationPresentation(controller.ApplySourceEquipment(sourceAnimation,
                moving, input.CrouchHeld, _fusionUseStarted, _fusionPoseTriggerHeld));
        else
        {
            controller.ClearSourceEquipment();
            _playerAvatarAnimationSink?.SetAnimationPresentation(controller.ApplyEquipmentPose(equipmentPose,
                walloper ? _rac1WalloperAge / 60d : _rac1EquipmentUseAge / 60d, !walloper, fullBody: walloper || !moving));
        }
        if (_avatarPhysicsPlayer is { } player)
        {
            player.Rac1TraversalAuthoredAnimationTime = controller.TraversalAuthoredAnimationTime;
            player.Rac1TraversalAnimationCycleCompleted = controller.TraversalAnimationCycleCompleted;
            player.Rac1WaterAnimationFacts = controller.WaterAnimationFacts;
            player.Rac1WrenchAnimationBlending = controller.TraversalSample?.IsBlending ?? false;
            if (_rac1WrenchView is not null &&
                controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey) is { } hand)
                _rac1WrenchView.StepAttached(hand, player.Rac1WrenchFrame, _rac1WrenchThrow.Active);
            if (_rac1BombGloveView is not null &&
                controller.CurrentAttachmentPose(OBP.RAC1.Gameplay.Rac1BombProjectileSession.GloveSocketKey) is { } gloveHand)
            {
                _rac1BombGloveView.StepAttached(gloveHand, _rac1BombThrowGeneration);
                if (_rac1StagedBombView is not null)
                    _rac1StagedBombView.Position = _rac1BombGloveView.BombLocal;
            }
            StepRac1EquipmentViews(controller);
            StepFusionWeaponViews(controller);
        }
        StepRac1PlayerAudio(controller);
        StepRac1BombProjectilesPhysics();
        StepRac1AdditionalWeaponWorld();
        StepRac1BombEffectsPhysics();
        _playerAvatarView?.SetClock(_playerAvatarClock);
        RefreshRac1WrenchPresentationVisibility();
    }

    private void ResetPlayerAvatarPhysics()
    {
        ClearFusionTransients();
        ResetRac1PlayerAudio();
        ResetRac1WrenchHost();
        _rac1BombThrowActive = false;
        _playerAvatarClock = 0;
        if (_playerAvatarAnimationController is Rac1PlayerAnimationPresentationController controller)
        {
            controller.ResetPhysicsPresentation();
            _playerAvatarAnimationSink?.SetAnimationPresentation(controller.Current);
            _playerAvatarView?.SetClock(0);
        }
    }

    private int? CurrentPlayerAvatarSourceSequence()
    {
        string? key = _playerAvatarAnimationSink?.CurrentAnimationPresentation.SourceSequenceKey;
        return int.TryParse(
            key,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int sequenceId)
            ? sequenceId
            : null;
    }

    private bool PlayerAvatarPresentationIsSynchronized() =>
        _playerAvatarAnimationSink?.CurrentAnimationPresentation ==
        _playerAvatarAnimationController?.Current;

    private void ClearPlayerAvatarView()
    {
        ResetRac1PlayerAudio();
        if (_avatarPhysicsPlayer is not null)
        {
            _avatarPhysicsPlayer.AnimationPhysicsUpdated -= StepPlayerAvatarPhysics;
            _avatarPhysicsPlayer.AnimationPhysicsReset -= ResetPlayerAvatarPhysics;
            _avatarPhysicsPlayer = null;
        }
        _playerAvatarAnimationSink = null;
        _playerAvatarAnimationController = null;
        _playerAvatarView = null;
        _rac1WrenchView = null;
        _rac1BombGloveView = null;
        _rac1StagedBombView = null;
        _rac1EquipmentViews.Clear(); _rac1StagedEquipment.Clear();
        _playerAvatarClock = 0;
    }

    private void HoldSelectedRac1HandItem()
    {
        int? previous = _rac1HeldItem.NativeItemId;
        _rac1HeldItem.HoldSelected(_rac1Weapons);
        RefreshRac1HeldItem(previous);
    }

    private void RefreshRac1HeldItem(int? previous)
    {
        if (previous != _rac1HeldItem.NativeItemId &&
            _rac1EquipmentViews.TryGetValue(_rac1Weapons.Equipped, out var view) &&
            _playerAvatarAnimationController is Rac1PlayerAnimationPresentationController controller)
        {
            // A hidden weapon has not been stepping. Refresh its attachment
            // before the first shot reads its muzzle, without advancing its clock.
            var profile = OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped);
            view.RefreshAttachment(controller.CurrentAttachmentPose(
                OBP.RAC1.Gameplay.Rac1WeaponProfiles.HandSocket(profile.HandSemantic)));
        }
        RefreshRac1WrenchPresentationVisibility();
    }

    private bool IsRac1HandEquipmentVisible(OBP.RAC1.Gameplay.Rac1WeaponId item) =>
        _fusion?.Selected.SourceGame is not ("rac2" or "rac3") &&
        _rac1HeldItem.Hand.IsHolding(new("rac1", (int)item)) &&
        _player?.Rac1WaterHandItemHidden != true &&
        _player?.Rac1WrenchFrame.Active != true && !_rac1WrenchThrow.Active;

    private void RefreshRac1WrenchPresentationVisibility()
    {
        foreach (var pair in _rac1EquipmentViews)
            if (IsInstanceValid(pair.Value)) pair.Value.Visible = IsRac1HandEquipmentVisible(pair.Key);
        bool bombVisible = IsRac1HandEquipmentVisible(OBP.RAC1.Gameplay.Rac1WeaponId.FirstRanged);
        if (_rac1BombGloveView is not null && IsInstanceValid(_rac1BombGloveView))
            _rac1BombGloveView.Visible = bombVisible;
        if (_rac1StagedBombView is not null && IsInstanceValid(_rac1StagedBombView))
            _rac1StagedBombView.Visible = bombVisible && _rac1Weapons.FirstRangedAmmo > 0;
        if (_rac1WrenchView is null || !GodotObject.IsInstanceValid(_rac1WrenchView))
            return;
        _rac1WrenchView.Visible =
            _player?.Rac1WaterHandItemHidden != true &&
            (_rac1HeldItem.Hand.WrenchHeld ||
             _player?.Rac1WrenchFrame.Active == true || _rac1WrenchThrow.Active);
    }

    /// <summary>
    /// R&C1 still exposes the authored class-0 standing Ratchet through the world
    /// importer for archaeology/equivalence. Once a player avatar is attached,
    /// hide that duplicate Godot presentation only; RuntimeWorld stays untouched.
    /// </summary>
    private void HideAuthoredWorldRatchetPresentation()
    {
        if (_activeDestination?.Game != ObpSourceGame.Rac1 || _sceneResult?.AnimatedMeshes is null)
        {
            return;
        }

        int hidden = 0;
        foreach (var animated in _sceneResult.AnimatedMeshes)
        {
            if (!animated.Name.StartsWith("ratchet_i", StringComparison.Ordinal))
            {
                continue;
            }

            animated.Instance.Visible = false;
            hidden++;
        }

        if (hidden > 0)
        {
            GD.Print($"[player-avatar] hid {hidden} authored Ratchet surface(s) from world presentation");
        }
    }
}
