using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using OBP.Core.Math;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private bool _movementStateLoading;
    private string? _movementStatePathOverride;
    private Label? _movementStateNotice;
    private int _movementStateNoticeGeneration;
    private string MovementStatePath => _movementStatePathOverride ?? _args.MovementStatePath ??
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", "quick-state.json"));

    private MovementReproState CaptureMovementState()
    {
        if (_world is null || _player is null || _activeDestination is null || _movementStateLoading)
            throw new InvalidOperationException("Enter an R&C1 world before saving a movement state.");
        return new MovementReproState(2, _activeDestination.DestinationId,
            _world.BuildId, DateTimeOffset.UtcNow, typeof(OBPGame).Module.ModuleVersionId,
            _player.CaptureMovementRepro(), CaptureMovementWorld()).Validate();
    }

    private MovementReproWorld CaptureMovementWorld() => new(
        Rac1World.Context.MobyRuntime.Instances
            .Where(instance => !instance.IsActive ||
                _rac1Enemies.TryGetValue(instance.InstanceIndex, out var enemy) && enemy.RewardEmitted)
            .OrderBy(instance => instance.InstanceIndex)
            .Select(instance => new MovementReproInactiveObject(instance.Key, instance.State.NativeState,
                instance.State.IsTerminalized ||
                _rac1Enemies.TryGetValue(instance.InstanceIndex, out var enemy) && enemy.RewardEmitted)).ToArray(),
        _rac1Weapons.CapturePersistentState(), _rac1Weapons.CurrentItemId, _rac1BoltWallet.Balance,
        _rac1AdditionalWeapons?.Cargo.ToArray(), _persistentPlayer.WeaponPreview is not null,
        _rac1Chickens.Values.Where(chicken => IsRac1GameplayActive(chicken.Enemy.Node) && !chicken.Enemy.RewardEmitted)
            .Select(chicken => new Rac1MobyRuntimeKey(chicken.Enemy.Node.Source.NativeClassId, chicken.Enemy.Node.Source.InstanceIndex))
            .Concat(_rac1ChickenCargo).Distinct().ToArray())
    {
        HeldItemId = _rac1HeldItem.NativeItemId,
    };

    private void RestoreMovementWorld(MovementReproWorld world)
    {
        _rac1AdditionalWeapons?.RestoreCargo(world.CapturedMobies ?? []);
        foreach (var key in world.MorphedMobies ?? [])
        {
            if (!_rac1Enemies.TryGetValue(key.InstanceIndex, out var enemy) || enemy.Node.Source.NativeClassId != key.NativeClassId)
                throw new InvalidDataException("Saved Morph-o-Ray victim is unavailable in this level.");
            if (world.CapturedMobies?.Contains(key) == true) _rac1ChickenCargo.Add(key);
            else MorphArsenalEnemy(enemy.Node);
        }
        var objects = world.InactiveObjects.Select(saved =>
            (Saved: saved, Instance: Rac1World.Context.MobyRuntime.TryGet(saved.Key, out var instance) ? instance! :
                throw new InvalidDataException($"Saved object {saved.Key} is unavailable in this world."))).ToArray();
        foreach (var (saved, instance) in objects)
        {
            Rac1World.Context.MobyRuntime.SetNativeState(instance, saved.NativeState);
            if (saved.Terminalized && !instance.State.IsTerminalized) Rac1World.Context.MobyRuntime.Terminalize(instance);
            var node = Rac1WorldPresentation.FindPresentedDynamic(_sceneResult!, instance.Source);
            if (node is not null) SetRac1HostedPresence(node, RuntimeEntityPresence.Inactive);
            else Rac1World.Context.MobyRuntime.SetPresence(instance, RuntimeEntityPresence.Inactive);
            if (_rac1Enemies.TryGetValue(instance.InstanceIndex, out var enemy))
            {
                enemy.RewardEmitted = enemy.DeathPresented = saved.Terminalized;
                enemy.Body.CollisionLayer = 0;
                if (saved.Terminalized) enemy.Body.CollisionMask = 0;
                SetRac1EnemyFlame(enemy, false);
            }
            if (Rac1BoltCrate.IsCrateClass(instance.NativeClassId))
            {
                DisableRac1CrateCollider(instance.InstanceIndex);
                if (saved.Terminalized)
                {
                    var authored = Rac1BoltCrate.ReadAuthored(instance.Source)!;
                    Rac1World.Context.MobyPersistence.UpdateUid(instance.Source with { NativeUid = authored.Uid },
                        Rac1MobyUidPersistenceBits.BothSet);
                }
            }
            if (Rac1PlacedBolt.IsBoltClass(instance.NativeClassId))
            {
                int id = Rac1PlacedBoltSession.PickupId(instance.InstanceIndex);
                _rac1PendingPlacedBolts.RemoveAll(pending => pending.Registration.PickupId == id);
                _rac1PlacedBoltNodes.Remove(id);
                Rac1World.Presentation.PickupNodes.Remove(id);
                _rac1PickupMotion.Remove(id);
            }
        }
    }

    private void SaveMovementState()
    {
        if (OrdinaryPlayerSession)
        {
            if (_captureReplay?.Recorder is { } capture) capture.RequestBookmark(MovementStatePath);
            else ReportMovementState("Universal bookmarks need an input history. Start Bug Capture or --record-playtest, then press F12; the completed prefix preserves native actors too.");
            return;
        }
        if (FusionMovementStateBlocked) { ReportMovementState("Cross-game weapons use input recordings; use --record-playtest to retain all three sources."); return; }
        try
        {
            MovementReproState state = CaptureMovementState();
            MovementReproStateFile.Save(MovementStatePath, state);
            ReportMovementState($"Movement state saved. Shift+F12 loads it.");
            GD.Print($"[movement-state] saved {MovementStatePath}; position={state.Player.Position}; " +
                $"facing={state.Player.FacingYaw:R}; control={state.Player.CameraController.ControlHeading:R}; " +
                $"assembly={state.HostAssemblyId}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or InvalidOperationException)
        {
            ReportMovementState($"Movement state not saved: {ex.Message}");
        }
    }

    public void LoadMovementStateFromBootstrap() => StartMovementStateLoad(_args.LoadMovementState!, startup: true);

    private void StartMovementStateLoad(string path, bool startup = false) =>
        _hostOperations.Start("movement-state-load", token => LoadMovementStateAsync(path, startup, cancellationToken: token), lifetime: CancellationToken.None);

    private async Task<List<MovementReplaySample>?> LoadMovementStateAsync(string path, bool startup = false, int? recordFrames = null,
        CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled) cancellationToken = _hostOperations.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (_captureReplay?.Recorder?.RejectMovementLoad() == true) return null;
        if (_movementStateLoading) return null;
        PlayerHost? restoringPlayer = null;
        try
        {
            MovementReproState state = MovementReproStateFile.Load(path);
            if (state.Version == MovementReproState.CurrentVersion)
            {
                OpenReplayBookmark(path, state);
                return null;
            }
            if (OrdinaryPlayerSession) throw new InvalidOperationException("Legacy native R&C1 bookmarks require an explicit parity session. Open them with --load-movement-state at launch.");
            if (FusionMovementStateBlocked) throw new InvalidOperationException("Legacy R&C1 bookmarks require an R&C1 weapon with no active foreign shots.");
            if (_args.ReplayForwardFrames is { } frames && (frames < 1 || frames > 3600))
                throw new InvalidDataException("Forward replay must contain 1 through 3600 physics updates.");
            var destination = TrilogyWorldProviders.Resolve(state.Destination)
                ?? throw new InvalidDataException("The saved destination is unavailable.");
            Sources.Initialize();
            var source = Sources.Setup.Library.Get(destination.Game);
            if (source is null || source.Identity.BuildId != state.SourceBuildId || !File.Exists(source.Path))
                throw new InvalidDataException("Attach the saved state's supported R&C1 source before loading.");
            _movementStateLoading = true;
            if (PauseMenuOpen) ClosePauseMenu();
            ClearMovementSmokeInput();
            // Rebuild transient controllers, then restore saved removals without
            // replaying damage, rewards or death effects. Version1 had no world
            // snapshot and retains its original fresh-level behaviour.
            if (state.World is { } savedWorld)
            {
                EnsureRac1CampaignPersistenceInitialized();
                var inventory = new Rac1WeaponInventory(savedWorld.Inventory, savedWorld.CurrentItemId);
                _persistentPlayer.WeaponPreview = savedWorld.WeaponPreview ? inventory : null;
                if (!savedWorld.WeaponPreview)
                    _rac1Campaign.Session = new Rac1CampaignRuntimeSession(_rac1Campaign.Session.Campaign, inventory);
                _rac1BoltWallet = new Rac1BoltWallet(savedWorld.Bolts);
            }
            OpenDestinationFromBootstrap(state.Destination);
            if (_world?.BuildId != state.SourceBuildId || _player is null || !_player.CanSaveMovementState)
                throw new InvalidDataException("The movement state does not match the loaded retail source build.");
            if (state.World is { } world) RestoreMovementWorld(world);

            restoringPlayer = _player;
            restoringPlayer.SetPhysicsProcess(false);
            // A newly imported collision mesh needs registration before the
            // floor-contact cache can be primed. Never use spawn ground snapping.
            for (int frame = 0; frame < 2; frame++)
                await GodotAsyncWait.SignalAsync(GetTree(), SceneTree.SignalName.PhysicsFrame, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_player != restoringPlayer || !IsInstanceValid(restoringPlayer))
                throw new InvalidOperationException("The world changed while loading the movement state.");
            restoringPlayer.RestoreMovementRepro(state.Player);
            if (state.World is { } handWorld)
                _rac1HeldItem.Restore(handWorld.HeldItemId ?? handWorld.CurrentItemId, _rac1Weapons);
            RefreshRac1WrenchPresentationVisibility();
            ReportMovementState(state.World is null
                ? "Movement state loaded (legacy pose only; enemies reset). F12 saves world removals too."
                : "Movement state loaded, including defeated enemies and broken crates.");
            RefreshRac1HudState();
            GD.Print($"[movement-state] loaded {path}; position={restoringPlayer.GlobalPosition}; " +
                $"saved-assembly={state.HostAssemblyId}; current-assembly={typeof(OBPGame).Module.ModuleVersionId}");
        }
        catch (OperationCanceledException error) when (error.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested)
        { return null; }
        catch (Exception ex)
        {
            ReportMovementState($"Movement state not loaded: {ex.Message}");
            GD.PrintErr($"[movement-state] load failed: {ex.Message}");
            if (startup) ApplicationLifecycle.RequestQuit(this, "movement-state-load-fail", 2);
            return null;
        }
        finally
        {
            if (restoringPlayer is not null && IsInstanceValid(restoringPlayer))
                restoringPlayer.SetPhysicsProcess(true);
            _movementStateLoading = false;
        }

        if ((recordFrames ?? (startup ? _args.ReplayForwardFrames : null)) is { } replayFrames)
        {
            try
            {
                Engine.MaxFps = 60;
                var samples = await RecordForwardMovementReplayAsync(replayFrames, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!startup) return samples;
                string output = _args.ReplayOut ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "forward-replay.json");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    StateFile = Path.GetFileName(path),
                    HostAssemblyId = typeof(OBPGame).Module.ModuleVersionId,
                    PhysicsHz = Engine.PhysicsTicksPerSecond,
                    Samples = samples,
                }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"[movement-replay] PASS; frames={replayFrames}, output={output}, " +
                    $"start={samples[0].Position}, end={samples[^1].Position}");
                ApplicationLifecycle.RequestQuit(this, "movement-replay-pass", 0);
            }
            catch (OperationCanceledException error) when (error.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested)
            { return null; }
            catch (Exception ex)
            {
                GD.PrintErr($"[movement-replay] FAIL: {ex.Message}");
                ApplicationLifecycle.RequestQuit(this, "movement-replay-fail", 3);
            }
        }
        else
        {
            // An empty sample list confirms a successful restore without a requested forward replay.
            return [];
        }
        return null;
    }

    private sealed record MovementReplaySample(int Tick, Vec3 Position, Vec3 Velocity, double FacingYaw,
        double ControlHeading, string Phase, bool Floor, bool Support, string SmallStep, Vec3[] ContactNormals,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MovementReplayProfile? Profile);

    private sealed record MovementReplayProfile(double ElapsedMs, long RenderFrame, double ControllerMs,
        double ContactMs, double SlideMs, double SmallStepMs, double CrestMs, double PresentationMs,
        double CameraMs, double EngineProcessMs, double EnginePhysicsMs, double RenderCpuMs, double RenderGpuMs,
        double Fps);

    private MovementReplaySample CaptureMovementReplaySample(int tick, System.Diagnostics.Stopwatch? timer = null)
    {
        PlayerHost player = _player ?? throw new InvalidOperationException("Replay player is missing.");
        static Vec3 V(Vector3 value) => new(value.X, value.Y, value.Z);
        var normals = new List<Vec3>();
        for (int i = 0; i < player.GetSlideCollisionCount(); i++)
        {
            var collision = player.GetSlideCollision(i);
            for (int j = 0; j < collision.GetCollisionCount(); j++) normals.Add(V(collision.GetNormal(j)));
        }
        return new(tick, V(player.GlobalPosition), V(player.Velocity), player.Rac1CurrentYaw,
            player.MovementReproControlHeading,
            player.Rac1MovementPhase.ToString(), player.IsOnFloor(), player.Rac1RecoveredSupportGrounded,
            player.Rac1SmallStepDecision, normals.ToArray(), timer is null ? null :
                new(timer.Elapsed.TotalMilliseconds, _frame, player.DevelopmentProfile.Controller,
                    player.DevelopmentProfile.Contact, player.DevelopmentContactProfile.Slide,
                    player.DevelopmentContactProfile.SmallStep, player.DevelopmentContactProfile.Crest,
                    player.DevelopmentProfile.Presentation, player.DevelopmentProfile.Camera,
                    Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000,
                    Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000,
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) +
                        RenderingServer.GetFrameSetupTimeCpu(),
                    RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
                    Engine.GetFramesPerSecond()));
    }

    private async Task<List<MovementReplaySample>> RecordForwardMovementReplayAsync(int frames, CancellationToken cancellationToken)
    {
        PlayerHost player = _player ?? throw new InvalidOperationException("Replay player is missing.");
        using var worldWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            _worldScenes?.Current?.Lifetime ?? throw new InvalidOperationException("Replay world lease is missing."));
        player.DevelopmentProfileEnabled = _args.ReplayProfile;
        System.Diagnostics.Stopwatch? timer = _args.ReplayProfile ? System.Diagnostics.Stopwatch.StartNew() : null;
        if (_args.ReplayProfile)
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        var samples = new List<MovementReplaySample> { CaptureMovementReplaySample(0, timer) };
        var complete = new TaskCompletionSource<List<MovementReplaySample>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CompletedPhysics()
        {
            try
            {
                if (_player != player) throw new InvalidOperationException("The replay world changed.");
                samples.Add(CaptureMovementReplaySample(samples.Count, timer));
                if (samples.Count == frames + 1)
                {
                    ClearMovementSmokeInput();
                    player.DevelopmentPhysicsCompleted -= CompletedPhysics;
                    complete.TrySetResult(samples);
                }
            }
            catch (Exception ex) { complete.TrySetException(ex); }
        }
        // Inject only the ordinary forward action. The saved control/camera
        // heading determines the route; this never steers toward a guessed point.
        try
        {
            player.DevelopmentPhysicsCompleted += CompletedPhysics;
            SetAnalogueSmokeInput(0f, 1f);
            return await complete.Task.WaitAsync(TimeSpan.FromSeconds(frames / 60d + 30d), worldWork.Token);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == worldWork.Token && worldWork.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The replay world changed.", error);
        }
        finally
        {
            player.DevelopmentPhysicsCompleted -= CompletedPhysics;
            if (IsInstanceValid(player)) player.DevelopmentProfileEnabled = false;
            if (_args.ReplayProfile)
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
            ClearMovementSmokeInput();
        }
    }

    private void ReportMovementState(string message)
    {
        GD.Print($"[movement-state] {message}");
        if (_hostOperations.Stopping) return;
        _pauseMenu?.SetMovementStatus(message);
        if (!_debugOutputEnabled) return;
        if (_movementStateNotice is null || !IsInstanceValid(_movementStateNotice) || _movementStateNotice.IsQueuedForDeletion())
        {
            _movementStateNotice = new Label
            {
                Name = "DevelopmentNotice",
                AnchorTop = 1f,
                AnchorBottom = 1f,
                AnchorRight = 1f,
                OffsetLeft = 16f,
                OffsetRight = -16f,
                OffsetTop = -64f,
                OffsetBottom = -12f,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _movementStateNotice.AddThemeColorOverride("font_color", new Color(1f, .95f, .5f));
            _ui.AddChild(_movementStateNotice);
        }
        _movementStateNotice.Text = message;
        _movementStateNotice.Visible = true;
        if (!_hostOperations.Stopping)
            _hostOperations.Start("movement-state-notice", token => ClearMovementStateNoticeAsync(++_movementStateNoticeGeneration, token), lifetime: CancellationToken.None);
    }

    private async Task ClearMovementStateNoticeAsync(int generation, CancellationToken cancellationToken)
    {
        await GodotAsyncWait.SignalAsync(GetTree().CreateTimer(6d, processAlways: true), SceneTreeTimer.SignalName.Timeout, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation == _movementStateNoticeGeneration && IsInstanceValid(_movementStateNotice))
            _movementStateNotice!.Visible = false;
    }
}
