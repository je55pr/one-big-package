using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.Game.Diagnostics;
using OBP.Fusion;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Owns the lazily built capture, replay, movement-reproduction and source-verification
/// services and binds them to the application's owned components. Recording formats, replay
/// validation and reproduction semantics stay in their services; this module only supplies
/// typed observation and action ports.
/// </summary>
internal sealed class CaptureComposition(ApplicationComponents components) : CompositionBase(components)
{
    private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private IReadOnlyList<ProfileSource> CaptureProfileSources() => Profiles.CaptureProfileSources();
    private ObpProfile CaptureSharedProfile() => Profiles.Capture();
    private ApplicationDiagnostics Diagnostics => Context.Diagnostics();
    // Reproduction owns save/load and forward sampling; the root exposes typed
    // world-entry and presentation ports rather than a second scene loader.
    private GodotMovementStateNotice? _movementNotices;
    internal GodotMovementStateNotice MovementNotices => _movementNotices ??= new(_host, _hostOperations,
        () => _ui, () => _pauseMenu, () => Modals.DebugOutputEnabled);
    private GodotReplayBookmarkNavigation? _replayBookmarkNavigation;
    internal GodotReplayBookmarkNavigation ReplayBookmarkNavigation => _replayBookmarkNavigation ??= new(
        () => _host.GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free(),
        (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code), MovementNotices.Report);
    private Rac1MovementWorldStateAdapter? _movementWorldState;
    private Rac1MovementWorldStateAdapter MovementWorldState => _movementWorldState ??= new(new(
        World: () => Rac1World, Scene: () => _sceneResult, Weapons: () => _rac1Weapons,
        Wallet: () => _rac1BoltWallet, AdditionalWeapons: () => _persistentPlayer.Rac1Use.Session,
        WeaponPreview: () => _persistentPlayer.WeaponPreview is not null,
        MorphedMobies: () => Rac1World.Weapons.Morph.ActiveKeys()
            .Concat(Rac1World.Weapons.SuckCannon.ChickenCargoKeys).Distinct().ToArray(),
        ClearChickenCargo: () => Rac1World.Weapons.SuckCannon.ClearChickenCargo(),
        RememberChickenCargo: key => Rac1World.Weapons.SuckCannon.RememberChickenCargo(key),
        RetireCapturedCargoPresentation: () => Rac1World.Weapons.SuckCannon.RetirePresentation(),
        HeldItemId: () => _rac1HeldItem.NativeItemId,
        MorphEnemy: node => Rac1World.Weapons.Morph.Morph(node), DisableCrateCollider: id => Rac1World.Loot.DisableRac1CrateCollider(id),
        RemovePlacedBolt: id => Rac1World.Loot.RemovePlacedBolt(id)));
    private MovementReproductionService? _movementReproduction;
    internal MovementReproductionService Reproduction => _movementReproduction ??= new(new(
        Owner: _host, HostOperations: _hostOperations, Args: () => _args,
        Player: () => _player, WorldBuildId: () => _world?.BuildId,
        DestinationId: () => _activeDestination?.DestinationId,
        OrdinaryPlayerSession: () => OrdinaryPlayerSession,
        FusionBlocked: () => FusionWeapons.MovementStateBlocked,
        HasRecorder: () => _captureReplay?.Recorder is not null,
        RequestBookmark: path => _captureReplay!.Recorder!.RequestBookmark(path),
        RejectMovementLoad: () => _captureReplay?.Recorder?.RejectMovementLoad() == true,
        OpenBookmark: (path, state) => ReplayBookmarkNavigation.Open(path, state, _args),
        CaptureWorld: MovementWorldStateCapture, PrepareWorld: PrepareMovementWorld,
        RestoreWorld: world => MovementWorldState.Restore(world),
        RestoreHeld: world => _rac1HeldItem.Restore(world.HeldItemId ?? world.CurrentItemId, _rac1Weapons),
        InitializeSources: () => Sources.Initialize(),
        VerifySource: (destination, build) =>
        {
            var source = Sources.Setup.Library.Get(destination.Game);
            return source is not null && source.Identity.BuildId == build && File.Exists(source.Path);
        },
        OpenDestination: OpenDestinationFromBootstrap,
        PauseMenuOpen: () => PauseMenuOpen, ClosePauseMenu: () => Modals.ClosePause(),
        ClearSmokeInput: GodotMovementCompatibility.Clear, SetAnalogueSmokeInput: GodotMovementCompatibility.Analogue,
        RefreshWrench: () => _equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation), RefreshHud: () => RefreshRac1HudState(),
        WorldLifetime: () => _worldScenes?.Current?.Lifetime ??
            throw new InvalidOperationException("Replay world lease is missing."),
        Frame: () => _frame,
        Quit: (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code),
        Report: MovementNotices.Report));

    private MovementReproWorld MovementWorldStateCapture() => MovementWorldState.Capture();

    private void PrepareMovementWorld(MovementReproWorld savedWorld)
    {
        _rac1Campaign.Persistence.EnsureInitialized();
        Rac1MovementWorldStateAdapter.PrepareLegacyCampaign(savedWorld, _persistentPlayer, _rac1Campaign);
    }

    private CaptureReplayCoordinator? _replay;
    internal CaptureReplayCoordinator Replay => _replay ??= CreateCaptureReplay();
    private GodotCapturePresentation? _capturePresentation;
    internal GodotCapturePresentation CaptureView => _capturePresentation ??= CreateCapturePresentation();
    private SourceVerificationCoordinator? _sourceVerification;
    internal SourceVerificationCoordinator Verification => _sourceVerification ??= new(
        game => SourceVerificationFiles.Stamp(Sources.Setup.Library.Get(game)), SourceVerificationFiles.VerifyRac2,
        () => _navigation.LoadSummary, (text, error) => { GD.Print($"[verification] {text}"); Sources.SetStatus(text, error); });

    private CaptureReplayCoordinator CreateCaptureReplay() => new(new(
        () => OrdinaryPlayerSession, () => FusionWeapons.Session?.Tick ?? 0,
        () => new(_currentDestinationId!, _world!.BuildId, GetWindow().Size.X, GetWindow().Size.Y,
            OrdinaryPlayerSession ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(
                Rac1CampaignSavePolicy.Capture(_rac1Campaign.Session.Campaign, _rac1Campaign.Session.Weapons,
                    _rac1BoltWallet, _rac1Campaign.CollectedNovalisGoldBolts, _rac1Campaign.Session.BuriedBolts)),
            JsonSerializer.SerializeToElement(CameraSettingsStore.Current), FusionWeapons.Configuration, OrdinaryPlayerSession ? CaptureSharedProfile() : null),
        () => new(_currentDestinationId, GetTree().Paused, GetWindow().Size.X, GetWindow().Size.Y,
            PauseMenuOpen, QuickSelectOpen, Rac1PlanetMapOpen, ControllerSettingsOpen,
            GetViewport().GuiGetFocusOwner() is Button button ? button.Text : GetViewport().GuiGetFocusOwner()?.GetClass(),
            _quickSelect?.SelectedWeapon,
            new(_rac1Campaign.Session.Campaign.CurrentLevel, _rac1Campaign.Session.Campaign.VisitedPlanets.Select(v => (double)v).ToArray(),
                _rac1Campaign.Session.Campaign.GalacticMap.Select(v => (double)v).ToArray(), _rac1Campaign.Session.Campaign.LevelStates.Select(v => (double)v).ToArray()),
            new(_rac1Nanotech.Probe().Nanotech, _rac1Nanotech.Probe().IsDead, _rac1BoltWallet.Balance,
                _rac1Weapons.CurrentItemId, _rac1HeldItem.NativeItemId ?? -1, _rac1HeldItem.Hand.Equipment?.ToString() ?? "wrench",
                JsonSerializer.SerializeToElement(_rac1Weapons.CapturePersistentState()), JsonSerializer.SerializeToElement(CameraSettingsStore.Current)),
            FusionWeapons.Session is { } fusion ? new(fusion.Selected.ToString(), JsonSerializer.SerializeToElement(FusionWeapons.Configuration),
                FusionWeaponSession.Weapons.Select(key => new CaptureWeaponCheckpoint(key.ToString(), JsonSerializer.SerializeToElement(fusion.Status(key)))).ToArray(),
                [fusion.Tick, fusion.ShotsFired, FusionWeapons.AcceptedHits, FusionWeapons.Projectiles.Count, FusionWeapons.EffectCount], JsonSerializer.SerializeToElement(FusionWeapons.LastDamage),
                JsonSerializer.SerializeToElement(new { fusion.CannonSession.State, Charge = fusion.CannonFrame?.ChargeUpdates ?? 0, Beam = fusion.CannonFrame?.BeamUpdatesRemaining ?? 0 }),
                FusionWeapons.Projectiles.Where(p => IsInstanceValid(p.Value)).Select(p => new CaptureProjectileCheckpoint(p.Key.Generation, p.Key.Sequence, ToNumerics(p.Value.GlobalPosition))).ToArray()) : null,
            _player is { } player && IsInstanceValid(player) ? new(ToNumerics(player.GlobalPosition), ToNumerics(player.Velocity), player.Rac1CurrentYaw, player.Rac1ControlYaw,
                player.IsOnFloor(), player.Rac1RecoveredSupportGrounded, player.Rac1MovementPhase.ToString(), player.AnimationState.ToString(),
                CurrentPlayerAvatarSourceSequence() ?? -1, _avatarPresentation.View?.CurrentFrame ?? -1, _avatarPresentation.View?.FramesPerSecond ?? 0,
                player.Rac1SmallStepDecision, JsonSerializer.SerializeToElement(_rac1Nanotech.Actions.Probe()), JsonSerializer.SerializeToElement(player.Rac1WrenchFrame),
                ToNumerics(player.Camera.GlobalPosition), ToNumerics(player.Camera.GlobalRotation)) : null,
            Rac1World.Context.MobyRuntime.Instances.Select(v => new CaptureMobyCheckpoint(v.NativeClassId, v.InstanceIndex, v.State.NativeState, v.IsActive, v.State.IsTerminalized)).ToArray(),
            (_sceneResult?.DynamicObjectNodes ?? []).Where(v => IsInstanceValid(v.Root)).Select(v => new CaptureObjectCheckpoint(v.Source.NativeClassId, v.Source.InstanceIndex,
                ToNumerics(v.Root.GlobalPosition), ToNumerics(v.Root.GlobalRotation), v.Root.Visible)).ToArray(),
            Rac1World.Enemies.Roster.Where(v => IsInstanceValid(v.Value.Body)).Select(v => new CaptureEnemyCheckpoint(v.Key, ToNumerics(v.Value.Body.Velocity), v.Value.Animation.Sequence,
                v.Value.Animation.Position, v.Value.DeathTicks, v.Value.RewardEmitted, v.Value.Buried,
                v.Value.Node.Source.NativeClassId == 1440 && Rac1World.Enemies.Class1440 is not null ? Rac1World.Enemies.Class1440.Get(v.Key).Health : null,
                Rac1World.Presentation.HostileNodes.ContainsKey(v.Key) ? Rac1World.Context.Hostiles.Probe(v.Value.Node.Source).Health : null)).ToArray(),
            [Rac1World.Presentation.Projectiles.Count, Rac1World.Loot.PickupMotions.Count, Rac1World.Weapons.Projectiles.Shots.Count, Rac1World.Weapons.Projectiles.DoomAgents.Count, Rac1World.Loot.AmmoPickups.Count, Rac1World.Loot.NanotechClouds.Count],
            Rac1World.Presentation.Projectiles.Select(v => new CaptureBombCheckpoint(v.Key, ToNumerics(Rac1LootPresentation.FromEffectNative(v.Value.Motion.Position)), ToNumerics(Rac1LootPresentation.FromEffectNative(v.Value.Motion.Velocity)),
                v.Value.Motion.NativeState, v.Value.Motion.BlastAge)).ToArray(),
            Rac1World.Weapons.Projectiles.Shots.Select(v => new CaptureShotCheckpoint(v.Id, ToNumerics(v.Position), ToNumerics(v.Step), (int)v.Weapon, v.Age, v.Grounded)).ToArray(),
            Rac1World.Weapons.Projectiles.DoomAgents.Where(v => IsInstanceValid(v.Body)).Select(v => new CaptureBodyCheckpoint(v.Id, ToNumerics(v.Body.GlobalPosition), ToNumerics(v.Body.Velocity))).ToArray(),
            Rac1World.Presentation.PickupNodes.Where(v => IsInstanceValid(v.Value)).Select(v => new CapturePositionCheckpoint(v.Key, ToNumerics(v.Value.GlobalPosition))).ToArray(),
            Rac1World.Mechanisms.Cranks is { } cranks
                ? cranks.CrankNodes.Keys.Select(v => new CaptureCrankCheckpoint(v, cranks.Progress(v))).ToArray()
                : []),
        () => new(ProfilePlayer.Capture(_playerSession!.Capture()), CaptureProfileSources(), ActorHost.ReadActors().Select(actor => new CaptureActorCheckpoint(
            actor.Registration.Instance.Target, actor.State.Position, actor.State.Alive, actor.State.Retired, actor.State.Health ?? -1, actor.State.NativeState, actor.State.NativeSequence ?? -1)).ToArray(), ActorHost.TickCount),
        () => Sources.Setup.Library.Attached.Values.Select(source => source.Identity).ToArray(), () => ActorHost.ReadReplayAdapters(),
        () => GodotCaptureEffects.Readiness(FusionWeapons.Sources.Preparation is not null, ActorHost, _player is not null && _world is not null),
        new(() => GodotCaptureEffects.Prepare(_worldScenes?.Current?.Resources), () => _worldScenes?.Current?.Resources.Effects?.ReplayIdentity,
            values => _worldScenes?.Current?.Resources.Effects?.AddCheckpoint(values)),
        new(destination => destination == "obp:fusion-lab" ? EnterFusionLab() : TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native),
            item => SelectPlayerEquipment(item).Accepted, (item, enabled) => SetPlayerGadgetEnabled(item, enabled).Accepted,
            (slot, item) => AssignPlayerQuickSelect(slot, item).Accepted, () => UnequipPlayerEquipment().Accepted,
            operation => _worldScenes?.Current?.Resources.Effects?.Control(operation).Accepted == true),
        destination => TrilogyWorldProviders.Resolve(destination) is not null,
        () => Sources.Setup.Library.Attached.Values.Select(source => SourceVerificationFiles.Stamp(source)
            ?? throw new InvalidDataException("Recording source attachment is unavailable.")).ToArray()),
        GodotCaptureReplayStartup.Environment(), CaptureBuildIdentity.Read(typeof(CaptureComposition).Assembly));

    internal PlaytestDriverPorts CreatePlaytestDriverPorts() => new(new(_host, () => _player, () => _pauseMenu), _hostOperations, () => OrdinaryPlayerSession,
        () => MovementReproduction.IsLoading, () => ReplayBookmarkNavigation.Step,
        new(() => _bugReporting?.ReplayReport, report => _bugReporting!.OpenForm(report), () => _bugReporting!.OpenInbox(), () => _bugReporting?.Close()),
        () => _world?.BuildId, () => FusionWeapons.Configuration,
        () => (_currentDestinationId!, _activeDestination?.DisplayName ?? "Fusion Lab"),
        () => new MovementReproState(MovementReproState.CurrentVersion, _currentDestinationId!, _world!.BuildId, DateTimeOffset.UtcNow,
            CaptureReplay.Build.AssemblyId, _player!.CaptureMovementRepro())
        { Profile = JsonSerializer.Deserialize<JsonElement>(CaptureSharedProfile().Encode()) },
        MovementReproduction.Report, new((fusion, token) => Diagnostics.CreateCaptureDiagnosticHarness().StartRecording(fusion, token),
            token => Diagnostics.CreateCaptureDiagnosticHarness().StartReports(token), token => Diagnostics.CreateCaptureDiagnosticHarness().StartReconciliation(token),
            token => Diagnostics.CreateCaptureDiagnosticHarness().VerifyFinishedContinuationAsync(token),
            error => Diagnostics.CreateCaptureDiagnosticHarness().Acceptance.Fail("capture-route", error, "playtest-save-failed")));

    private GodotCapturePresentation CreateCapturePresentation() => new(_host, () => new(_frame, CaptureHarness.ActiveRenderer(),
        (string)Engine.GetVersionInfo()["string"], _world?.BuildId ?? Rac2Authority.Primary.BuildId, _world?.Game,
        _world?.PlanetName, _world?.LocationName, _world?.LevelId, _worldSwitches, ToNumerics(_activeCamera.GlobalPosition), Presentation.Overlay?.StatusLine(),
        _worldHost.AnimationClockSeconds, _worldHost.AnimationStates().Select(a => new CaptureAnimationSnapshot(a.Name, a.CurrentFrame, a.FrameCount, a.Playing)).ToArray(),
        _sceneResult?.MeshInstances ?? 0, _sceneResult?.Triangles ?? 0, _sceneResult?.Textures ?? 0, _sceneResult?.CollisionBodies ?? 0, _sceneResult?.CollisionTriangles ?? 0,
        _sceneResult?.TieInstances ?? 0, _sceneResult?.ShrubInstances ?? 0, _sceneResult?.MobyInstances ?? 0, _sceneResult?.DynamicObjects ?? 0,
        _worldScenes?.Current?.Resources is { } resources && resources.CrateDiagnostics.Capture(resources.Rac2CrateBolts) is { } crates ? JsonSerializer.SerializeToElement(crates) : null,
        JsonSerializer.SerializeToElement(_worldScenes?.Current?.Resources.Rac1Gameplay?.Hud.Snapshot()), JsonSerializer.SerializeToElement(_hudState.Current),
        _player is { } player && IsInstanceValid(player) ? new(ToNumerics(player.GlobalPosition), player.IsOnFloor(), player.MovementControllerLabel, player.CameraControllerLabel,
            player.HasActiveRecoveredCamera, player.Rac1RuntimeCameraState?.ControlHeadingRadians, player.Rac1RuntimeCameraState?.PreferredDistance,
            player.Rac1RuntimeCameraState?.EffectiveDistance, player.Rac1CameraManualYaw, player.Rac1CameraManualPitch, player.Rac1CameraObstructionCorrection,
            player.Rac1CameraObstructionReleaseTicks, player.Rac1LocomotionState.ToString(), player.Rac1YawMode.ToString(), player.AnimationState.ToString()) : null),
        () => Context.SceneKind(), () => _args.CaptureOut);

    internal CaptureReplayCoordinator? ReplayIfStarted => _replay;
    internal SourceVerificationCoordinator? VerificationIfStarted => _sourceVerification;
}
