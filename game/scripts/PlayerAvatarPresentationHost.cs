using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using Godot;

namespace OBP.Game;

/// <summary>
/// World-scoped Godot player visuals and fixed native animation clock. Source-decoded
/// data can survive a destination change; nodes, event subscriptions and clock cannot.
/// Movement, damage, inventory and native weapon decisions remain with their owners.
/// </summary>
internal sealed class PlayerAvatarPresentationHost
{
    internal static readonly PlayerAvatarProviderRegistry Registry = new(
        [Rac1PlayerAvatarProvider.Instance, DiagnosticAvatarProvider.Instance]);
    private static PlayerAvatarProviderRegistry Providers => Registry;

    private string? _sourcePath;
    private ObpSourceGame? _sourceGame;
    private (long Length, long Written)? _sourceStamp;
    private string? _wrenchSourcePath;
    private string? _gloveSourcePath;
    private PlayerHost? _physicsPlayer;
    private Action<double, PlayerAnimationState, Rac1LocomotionAnimationInput>? _step;
    private Action? _reset;

    internal PlayerAvatar? Avatar { get; private set; }
    internal PlayerAvatarView? View { get; private set; }
    internal IPlayerAnimationPresentationSink? Sink { get; private set; }
    internal IPlayerAnimationPresentationController? Controller { get; private set; }
    internal PlayerNativeClock? Clock { get; private set; }
    internal Rac1WrenchAsset.Asset? WrenchAsset { get; private set; }
    internal Rac1BombGloveAssets.Asset? GloveAsset { get; private set; }
    internal Rac1WrenchView? WrenchView { get; private set; }
    internal Rac1BombGloveView? GloveView { get; private set; }
    internal Node3D? StagedBombView { get; private set; }
    internal double AnimationTime { get; private set; }
    internal PlayerHost? PhysicsPlayer => _physicsPlayer;

    internal static IPlayerAvatarProvider Provider(ObpSourceGame game) =>
        Providers.Get(game) ?? throw new InvalidOperationException($"No provider for selected avatar source {game}.");

    /// <summary>Stage verified source data, invalidating all borrowed world visuals on replacement.</summary>
    internal bool Stage(PlayerAvatarIdentity identity, string path, (long Length, long Written) stamp)
    {
        bool changed = Avatar?.Identity != identity || _sourcePath != path ||
            _sourceGame != identity.SourceGame || _sourceStamp != stamp;
        if (changed)
        {
            Retire();
            // Load only after the caller has verified the source build and file stamp.
            var avatar = Provider(identity.SourceGame).Load(path, identity.AvatarId);
            if (avatar.Identity != identity)
                throw new InvalidOperationException("Decoded avatar identity differs from the selected player profile.");
            Avatar = avatar;
            _sourcePath = path;
            _sourceGame = identity.SourceGame;
            _sourceStamp = stamp;
            WrenchAsset = null; GloveAsset = null;
            _wrenchSourcePath = null; _gloveSourcePath = null;
        }
        if (Avatar?.Identity != identity)
            throw new InvalidOperationException("Decoded avatar identity differs from the selected player profile.");
        if (identity.SourceGame == ObpSourceGame.Rac1)
        {
            if (WrenchAsset is null || _wrenchSourcePath != path)
            {
                WrenchAsset = Rac1WrenchAsset.Load(path);
                _wrenchSourcePath = path;
            }
            if (GloveAsset is null || _gloveSourcePath != path)
            {
                GloveAsset = Rac1BombGloveAssets.Load(path);
                _gloveSourcePath = path;
            }
        }
        return changed;
    }

    internal (PlayerAvatarView View, Rac1PlayerAnimationPresentationController Controller) CreateWorldVisual(
        PlayerHost player, PlayerAnimationProfile profile, WorldMaterialFactory? gloveMaterials)
    {
        var avatar = Avatar ?? throw new InvalidOperationException("Selected avatar was not staged before world entry.");
        // Validate mandatory source assets before creating Godot nodes so a failed
        // world entry cannot leave unattached visuals or material handles behind.
        if (avatar.Identity.SourceGame == ObpSourceGame.Rac1)
        {
            _ = WrenchAsset ?? throw new InvalidOperationException("Selected wrench asset was not staged.");
            _ = GloveAsset ?? throw new InvalidOperationException("Selected glove asset was not staged.");
            _ = gloveMaterials ?? throw new InvalidOperationException("Selected glove materials are unavailable.");
        }
        var controller = CreateController(avatar);
        if (controller is not Rac1PlayerAnimationPresentationController native ||
            profile.AdapterId != PlayerHostProfiles.Animation)
            throw new InvalidOperationException("Selected native animation adapter did not produce its admitted physics controller.");
        controller.SetAnimationState(player.AnimationState);
        var view = new PlayerAvatarView(avatar, controller.Current, alignGeometricBase: true) { Name = "PlayerAvatar" };
        if (avatar.Identity.SourceGame == ObpSourceGame.Rac1)
        {
            WrenchView = new(WrenchAsset!, avatar);
            view.AddChild(WrenchView);
            GloveView = new(GloveAsset!, avatar, gloveMaterials!);
            view.AddChild(GloveView);
        }
        View = view;
        Sink = view;
        Controller = native;
        AnimationTime = 0;
        Clock = new(profile.NativeTicksPerSecond);
        return (view, native);
    }

    internal static IPlayerAnimationPresentationController CreateController(PlayerAvatar avatar)
    {
        if (Provider(avatar.Identity.SourceGame) is IPlayerAnimationControllerProvider sourceAware)
            return sourceAware.CreateAnimationController(avatar);
        throw new InvalidOperationException($"The selected {avatar.Identity.SourceGame} provider has no admitted native animation controller.");
    }

    internal void InstallStagedBomb(Node3D staged)
    {
        var glove = GloveView ?? throw new InvalidOperationException("Glove view was not attached.");
        StagedBombView = staged;
        staged.Reparent(glove, keepGlobalTransform: false);
        staged.Rotation = new(0, MathF.PI, 0);
    }

    internal void Subscribe(PlayerHost player,
        Action<double, PlayerAnimationState, Rac1LocomotionAnimationInput> step, Action reset)
    {
        _physicsPlayer = player; _step = step; _reset = reset;
        player.AnimationPhysicsUpdated += step;
        player.AnimationPhysicsReset += reset;
    }

    internal Rac1PlayerAnimationPresentationController? NativeController =>
        Controller as Rac1PlayerAnimationPresentationController;

    internal void StepNative(PlayerAnimationState state, Rac1LocomotionAnimationInput input)
    {
        var controller = NativeController ?? throw new InvalidOperationException("The native animation controller is unavailable.");
        AnimationTime = (Clock ?? throw new InvalidOperationException("The player clock is unavailable.")).Step();
        controller.StepOrdinaryAnimation(state, AnimationTime, input);
    }

    internal void SetPresentation(PlayerAnimationPresentation presentation) =>
        Sink?.SetAnimationPresentation(presentation);

    internal void StepHandAttachments(Rac1PlayerAnimationPresentationController controller,
        Rac1WrenchAttackSession.Frame wrench, bool wrenchThrown, long bombGeneration)
    {
        if (_physicsPlayer is not { } player) return;
        player.Rac1TraversalAuthoredAnimationTime = controller.TraversalAuthoredAnimationTime;
        player.Rac1TraversalAnimationCycleCompleted = controller.TraversalAnimationCycleCompleted;
        player.Rac1WaterAnimationFacts = controller.WaterAnimationFacts;
        player.Rac1WrenchAnimationBlending = controller.TraversalSample?.IsBlending ?? false;
        if (WrenchView is not null && controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey) is { } hand)
            WrenchView.StepAttached(hand, wrench, wrenchThrown);
        if (GloveView is not null &&
            controller.CurrentAttachmentPose(Rac1BombProjectileSession.GloveSocketKey) is { } gloveHand)
        {
            GloveView.StepAttached(gloveHand, bombGeneration);
            if (StagedBombView is not null) StagedBombView.Position = GloveView.BombLocal;
        }
    }

    internal void SampleClock() => View?.SetClock(AnimationTime);

    internal void ResetClock()
    {
        AnimationTime = 0;
        Clock?.Reset();
        if (NativeController is { } controller)
        {
            controller.ResetPhysicsPresentation();
            SetPresentation(controller.Current);
            View?.SetClock(0);
        }
    }

    internal int? CurrentSourceSequence()
    {
        string? key = Sink?.CurrentAnimationPresentation.SourceSequenceKey;
        return int.TryParse(key, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var sequence) ? sequence : null;
    }

    internal bool IsSynchronized => Sink?.CurrentAnimationPresentation == Controller?.Current;

    internal Control? CreateAppearancePreview(EquipmentCatalogEntry entry)
    {
        if (!entry.Selectable || entry.Definition.Appearance != Avatar?.Identity || Avatar is not { } avatar) return null;
        var container = new SubViewportContainer
        {
            CustomMinimumSize = new(220, 170),
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var viewport = new SubViewport
        {
            Size = new(300, 190),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false
        };
        container.AddChild(viewport);
        var controller = CreateController(avatar);
        var view = new PlayerAvatarView(avatar, controller.Current, alignGeometricBase: true);
        viewport.AddChild(view); view.SetClock(0);
        float height = (float)avatar.AnimationBounds.Height;
        var camera = new Camera3D { Position = new(0, height * .65f, height * 2.2f), Current = true };
        viewport.AddChild(camera);
        camera.Basis = Basis.LookingAt(new Vector3(0, height * .5f, 0) - camera.Position);
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-35, -25, 0), LightEnergy = 1.5f });
        return container;
    }

    internal void Retire()
    {
        if (_physicsPlayer is not null && GodotObject.IsInstanceValid(_physicsPlayer))
        {
            if (_step is not null) _physicsPlayer.AnimationPhysicsUpdated -= _step;
            if (_reset is not null) _physicsPlayer.AnimationPhysicsReset -= _reset;
            if (View is not null && GodotObject.IsInstanceValid(View) &&
                ReferenceEquals(_physicsPlayer.VisualRoot.CustomVisual, View))
                _physicsPlayer.VisualRoot.ReplaceVisual(null);
        }
        else if (View is not null && GodotObject.IsInstanceValid(View)) View.QueueFree();
        _physicsPlayer = null; _step = null; _reset = null;
        Sink = null; Controller = null; View = null;
        WrenchView = null; GloveView = null; StagedBombView = null;
        AnimationTime = 0; Clock = null;
    }
}