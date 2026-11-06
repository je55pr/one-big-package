using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

/// <summary>
/// Diagnostic fixture catalogue. Each factory binds a scenario harness to focused
/// observation and action ports over the owned application components. This is the
/// only place those bindings live; <c>OBPGame</c> only supplies the application components.
/// </summary>
internal sealed class ApplicationDiagnostics(ApplicationComponents components) : CompositionBase(components)
{
    private bool PlayerAvatarPresentationIsSynchronized() => _avatarPresentation.IsSynchronized;
    private void UpdateWorldHud() => Presentation.UpdateWorldHud(_worldScenes?.Current, _worldHost, _worldSwitches, Context.Mode().World);
    private void ApplyOverlaySpec(string? spec) => Presentation.ApplyOverlaySpec(spec);
    private bool SaveSharedProfile(string reason) => Profiles.Save(reason);
    private void FlushRac1CampaignSaves(string boundary) => _rac1Campaign.Persistence.FlushAndReport(boundary);
    private void TickRac1CampaignSaves(double delta) => _rac1Campaign.Persistence.TickAndReport(delta);
    private Rac1DestinationDiscoveryResult ApplyRac1CampaignProgressionEvent(int id) => Context.ApplyProgressionEvent(id);
    private Rac1PlanetTravelStartResult TravelRac1CampaignTo(int id) => _navigation.TravelRac1CampaignTo(id);
    private void RememberCommandLineSource(ObpSourceGame game, string path, bool persist = true) => Sources.Remember(game, path, persist);
    private void RefreshPlayerSourceAvailability() => Context.RefreshPlayerSources();
    private bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination, string? sourcePath) =>
        Context.EnterWorld(destinationId, load, nativeDestination, sourcePath);
    private void CloseWeaponQuickSelect(SourceWeaponKey? weapon = null) => Modals.CloseQuickSelect(weapon);
    private bool TryOpenWeaponQuickSelect(InputEvent input) => Modals.OpenQuickSelect(input);
    private bool TryClosePauseMenu() => Modals.TryClosePause();
    private bool TryOpenPauseMenu() => Modals.OpenPause();
    private bool TryHandleInteractiveBack() => _navigation.HandleBack(Modals);
    private void ShowDestinationSelector() => _navigation.ShowSelector();
    private void ShowTitleScreen() => _navigation.ShowTitle();
    private bool EnterProviderWorld(string path, ObpDestination destination) => _navigation.EnterProviderWorld(path, destination);
    private string? PlayerEquipmentConfigurationIdentity => _persistentPlayer.Equipment.ConfigurationIdentity;
    private Rac1CampaignRestoreKind _rac1CampaignRestoreKind => _rac1Campaign.Persistence.RestoreKind;
    private string? _rac1CampaignRestoreWarning => _rac1Campaign.Persistence.RestoreWarning;
    private bool _profileWriteBlocked => Profiles.WriteBlocked;
    private ObpProfile? _profileDocument => Profiles.Document;
    private string? SharedProfilePath => Profiles.SharedProfilePath;
    private bool BugReportModalOpen => _bugReporting?.IsOpen == true || _captureReplay?.Recorder?.BugInputSuspended == true;
    private PlayerHud? _playerHud => Presentation.PlayerHud;
    private Node3D _playerRoot => Presentation.PlayerRoot;
    private Node3D _worldRoot => Presentation.WorldRoot;
    private SourceWeaponKey CurrentWeaponKey => Context.CurrentWeapon();
    private GodotCapturePresentation CapturePresentation => Context.CapturePresentation();
    private WorldSceneLifetime<GodotWorldResources> WorldScenes => Context.WorldScenes();
    private enum Mode { Smoke, Picker, Selector, World, Title }
    private Mode _mode => Context.Mode() switch { { Title: true } => Mode.Title, { Selector: true } => Mode.Selector, { World: true } => Mode.World, { Picker: true } => Mode.Picker, _ => Mode.Smoke };

    // --- fixture factories -------------------------------------------------

    internal MovementDiagnosticHarness CreateMovementDiagnosticHarness()
    {
        var clock = new GodotDiagnosticFrameClock(_host);
        var ports = new MovementScenarioPorts(new GodotMovementPlayer(() => _player, () => _world),
            new GodotMovementAnimation(new(() => _avatarPresentation.View, () => _avatarPresentation.Controller, () => _avatarPresentation.Sink,
                CurrentPlayerAvatarSourceSequence, PlayerAvatarPresentationIsSynchronized, () => _avatarPresentation.Avatar?.AnimationBounds.Height ?? double.NaN)),
            new GodotMovementGeometry(_host), new GodotMovementWorld(() => _world,
                () => _sceneResult?.CollisionBodies ?? 0,
                () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.HasWater == true),
            new GodotDiagnosticInputActions(), new GodotMovementPhysicalInput(), clock, message => GD.Print(message), message => GD.PrintErr(message));
        var water = new GodotMovementWater(() => [.. Rac1World.Enemies.Roster.Values.Select(enemy => ((Node3D)enemy.Node.Root, (Node3D)enemy.Body))],
            position => _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.SampleWaterHeight(GodotMovementPlayer.Engine(position)),
            () => _playerHud?.FindChild("Air", true, false) is Control air ? air.Visible : null, _rac1Nanotech.Probe);
        var environment = new DiagnosticEnvironmentalDeathFixture(() => new(_rac1Nanotech.EnvironmentalDeathGeneration,
            _rac1Nanotech.EnvironmentalRestartGeneration, _rac1Nanotech.Probe(), Rac1World.Context.LastEnvironmentalDeathBoundary,
            GodotMovementPlayer.Copy(Rac1World.Presentation.LastEnvironmentalDeathPosition), Rac1World.Context.LastEnvironmentalDeathContactSeparation,
            _player is { } measured ? Rac1LifecycleWorldHost.MeasureContactSeparation(measured) : double.NaN,
            _rac1Campaign.Session.LevelCheckpoint?.AuthoredClass0.Yaw));
        var equipment = new DiagnosticControllerEquipmentFixture(() =>
        {
            var player = _player ?? throw new DiagnosticUnavailableException("The controller fixture player is unavailable.");
            return new(_rac1Weapons.Equipped, _rac1Weapons.FirstRangedAmmo,
                _rac1HeldItem.NativeItemId, _avatarPresentation.WrenchView?.Visible == true, _avatarPresentation.GloveView?.Visible == true,
                player.Rac1WrenchFrame.Active, player.Rac1AudioMovementAction, CurrentWeaponKey, [.. _persistentPlayer.Equipment.WeaponOrder()]);
        },
            Rac1CombatInput.Select, Rac1CombatInput.Cycle);
        return new(ports, water, environment, equipment, _activeDestination,
            [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceRequirement(source.Game, source.Identity.BuildId))],
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source =>
                new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]), clock), _hostOperations, _args.MovementCase);
    }
    internal Task CheckControllerWeaponInputAsync() => CreateMovementDiagnosticHarness().RunControllerAsync(_hostOperations.Token);

    private static string ReproPath(string name) =>
        DiagnosticReproPath.Resolve(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures"), name);
    private static void SetSyntheticNativeHeading(PlayerHost player, double yaw, double magnitude = 1) => GodotMovementCompatibility.Heading(player, yaw, magnitude);
    private static void AddSyntheticTraversalMesh(Node3D parent, string name, int material, params (Vector3 A, Vector3 B, Vector3 C)[] triangles) =>
        GodotRac1DiagnosticSurface.Add(parent, name, material, triangles);
    private static void SetAnalogueSmokeInput(float right, float forward) => GodotMovementCompatibility.Analogue(right, forward);
    private static void SetRac1SmokeConditionedInput(double right, double forward) => GodotMovementCompatibility.Conditioned(right, forward);
    private static void ClearMovementSmokeInput() => GodotMovementCompatibility.Clear();


    internal FusionWeaponDiagnosticHarness CreateFusionWeaponDiagnosticHarness()
    {
        var weapons = CreateWeaponDiagnosticHarness();
        var fixture = new GodotFusionWeaponDiagnosticFixture(_host, new()
        {
            Status = () => new(FusionWeapons.Session is not null, FusionWeapons.Sources.Assets is not null, FusionWeapons.Sources.Assets?.Problems.Count ?? -1, FusionWeapons.Sources.Preparation is not null,
                FusionWeapons.Sources.Failure, _frame, FusionWeapons.TestActive, FusionWeapons.Configuration?.LoadoutId, _captureReplay?.Recorder?.WaitingForEntry == true, _captureReplay?.Recorder is not null),
            Session = () => new(FusionWeapons.Session?.Selected, FusionWeapons.Session?.Hand.Equipment, _rac1HeldItem.Hand.WrenchHeld, FusionWeapons.Session?.LancerSession.Ammo ?? 0,
                FusionWeapons.Session?.CannonSession.Ammo ?? 0, FusionWeapons.AcceptedHits, FusionWeapons.AudioStarted, FusionWeapons.BeamVisible, FusionWeapons.Session?.CannonFrame?.BeamUpdatesRemaining ?? 0,
                FusionWeapons.LastDamage is { } damage ? new(damage.Shot.Weapon, damage.SourceDamage, damage.Kind) : null,
                _player?.Rac1WrenchFrame.Active == true, FusionWeapons.Session is not null && ReferenceEquals(FusionWeapons.Session.Rac1Inventory, _rac1Campaign.Session.Weapons),
                ReferenceEquals(_rac1Weapons, _rac1Campaign.Session.Weapons)),
            Transients = () => new(FusionWeapons.Projectiles.Count, FusionWeapons.Session?.LancerSession.ActiveShots.Count ?? 0, FusionWeapons.EffectCount, FusionWeapons.PendingAudioCount, FusionWeapons.AudioPlayerCount),
            Menu = () => new(QuickSelectOpen, _quickSelect is { } menu ? [.. menu.Entries.Select(entry => entry.Weapon)] : [], _quickSelect?.SelectedWeapon),
            NativeSession = () => FusionWeapons.Session,
            Assets = () => FusionWeapons.Sources.Assets,
            ReplaceAssets = assets => FusionWeapons.Sources.Assets = assets,
            Preparation = () => FusionWeapons.Sources.Preparation,
            Sources = () => Sources.Setup.Library,
            ReplaceSources = Sources.Setup.ReplaceDiagnosticLibrary,
            Views = () => [.. FusionWeapons.HeldViews],
            Animation = () => _avatarPresentation.Controller,
            Avatar = () => _avatarPresentation.Avatar,
            OriginalInventory = () => System.Text.Json.JsonSerializer.Serialize((FusionWeapons.OriginalInventory ?? throw new DiagnosticUnavailableException("The isolated original campaign is unavailable.")).CapturePersistentState()),
            CurrentInventory = () => System.Text.Json.JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()),
            Prepare = () => FusionWeapons.PrepareSources(force: true),
            Reset = () => FusionWeapons.ResetWorld(),
            Clear = () => FusionWeapons.ClearTransients(),
            Reenter = () => { Context.ReplaceArgs(_args with { FusionTest = false }); return OnDestinationChosen(_activeDestination!); },
            ShowDestinations = ShowDestinationSelector,
            Camera = () => _activeCamera,
            Player = () => _player,
            Forward = () => GodotMovementPlayer.Copy(Rac1WeaponUsePresentationHost.EquipmentForward(_player)),
            SourceSequence = CurrentPlayerAvatarSourceSequence,
            Held = () => _rac1HeldItem.Hand.Equipment,
        }, new GodotDiagnosticFrameClock(_host));
        return new(weapons, fixture,
            [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))],
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(_host)), _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code), message => GD.PrintErr(message));
    }

    internal Rac1CampaignDiagnosticHarness CreateRac1CampaignDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness();
        var facts = new Rac1CampaignDiagnosticFacts(() =>
        {
            var campaign = _rac1Campaign.Session.Campaign; var travel = _rac1Campaign.Session.Travel;
            return new(campaign.CurrentLevel, travel.SelectedDestination, [.. campaign.GalacticMap.Take(campaign.AdmittedDestinationCount)], [.. campaign.LevelStates], travel.TransitionActive)
            { NovalisAvailable = campaign.CanSelectDestination(1), AridiaAvailable = campaign.CanSelectDestination(2) };
        }, () =>
        {
            var state = _rac1Nanotech.Probe();
            return new(state.Nanotech, state.IsDead, state.HasRecoveredEnvironmentalRespawn, _rac1Nanotech.EnvironmentalDeathGeneration);
        }, () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Missions.Probe().SingleOrDefault(actor => actor.Key.NativeClassId == 774)?.Retired == true);
        var actions = new Rac1CampaignDiagnosticActions(ApplyRac1CampaignProgressionEvent, TravelRac1CampaignTo, () => _rac1Campaign.Session.Travel.OpenPlanetMap(),
            level => OpenDestinationFromBootstrap($"rac1:LEVEL{level}"), TickRac1CampaignSaves, FlushRac1CampaignSaves);
        var checkpoints = new GodotRac1CampaignCheckpointFixture(() =>
        {
            var checkpoint = _rac1Campaign.Session.LevelCheckpoint ?? throw new DiagnosticUnavailableException("R&C1 smoke checkpoint session is missing.");
            return new(checkpoint.NativeLevelId, checkpoint.AuthoredClass0, _world?.PlayerStart, checkpoint.ActiveCheckpoint is not null, checkpoint.ResolveEnvironmentalRestart().Kind);
        }, activation => (_rac1Campaign.Session.LevelCheckpoint ?? throw new DiagnosticUnavailableException("The checkpoint session is unavailable.")).Activate(activation),
        () =>
        {
            var player = _player ?? throw new DiagnosticUnavailableException("The checkpoint fixture player is unavailable.");
            var dead = _rac1Nanotech.ApplyEnvironmentalDeathReset(); player.Rac1GameplayState = dead;
            return new(dead.Nanotech, dead.IsDead, dead.HasRecoveredEnvironmentalRespawn, _rac1Nanotech.EnvironmentalDeathGeneration);
        }, () => TryCompleteRac1EnvironmentalRestart(automatic: false));
        var save = new Rac1CampaignSaveDiagnostics(() => new(ObpSourceGame.Rac1, Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Identity.BuildId,
            !string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath), OrdinaryPlayerSession, _rac1Campaign.Persistence.Enabled, _rac1Campaign.Persistence.HasWriter,
            _rac1CampaignRestoreWarning is not null, _rac1CampaignRestoreKind), () => _args.Rac1CampaignSavePath, () => _rac1Campaign.Persistence.SavePath);
        return new(movement.Ports, new(facts, actions, checkpoints, save, new GodotRac1MissionDiagnosticUi(() => _ui)));
    }

    internal Rac1LootDiagnosticHarness CreateRac1LootDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness(); var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var targets = new GodotRac1WeaponTargetFixture(new(() => [.. Rac1World.Presentation.CrateNodes], () => [.. Rac1World.Enemies.Roster.Values], Rac1World.Enemies.IsRac1GameplayActive,
            (node, position) => GodotRac1CrateStaging.Place(node, position, Rac1World.Loot.FallingCrates),
            id => Rac1World.Enemies.Class1440?.Get(id).Health ?? throw new DiagnosticUnavailableException("The robot health source is unavailable.")), worlds);
        var loot = new GodotRac1LootDiagnosticFixture(new()
        {
            Read = () => new(ObpSourceGame.Rac1, _rac1BoltWallet.Balance, Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet).Bolts?.Balance,
                Rac1World.Loot.PickupSoundCount, _rac1Nanotech.Probe().Nanotech, _rac1Weapons.FirstRangedAmmo, Rac1World.Context.BoltCrates.OutstandingPickupCount,
                [.. Rac1World.Loot.Debris.Select(piece => piece.Motion.NativeClassId)],
                [.. Rac1World.Loot.PickupMotions.Select(pair => new Rac1LootMotionDiagnostic(pair.Key, pair.Value.NativeClassId, pair.Value.NativeState, pair.Value.Position,
                    GodotMovementPlayer.Copy(Rac1LootPresentation.FromEffectNative(pair.Value.Position))))],
                [.. Rac1World.Loot.AmmoPickups.Select(pickup => new Rac1AmmoPickupDiagnostic(new(ObpSourceGame.Rac1, pickup.Motion.Definition.Item), pickup.Motion.Definition.Amount,
                    pickup.Motion.Position, pickup.Motion.Velocity, pickup.Motion.DelayTicks, _rac1Weapons.GetAmmo(pickup.Motion.Definition.Item)))],
                [.. Rac1World.Loot.NanotechClouds.Select(cloud => cloud.Motion.Attracting)],
                Rac1World.Loot.NanotechRiderCount, Rac1World.Loot.NanotechHealingCount,
                Rac1World.Loot.NanotechParticles?.OrbSpriteCount ?? 0,
                Rac1World.Loot.NanotechParticles?.Count ?? 0,
                System.Collections.Immutable.ImmutableDictionary.CreateRange(Rac1World.Loot.NanotechRiderOrbSprites)),
            OriginalPickupMeshes = () => Rac1World.Presentation.PickupNodes.Values.All(node => GodotWeaponModelFacts.Read(node).OriginalMeshes),
            Pending = () => Rac1World.Loot.PendingPlacedBolts.Count,
            Placed = () => Rac1World.Context.PlacedBolts,
            PlacedNodes = () => Rac1World.Loot.PlacedBoltNodes,
            Motions = () => Rac1World.Loot.PickupMotions,
            PickupNodes = () => Rac1World.Presentation.PickupNodes,
            Random = () => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed),
            ToNative = Rac1LootPresentation.ToEffectNative,
            FromNative = Rac1LootPresentation.FromEffectNative,
            Orientation = Rac1LootPresentation.FromEffectOrientation,
            CrateBody = id => Rac1World.Loot.CrateBodies.ContainsKey(id),
            Strike = node => Rac1World.WrenchDamage.TryStrikeCrate(node),
            Damage = () => _rac1Nanotech.ApplyDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 149), new(34, 1))),
        }, targets, worlds);
        return new(movement.Ports, new(loot, targets, movement.Equipment, new GodotRac1LootDiagnosticCapture(_host,
            () => _world?.LevelId ?? throw new DiagnosticUnavailableException("The loot capture world is unavailable."), () => _ui, () => _player, new GodotDiagnosticFrameClock(_host)),
            new GodotRac1BreakableDiagnosticFixture(() => Rac1World.Breakables.Observe(),
                id => Rac1World.Breakables.TouchExplosiveForDiagnostics(id),
                (id, contact) => Rac1World.Breakables.BreakNovalisForDiagnostics(id, contact), worlds)));
    }

    internal WeaponDiagnosticHarness CreateWeaponDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness();
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var targets = new GodotRac1WeaponTargetFixture(new(() => [.. Rac1World.Presentation.CrateNodes], () => [.. Rac1World.Enemies.Roster.Values],
            Rac1World.Enemies.IsRac1GameplayActive, (node, position) => GodotRac1CrateStaging.Place(node, position, Rac1World.Loot.FallingCrates),
            id => Rac1World.Enemies.Class1440?.Get(id).Health ?? throw new DiagnosticUnavailableException("The robot health source is unavailable.")), worlds);
        var wrench = new GodotRac1WrenchDiagnosticFacts(new(() => _player, () => _avatarPresentation.WrenchView, () => _avatarPresentation.Avatar,
            () => _avatarPresentation.Controller, () => Rac1World.Wrench.Generation, () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true),
            () => Rac1World.Wrench.WorldPosition, () => _avatarPresentation.GloveView?.Visible == true));
        var capture = new GodotWeaponDiagnosticCapture(_host, () => _player, () => _sceneResult?.Root, () => _ui, new GodotDiagnosticFrameClock(_host));
        var bomb = new GodotRac1BombAcceptanceFixture(new(
            () =>
            {
                var glove = _avatarPresentation.GloveView; var staged = _avatarPresentation.StagedBombView;
                bool glovePresent = glove is not null && IsInstanceValid(glove), stagedPresent = staged is not null && IsInstanceValid(staged);
                return new(glovePresent && glove!.Visible, stagedPresent && staged!.Visible,
                    _avatarPresentation.WrenchView is { } view && IsInstanceValid(view) && view.Visible,
                    _avatarPresentation.GloveAsset?.VertexCount ?? 0, _avatarPresentation.GloveAsset?.TriangleCount ?? 0,
                    glovePresent ? GodotMovementPlayer.Copy(glove!.BombLocal) : System.Numerics.Vector3.Zero,
                    stagedPresent ? GodotMovementPlayer.Copy(staged!.Position) : System.Numerics.Vector3.Zero,
                    glovePresent && stagedPresent && staged!.Position.IsEqualApprox(glove!.BombLocal));
            },
            () => new(_equipmentPresentation.Explosions.FragmentCount, _equipmentPresentation.Explosions.BurstCount, _equipmentPresentation.Explosions.Particles?.Count ?? 0, Rac1World.Bombs.ImpactCount,
                [.. _equipmentPresentation.Explosions.ParticleTypes]),
            () =>
            {
                var shot = Rac1World.Presentation.Projectiles.Single();
                bool originalModel = GodotWeaponModelFacts.Read(shot.Value.Node).OriginalMeshes;
                return new GodotRac1BombAcceptanceWitness(new(worlds), () => new(shot.Key, FusionWeaponSession.BombGlove,
                    shot.Value.Motion.Position, shot.Value.Motion.Velocity, shot.Value.Motion.NativeState, shot.Value.Motion.Flying, originalModel));
            }, (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to), (from, to) => Rac1World.Weapons.Contacts.ProbeBombWorldContact(from, to),
            amount => _rac1Weapons.TryConsumeAmmo((int)Rac1WeaponId.FirstRanged, amount), () => _player));
        var transients = new GodotWeaponDiagnosticTransientFixture(worlds, () => DiagnosticCleanup.Run("Weapon fixture transient cleanup",
            () => Rac1World.Bombs.RetireAll(),
            () => Rac1WeaponEffectsHost.ResetOrFallback(_worldScenes?.Current?.Resources.Rac1Gameplay, _equipmentPresentation), () => Rac1World.UsePresentation.ResetAdditional(), () => FusionWeapons.ClearTransients()));
        async Task<bool> LoadWeaponStateAsync(string path, CancellationToken token) => await MovementReproduction.LoadMovementStateAsync(path, cancellationToken: token) is not null;
        var reproduction = new GodotWeaponMovementStateFixture(() => MovementReproduction.IsLoading, () => MovementReproduction.CaptureMovementState(), LoadWeaponStateAsync,
            ReproPath, transients);
        return new(movement.Ports, wrench, new(movement.Equipment, targets, CreateRac1EncounterDiagnosticHarness().Combat, capture, bomb, reproduction,
            CreateRac1AdditionalWeaponDiagnosticFixture(worlds), CreateRac1ArsenalDiagnosticFixture(worlds, targets)),
            transients);
    }

    private GodotRac1AdditionalWeaponDiagnosticFixture CreateRac1AdditionalWeaponDiagnosticFixture(IDiagnosticWorldObservations worlds)
    {
        var scope = new GodotRac1DiagnosticWorldScope(worlds);
        return new(new(
        () => new(_persistentPlayer.WeaponPreview is not null, _rac1Campaign.Session.Weapons.Owns(Rac1WeaponId.Blaster), _persistentPlayer.Rac1Use.Session?.Cargo.Count ?? 0,
            _persistentPlayer.Rac1Use.UseTicks, _persistentPlayer.Rac1Use.ShotsFired, _persistentPlayer.Rac1Use.PyroEmitting, _equipmentPresentation.WeaponAudio.Loop, _hudState.Current.CurrentWeapon,
            GodotMovementPlayer.Copy(Rac1WeaponUsePresentationHost.EquipmentForward(_player)), _player?.Rac1RemoteControlActive == true, _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.GuidedCamera.Camera is not null,
            _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.GuidedCamera.Camera?.Current == true, _player?.Camera.Current == true, _equipmentPresentation.Explosions.RenderedCount, _equipmentPresentation.Explosions.DrawCalls),
        weapon => _equipmentPresentation.NativeViews.View(weapon), weapon => Rac1WeaponUsePresentationHost.EquipmentMuzzle(weapon, 0, _equipmentPresentation, _avatarPresentation, _player), item => _rac1Weapons.GetAmmo(item),
        (nativeClass, sound) => Rac1World.Loot.PickupAssets?.Sounds?.TryGetValue((nativeClass, sound), out var voice) == true && voice.Clip.InterleavedPcm16.Any(sample => sample != 0),
        () => [.. _equipmentPresentation.WeaponAudio.Events.Select(cue => new Rac1WeaponAudioDiagnostic(ObpSourceGame.Rac1, cue.Tick, cue.Class, cue.Sound, cue.Loop))],
        () =>
        {
            scope.RequireCurrent();
            var gameplay = _worldScenes?.Current?.Resources.Rac1Gameplay
                ?? throw new DiagnosticUnavailableException("R&C1 gameplay is unavailable.");
            return [.. gameplay.Weapons.Projectiles.Shots.Select(shot => new GodotWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>(scope, () => new(
                new(ObpSourceGame.Rac1, (int)shot.Weapon), shot.Id, GodotMovementPlayer.Copy(shot.Position), shot.Grounded, GodotWeaponModelFacts.Read(shot.Node))))];
        },
        () =>
        {
            scope.RequireCurrent();
            var gameplay = _worldScenes?.Current?.Resources.Rac1Gameplay
                ?? throw new DiagnosticUnavailableException("R&C1 gameplay is unavailable.");
            return [.. gameplay.Weapons.Projectiles.DoomAgents.Select(agent => new GodotWeaponDiagnosticWitness<Rac1DoomAgentDiagnostic>(scope, () => new(
                new(ObpSourceGame.Rac1, (int)Rac1WeaponId.GloveOfDoom), agent.Id, GodotMovementPlayer.Copy(agent.Body.GlobalPosition), agent.Motion.Wander,
                GodotWeaponModelFacts.Read(agent.View))))];
        }, () => _persistentPlayer.WeaponPreview is not null, disable => Rac1WeaponUsePresentationHost.RequestPreview(disable, () => FusionWeapons, () => _worldScenes?.Current?.Resources.Rac1Gameplay, MovementReproduction.Report)));
    }

    private GodotRac1ArsenalDiagnosticFixture CreateRac1ArsenalDiagnosticFixture(IDiagnosticWorldObservations worlds, GodotRac1WeaponTargetFixture targets) => new(new(
        () => new(Rac1World.Weapons.Missiles.SalvoRemaining, Rac1World.Weapons.LastArsenalId, Rac1World.Weapons.Deployables.DroneCount, Rac1World.Weapons.SuckCannon.ChickenCargoCount,
            [.. Rac1World.Weapons.Beams.VisibleSlots()], [.. Rac1World.Weapons.Beams.ShaderSlots()]),
        () => [.. Rac1World.Weapons.Missiles.Project((weapon, id, view) =>
            new GodotWeaponDiagnosticWitness<Rac1ArsenalMissileDiagnostic>(new(worlds), () => new(
                new(ObpSourceGame.Rac1, (int)weapon), id, GodotWeaponModelFacts.Read(view))))],
        () => [.. Rac1World.Weapons.Deployables.ProjectDecoys((snapshot, forceExpire) =>
            new GodotRac1DecoyDiagnosticFixture(new(worlds), () =>
            {
                var state = snapshot();
                return new(new(ObpSourceGame.Rac1, (int)Rac1WeaponId.DecoyGlove),
                    GodotWeaponModelFacts.Read(state.View), state.Grounded, state.RetiringTicks);
            }, forceExpire))],
        () => [.. Rac1World.Weapons.Morph.Project((enemy, view) => new Rac1ChickenDiagnostic(targets.Key(enemy.Node), Rac1World.Enemies.IsRac1GameplayActive(enemy.Node),
            enemy.RewardEmitted, GodotWeaponModelFacts.Read(view)))],
        () => Rac1World.Weapons.Deployables.RetireDrones(),
        () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.ResetWorldTick(),
        (actor, player) =>
        {
            targets.Read(actor); return GodotMovementPlayer.Copy(Rac1World.Weapons.Deployables.EnemyGoal(
            Rac1World.Enemies.Roster.TryGetValue(actor.Target.InstanceIndex, out var enemy) ? enemy : throw new DiagnosticUnavailableException("The arsenal fixture enemy is unavailable."),
            GodotMovementPlayer.Engine(player)));
        }));

    internal Rac1EncounterDiagnosticHarness CreateRac1EncounterDiagnosticHarness()
    {
        var actors = new GodotRac1EnemyDiagnosticFixture(new(() => [.. Rac1World.Enemies.Roster.Values],
            () => Rac1World.Enemies.Class1440, () => Rac1World.Enemies.Peckbots, () => Rac1World.Enemies.Birdbots, () => Rac1World.Context.Hostiles,
            () => new(Rac1World.Enemies.Roster.Count, Rac1World.Enemies.Class1440?.RegisteredCount ?? 0, Rac1World.Enemies.Peckbots?.RegisteredCount ?? 0,
                Rac1World.Enemies.Birdbots?.RegisteredCount ?? 0, Rac1World.Enemies.BirdbotShotCount, _rac1Nanotech.Probe().Nanotech,
                _worldSwitches, (_worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.RetryPanel ?? PlayerLife.RetryPanel)?.Visible == true), () => _player, node => Rac1World.WrenchDamage.TryStrikeHostile(node),
            Rac1World.Enemies.TryGroundRac1Enemy, Rac1EnemyHost.EnemyForward, Rac1World.Enemies.EnemySocket, Rac1World.Enemies.EnemyRayTouchesPlayer,
            id => Rac1World.Enemies.HasBirdbotShot(id)), new GodotDiagnosticWorldObservations(WorldScenes));
        var movement = CreateMovementDiagnosticHarness();
        var loot = new GodotRac1EncounterLoot(() => new(_rac1BoltWallet.Balance, Rac1World.Context.BoltPickups.OutstandingCount,
            [.. Rac1World.Loot.PickupMotions.Select(pair => new Rac1DiagnosticPickup(pair.Key, pair.Value.NativeClassId,
                pair.Value.Position, pair.Value.NativeState, pair.Value.CollectionStep))],
            [.. Rac1World.Loot.Debris.Select(piece => piece.Motion.NativeClassId)]));
        var lifecycle = new GodotRac1EncounterLifecycle(() => _rac1Nanotech.Probe(), Rac1World.Enemies.ApplyRac1EnemyPlayerDamage, () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.RetryPanel ?? PlayerLife.RetryPanel);
        var approach = new GodotRac1WrenchApproachFixture(() => _player, _persistentPlayer.Wrench.ResolveFirstSwingFacing, Rac1WorldPresentation.WrenchHostRootHeight);
        var combat = new Rac1CombatScenarioPorts(actors, actors, CreateRac1CombatDiagnosticFixture(actors), approach, movement.Equipment,
            new DiagnosticNavigationActions(destination => { OpenDestinationFromBootstrap(destination); return _world is not null && _currentDestinationId == destination; }),
            CreateUiDiagnosticHarness(), movement.Environment, lifecycle);
        return new(movement.Ports, loot,
            new GodotRac1EnemyDiagnosticCapture(_host, () => _ui, () => _player, new GodotDiagnosticFrameClock(_host)),
            CreateRac1AridiaDiagnosticFixture(actors), CreateRac1FrogDiagnosticFixture(actors), combat);
    }

    private GodotRac1CombatDiagnosticFixture CreateRac1CombatDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors) => new(actors,
        new GodotDiagnosticWorldObservations(WorldScenes), new()
        {
            Crates = () => [.. Rac1World.Presentation.CrateNodes],
            Hostiles = () => [.. Rac1World.Presentation.HostileNodes.Values],
            World = () => _world,
            Player = () => _player,
            Opening = () => new(_args.Rac1CampaignPersistenceRequested, _rac1Campaign.Persistence.Enabled, _rac1CampaignRestoreKind,
                _rac1Campaign.Session.Campaign.CurrentLevel, _rac1Campaign.Session.Campaign.AdmittedDestinationCount, _rac1Weapons.OwnsFirstRanged),
            Population = () => new(_world?.DynamicObjects?.Count(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId) ?? 0,
                Rac1World.Presentation.Class749Nodes.Count(node => IsInstanceValid(node.Root) && node.Root.Visible), Rac1World.Presentation.HostileNodes.Count, Rac1World.Context.Hostiles.RegisteredCount),
            Presentation = () =>
            {
                var wrench = _avatarPresentation.WrenchView; bool present = wrench is not null && IsInstanceValid(wrench);
                return new(_hudState.Current.CurrentWeapon, present, present && wrench!.Visible,
                    present && _avatarPresentation.View is not null && wrench!.GetParent() == _avatarPresentation.View);
            },
            Loot = () => new(Rac1World.Context.BoltCrates.DestroyedCrateCount, Rac1World.Context.BoltCrates.CollectedBolts,
                [.. Rac1World.Presentation.PickupNodes.Select(pair => new Rac1CombatPickupDiagnostic(pair.Key, IsInstanceValid(pair.Value),
                    IsInstanceValid(pair.Value) ? GodotMovementPlayer.Copy(pair.Value.GlobalPosition) : System.Numerics.Vector3.Zero, Rac1World.Context.BoltCrates.OwnsPickup(pair.Key)))]),
            Bombs = () => new(Rac1World.Context.BombFireRequested, [.. Rac1World.Presentation.Projectiles.Keys],
                Rac1World.Context.LastBombContactResolution is { } contact ? new(contact.ProjectileId, [.. contact.DamageResults]) : null),
            BombWitness = () =>
            {
                var shot = Rac1World.Presentation.Projectiles.FirstOrDefault();
                return shot.Value is null ? null : new GodotRac1BombDiagnosticWitness(() => new(shot.Key,
                    new(ObpSourceGame.Rac1, (int)Rac1WeaponId.FirstRanged), shot.Value.Motion.Position, shot.Value.Motion.NativeState));
            },
            Active = Rac1World.Enemies.IsRac1GameplayActive,
            Forward = Rac1EnemyHost.EnemyForward,
            Presence = Rac1World.Enemies.SetRac1HostedPresence,
            PrimaryAttack = Rac1CombatInput.PrimaryAttack,
            Damage = () => Rac1World.Context.DamageTransport,
        });

    private GodotRac1AridiaDiagnosticFixture CreateRac1AridiaDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors)
    {
        var aridia = Rac1World.Enemies.Aridia;
        return new(actors, new(() => Rac1World.Context.MobyRuntime, () => aridia.Session, session => aridia.Session = session,
            () => aridia.ClipVolumeCount, aridia.ClipVolumeCentre,
            aridia.ClipVolumeContains,
            aridia.Waypoint, aridia.RoamingTarget, aridia.RoamExitFlag,
            aridia.RemoveRoamingTarget,
            id => aridia.NativeLiftCount(id),
            id => aridia.SweepProbeCount(id),
            id => aridia.VerticalStep(id), Rac1EnemyHost.AridiaHostUpdateRadius,
            aridia.ProbeRac1ConstructorbotStateThreeFloor, aridia.ProbeRac1AridiaWalkerGroundLift, aridia.ProbeRac1AridiaWalkerSupport,
            aridia.ProbeRac1AridiaWalkerSpherePushouts, aridia.MoveRac1AridiaWalkerContact, Rac1EnemyHost.SelectRac1EnemySequence, aridia.ActivateNearbyRac1AridiaShark));
    }

    private GodotRac1FrogDiagnosticFixture CreateRac1FrogDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors)
    {
        Func<Rac1FrogEncounter?> encounter = () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Enemies.Frogs;
        return new(actors,
            new GodotRac1DiagnosticWorldScope(new GodotDiagnosticWorldObservations(WorldScenes)), new()
            {
                Frogs = () => encounter()?.Session,
                Shuttle = () => encounter()?.Shuttle,
                Waves = () => encounter()?.Waves,
                WaveControllers = () => encounter()?.WaveControllerIds ?? [],
                Carriers = () => encounter()?.CarrierChildren ?? System.Collections.Immutable.ImmutableDictionary<int, Node3D>.Empty,
                HasPickupModel = nativeClass => Rac1World.Loot.HasPickupModel(nativeClass),
                Counts = () => (Rac1World.Presentation.CrateNodes.Count, Rac1World.Loot.PickupMotions.Count, Rac1World.Loot.Debris.Count),
                TickFrog = enemy => encounter()?.TickRac1Frog(enemy),
                TickCarrier = enemy => encounter()?.TickRac1FrogCarrier(enemy),
                MeleeTouchesPlayer = Rac1World.Enemies.EnemyMeleeTouchesPlayer,
                ResolveWrench = (previousHand, previousTip, hand, tip, radius, damage) =>
                    Rac1World.Wrench.ResolveContacts(previousHand, previousTip, hand, tip, radius, damage),
                TickShuttle = () => encounter()?.TickRac1FrogShuttleSwitch(),
                ApplyShuttle = () => encounter()?.ApplyRac1FrogShuttleSelection(),
                TickWaves = () => encounter()?.TickRac1FrogWaves(),
                Queen = CreateRac1QueenDiagnosticBindings(),
            });
    }

    private GodotRac1QueenDiagnosticBindings CreateRac1QueenDiagnosticBindings()
    {
        Func<Rac1FrogEncounter?> encounter = () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Enemies.Frogs;
        return new()
        {
            Group = () => encounter()?.QueenGroup,
            Recovery = () => encounter()?.Recovery,
            Entrance = () => encounter()?.Entrance,
            Flight = () => encounter()?.Flight,
            PendingCamera = () => encounter()?.PendingCamera,
            Linked = () => encounter()?.LinkedGate,
            Tuning = () => encounter()?.GateNative,
            Animation = () => encounter()?.QueenAnimation,
            Node = () => encounter()?.QueenNode,
            AuthoredNativeZ = () => encounter()?.AuthoredQueenZ ?? 0,
            Parents = () => encounter()?.QueenParents ?? System.Collections.Immutable.ImmutableDictionary<int, RuntimeWorldScene.DynamicObjectNode>.Empty,
            Children = () => encounter()?.QueenChildren ?? System.Collections.Immutable.ImmutableDictionary<int, Node3D>.Empty,
            WorldRoot = () => _sceneResult?.Root,
            Player = () => _player,
            ActiveCamera = () => _activeCamera,
            QueenCamera = () => encounter()?.QueenCamera,
            GuidedCamera = () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.GuidedCamera.Camera,
            DamageSequence = () => Rac1World.Context.DamageTransport.Sequence,
            ForgetWrenchContact = key => Rac1World.Wrench.ForgetVictim(key),
            ResolveWrench = (previousHand, previousTip, hand, tip, radius, result) =>
                encounter()?.ResolveRac1QueenWrenchContacts(previousHand, previousTip, hand, tip, radius, result),
            InvalidateEquipmentGeometry = () => Rac1World.Weapons.Contacts.Invalidate(),
            GeometryContainsQueen = () => encounter()?.QueenNode is { } queen && Rac1World.Weapons.Contacts.Geometry.Any(entry => entry.Node == queen),
            AimTargetAdmitted = () => encounter()?.QueenNode is { } queen && Rac1World.Weapons.Contacts.IsAimTarget(queen),
            TickFlight = () => encounter()?.TickRac1QueenFlight(),
            TickRecovery = () => encounter()?.TickRac1QueenRecovery(),
            TickLinked = () => encounter()?.TickRac1QueenLinkedChildren(),
            SynchronizeLinked = () => encounter()?.SynchronizeRac1QueenLinkedParents(),
            ObserveFrogEvent = (state, sequence, completed) => encounter()?.ObserveRac1QueenFrogEvent(state, sequence, completed) ?? 0,
            BeginGuided = () => Rac1World.Weapons.GuidedCamera.Begin(_sceneResult!.Root, _player!),
            EndGuided = () => Rac1World.Weapons.GuidedCamera.End(),
            EndQueen = () => encounter()?.EndRac1QueenScriptedCamera(),
        };
    }

    internal CaptureDiagnosticHarness CreateCaptureDiagnosticHarness() => CaptureReplay.Diagnostics(() =>
    {
        var clock = new GodotDiagnosticFrameClock(_host);
        var recorder = new GodotCaptureRecorderDiagnosticFixture(() => new()
        {
            RecorderPresent = _captureReplay?.Recorder is not null,
            WaitingForEntry = _captureReplay?.Recorder?.WaitingForEntry == true,
            PreparingWorld = _captureReplay?.Recorder?.PreparingWorld == true,
            Finished = _captureReplay?.Recorder?.Finished == true,
            PlayerPresent = _player is not null,
            LevelId = _world?.LevelId,
            Destination = _currentDestinationId,
            Configuration = _playerSession?.Capture().Configuration,
            ProfileJson = _playerSession is null ? "" : System.Text.Json.JsonSerializer.Serialize(OBP.Fusion.ProfilePlayer.Capture(_playerSession.Capture()) with { Generation = 0 }),
            GenuineAvatar = _avatarPresentation.View is not null && _avatarPresentation.Avatar?.Identity == _playerSession?.Capture().Configuration.Avatar,
            NativeProfile = _player?.ControlProfile == PlayerHostProfiles.Native,
            PhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            HudHealth = _hudState.Current.Health is not null,
            HudBolts = _hudState.Current.Bolts is not null,
            HudVisible = _playerHud?.Visible == true,
            QuickSelectOpen = QuickSelectOpen,
            PauseOpen = PauseMenuOpen,
            TreePaused = GetTree().Paused,
            PhysicsTick = _avatarPresentation.Clock?.Tick ?? 0,
            PlayerNode = _player?.GetInstanceId(),
            Shots = FusionWeapons.Session?.ShotsFired ?? 0,
        }, (width, height) => GetWindow().Size = new(width, height), () => _args.RecordPlaytest,
            path => _captureReplay!.Recorder!.RequestBookmark(path), () => _captureReplay!.Recorder!.Save());
        var navigation = new DiagnosticNavigationActions(destination => destination == "obp:fusion-lab" ? EnterFusionLab() :
            TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native));
        var equipment = new CaptureEquipmentActions((slot, item) => AssignPlayerQuickSelect(slot, item).Accepted, item => SelectPlayerEquipment(item).Accepted);
        var reports = new GodotBugReporterDiagnosticFixture(_host, () => _bugReporting, () => BugReportModalOpen,
            () => new(GodotMovementPlayer.Copy(_player!.GlobalPosition), _rac1BoltWallet.Balance,
                System.Text.Json.JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()), QuickSelectOpen, PauseMenuOpen, GetTree().Paused),
            () => _captureReplay?.Recorder as Node);
        var acceptance = new OwnedDiagnosticAcceptanceReporter(() => _currentDestinationId, () => _args.AcceptanceOut,
            () => new(DisplayServer.GetName() == "headless", [.. Sources.Setup.Library.Attached.Values.Select(source => source.Identity)],
                _playerSession?.Capture().Configuration, [.. ReadPlayerEquipment().Select(entry => new AcceptanceEquipment(entry.Definition.Id,
                    entry.Definition.SourceGame, entry.Definition.Implementation, entry.Usability, entry.Reason))]),
            message => GD.PrintErr(message), (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code));
        var captures = ProjectSettings.GlobalizePath("res://../captures");
        var ports = new CaptureDiagnosticPorts(recorder, equipment, reports, CreateMovementStateDiagnosticFixture,
            CreateTrilogyPlayerDiagnosticFixture(), CreateTrilogyActorDiagnosticFixture(), navigation,
            token => CreateFusionWeaponDiagnosticHarness().RunRecordingAsync(token),
            () => CaptureDiagnosticIsolation.Isolated(SharedProfilePath, captures) && CaptureDiagnosticIsolation.Isolated(_bugReporting?.StorageRoot, captures));
        return new(new(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceRequirement(source.Game, source.Identity.BuildId))],
                [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))]),
            ports, CreateMovementDiagnosticHarness().Ports, new GodotCaptureDiagnosticInputFactory(), new GodotCaptureDiagnosticTime(), acceptance,
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]), clock),
            _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code), message => GD.Print(message), message => GD.PrintErr(message));
    });

    internal IMovementStateDiagnosticFixture CreateMovementStateDiagnosticFixture() => new GodotMovementStateDiagnosticFixture(new()
    {
        Player = () => _player,
        Enemies = () => [.. Rac1World.Enemies.Roster.Values.Select(enemy => enemy.Node)],
        Crates = () => [.. Rac1World.Presentation.CrateNodes],
        Active = Rac1World.Enemies.IsRac1GameplayActive,
        Damage = Rac1World.Enemies.ApplyRac1EnemyWeaponDamage,
        Strike = node => Rac1World.WrenchDamage.TryStrikeCrate(node),
        Read = () => new([.. Rac1World.Enemies.Roster.Values.Select(enemy => new MovementStateEnemyDiagnostic(new(ObpSourceGame.Rac1, enemy.Node.Source.NativeClassId,
                enemy.Node.Source.InstanceIndex), Rac1World.Enemies.IsRac1GameplayActive(enemy.Node), enemy.Node.Root.Visible, enemy.Body.CollisionLayer, enemy.RewardEmitted))],
            [.. Rac1World.Presentation.CrateNodes.Select(node => new MovementStateCrateDiagnostic(new(ObpSourceGame.Rac1, node.Source.NativeClassId, node.Source.InstanceIndex),
                Rac1World.Enemies.IsRac1GameplayActive(node), Rac1World.Loot.CrateBodies.ContainsKey(node.Source.InstanceIndex)))], Rac1World.Context.BoltCrates.DestroyedCrateCount,
            _rac1Weapons.Equipped, _rac1Weapons.FirstRangedAmmo, _rac1BoltWallet.Balance, Rac1World.Loot.PickupMotions.Keys.Any(id => id > 0)),
        Consume = () => { Rac1CombatInput.Select(Rac1WeaponId.FirstRanged); return _rac1Weapons.TryUseEquipped(); },
        Credit = amount => _rac1BoltWallet.Credit(amount),
        Loading = () => MovementReproduction.IsLoading,
        Override = () => MovementReproduction.PathOverride,
        SetOverride = path => MovementReproduction.PathOverride = path,
        Path = () => MovementReproduction.MovementStatePath,
        Load = async (path, frames, token) =>
        {
            var result = await MovementReproduction.LoadMovementStateAsync(path, recordFrames: frames, cancellationToken: token); token.ThrowIfCancellationRequested();
            if (result is null) throw new InvalidOperationException("Movement state load/replay failed.");
            return [.. result.Select(sample => new MovementStateReplayDiagnostic(sample.Position, sample.ControlHeading))];
        },
        Forward = async (frames, token) => [.. (await MovementReproduction.RecordForwardMovementReplayAsync(frames, token)).Select(sample => new MovementStateReplayDiagnostic(sample.Position, sample.ControlHeading))],
    });

    internal ITrilogyPlayerDiagnosticFixture CreateTrilogyPlayerDiagnosticFixture() => new GodotTrilogyPlayerDiagnosticFixture(_host, new()
    {
        Session = () => _playerSession!,
        Player = () => _player,
        Scene = () => _sceneResult!.Root,
        Read = () => new(OrdinaryPlayerSession, _playerNative is not null, _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is not null, _playerAudio?.Ready == true,
            _rac1HeldItem.Hand.WrenchHeld, _avatarPresentation.Clock?.Tick ?? 0, FusionWeapons.Session?.ShotsFired ?? 0, _pauseMenu?.UniversalEnabled == true,
            _pauseMenu?.RetailEnabled == true, _persistentPlayer.Rac1Use.PyroEmitting, _equipmentPresentation.Explosions.Particles?.Live.Any(particle => particle.Type == 12) == true,
            _equipmentPresentation.Explosions.RenderedCount, _playerNative?.Life.Capture().Current ?? 0, GodotMovementPlayer.Copy(_player?.GlobalPosition ?? Vector3.Zero),
            _player?.Rac1CurrentYaw ?? 0, FusionWeapons.AcceptedHits, FusionWeapons.Projectiles.Count, FusionWeapons.AudioPlayerCount),
        ResetAudio = () => _playerAudio?.ResetDiagnosticRandom(1440),
        Audio = () => [.. (_playerAudio?.ReadSelected() ?? []).Select(cue => new TrilogyAudioDiagnostic(SourceIdentityWire.FormatSource(cue.Source), cue.Build, cue.Level, cue.Class, cue.Sound, cue.LevelSound, cue.Loop, cue.NativeTick))],
        Trial = token => CreatePlayerDiagnosticHarness().CaptureTrialAsync(token),
        OpenPause = TryOpenPauseMenu,
        PauseSection = section => _pauseMenu!.ShowUniversalSection(section),
        ClosePause = () => TryClosePauseMenu(),
        Equipment = () => [.. ReadPlayerEquipment().Select(entry => new TrilogyEquipmentDiagnostic(entry.Definition.Id, entry.Definition.Item,
            entry.Selectable, entry.Reason, entry.Definition.Item is { } item ? FusionWeapons.Session!.Status(item).Ammo : 0))],
        Respawn = () => _playerNative!.Life.Respawn(),
    });

    internal ITrilogyActorDiagnosticFixture CreateTrilogyActorDiagnosticFixture() => new GodotTrilogyActorDiagnosticFixture(new()
    {
        Actors = () => ActorHost.Diagnostics,
        ResetEncounter = () => { FusionWeapons.ClearTransients(); _persistentPlayer.ResetRac1EffectRandom(1440); },
        ResetSharedAssets = FusionWeapons.ResetSources,
    });

    internal UiDiagnosticHarness CreateUiDiagnosticHarness()
    {
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var menus = new GodotDiagnosticMenus(_host, _worldRoot,
            () => new(_mode == Mode.Title, _mode == Mode.Selector, _mode == Mode.World, _mode == Mode.Picker,
                (Sources.Setup.Access == SourceAccessMode.ExplicitDiagnostic), Sources.HasView, _navigation.Title is not null, _world is not null,
                _currentDestinationId, _profileDocument?.Destination, _profileWriteBlocked, null, false, false),
            () => new([], _player?.VisualRoot.FallbackVisible == true, _sceneResult is not null,
                _player is not null && IsInstanceValid(_player), _avatarPresentation.View is not null && IsInstanceValid(_avatarPresentation.View),
                _player is not null && ReferenceEquals(_activeCamera, _player.Camera) && _player.Camera.Current, _world?.LevelId),
            surface => surface switch
            {
                DiagnosticMenuSurface.Title => _navigation.Title,
                DiagnosticMenuSurface.WorldSelect => _navigation.Selector,
                DiagnosticMenuSurface.Overlay => _ui,
                DiagnosticMenuSurface.World => _sceneResult?.Root,
                DiagnosticMenuSurface.Pause => _pauseMenu,
                _ => null
            },
            () => _navigation.Title, () => _navigation.Selector, () => _pauseMenu,
            ShowTitleScreen, ShowDestinationSelector, TryHandleInteractiveBack, TryOpenPauseMenu, TryClosePauseMenu);
        return new(_args, Sources.Setup.Access != SourceAccessMode.ExplicitDiagnostic,
            [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))], menus,
            new DiagnosticPlayerContinuityAdapter(() => _playerSession?.Capture() ?? throw new DiagnosticUnavailableException("The player session is missing.")),
            new DiagnosticProfileEvidence(() => SharedProfilePath, SaveSharedProfile),
            new DiagnosticSourceRepairFixture(Sources.Setup.Library.Remove, Sources.RequireOrdinary, () => _rac1Campaign.Session),
            new GodotRetailPauseFixture(_host, () => _player, () => _pauseMenu, () => PauseMenuOpen, TryOpenPauseMenu, TryClosePauseMenu, () => _rac1Weapons, worlds), worlds,
            new DiagnosticNavigationActions(destination => { OpenDestinationFromBootstrap(destination); return _world is not null && _currentDestinationId == destination; }),
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(_host)), _hostOperations,
            (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code), message => GD.Print(message), message => GD.PrintErr(message));
    }

    internal PlayerSessionDiagnosticHarness CreatePlayerDiagnosticHarness()
    {
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var player = new GodotPersistentPlayerFixture(new(() => OrdinaryPlayerSession, () => _playerSession, () => _playerNative,
            () => FusionWeapons.Session, () => _player, () => _avatarPresentation.View, () => _avatarPresentation.Avatar?.Identity, _playerRoot, _worldRoot,
            () => (FusionWeapons.Projectiles.Count, FusionWeapons.AudioPlayerCount, FusionWeapons.EffectCount), () => _activeDestination is not null, () => _currentDestinationId),
            worlds, FusionWeapons.Select, disable => Rac1WeaponUsePresentationHost.RequestPreview(disable, () => FusionWeapons, () => _worldScenes?.Current?.Resources.Rac1Gameplay, MovementReproduction.Report));
        var sources = new DiagnosticSourceAttachmentFixture(Sources.Setup.Library, RefreshPlayerSourceAvailability);
        var navigation = new DiagnosticNavigationActions(destination => destination == "obp:fusion-lab" ? EnterFusionLab() :
            TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native));
        var menus = CreateUiDiagnosticHarness().Menus;
        var equipment = new DiagnosticPlayerEquipmentFixture(ReadPlayerEquipment, SelectPlayerEquipment, AssignPlayerQuickSelect,
            UnequipPlayerEquipment, SetPlayerGadgetEnabled, () => _playerNative?.GadgetEnabled(OBP.RAC1.Player.Rac1TraversalEquipmentId.HeliPack) == true);
        var ui = new DiagnosticPlayerInterfaceFixture(() => new(_playerHud?.Visible == true, _hudState.Current.Health, _hudState.Current.Bolts,
            _quickSelect is null ? [] : [.. _quickSelect.Entries.Select(entry => entry.Weapon)], Rac1World.Weapons.Detector.Equipped),
            () => { using var press = new InputEventAction { Action = ControllerBindings.QuickSelect, Pressed = true }; return TryOpenWeaponQuickSelect(press); },
            () => CloseWeaponQuickSelect(), () => Rac1World.Weapons.Detector.Toggle());
        var profile = new DiagnosticSharedProfileFixture(() => new(OrdinaryPlayerSession, SharedProfilePath is not null, _profileWriteBlocked,
            FusionWeapons.Sources.Preparation is not null, _profileDocument?.Destination, _currentDestinationId, _profileDocument?.Sources.Count ?? 0), () => _playerNative);
        var samples = new GodotCapabilitySamples(new(() => _player, () => _avatarPresentation.Clock, () => PlayerLife.ServiceTick,
            () => _avatarPresentation.Controller, () => FusionWeapons.Session?.ShotsFired ?? 0));
        var health = new GodotCapabilityHealthFixture(new(() => _playerNative, () => _player,
            () => _worldScenes?.Current?.Resources.Rac2Gameplay?.ParityNanotech ?? 0,
            () => _hudState.Current.Health, () => [.. (_worldScenes?.Current?.Resources.Rac2Gameplay?.OpeningHostileNodes() ?? [])], PlayerLife.Respawn));
        var audio = new GodotPlayerAudioFixture(_host, new(() => _player,
            () => new(_playerAudio?.Ready == true, _world?.LevelId, _playerAudio?.PackSound, _playerAudio?.ThrownWrenchSound, _equipmentPresentation.WeaponAudio.Loop is not null,
                (_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true), Rac1World.Context.BoltCrates.DestroyedCrateCount, _rac1Nanotech.Probe().Nanotech, _rac1Nanotech.Probe().IsDead),
            () => _playerAudio?.ReadVoices() ?? [],
            ClearMovementSmokeInput, SetAnalogueSmokeInput, SetRac1SmokeConditionedInput, TryHandleInteractiveBack,
            Rac1CombatInput.Select, disable => Rac1WeaponUsePresentationHost.RequestPreview(disable, () => FusionWeapons, () => _worldScenes?.Current?.Resources.Rac1Gameplay, MovementReproduction.Report), () => [.. Rac1World.Presentation.CrateNodes], Rac1World.Enemies.IsRac1GameplayActive,
            (node, position) => GodotRac1CrateStaging.Place(node, position, Rac1World.Loot.FallingCrates), Rac1World.Enemies.ApplyRac1EnemyPlayerDamage));
        var ports = new PlayerSessionDiagnosticPorts(player,
            new GodotPlayerObservations(() => _player, () => _avatarPresentation.Avatar?.Identity,
                () => _avatarPresentation.Clock?.Tick ?? throw new DiagnosticUnavailableException("The native player clock is unavailable."), () => _playerNative?.Life.Capture().Current),
            new GodotPlayerInputFixture(() => _player), navigation, menus, sources,
            new GodotWorldLifecycleFixture(_host, WorldScenes, EnterFusionLab,
                () => EnterWorld(DiagnosticWorldFailures.Destination, DiagnosticWorldFailures.Load, null, null)),
            new GodotSessionOperationChecks(_host, token => CreateCaptureDiagnosticHarness().CheckMovementCancellationAsync(token), () => _hostOperations.Stopping, message => GD.Print(message)),
            equipment, ui, profile, new DiagnosticProfileEvidence(() => SharedProfilePath, SaveSharedProfile),
            CreateCaptureDiagnosticHarness().Acceptance, samples,
            new GodotCommonContactFixture(() => _sceneResult?.Root, () => _player),
            new DiagnosticRetiredActorProbe(() => [.. Rac1World.Presentation.HostileNodes.Values], PlayerLife.ApplySourceDamage), health,
            () => PlayerEquipmentConfigurationIdentity, audio, _playerAudio!.Events);
        return new(_args, [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))], ports,
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(_host)), _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code),
            message => GD.Print(message), message => GD.PrintErr(message));
    }

    internal Rac1WorldRouteDiagnosticHarness CreateRac1WorldRouteDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness(); var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var ui = CreateUiDiagnosticHarness();
        var lifetime = new GodotRac1WorldRouteLifetimeFixture(_host,
            () =>
            {
                var taxi = (_worldScenes?.Current?.Resources.Rac1Gameplay?.Taxis)?.ReadSnapshot();
                var mechanisms = _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms;
                var music = mechanisms?.Music;
                return new(worlds.ReadWorld(), mechanisms?.Route?.Mechanisms.Count ?? 0, taxi?.Nodes ?? 0, taxi?.Hulls ?? 0,
                    music?.Music?.Tracks.Count ?? 0, music?.DecodedCount ?? 0, music?.ReaderOpen == true,
                    taxi?.CameraWorldOwned ?? false,
                    taxi is { Flying: true } || taxi?.PendingStop is not null || taxi is { RiderFrame: true },
                    SharedProfilePath is not null);
            },
            () => _worldHost.Root, () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Taxis)?.CaptureHulls() ?? [], () => _player?.Camera);
        return new(movement.Ports,
            () => new GodotRac1NovalisRouteFixture(() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Route?.Mechanisms ?? [], () => _player, worlds),
            () => new GodotRac1NovalisMusicFixture(worlds, () => new(worlds.ReadWorld(),
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Music?.Music is { } music
                    ? [.. music.Tracks.Select(track => new Rac1NovalisMusicTrackDiagnostic(track.Index, track.Sector, track.DurationSeconds))] : [],
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Music?.Area,
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Music?.Slot ?? -1,
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Music?.IntroRemaining ?? 0,
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Music?.DecodedCount ?? 0,
                [.. _worldHost.AudioDiagnostics.Select(item => item.Message)], _worldHost.AudioStatusLine),
                index => _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.SelectAuthoredMusicCrank(index),
                delta => _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.Tick(delta, Rac1AuthoredTickPhase.Music)),
            CreateRac1TaxiDiagnosticFixture, ui, lifetime, ui.Menus,
            new DiagnosticNavigationActions(destination => TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native)),
            new DiagnosticSourceAttachmentFixture(Sources.Setup.Library, RefreshPlayerSourceAvailability));
    }

    private IRac1TaxiDiagnosticFixture CreateRac1TaxiDiagnosticFixture() => new GodotRac1TaxiDiagnosticFixture(new()
    {
        Host = () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Taxis,
        Player = () => _player,
    }, new GodotDiagnosticWorldObservations(WorldScenes));

    internal ShotListCliService CreateShotListCliService() => new(_args, (game, path) => RememberCommandLineSource(game, path),
        new GodotShotListCaptureFixture(_host, _camera, camera => _activeCamera = camera, OpenDestinationFromBootstrap,
            () => _mode == Mode.World ? _world : null,
            world => { GodotWorldCaptureCamera.FrameShowcase(_camera, world); _activeCamera = _camera; },
            ApplyOverlaySpec, UpdateWorldHud, CapturePresentation.Metadata),
        (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code), message => GD.Print(message), message => GD.PrintErr(message));

    internal StressSwitchCliService CreateStressSwitchCliService() => new(
        new GodotStressWorldFixture(() => new(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld(),
                _worldHost.Root?.GetInstanceId(), _player?.GetInstanceId(), _rac1Campaign.Session.Campaign.CurrentLevel),
            path => _activeDestination is { } destination && _worldHost.Root is not null && !EnterProviderWorld(path, destination),
            ShowDestinationSelector, OpenDestinationFromBootstrap, _worldRoot, () => _world?.LevelId ?? -1, () => _player?.IsOnFloor()),
        new GodotDiagnosticFrameClock(_host), (reason, code) => ApplicationLifecycle.RequestQuit(_host, reason, code),
        message => GD.Print(message), message => GD.PrintErr(message));

    internal void StartFusionLabTraversalSmoke(PlayerHost player) => FusionLabTraversalScenario.Start(
        new GodotFusionLabTraversalFixture(player, () => _frame, new GodotDiagnosticWorldObservations(WorldScenes),
            ShowDestinationSelector, EnterFusionLab, () => _player is not null),
        new GodotDiagnosticFrameClock(_host), new GodotDiagnosticSources(() => []), _hostOperations,
        message => GD.Print(message), message => GD.PrintErr(message));
}
