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

    private bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination,
        string? sourcePath, FusionLabDefinition? lab = null)
    {
        _worldScenes?.CancelPreparation();
        if (!Sources.RequireOrdinary("world entry")) return false;
        try
        {
            var worldSourceStamp = WorldEntryPreparationPolicy.SourceFileStamp(sourcePath);
            PrepareSelectedPlayerAssets();
            CaptureReplay.PrepareControllers(() => { _playerNative!.ResetReplayControllers(); FusionWeapons.BindPersistent(_playerNative.Weapons); });
            var request = _worldEntries!.Request(destinationId, PlayerEntryMode);
            var plan = _worldEntries.Prepare(request,
                WorldEntryPreparationPolicy.CreateWorldSource(load), PlayerAvatarPresentationHost.Registry);
            using var staged = WorldScenes.Prepare(plan, nativeDestination, lab, WorldEntryPreparationPolicy.SourceEntrySnapReach(plan.Prepared.World),
                () => new GodotWorldResources(WorldHost.PrepareScene(plan.Prepared.World, destinationId, WorldEntryPreparationPolicy.WorldOptions(plan.Prepared.World, _args))));
            // Recheck attachments before the destructive boundary. Native source
            // data and selected player assets are independent dependencies.
            PrepareSelectedPlayerAssets();
            WorldEntryPreparationPolicy.RequireUnchangedSourceFile(sourcePath, worldSourceStamp);
            if (!Sources.RequireOrdinary("world activation"))
                return false;
            _worldEntries.Commit(plan, WorldScenes);
            _lastCommittedProfileDestination = destinationId;
            if (nativeDestination is not null)
            {
                GodotWorldSmokeDispatch.Start(new(
                    this, _args, nativeDestination, _worldSwitches,
                    new WorldSmokeHarness.Factories(
                        CreateRac1EncounterDiagnosticHarness, CreateRac1CampaignDiagnosticHarness,
                        CreateMovementDiagnosticHarness, CreateRac1LootDiagnosticHarness,
                        CreateWeaponDiagnosticHarness, CreateCaptureDiagnosticHarness,
                        CreateRac1WorldRouteDiagnosticHarness, CreatePlayerDiagnosticHarness,
                        definition => new Rac3RatchetSeedScenario(definition,
                            new GodotRac3RatchetSeedFixture(_worldScenes?.Current?.Resources.Rac3Gameplay, () => _player).Sample,
                            message => GD.Print(message))),
                    Sources.Setup.Access != SourceAccessMode.ExplicitDiagnostic, Sources.Setup.Library,
                    new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations,
                    (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message)));
                SourceVerification.StartForWorldEntry(nativeDestination, _args.VerifyHash, _worldSwitches);
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
            _navigation.ReportWorldEntryFailure(destinationId, error, _world is not null);
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
        }
        _avatarSourceStamp = _avatarPresentation.CommittedSourceStamp;
        if (OrdinaryPlayerSession) _equipmentPresentation.ApplyPreparedBank();
        entry.Resources.EffectScope = entry.Plan.Request;
        GodotSmokeSceneFixture.RetireBootstrap(this, _worldRoot, _ui);
        entry.Resources.PlayerLife = PlayerLife.BindWorld(entry.Plan.Request.WorldInstanceId, entry.Plan.Request.Generation, entry.Resources.Scene.Root);
        _navigation.RetireMenuPages();
        Sources.RetireView();
        entry.Resources.Rac1Gameplay = CreateRac1WorldModule(entry.Plan.Prepared.World, entry.Resources.Scene);
        entry.Resources.Rac2Gameplay = CreateRac2WorldModule(entry.Plan.Prepared.World, entry.Resources);
        entry.Resources.Rac3Gameplay = Rac3WorldModule.CreateForWorld(entry.Plan.Prepared.World, entry.Resources.Scene);
        ActivateWorld(entry.Plan.Prepared.World, entry.Resources, entry.NativeDestination, entry.Lab);
        entry.Resources.BindPlayer(_player);
        WorldGameplayTickBindings.Activate(entry, new(
            MovementSuspended: () => MovementReproduction.IsLoading,
            ReplayWaitingForEntry: () => _captureReplay?.Recorder is { WaitingForEntry: true },
            PlayerDead: () => _player?.Rac1GameplayAlive == false,
            PlayerReactions: () => _playerAudio?.StepReactions(),
            ActorPhysics: _ =>
            {
                if (_player is { } player)
                    ActorHost.Step(ToNumerics(player.GlobalPosition), PlayerLife.Alive, MovementReproduction.IsLoading);
            },
            ScenePresentation: delta => _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero),
            HudPresentation: () => { UpdatePlayerHud(); UpdateWorldHud(); }));
    }

    private void ActivateWorld(RuntimeWorld world, GodotWorldResources resources,
        ObpDestination? destination, FusionLabDefinition? lab)
    {
        _worldSwitches++;
        if (_rac1Campaign.BeginWorldEntry(world) is { } authoredEntry)
            WorldScenes.OverrideAuthoredEntry(authoredEntry);
        resources.Load(this, _worldRoot, world, _currentDestinationId!, WorldEntryPreparationPolicy.WorldOptions(world, _args));
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
        if (_args.AnimSolo)
        {
            GodotWorldCaptureCamera.FrameAnimated(_camera, world);
            _activeCamera = _camera;
        }
        else if (staticCamera)
        {
            GodotWorldCaptureCamera.FrameShowcase(_camera, world);
            _activeCamera = _camera;
        }
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
        resources.BindEffects(new GodotWorldResources.EffectBindings(
            Camera: () => GetViewport().GetCamera3D(),
            Sources: () => Sources.Setup.Library.Attached.Values.ToArray(),
            ActorAlive: key => ActorHost.IsAlive(key),
            PlacementOffset: () => lab is not null ? System.Numerics.Vector3.Zero :
                WorldEffectHost.Runtime((_player?.GlobalPosition ?? Vector3.Zero) + Rac1WeaponUsePresentationHost.EquipmentForward(_player) * 6 + Vector3.Up * 8) - new System.Numerics.Vector3(0, 0, 18),
            Suspended: () => MovementReproduction.IsLoading || _captureReplay?.Recorder is { WaitingForEntry: true } or { PreparingWorld: true },
            RecordOperation: operation => CaptureReplay.RecordCommand(new("effects", operation)),
            CommandAllowed: () => _captureReplay?.Recorder is null || EffectCaptureContract.Includes(_args, _captureReplay!.Recorder!.Reader?.Header),
            LogError: message => GD.PrintErr(message)));
        EnsurePlayerHud(); EnsureWorldHud();
        if (OrdinaryPlayerSession) PlayerLife.PublishHud();
        UpdatePlayerHud(); UpdateWorldHud();
        if (_rac1Campaign.FinishWorldEntry(world))
            PersistRac1CampaignState(Rac1CampaignPersistenceHost.TravelPersistenceReason);
        GodotEffectsSmoke.StartIfRequested(_args, _worldSwitches, () => new(
            this, Sources.Setup.Library, new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations,
            () => _worldScenes?.Current?.Resources.Effects,
            () => _worldScenes?.Current?.Resources.Scene.Root,
            () => _worldScenes?.Current?.Resources.EffectsFailure,
            () => System.Text.Json.JsonSerializer.Serialize(new
            {
                Configuration = _playerSession?.Capture().Configuration,
                Loadout = _playerSession?.Capture().Loadout,
                Actors = ActorHost.ReadActors().Select(actor => new { actor.Registration.Instance, actor.State.Health }).ToArray(),
            }),
            id => id == "obp:fusion-lab" ? EnterFusionLab() :
                TrilogyWorldProviders.Resolve(id) is { } next && OnDestinationChosen(next),
            (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code),
            message => GD.Print(message), message => GD.PrintErr(message)));
        _loadSummary = $"✓ {destination?.DisplayName ?? (lab is not null ? "Fusion Lab" : world.DisplayName)} · {resources.Scene.MeshInstances} meshes";
        GD.Print($"[destinations] world ready — {_loadSummary} (switch #{_worldSwitches}, player generation {_playerSession!.Capture().Generation})");
    }

    private void PrepareSelectedPlayerAssets()
    {
        EnsurePlayerSession();
        RefreshPlayerSourceAvailability();
        _persistentPlayer.ResolvePresentationProfiles();
        _avatarPresentation.PrepareSelectedWorldEntry(_playerSession!, _playerAnimationProfile!,
            Engine.PhysicsTicksPerSecond, Sources.Setup.VerifySelectedSourceFile,
            _equipmentPresentation, OrdinaryPlayerSession);
    }

    /// <summary>
    /// Bind the staged, session-selected presentation independently of destination.
    /// Missing required assets fail entry; the capsule is an explicit diagnostic.
    /// </summary>

    private void AttachPlayerAvatarVisual(PlayerHost player) => _avatarPresentation.AttachWorldVisual(player, new(
        Session: () => _playerSession, AnimationProfile: () => _playerAnimationProfile!,
        World: () => _world!, Destination: () => _activeDestination, Scene: () => _sceneResult,
        Gameplay: () => _worldScenes?.Current?.Resources.Rac1Gameplay,
        Hand: () => Rac1HandPresentation, Player: () => _player,
        Equipment: _equipmentPresentation, Use: _persistentPlayer.Rac1Use,
        EquipmentSources: () => _persistentPlayer.Equipment.Sources,
        Weapons: () => FusionWeapons, CombatInput: () => Rac1CombatInput,
        Alive: () => PlayerLife.Alive, Ordinary: () => OrdinaryPlayerSession,
        Audio: () => _playerAudio, MovementLoading: () => MovementReproduction.IsLoading));

    private int? CurrentPlayerAvatarSourceSequence() => _avatarPresentation.CurrentSourceSequence();
    private bool PlayerAvatarPresentationIsSynchronized() => _avatarPresentation.IsSynchronized;

    private void ClearPlayerAvatarView() => _avatarPresentation.ClearWorldVisual(_playerAudio, _equipmentPresentation);
}
