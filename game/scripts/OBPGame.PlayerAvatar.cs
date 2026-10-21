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
        [Rac1PlayerAvatarProvider.Instance]);

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
    /// Resolve player presentation from the active source game. A missing native
    /// avatar provider is an explicit unsupported boundary: keep the debug capsule
    /// rather than borrowing another game's model, clips, or animation selector.
    /// Controller/collision stay on PlayerHost and remain independent from visuals.
    /// </summary>
    private void AttachPlayerAvatarVisual(PlayerHost player)
    {
        if (_activeDestination is not { } destination)
            return;

        IPlayerAvatarProvider? provider = PlayerAvatarProviders.Get(destination.Game);
        if (provider is null)
        {
            GD.Print($"[player-avatar] {destination.Game} has no decoded native player-avatar provider; keeping debug capsule visual");
            return;
        }

        EnsureSourceLibraryInitialized();
        var source = _sources.Get(destination.Game);
        if (source is null)
        {
            GD.Print($"[player-avatar] {destination.Game} source is not attached; keeping debug capsule visual");
            return;
        }

        try
        {
            if (_playerAvatar is null ||
                _playerAvatarSourceGame != destination.Game ||
                !string.Equals(_playerAvatarSourcePath, source.Path, StringComparison.OrdinalIgnoreCase))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _playerAvatar = provider.Load(source.Path, provider.DefaultAvatarId);
                _playerAvatarSourcePath = source.Path;
                _playerAvatarSourceGame = destination.Game;
                sw.Stop();
                GD.Print($"[player-avatar] loaded {_playerAvatar.Identity.ModelId} in {sw.ElapsedMilliseconds} ms");
            }

            IPlayerAnimationPresentationController animationController =
                CreatePlayerAnimationController(provider, _playerAvatar);
            animationController.SetAnimationState(player.AnimationState);
            var view = new PlayerAvatarView(
                _playerAvatar,
                animationController.Current,
                alignGeometricBase: true)
            {
                Name = "PlayerAvatar",
            };
            ClearPlayerAvatarView();
            if (destination.Game == ObpSourceGame.Rac1)
            {
                if (_rac1WrenchAsset is null ||
                    !string.Equals(_rac1WrenchSourcePath, source.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _rac1WrenchAsset = Rac1WrenchAsset.Load(source.Path);
                    _rac1WrenchSourcePath = source.Path;
                }

                _rac1WrenchView = new Rac1WrenchView(_rac1WrenchAsset, _playerAvatar);
                view.AddChild(_rac1WrenchView);
                if (_rac1BombGloveAsset is null || !string.Equals(_rac1BombGloveSourcePath, source.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _rac1BombGloveAsset = OBP.RAC1.Gameplay.Rac1BombGloveAssets.Load(source.Path);
                    _rac1BombGloveSourcePath = source.Path;
                }
                _rac1BombGloveView = new Rac1BombGloveView(_rac1BombGloveAsset, _playerAvatar,
                    new WorldMaterialFactory(_world! with { Textures = _rac1BombGloveAsset.Textures }));
                view.AddChild(_rac1BombGloveView);
                _rac1StagedBombView = CreateRac1EffectNode(121, "Rac1RetailStagedBomb_Class121");
                _rac1StagedBombView.Reparent(_rac1BombGloveView, keepGlobalTransform: false);
                // World effect meshes map native X to -sceneX; avatar-local meshes map it to +X.
                _rac1StagedBombView.Rotation = new(0, MathF.PI, 0);
                AttachRac1EquipmentViews(view);
            }
            player.VisualRoot.ReplaceVisual(view);
            player.ConfigureAvatarPresentation((float)_playerAvatar.AnimationBounds.Height);
            _playerAvatarView = view;
            _playerAvatarAnimationSink = view;
            _playerAvatarAnimationController = animationController;
            _playerAvatarClock = 0;
            if (animationController is Rac1PlayerAnimationPresentationController && Engine.PhysicsTicksPerSecond == 60)
            {
                _avatarPhysicsPlayer = player;
                player.AnimationPhysicsUpdated += StepPlayerAvatarPhysics;
                player.AnimationPhysicsReset += ResetPlayerAvatarPhysics;
            }
            RefreshRac1WrenchPresentationVisibility();
            HideAuthoredWorldRatchetPresentation();
            if (_rac1WrenchView is not null)
            {
                GD.Print($"[player-avatar] attached retail class-71 Wrench ({Rac1WrenchView.AttachmentProvenance})");
            }
            GD.Print($"[player-avatar] attached {_playerAvatar.Identity.ModelId}: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
        }
        catch (Exception ex)
        {
            ClearPlayerAvatarView();
            player.VisualRoot.ReplaceVisual(null);
            GD.PrintErr($"[player-avatar] could not attach native avatar; keeping debug capsule: {ex.Message}");
        }
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
        StepRac1BombWeaponPhysics();
        StepRac1AdditionalWeapons();
        input = SelectRac1BombThrowAnimation(controller, input);
        _playerAvatarClock += Math.Max(0, delta);
        controller.StepOrdinaryAnimation(state, _playerAvatarClock, input);
        bool holdGun = _rac1EquipmentUseTicks > 0 && OBP.RAC1.Gameplay.Rac1WeaponProfiles.IsAdditional(_rac1Weapons.Equipped) &&
            OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped).BodySequence is > 0 and not 44 &&
            _player?.Rac1WrenchFrame.Active != true && !_rac1WrenchThrow.Active && input.Traversal is null &&
            !_rac1Nanotech.Probe().IsDead;
        bool walloper = _rac1Weapons.Equipped == OBP.RAC1.Gameplay.Rac1WeaponId.Walloper && _rac1WalloperAge > 0;
        bool moving = input.Active || state is PlayerAnimationState.Walk or PlayerAnimationState.Run or PlayerAnimationState.JumpRise or PlayerAnimationState.Fall;
        int? equipmentPose = walloper ? OBP.RAC1.Gameplay.Rac1ArsenalRules.WalloperSequence :
            holdGun ? (moving ? OBP.RAC1.Gameplay.Rac1ArsenalRules.MovingBodySequence(_rac1Weapons.Equipped) :
                OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped).BodySequence) : null;
        if (equipmentPose < 0) equipmentPose = null;
        _playerAvatarAnimationSink?.SetAnimationPresentation(controller.ApplyEquipmentPose(equipmentPose,
            walloper ? _rac1WalloperAge / 60d : _rac1EquipmentUseAge / 60d, !walloper, fullBody: walloper || !moving));
        if (_avatarPhysicsPlayer is { } player)
        {
            player.Rac1TraversalAuthoredAnimationTime = controller.TraversalAuthoredAnimationTime;
            player.Rac1TraversalAnimationCycleCompleted = controller.TraversalAnimationCycleCompleted;
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
        }
        StepRac1BombProjectilesPhysics();
        StepRac1AdditionalWeaponWorld();
        StepRac1BombEffectsPhysics();
        _playerAvatarView?.SetClock(_playerAvatarClock);
        RefreshRac1WrenchPresentationVisibility();
    }

    private void ResetPlayerAvatarPhysics()
    {
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

    private void RefreshRac1WrenchPresentationVisibility()
    {
        foreach (var pair in _rac1EquipmentViews)
            if (IsInstanceValid(pair.Value)) pair.Value.Visible = _rac1Weapons.Equipped == pair.Key &&
                _player?.Rac1WrenchFrame.Active != true && !_rac1WrenchThrow.Active;
        bool bombVisible = _rac1Weapons.Equipped == OBP.RAC1.Gameplay.Rac1WeaponId.FirstRanged &&
            _player?.Rac1WrenchFrame.Active != true && !_rac1WrenchThrow.Active;
        if (_rac1BombGloveView is not null && IsInstanceValid(_rac1BombGloveView))
            _rac1BombGloveView.Visible = bombVisible;
        if (_rac1StagedBombView is not null && IsInstanceValid(_rac1StagedBombView))
            _rac1StagedBombView.Visible = bombVisible && _rac1Weapons.FirstRangedAmmo > 0;
        if (_rac1WrenchView is null || !GodotObject.IsInstanceValid(_rac1WrenchView))
            return;
        _rac1WrenchView.Visible =
            _activeDestination?.Game == ObpSourceGame.Rac1 &&
            (_rac1Weapons.Equipped == OBP.RAC1.Gameplay.Rac1WeaponId.Wrench ||
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
