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
    /// <summary>
    /// Existing player and scene owners borrowed for one visual subscription.
    /// Live observations are resolved at use time; retirement releases the callbacks.
    /// No root reference or replacement native session is retained here.
    /// </summary>
    internal sealed record WorldBindings(
        Func<ObpPlayerSession?> Session,
        Func<PlayerAnimationProfile> AnimationProfile,
        Func<RuntimeWorld> World,
        Func<ObpDestination?> Destination,
        Func<RuntimeWorldScene.Result?> Scene,
        Func<Rac1WorldModule?> Gameplay,
        Func<Rac1HandPresentationContext> Hand,
        Func<PlayerHost?> Player,
        PlayerEquipmentPresentationBinder Equipment,
        Rac1AdditionalUseHost Use,
        Func<IReadOnlyDictionary<ObpSourceGame, string>> EquipmentSources,
        Func<FusionWeaponHost> Weapons,
        Func<IRac1WeaponPlayerInput> CombatInput,
        Func<bool> Alive,
        Func<bool> Ordinary,
        Func<Rac1PlayerAudioPresenter?> Audio,
        Func<bool> MovementLoading);

    internal static readonly PlayerAvatarProviderRegistry Registry = new(
        [Rac1PlayerAvatarProvider.Instance, DiagnosticAvatarProvider.Instance]);
    private static PlayerAvatarProviderRegistry Providers => Registry;

    private string? _sourcePath;
    private ObpSourceGame? _sourceGame;
    private (long Length, long Written)? _sourceStamp;
    private PlayerHost? _physicsPlayer;
    private Action<double, PlayerAnimationState, Rac1LocomotionAnimationInput>? _step;
    private Action? _reset;
    private PreparedAssets? _prepared;

    private sealed record PreparedAssets(PlayerAvatarIdentity Identity, string Path,
        (long Length, long Written) Stamp, PlayerAvatar Avatar, Rac1WrenchAsset.Asset? Wrench,
        Rac1BombGloveAssets.Asset? Glove, Rac1ClankPackAssets? Clank, bool Changed);

    internal (long Length, long Written)? CommittedSourceStamp => _sourceStamp;
    internal bool HasPrepared => _prepared is not null;
    internal bool PreparedSourceChanged => _prepared?.Changed == true;

    internal PlayerAvatar? Avatar { get; private set; }
    internal PlayerAvatarView? View { get; private set; }
    internal IPlayerAnimationPresentationSink? Sink { get; private set; }
    internal IPlayerAnimationPresentationController? Controller { get; private set; }
    internal PlayerNativeClock? Clock { get; private set; }
    internal Rac1WrenchAsset.Asset? WrenchAsset { get; private set; }
    internal Rac1BombGloveAssets.Asset? GloveAsset { get; private set; }
    internal Rac1ClankPackAssets? ClankAsset { get; private set; }
    internal Rac1ClankPackView? ClankView { get; private set; }
    internal Rac1WrenchView? WrenchView { get; private set; }
    internal Rac1BombGloveView? GloveView { get; private set; }
    internal Node3D? StagedBombView { get; private set; }
    internal double AnimationTime { get; private set; }
    internal PlayerHost? PhysicsPlayer => _physicsPlayer;

    internal readonly record struct WrenchContactPose(Vector3 Hand, Vector3 Tip);
    internal bool TryWrenchContactPose(out WrenchContactPose pose)
    {
        pose = default;
        if (View is null || WrenchView is null || !GodotObject.IsInstanceValid(View) ||
            !GodotObject.IsInstanceValid(WrenchView)) return false;
        pose = new(View.ToGlobal(WrenchView.HandLocal), View.ToGlobal(WrenchView.TipLocal));
        return true;
    }

    internal void UpdateThrownWrenchVisual(Vector3 position, float spin)
    {
        if (WrenchView is not null && GodotObject.IsInstanceValid(WrenchView))
            WrenchView.StepThrown(position, spin);
    }

    internal bool TryEquipmentMuzzle(Rac1EquipmentView equipment, int socket, out Vector3 position)
    {
        position = default;
        if (View is null || !GodotObject.IsInstanceValid(View) || !GodotObject.IsInstanceValid(equipment))
            return false;
        position = View.ToGlobal(equipment.MuzzleAt(socket));
        return true;
    }

    internal bool TryEquipmentHandOrigin(Rac1EquipmentView equipment, out Vector3 position)
    {
        position = default;
        if (View is null || !GodotObject.IsInstanceValid(View) || !GodotObject.IsInstanceValid(equipment) ||
            equipment.HandOriginLocal is not { } local) return false;
        position = View.ToGlobal(local);
        return true;
    }

    internal static IPlayerAvatarProvider Provider(ObpSourceGame game) =>
        Providers.Get(game) ?? throw new InvalidOperationException($"No provider for selected avatar source {game}.");

    /// <summary>
    /// Admit only the session-selected avatar with its resolved animation profile.
    /// The source owner remains responsible for authenticating the file and build;
    /// this presentation owner stages visuals without publishing them to a live world.
    /// </summary>
    internal void PrepareSelectedWorldEntry(ObpPlayerSession session, PlayerAnimationProfile profile,
        int physicsTicksPerSecond, Func<ObpSourceGame, string, VerifiedSourceFile> verifySource,
        PlayerEquipmentPresentationBinder equipment, bool ordinary)
    {
        var identity = session.Capture().Configuration.Avatar;
        if (physicsTicksPerSecond != profile.NativeTicksPerSecond)
            throw new InvalidOperationException($"Selected player animation requires {profile.NativeTicksPerSecond} physics ticks per second.");
        if (identity.SourceGame == ObpSourceGame.Synthetic) return;

        var availability = session.Availability(identity.SourceGame, identity.BuildId, true, true);
        if (!availability.Usable) throw new InvalidOperationException(availability.Reason);
        var source = verifySource(identity.SourceGame, identity.BuildId);
        PrepareSelected(identity, source.Path, source.Stamp, equipment, ordinary);
    }

    /// <summary>Stage presentation from a source already verified by the source owner.</summary>
    internal void PrepareSelected(PlayerAvatarIdentity identity, string path,
        (long Length, long Written) stamp, PlayerEquipmentPresentationBinder equipment, bool ordinary)
    {
        Prepare(identity, path, stamp);
        if (identity.SourceGame == ObpSourceGame.Rac1)
            equipment.PrepareSelectedBank(path, ordinary, PreparedSourceChanged);
    }
    /// <summary>
    /// Decode a pending candidate without touching the live visual or its callbacks.
    /// Failure before world activation must leave the active player unchanged.
    /// </summary>
    internal void Prepare(PlayerAvatarIdentity identity, string path, (long Length, long Written) stamp)
    {
        if (_prepared is { } previous)
        {
            if (previous.Identity != identity || previous.Path != path || previous.Stamp != stamp)
                throw new InvalidDataException("The selected avatar source changed during world preparation.");
            return;
        }
        bool changed = Avatar?.Identity != identity || _sourcePath != path ||
            _sourceGame != identity.SourceGame || _sourceStamp != stamp;
        var avatar = changed ? Provider(identity.SourceGame).Load(path, identity.AvatarId) :
            Avatar ?? throw new InvalidOperationException("The selected avatar is unavailable.");
        if (avatar.Identity != identity)
            throw new InvalidOperationException("Decoded avatar identity differs from the selected player profile.");
        var wrench = identity.SourceGame != ObpSourceGame.Rac1 ? null :
            changed || WrenchAsset is null ? Rac1WrenchAsset.Load(path) : WrenchAsset;
        var glove = identity.SourceGame != ObpSourceGame.Rac1 ? null :
            changed || GloveAsset is null ? Rac1BombGloveAssets.Load(path) : GloveAsset;
        var clank = identity.SourceGame != ObpSourceGame.Rac1 ? null :
            changed || ClankAsset is null ? Rac1ClankPackAssets.Load(path) : ClankAsset;
        _prepared = new(identity, path, stamp, avatar, wrench, glove, clank, changed);
    }

    /// <summary>Publish only after the previous world has retired its visuals and callbacks.</summary>
    internal bool ApplyPrepared()
    {
        var prepared = _prepared ?? throw new InvalidOperationException("No verified avatar preparation was committed.");
        _prepared = null;
        if (prepared.Changed) Retire();
        Avatar = prepared.Avatar;
        WrenchAsset = prepared.Wrench;
        GloveAsset = prepared.Glove;
        ClankAsset = prepared.Clank;
        _sourcePath = prepared.Path;
        _sourceGame = prepared.Identity.SourceGame;
        _sourceStamp = prepared.Stamp;
        return prepared.Changed;
    }

    internal void DiscardPrepared() => _prepared = null;

    internal (PlayerAvatarView View, Rac1PlayerAnimationPresentationController Controller) CreateWorldVisual(
        PlayerHost player, PlayerAnimationProfile profile, WorldMaterialFactory? gloveMaterials,
        WorldMaterialFactory? clankMaterials, Rac1PickupRandom random)
    {
        var avatar = Avatar ?? throw new InvalidOperationException("Selected avatar was not staged before world entry.");
        // Validate mandatory source assets before creating Godot nodes so a failed
        // world entry cannot leave unattached visuals or material handles behind.
        if (avatar.Identity.SourceGame == ObpSourceGame.Rac1)
        {
            _ = WrenchAsset ?? throw new InvalidOperationException("Selected wrench asset was not staged.");
            _ = GloveAsset ?? throw new InvalidOperationException("Selected glove asset was not staged.");
            _ = gloveMaterials ?? throw new InvalidOperationException("Selected glove materials are unavailable.");
            _ = ClankAsset ?? throw new InvalidOperationException("Selected Clank assets were not staged.");
            _ = clankMaterials ?? throw new InvalidOperationException("Selected Clank materials are unavailable.");
        }
        var controller = CreateController(avatar);
        if (controller is not Rac1PlayerAnimationPresentationController native ||
            profile.AdapterId != PlayerHostProfiles.Animation)
            throw new InvalidOperationException("Selected native animation adapter did not produce its admitted physics controller.");
        controller.SetAnimationState(player.AnimationState);
        var view = new PlayerAvatarView(avatar, controller.Current, alignGeometricBase: true) { Name = "PlayerAvatar" };
        try
        {
            if (avatar.Identity.SourceGame == ObpSourceGame.Rac1)
            {
                WrenchView = new(WrenchAsset!, avatar);
                view.AddChild(WrenchView);
                GloveView = new(GloveAsset!, avatar, gloveMaterials!);
                view.AddChild(GloveView);
                ClankView = new(ClankAsset!, avatar, clankMaterials!, random);
                player.Rac1BackpackReady = false;
                view.AddChild(ClankView);
            }
        }
        catch
        {
            view.Free();
            WrenchView = null;
            GloveView = null;
            ClankView = null;
            throw;
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

    /// <summary>Attach only the staged session-selected avatar, independently of destination.</summary>
    internal void AttachWorldVisual(PlayerHost player, WorldBindings bindings)
    {
        if (bindings.Session() is not { } session) return;
        var selected = session.Capture().Configuration.Avatar;
        if (selected.SourceGame == ObpSourceGame.Synthetic)
        {
            GD.Print("[player-avatar] EXPLICIT DIAGNOSTIC CAPSULE");
            return;
        }
        var avatar = Avatar ?? throw new InvalidOperationException("Selected avatar was not staged before world entry.");
        ClearWorldVisual(bindings.Audio(), bindings.Equipment);
        var glove = selected.SourceGame == ObpSourceGame.Rac1
            ? GloveAsset ?? throw new InvalidOperationException("Selected glove asset was not staged.")
            : null;
        var materials = glove is null ? null : new WorldMaterialFactory(bindings.World() with { Textures = glove.Textures });
        var clankMaterials = ClankAsset is null ? null : new WorldMaterialFactory(bindings.World() with { Textures = ClankAsset.Textures });
        var (view, _) = CreateWorldVisual(player, bindings.AnimationProfile(), materials, clankMaterials,
            bindings.Audio()?.Random ?? Rac1PickupRandom.FromSeed(0x52414331));
        if (selected.SourceGame == ObpSourceGame.Rac1)
        {
            if (bindings.Equipment.Assets(bindings.Ordinary(),
                (bindings.Gameplay() ?? throw new InvalidOperationException("R&C1 gameplay requires an active scene lease.")).Loot.PickupAssets) is not null)
                InstallStagedBomb(bindings.Weapons().CreateNativeEffect(121, "Rac1RetailStagedBomb_Class121"));
            bindings.Gameplay()?.UsePresentation.AttachViews(view);
        }
        // Shared weapon views belong to any selected avatar, not just RAC1.
        bindings.Weapons().AttachViews(view);
        player.VisualRoot.ReplaceVisual(view);
        player.ConfigureAvatarPresentation((float)avatar.AnimationBounds.Height);
        Subscribe(player, (_, state, input) => StepPlayerAvatarPhysics(bindings, state, input),
            () => ResetPlayerAvatarPhysics(bindings));
        bindings.Equipment.BindSelected(this, bindings.EquipmentSources(), bindings.Equipment.NativeViews.Assets, bindings.Weapons().Sources.Assets);
        bindings.Equipment.RefreshRac1HandVisibility(bindings.Hand(), this);
        HideAuthoredWorldRatchetPresentation(bindings.Destination(), bindings.Scene());
        GD.Print($"[player-avatar] attached selected {avatar.Identity.ModelId}: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
    }


    private void StepPlayerAvatarPhysics(
        WorldBindings bindings, PlayerAnimationState state, Rac1LocomotionAnimationInput input)
    {
        if (Controller is not Rac1PlayerAnimationPresentationController controller)
            return;
        bindings.CombatInput().StepInput();
        bindings.Gameplay()?.Bombs.StepUse();
        bindings.Gameplay()?.UsePresentation.StepAdditional();
        bindings.Weapons().Step();
        input = bindings.Gameplay()?.Weapons.ThrownAnimation.Select(input,
            bindings.Alive(), bindings.Player()?.Rac1WrenchFrame.Active == true,
            CurrentSourceSequence(), controller.TraversalAuthoredAnimationTime) ?? input;
        StepNative(state, input);
        StepJointEases(bindings, controller, input);

        bindings.Equipment.ApplyAvatarEquipmentPose(this, controller, state, input,
            new(bindings.Hand(), bindings.Use.UseTicks, bindings.Use.UseAge,
                bindings.Gameplay()?.Weapons.Walloper.Age ?? 0,
                bindings.Alive(), bindings.Weapons().Sources.Assets,
                bindings.Weapons().Pose.UseStarted, bindings.Weapons().Pose.TriggerHeld));
        if (PhysicsPlayer is { } player)
        {
            StepHandAttachments(controller, player.Rac1WrenchFrame,
                (bindings.Gameplay()?.Wrench.ThrowActive == true),
                bindings.Gameplay()?.Weapons.ThrownAnimation.Generation ?? 0);
            bindings.Gameplay()?.UsePresentation.StepViews(controller);
            bindings.Weapons().StepViews(controller, Avatar);
            if (ClankView is { } clank && controller.CurrentAttachmentPose(Rac1ClankPackAssets.SocketKey) is { } back)
            {
                var selection = input.Traversal ?? input.GroundEntry;
                clank.Visible = bindings.Alive() && !player.DevelopmentControls.FlyEnabled;
                clank.Step(back, new(player.Rac1PresentedPackId, controller.AudioNativeAction,
                    controller.AnimationSoundSample, controller.Current.ClipStartedAtSeconds, controller.AnimationPlaybackMultiplier,
                    selection?.EntryFrame ?? 0, selection?.BlendTicks ?? 7,
                    controller.JointEases?.Angle(3, 2) ?? 0, controller.JointEases?.Angle(3, 1) ?? 0, input.NativeYawVelocity),
                    player.Rac1ClankAvailable || player.Rac1DevelopmentPackPreview.HasValue);
                player.Rac1BackpackReady = clank.Session.Ready;
                clank.StepEffects(back, player.ClankPackEffectsInput(controller));
            }
        }


        GodotRac1PlayerAudioPlayback.Step(bindings.Audio(), controller, bindings.Player(), bindings.MovementLoading());
        bindings.Gameplay()?.Bombs.StepPhysics();
        bindings.Gameplay()?.Effects.StepAdditionalWeaponWorld();
        bindings.Gameplay()?.Effects.StepBombEffectsPhysics();
        SampleClock();
        bindings.Equipment.RefreshRac1HandVisibility(bindings.Hand(), this);
    }


    // Ratchet's procedural joints, including the held-weapon wall response (0x20a990): guns with
    // arm animations are raised when a wall is within about 1.3 units ahead.
    private void StepJointEases(WorldBindings bindings, Rac1PlayerAnimationPresentationController controller,
        Rac1LocomotionAnimationInput input)
    {
        var hand = bindings.Hand();
        int slots = bindings.Equipment.HeldArmSlots(hand);
        bool carries = slots != 0;
        float? wall = null;
        if (carries && PhysicsPlayer is { } player)
        {
            bool active = bindings.Use.UseTicks > 0;
            var (height, length) = controller.JointEaseWallProbe(active);
            wall = player.ProbeRac1ForwardWall(height, length, ignoreMovables: active);
        }
        // The held gun's arm layers (0x20dd20): one arm for one-handed guns, both for the big ones.
        controller.StepArmHold(slots);
        controller.StepJointEases(input.YawError, carries, wall);
    }

    private void ResetPlayerAvatarPhysics(WorldBindings bindings)
    {
        bindings.Weapons().ClearTransients();
        NativeController?.ResetJointEases();
        ClankView?.Reset();
        if (PhysicsPlayer is { } player) player.Rac1BackpackReady = false;
        bindings.Audio()?.Reset();
        Rac1WrenchHost.ResetOrFallback(bindings.Gameplay()?.Wrench, bindings.Audio(), bindings.Player());
        bindings.Gameplay()?.Weapons.ThrownAnimation.Stop();
        ResetClock();
    }

    internal void ClearWorldVisual(Rac1PlayerAudioPresenter? audio, PlayerEquipmentPresentationBinder equipment)
    {
        audio?.Reset();
        Retire();
        equipment.NativeViews.ClearViews();
        equipment.ClearBindings();
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
        player.Rac1LongJumpAnimationCompleted = controller.LongJumpAnimationCompleted;
        player.Rac1WaterAnimationFacts = controller.WaterAnimationFacts;
        player.Rac1WrenchAnimationBlending = controller.TraversalSample?.IsBlending ?? false;
        player.Rac1CrankAnimationSequence = controller.TraversalSample?.Sequence;
        if (WrenchView is not null && controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey) is { } hand)
            WrenchView.StepAttached(hand, wrench, wrenchThrown, player.Rac1CrankActive);
        if (GloveView is not null &&
            controller.CurrentAttachmentPose(Rac1BombProjectileSession.GloveSocketKey) is { } gloveHand)
        {
            GloveView.StepAttached(gloveHand, bombGeneration, controller.CurrentHandPose());
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

    /// <summary>Only the imported duplicate world actor is hidden; source data stays intact.</summary>
    internal void HideAuthoredWorldRatchetPresentation(ObpDestination? destination, RuntimeWorldScene.Result? scene)
    {
        if (destination?.Game != ObpSourceGame.Rac1 || scene?.AnimatedMeshes is null)
            return;

        int hidden = 0;
        foreach (var animated in scene.AnimatedMeshes)
        {
            if (!animated.Name.StartsWith("ratchet_i", StringComparison.Ordinal))
                continue;
            animated.Instance.Visible = false;
            hidden++;
        }

        if (hidden > 0)
            GD.Print($"[player-avatar] hid {hidden} authored Ratchet surface(s) from world presentation");
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
            else if (View is not null && GodotObject.IsInstanceValid(View)) View.QueueFree();
        }
        else if (View is not null && GodotObject.IsInstanceValid(View)) View.QueueFree();
        _physicsPlayer = null; _step = null; _reset = null;
        Sink = null; Controller = null; View = null;
        WrenchView = null; GloveView = null; StagedBombView = null;
        ClankView = null;
        AnimationTime = 0; Clock = null;
    }
}
