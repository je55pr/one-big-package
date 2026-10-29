using OBP.Runtime.Player;
using OBP.RAC1.Player;
using OBP.Godot.Player;
using OBP.RAC1.Presentation;
using OBP.IO;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Game.Diagnostics;

namespace OBP.Game;

public partial class OBPGame
{
    private WorldSceneLifetime<GodotWorldResources>? _worldScenes;
    private WorldSceneLifetime<GodotWorldResources> WorldScenes => _worldScenes ??=
        new(RetireWorldBindings, ActivateOwnedWorld, () => _worldEntries?.Unload());
    private RuntimeSpawn? _worldPlayerEntry => _worldScenes?.PlayerEntry;
    private float _worldEntrySnapReach => _worldScenes?.EntrySnapReach ?? 8;

    private sealed class WorldSource(Func<WorldEntryRequest, PreparedWorldEntry> prepare) : IWorldEntrySource
    {
        public PreparedWorldEntry Prepare(WorldEntryRequest request) => prepare(request);
    }

    private bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination,
        string? sourcePath, FusionLabDefinition? lab = null)
    {
        _worldScenes?.CancelPreparation();
        if (!Sources.RequireOrdinary("world entry")) return false;
        try
        {
            var worldSourceStamp = SourceFileStamp(sourcePath);
            PrepareSelectedPlayerAssets();
            CaptureReplay.PrepareControllers(() => { _playerNative!.ResetReplayControllers(); FusionWeapons.BindPersistent(_playerNative.Weapons); });
            var request = _worldEntries!.Request(destinationId, PlayerEntryMode);
            var source = new WorldSource(entry =>
            {
                var world = load();
                if (_args.MaktarDispenserPreview is { } previewIndex)
                {
                    if (sourcePath is null) throw new InvalidDataException("Dispenser preview requires an attached R&C2 disc.");
                    using var previewSource = new FileRandomAccessReader(sourcePath);
                    world = OBP.RAC2.Rac2WorldImport.WithMaktarDispenserPreview(
                        previewSource, world, previewIndex, _args.MaktarDispenserAnimate);
                }
                var spawn = _args.MaktarArenaStart
                    ? OBP.RAC2.Rac2MaktarArenaStaging.CreateValidatedEntry(world)
                    : ValidateSourceEntry(world);
                return new(world, spawn, []); // Mixed native actor adapters are registered by #256.
            });
            var plan = _worldEntries.Prepare(request, source, PlayerAvatarPresentationHost.Registry);
            using var staged = WorldScenes.Prepare(plan, nativeDestination, lab, SourceEntrySnapReach(plan.Prepared.World),
                () => new GodotWorldResources(WorldHost.PrepareScene(plan.Prepared.World, destinationId, WorldOptions(plan.Prepared.World))));
            // Recheck attachments before the destructive boundary. Native source
            // data and selected player assets are independent dependencies.
            PrepareSelectedPlayerAssets();
            if (SourceFileStamp(sourcePath) != worldSourceStamp)
                throw new InvalidDataException("The world source changed during preparation. Reattach it in Game Sources.");
            if (!Sources.RequireOrdinary("world activation"))
                return false;
            _worldEntries.Commit(plan, WorldScenes);
            _lastCommittedProfileDestination = destinationId;
            if (nativeDestination is not null)
            {
                var scenarios = WorldSmokeHarness.Create(_args, nativeDestination, _worldSwitches,
                    new WorldSmokeHarness.Entrypoints(definition => CreateRac1EncounterDiagnosticHarness().CreateVeldin(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateMovementContact(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateCombat(definition),
                        definition => CreateRac1CampaignDiagnosticHarness().CreateCampaign(definition), definition => CreateMovementDiagnosticHarness().CreateMovement(definition),
                        definition => CreateRac1LootDiagnosticHarness().CreateCrates(definition), definition => CreateRac1LootDiagnosticHarness().CreatePlacedBolts(definition), definition => CreateWeaponDiagnosticHarness().CreateWrench(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateEnemies(definition), definition => CreateCaptureDiagnosticHarness().CreateMovement(definition), definition => CreateWeaponDiagnosticHarness().CreateBomb(definition),
                        definition => CreateMovementDiagnosticHarness().CreateSwim(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreatePeckbot(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateBirdbot(definition),
                        definition => CreateRac1WorldRouteDiagnosticHarness().CreateMusic(definition), definition => CreateWeaponDiagnosticHarness().CreateWeapons(definition), definition => CreatePlayerDiagnosticHarness().CreateAudio(definition),
                        definition => CreateRac1WorldRouteDiagnosticHarness().CreateTaxi(definition), definition => CreateRac1CampaignDiagnosticHarness().CreateMission(definition), definition => CreateRac1WorldRouteDiagnosticHarness().CreateRoute(definition),
                        definition => CreateRac1EncounterDiagnosticHarness().CreateFrogs(definition),
                        definition => new Rac3RatchetSeedScenario(definition,
                            new GodotRac3RatchetSeedFixture(_worldScenes?.Current?.Resources.Rac3Gameplay, () => _player).Sample,
                            message => GD.Print(message))), Sources.Setup.Access != SourceAccessMode.ExplicitDiagnostic, TrilogySourceDefinitions.All);
                var runner = new DiagnosticScenarioRunner(
                    new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source =>
                        new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                    new GodotDiagnosticFrameClock(this),
                    new ExistingDiagnosticWorld(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations.Token));
                DiagnosticScenarioDispatch.Start(scenarios, _hostOperations, runner,
                    (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message));
                StartRequestedSourceVerification(nativeDestination, sourcePath!);
            }
            SaveSharedProfile("world transition");
            if (_args.SourceSetupCheck && _worldSwitches == 1) CreateUiDiagnosticHarness().Start("source-setup-check");
            if (_captureReplay?.Recorder is { HasEntered: true }) CaptureReplay.RecordCommand(new("destination", destinationId));
            _captureReplay?.Recorder?.WorldEntered();
            if (_args.FusionSmoke && _worldSwitches == 1 && !_args.PlaytestRequested) CreateFusionWeaponDiagnosticHarness().Start();
            if (_worldSwitches == 1 && PlayerSessionDiagnosticHarness.IsRequested(_args)) CreatePlayerDiagnosticHarness().StartRequested();
            if (_args.TrilogyGameplaySmoke && _worldSwitches == 1) CreateCaptureDiagnosticHarness().StartTrilogy();
            return true;
        }
        catch (Exception error)
        {
            AbandonFailedRac1Travel();
            DestinationError($"{destinationId} could not load: {error.Message}");
            if (_world is null)
            {
                ShowDestinationSelector();
                _navigation.SetSelectorHint(error.Message);
            }
            if (_args.CaptureFrame is not null || (_args.PlayerSessionSmoke || _args.PlayerCapabilitiesSmoke) && _world is null)
                ApplicationLifecycle.RequestQuit(this, "world-entry-failed", 2);
            return false;
        }
        finally
        {
            _avatarPresentation.DiscardPrepared();
            _equipmentPresentation.DiscardPrepared();
        }
    }

    private void ActivateOwnedWorld(WorldSceneLease<GodotWorldResources> entry)
    {
        // The previous world has been retired. Only now may prepared source
        // data replace the active avatar and equipment presentation bank.
        if (_avatarPresentation.HasPrepared && _avatarPresentation.ApplyPrepared())
        {
            _equipmentPresentation.InvalidateSource();
            _rac1EquipmentAssets = null;
            _rac1EquipmentSource = null;
        }
        _avatarSourceStamp = _avatarPresentation.CommittedSourceStamp;
        if (OrdinaryPlayerSession) _equipmentPresentation.ApplyPreparedBank();
        entry.Resources.EffectScope = entry.Plan.Request;
        ClearBootstrapSceneArtifacts();
        entry.Resources.PlayerLife = PlayerLife.BindWorld(entry.Plan.Request.WorldInstanceId, entry.Plan.Request.Generation, entry.Resources.Scene.Root);
        _navigation.RetireMenuPages();
        Sources.RetireView();
        entry.Resources.Rac1Gameplay = CreateRac1WorldModule(entry.Plan.Prepared.World, entry.Resources.Scene);
        entry.Resources.Rac2Gameplay = CreateRac2WorldModule(entry.Plan.Prepared.World, entry.Resources);
        entry.Resources.Rac3Gameplay = Rac3WorldModule.CreateForWorld(entry.Plan.Prepared.World, entry.Resources.Scene);
        ActivateWorld(entry.Plan.Prepared.World, entry.Resources, entry.NativeDestination, entry.Lab);
        entry.Resources.BindPlayer(_player);
    }

    private void ReportWorldPreparationFailure(Exception error, bool superseded)
    {
        GD.PrintErr($"[lifecycle] deferred world preparation failed (superseded={superseded}): {error}");
        if (!superseded && IsInsideTree()) ApplicationLifecycle.RequestQuit(this, "world-preparation-failed", 2);
    }

    private static (long Length, long Written)? SourceFileStamp(string? path)
    {
        if (path is null) return null;
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The world source is missing. Reattach it in Game Sources.");
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private void StartRequestedSourceVerification(ObpDestination destination, string sourcePath)
    {
        if (_args.VerifyHash && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac2)
            SourceVerification.Start(ObpSourceGame.Rac2);
    }

    private static RuntimeSpawn ValidateSourceEntry(RuntimeWorld world)
    {
        if (world.Game == ObpSourceGame.Rac1 && world.PlayerStart is null)
            throw new InvalidDataException("R&C1 world is missing its authored player start.");
        return WorldEntryPlanning.ResolveEntryPoint(world);
    }

    // World-local authored placement clearance, not a player capability/profile.
    private static float SourceEntrySnapReach(RuntimeWorld world) =>
        world.Game == WorldContentIdentity.FusionLab || (world.Game == ObpSourceGame.Rac2 && world.LevelId == 0 && world.PlayerStart is not null) ? .75f : 8f;

    private void ActivateWorld(RuntimeWorld world, GodotWorldResources resources,
        ObpDestination? destination, FusionLabDefinition? lab)
    {
        _worldSwitches++;
        BeginSourceWorldEntry(world);
        resources.Load(this, _worldRoot, world, _currentDestinationId!, WorldOptions(world));
        SetupOverlay(resources.Scene, world);
        resources.Rac2Gameplay?.Configure();
        bool staticCamera = _args.ShotsPath is not null || _args.CaptureFrame is not null &&
            (_args.DirectLoad || _args.Destination is not null) && _args.TestScene != "player" &&
            !_args.AnimSolo && !_args.CrateFocus && !_args.CrateAutoStrike;
        if (!staticCamera)
        {
            Rac1World.Configure();
            resources.Rac3Gameplay?.Configure();
        }
        resources.FusionWeapons = FusionWeapons.BindWorld(resources.Scene.Root);
        if (_args.AnimSolo) FrameAnimatedMobies(world);
        else if (staticCamera) FrameShowcaseCamera(world);
        else
        {
            SpawnPlayer(world);
            if (_player is { } player) resources.Rac2Gameplay?.Arm(player);
        }
        _mode = Mode.World;
        _sceneKind = lab is null ? "destination" : "fusion-lab";
        _navigation.WorldActivated(_args.CaptureFrame is null);
        if (lab is not null)
        {
            DecorateFusionLab(lab, _avatarPresentation.View is not null);
        }
        ActorHost.Configure(OrdinaryPlayerSession, PlayerLife.WorldId, PlayerLife.Generation, lab, resources.Scene.Root);
        var effectScope = resources.EffectScope ?? throw new InvalidOperationException("Effects require the existing world lease.");
        try
        {
            resources.Effects = new(resources.Scene.Root, worldId: effectScope.WorldInstanceId,
                generation: effectScope.Generation, camera: () => GetViewport().GetCamera3D(),
                sources: () => Sources.Setup.Library.Attached.Values.ToArray(),
                actorAlive: key => ActorHost.IsAlive(key),
                placementOffset: () => lab is not null ? System.Numerics.Vector3.Zero :
                    WorldEffectHost.Runtime((_player?.GlobalPosition ?? Vector3.Zero) + EquipmentForward * 6 + Vector3.Up * 8) - new System.Numerics.Vector3(0, 0, 18),
                suspended: () => MovementReproduction.IsLoading || _captureReplay?.Recorder is { WaitingForEntry: true } or { PreparingWorld: true },
                recordOperation: operation => CaptureReplay.RecordCommand(new("effects", operation)),
                commandAllowed: () => _captureReplay?.Recorder is null || EffectCaptureContract.Includes(_args, _captureReplay!.Recorder!.Reader?.Header));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            resources.EffectsFailure = "Native effects setup is unavailable (" + error.GetType().Name + "). Check the experiment definition.";
            GD.PrintErr("[effects] " + resources.EffectsFailure);
        }
        EnsurePlayerHud(); EnsureWorldHud();
        if (OrdinaryPlayerSession) PlayerLife.PublishHud();
        UpdatePlayerHud(); UpdateWorldHud();
        CommitSourceWorldEntry(world);
        if (_args.EffectsSmoke && !_args.PlaytestRequested && _worldSwitches == 1)
        {
            string report = _args.AcceptanceOut ?? ProjectSettings.GlobalizePath("res://captures/effects-285/report.json");
            var effectsFixture = new GodotEffectsFixture(this, () => _worldScenes?.Current?.Resources.Effects,
                () => _worldScenes?.Current?.Resources.Scene.Root,
                () => System.Text.Json.JsonSerializer.Serialize(new
                {
                    Configuration = _playerSession?.Capture().Configuration,
                    Loadout = _playerSession?.Capture().Loadout,
                    Actors = ActorHost.ReadActors().Select(actor => new { actor.Registration.Instance, actor.State.Health }).ToArray(),
                }), Path.GetDirectoryName(Path.GetFullPath(report))!, () => _worldScenes?.Current?.Resources.EffectsFailure);
            var definition = new DiagnosticScenarioDefinition("effects-room/v1", DiagnosticSourceAccess.OrdinaryPlay, DiagnosticSetup.ExistingWorld,
                [.. TrilogySourceDefinitions.All.Select(s => new DiagnosticSourceRequirement(s.Game, s.Authority.BuildId))], TimeSpan.FromMinutes(10), 30000, "obp:fusion-lab");
            var scenario = new EffectsExperimentScenario(definition, effectsFixture,
                new EffectDiagnosticNavigation(id => id == "obp:fusion-lab" ? EnterFusionLab() :
                    TrilogyWorldProviders.Resolve(id) is { } next && OnDestinationChosen(next)),
                new DiagnosticSourceAttachmentFixture(Sources.Setup.Library, () => { }), report, message => GD.Print(message));
            var runner = new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(s => new DiagnosticSourceSnapshot(s.Game, s.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this), new ExistingDiagnosticWorld(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations.Token));
            DiagnosticScenarioDispatch.Start([scenario], _hostOperations, runner, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message));
        }
        _loadSummary = $"✓ {destination?.DisplayName ?? (lab is not null ? "Fusion Lab" : world.DisplayName)} · {resources.Scene.MeshInstances} meshes";
        GD.Print($"[destinations] world ready — {_loadSummary} (switch #{_worldSwitches}, player generation {_playerSession!.Capture().Generation})");
    }

    private WorldHost.Options WorldOptions(RuntimeWorld world)
    {
        bool framedCapture = _args.CaptureFrame is not null
            && (_args.DirectLoad || _args.Destination is not null)
            && _args.TestScene != "player"
            && !_args.AnimSolo
            && !_args.CrateFocus && !_args.CrateAutoStrike;
        return new WorldHost.Options
        {
            // Pink placeholders are diagnostics for unresolved native
            // models, not retail scenery. Never show them in normal play or
            // capture unless explicitly requested for a debug world.
            IncludeMobyMarkers = _args.MobyMarkers && _args.ShotsPath is null &&
                !(_args.CaptureFrame is not null && !framedCapture),
            IncludeSky = !_args.AnimSolo,
            IncludeCollision = true,
            AdmitCollisionFace = world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 }
                ? Rac1NovalisWaterMechanism.AdmitCollisionFace
                : null,
            ShowCollisionDebug = _args.CollisionDebug,
            OnlyAnimatedMobies = _args.AnimSolo,
        };
    }

    private void BeginSourceWorldEntry(RuntimeWorld world)
    {
        if (world.Game != ObpSourceGame.Rac1) return;
        _rac1Campaign.Session.StartLevelCheckpointSession(world.LevelId, world.PlayerStart!);
        WorldScenes.OverrideAuthoredEntry(_rac1Campaign.Session.LevelCheckpoint!.AuthoredClass0);
    }

    private void CommitSourceWorldEntry(RuntimeWorld world)
    {
        if (world.Game != ObpSourceGame.Rac1) return;
        var entry = _rac1Campaign.Session.CommitLoadedLevel(world.LevelId);
        _rac1Campaign.Session.FinishLoadedLevel(entry);
        if (entry == Rac1LevelEntryKind.CampaignTravel) PersistRac1CampaignState("completed campaign travel");
    }


    private void PrepareSelectedPlayerAssets()
    {
        EnsurePlayerSession();
        RefreshPlayerSourceAvailability();
        var selected = _playerSession!.Capture().Configuration;
        _persistentPlayer.ResolvePresentationProfiles();
        if (Engine.PhysicsTicksPerSecond != _playerAnimationProfile!.NativeTicksPerSecond)
            throw new InvalidOperationException($"Selected player animation requires {_playerAnimationProfile.NativeTicksPerSecond} physics ticks per second.");
        var identity = selected.Avatar;
        if (identity.SourceGame == ObpSourceGame.Synthetic) return;
        var availability = _playerSession.Availability(identity.SourceGame, identity.BuildId, true, true);
        if (!availability.Usable) throw new InvalidOperationException(availability.Reason);
        var source = Sources.Setup.Library.Get(identity.SourceGame)!;
        var file = new FileInfo(source.Path);
        if (!file.Exists || file.Length != source.SizeBytes)
            throw new InvalidOperationException("The selected avatar source is missing or has changed size. Reattach it in Game Sources.");
        using (var reader = new FileRandomAccessReader(source.Path))
        {
            var probe = Sources.Setup.Library.Probe(reader);
            if (!probe.Supported || probe.Definition?.Authority.BuildId != identity.BuildId)
                throw new InvalidOperationException("The selected avatar source no longer identifies as the selected supported build.");
        }
        _avatarPresentation.Prepare(identity, source.Path, (file.Length, file.LastWriteTimeUtc.Ticks));
        if (identity.SourceGame == ObpSourceGame.Rac1)
            _equipmentPresentation.PrepareSelectedBank(source.Path, OrdinaryPlayerSession,
                _avatarPresentation.PreparedSourceChanged);
    }


    private PlayerSessionConfiguration SelectedPlayerConfiguration => _playerSession!.Capture().Configuration;



    /// <summary>
    /// Bind the staged, session-selected presentation independently of destination.
    /// Missing required assets fail entry; the capsule is an explicit diagnostic.
    /// </summary>

    private void AttachPlayerAvatarVisual(PlayerHost player)
    {
        if (_playerSession is null) return;
        var selected = SelectedPlayerConfiguration.Avatar;
        if (selected.SourceGame == ObpSourceGame.Synthetic)
        {
            GD.Print("[player-avatar] EXPLICIT DIAGNOSTIC CAPSULE");
            return;
        }
        var avatar = _avatarPresentation.Avatar ?? throw new InvalidOperationException("Selected avatar was not staged before world entry.");
        ClearPlayerAvatarView();
        var glove = selected.SourceGame == ObpSourceGame.Rac1
            ? _avatarPresentation.GloveAsset ?? throw new InvalidOperationException("Selected glove asset was not staged.")
            : null;
        var materials = glove is null ? null : new WorldMaterialFactory(_world! with { Textures = glove.Textures });
        var (view, controller) = _avatarPresentation.CreateWorldVisual(player, _playerAnimationProfile!, materials);
        if (selected.SourceGame == ObpSourceGame.Rac1)
        {
            if (_equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is not null)
                _avatarPresentation.InstallStagedBomb(FusionWeapons.CreateNativeEffect(121, "Rac1RetailStagedBomb_Class121"));
            AttachRac1EquipmentViews(view);
        }
        // Shared weapon views belong to any selected avatar, not just RAC1.
        FusionWeapons.AttachViews(view);
        player.VisualRoot.ReplaceVisual(view);
        player.ConfigureAvatarPresentation((float)avatar.AnimationBounds.Height);
        _avatarPresentation.Subscribe(player, StepPlayerAvatarPhysics, ResetPlayerAvatarPhysics);
        _equipmentPresentation.BindSelected(_avatarPresentation, _persistentPlayer.Equipment.Sources, _rac1EquipmentAssets, FusionWeapons.Sources.Assets);
        RefreshRac1WrenchPresentationVisibility();
        HideAuthoredWorldRatchetPresentation();
        GD.Print($"[player-avatar] attached selected {avatar.Identity.ModelId}: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
    }


    private void StepPlayerAvatarPhysics(
        double delta, PlayerAnimationState state, Rac1LocomotionAnimationInput input)
    {
        if (_avatarPresentation.Controller is not Rac1PlayerAnimationPresentationController controller)
            return;
        StepRac1EquippedUseInput();
        StepRac1BombWeaponPhysics();
        StepRac1AdditionalWeapons();
        FusionWeapons.Step();
        input = SelectRac1BombThrowAnimation(controller, input);
        _avatarPresentation.StepNative(state, input);


        bool holdGun = _rac1EquipmentUseTicks > 0 && OBP.RAC1.Gameplay.Rac1WeaponProfiles.IsAdditional(_rac1Weapons.Equipped) &&
            OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped).BodySequence is > 0 and not 44 &&
            IsRac1HandEquipmentVisible(_rac1Weapons.Equipped) && input.Traversal is null &&
            PlayerLife.Alive;
        bool walloper = IsRac1HandEquipmentVisible(OBP.RAC1.Gameplay.Rac1WeaponId.Walloper) && _rac1WalloperAge > 0;
        bool moving = input.Active || state is PlayerAnimationState.Walk or PlayerAnimationState.Run or PlayerAnimationState.JumpRise or PlayerAnimationState.Fall;
        int? equipmentPose = walloper ? OBP.RAC1.Gameplay.Rac1ArsenalRules.WalloperSequence :
            holdGun ? (moving ? OBP.RAC1.Gameplay.Rac1ArsenalRules.MovingBodySequence(_rac1Weapons.Equipped) :
                OBP.RAC1.Gameplay.Rac1WeaponProfiles.Get(_rac1Weapons.Equipped).BodySequence) : null;
        // Jump/traversal clocks still own the body. Evaluate equipment from
        // that clock too, including the attachment when no arm bank applies.
        bool fusionPose = FusionWeapons.Session is { Selected.SourceGame: not ObpSourceGame.Rac1 } && FusionWeapons.Session.Hand.IsHolding(FusionWeapons.Session.Selected) &&
            PlayerLife.Alive && _player?.Rac1WrenchFrame.Active != true && _player?.Rac1WaterHandItemHidden != true;
        if (equipmentPose < 0) equipmentPose = null;
        if (fusionPose && FusionWeapons.Sources.Assets?.AvatarAnimations.TryGetValue(FusionWeapons.Session!.Selected, out var sourceAnimation) == true)
            _avatarPresentation.Sink?.SetAnimationPresentation(controller.ApplySourceEquipment(sourceAnimation,
                moving, input.CrouchHeld, FusionWeapons.Pose.UseStarted, FusionWeapons.Pose.TriggerHeld));
        else
        {
            controller.ClearSourceEquipment();
            _avatarPresentation.Sink?.SetAnimationPresentation(controller.ApplyEquipmentPose(equipmentPose,
                walloper ? _rac1WalloperAge / 60d : _rac1EquipmentUseAge / 60d, !walloper, fullBody: walloper || !moving));
        }

        if (_avatarPresentation.PhysicsPlayer is { } player)
        {
            _avatarPresentation.StepHandAttachments(controller, player.Rac1WrenchFrame,
                _rac1WrenchThrow.Active, _rac1BombThrowGeneration);
            StepRac1EquipmentViews(controller);
            FusionWeapons.StepViews(controller, _avatarPresentation.Avatar);
        }


        GodotRac1PlayerAudioPlayback.Step(_playerAudio, controller, _player, MovementReproduction.IsLoading);
        StepRac1BombProjectilesPhysics();
        StepRac1AdditionalWeaponWorld();
        StepRac1BombEffectsPhysics();
        _avatarPresentation.SampleClock();
        RefreshRac1WrenchPresentationVisibility();
    }


    private void ResetPlayerAvatarPhysics()
    {
        FusionWeapons.ClearTransients();
        _playerAudio?.Reset();
        ResetRac1WrenchHost();
        _rac1BombThrowActive = false;
        _avatarPresentation.ResetClock();
    }

    private int? CurrentPlayerAvatarSourceSequence() => _avatarPresentation.CurrentSourceSequence();
    private bool PlayerAvatarPresentationIsSynchronized() => _avatarPresentation.IsSynchronized;

    private void ClearPlayerAvatarView()
    {
        _playerAudio?.Reset();
        _avatarPresentation.Retire();
        _rac1EquipmentViews.Clear();
        _rac1StagedEquipment.Clear();
        _equipmentPresentation.ClearBindings();
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
            _avatarPresentation.Controller is Rac1PlayerAnimationPresentationController controller)
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
        FusionWeapons.Session?.Selected.SourceGame is not (ObpSourceGame.Rac2 or ObpSourceGame.Rac3) &&
        _rac1HeldItem.Hand.IsHolding(new(ObpSourceGame.Rac1, (int)item)) &&
        _player?.Rac1WaterHandItemHidden != true &&
        _player?.Rac1WrenchFrame.Active != true && !_rac1WrenchThrow.Active;

    private void RefreshRac1WrenchPresentationVisibility()
    {
        foreach (var pair in _rac1EquipmentViews)
            if (IsInstanceValid(pair.Value)) pair.Value.Visible = IsRac1HandEquipmentVisible(pair.Key);
        bool bombVisible = IsRac1HandEquipmentVisible(OBP.RAC1.Gameplay.Rac1WeaponId.FirstRanged);
        if (_avatarPresentation.GloveView is not null && IsInstanceValid(_avatarPresentation.GloveView))
            _avatarPresentation.GloveView.Visible = bombVisible;
        if (_avatarPresentation.StagedBombView is not null && IsInstanceValid(_avatarPresentation.StagedBombView))
            _avatarPresentation.StagedBombView.Visible = bombVisible && _rac1Weapons.FirstRangedAmmo > 0;
        if (_avatarPresentation.WrenchView is null || !GodotObject.IsInstanceValid(_avatarPresentation.WrenchView))
            return;
        _avatarPresentation.WrenchView.Visible =
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
