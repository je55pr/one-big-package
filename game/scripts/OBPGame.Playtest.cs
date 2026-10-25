using System.Reflection;
using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Fusion;
using OBP.RAC1.Camera;
using OBP.RAC1.Progression;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

public partial class OBPGame
{
    private PlaytestDriver? _playtest;
    private ObpProfile? _captureStartingProfile;
    private bool _captureControllersInitialized;

    private bool InitializePlaytest()
    {
        if (!_args.PlaytestRequested) return true;
        try
        {
            if (_args.RecordPlaytest is not null && _args.ReplayPlaytest is not null ||
                _args.RecordPlaytest == string.Empty || _args.ReplayPlaytest == string.Empty ||
                _args.LoadMovementState is not null || _args.CaptureFrame is not null ||
                _args.ShotsPath is not null || _args.ReplayForwardFrames.HasValue || _args.StressSwitch is not null ||
                WorldSmokeHarness.IsRequested(_args))
                throw new InvalidDataException("Choose one playtest mode, starting from an ordinary level entry.");
            // Godot consumes --fixed-fps before exposing command-line arguments.
            // Check the configured physics rate here and actual callback deltas
            // at runtime; the wrappers pass the fixed engine flag explicitly.
            if (Engine.PhysicsTicksPerSecond != 60)
                throw new InvalidDataException("Playtest runs require --fixed-fps 60 before --. Use the record/replay wrappers.");
            var reader = _args.ReplayPlaytest is { } path ? new PlaytestRunReader(path) : null;
            _playtest = new PlaytestDriver { Game = this, Reader = reader };
            if (reader is not null)
            {
                var header = reader.Header;
                if (header.Weapons is { } weapons)
                {
                    bool isolatedLoadout = weapons.LoadoutId == FusionWeaponSession.LoadoutId;
                    if ((!isolatedLoadout && weapons.LoadoutId != FusionWeaponSession.DefaultLoadoutId) || weapons.DamagePolicy != FusionDamagePolicy.Id ||
                        isolatedLoadout && header.Destination != "rac1:LEVEL0")
                        throw new InvalidDataException("Recording requires an unsupported cross-game loadout or damage policy.");
                    _args = _args with { FusionTest = isolatedLoadout };
                }
                else if (_args.FusionTest)
                    throw new InvalidDataException("Recording has no fusion loadout; remove --fusion-test.");
                ValidatePlaytestDestination(header.Destination);
                header.VerifyEnvironment(Engine.GetVersionInfo()["string"].AsString(), OS.GetName(), PlaytestPhysicsEngine(), Engine.PhysicsTicksPerSecond);
                if (!PlaytestBuildMatches(header) && !_args.PlaytestAllowBuildChange)
                    throw new InvalidDataException("Recording was made with a different build; explicitly allow build changes to investigate it.");
                if (header.Version == PlaytestRunHeader.LegacyVersion)
                {
                    var envelope = header.Campaign.Deserialize<Rac1CampaignSaveEnvelope>() ?? throw new InvalidDataException("Recording has no starting campaign.");
                    _ = Rac1CampaignSavePolicy.RestoreOrDefault(envelope);
                    string isolated = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../captures/repros/replay-" + Guid.NewGuid().ToString("N"), "campaign.json"));
                    Directory.CreateDirectory(Path.GetDirectoryName(isolated)!); File.WriteAllText(isolated, header.Campaign.GetRawText());
                    _args = _args with { RetailParity = true, Rac1CampaignSavePath = isolated };
                }
                else _captureStartingProfile = ObpProfile.Decode(System.Text.Encoding.UTF8.GetBytes(header.PlayerProfile!.Value.GetRawText()));
                _args = _args with { Destination = header.Destination, FixedSeed = header.Seed, NoProfile = true };
                _playtest.OriginalCameraOptions = CameraSettingsStore.Current;
                CameraSettingsStore.Set(header.CameraOptions.Deserialize<Rac1CameraOptions>()!.Validate(), persist: false);
            }
            else
            {
                ValidatePlaytestDestination(_args.Destination);
                _args = _args with { FixedSeed = _args.FixedSeed ?? 1440 };
                _playtest.OriginalCameraOptions = CameraSettingsStore.Current;
            }
            // Last root sibling receives game-window input before descendant UI.
            // Processing priorities bracket both physics and render callbacks.
            var driver = _playtest;
            Callable.From(() => GetTree().Root.AddChild(driver)).CallDeferred();
            TreeExiting += driver.FinishOnExit;
            return true;
        }
        catch (Exception error)
        {
            GD.PrintErr("[playtest] startup failed: " + error.Message);
            if (_args.BugCapture && !_args.BugReportSmoke && DisplayServer.GetName() != "headless")
                OS.Alert(error.Message, "OBP Bug Capture could not start");
            if (_args.ReplayPlaytest is not null)
            {
                try
                {
                    string output = PlaytestReportPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    File.WriteAllText(output, JsonSerializer.Serialize(new { Result = "invalid", Error = error.Message }));
                }
                catch (Exception reportError) { GD.PrintErr("[playtest] report failed: " + reportError.Message); }
            }
            _playtest?.Reader?.Dispose();
            _playtest?.Free();
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(this, "playtest-invalid", 2);
            return false;
        }
    }

    private string PlaytestReportPath() => Path.GetFullPath(_args.PlaytestReport ?? _args.ReplayPlaytest + ".report.json");

    private static void ValidatePlaytestDestination(string? destination)
    {
        if (destination != "obp:fusion-lab" && (destination is null || TrilogyWorldProviders.Resolve(destination) is null))
            throw new InvalidDataException("Recording requires a registered source-qualified world.");
    }

    private static string PlaytestPhysicsEngine() => ProjectSettings.GetSetting("physics/3d/physics_engine", "DEFAULT").AsString();

    private static IReadOnlyDictionary<string, Guid> PlaytestAssemblyIds()
    {
        var result = new SortedDictionary<string, Guid>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        pending.Enqueue(typeof(OBPGame).Assembly);
        while (pending.TryDequeue(out var assembly))
        {
            if (!result.TryAdd(assembly.GetName().Name!, assembly.ManifestModule.ModuleVersionId)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(a => a.Name?.StartsWith("OBP.", StringComparison.Ordinal) == true))
                pending.Enqueue(Assembly.Load(reference));
        }
        return result;
    }

    private static bool PlaytestBuildMatches(PlaytestRunHeader header) =>
        header.AssemblyId == typeof(OBPGame).Module.ModuleVersionId && header.MatchesAssemblies(PlaytestAssemblyIds());

    private PlaytestRunHeader CapturePlaytestHeader() => new(
        OrdinaryPlayerSession ? PlaytestRunHeader.CurrentVersion : PlaytestRunHeader.LegacyVersion, _currentDestinationId!, _world!.BuildId,
        typeof(OBPGame).Module.ModuleVersionId, Engine.GetVersionInfo()["string"].AsString(), OS.GetName(),
        PlaytestPhysicsEngine(), Engine.PhysicsTicksPerSecond, _args.FixedSeed!.Value,
        GetWindow().Size.X, GetWindow().Size.Y, DateTimeOffset.UtcNow,
        OrdinaryPlayerSession ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(Rac1CampaignSavePolicy.Capture(_rac1CampaignSession.Campaign,
            _rac1CampaignSession.Weapons, _rac1BoltWallet, _rac1CollectedNovalisGoldBolts, _rac1CampaignSession.BuriedBolts)),
        JsonSerializer.SerializeToElement(CameraSettingsStore.Current), CapturePlaytestCheckpoint())
    {
        Assemblies = PlaytestAssemblyIds(),
        Weapons = FusionConfiguration,
        PlayerProfile = OrdinaryPlayerSession ? JsonSerializer.Deserialize<JsonElement>(CaptureSharedProfile().Encode()) : null,
        Sources = OrdinaryPlayerSession ? _sources.Attached.Values.Select(source => source.Identity).OrderBy(source => source.Game).ToArray() : [],
        Adapters = OrdinaryPlayerSession ? CaptureReplayAdapters() : [],
        ApplicationVersion = typeof(OBPGame).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
    };

    private PlaytestCheckpoint CapturePlaytestCheckpoint()
    {
        var values = new SortedDictionary<string, PlaytestValue>(StringComparer.Ordinal);
        void Text(string key, string? value) => values.Add(key, new(Text: value ?? ""));
        void Numbers(string key, params double[] data) => values.Add(key, new(data));
        void Vector(string key, Vector3 value) => values.Add(key, new([value.X, value.Y, value.Z], Tolerance: .002));
        Text("world.destination", _currentDestinationId);
        Numbers("world.paused", GetTree().Paused ? 1 : 0);
        Numbers("world.viewport", GetWindow().Size.X, GetWindow().Size.Y);
        Numbers("world.ui", PauseMenuOpen ? 1 : 0, QuickSelectOpen ? 1 : 0, Rac1PlanetMapOpen ? 1 : 0, ControllerSettingsOpen ? 1 : 0);
        var focus = GetViewport().GuiGetFocusOwner();
        Text("ui.focus", focus is Button button ? button.Text : focus?.GetClass());
        Numbers("ui.quickSelect", _quickSelect?.SelectedWeapon?.NativeItemId ?? -1);
        if (_fusion is not null) Text("ui.quickSelect.source", _quickSelect?.SelectedWeapon?.SourceGame is { } selectedSource ? SourceIdentityWire.FormatSource(selectedSource) : null);
        var campaign = _rac1CampaignSession.Campaign;
        Numbers("campaign.current", campaign.CurrentLevel);
        Numbers("campaign.admissions", campaign.VisitedPlanets.Select(v => (double)v).ToArray());
        Numbers("campaign.map", campaign.GalacticMap.Select(v => (double)v).ToArray());
        Numbers("campaign.levels", campaign.LevelStates.Select(v => (double)v).ToArray());
        Numbers("player.nanotech", _rac1Nanotech.Probe().Nanotech, _rac1Nanotech.Probe().IsDead ? 1 : 0);
        Numbers("player.bolts", _rac1BoltWallet.Balance);
        Numbers("player.equipment", _rac1Weapons.CurrentItemId, _rac1HeldItem.NativeItemId ?? -1);
        Text("player.heldEquipment", _rac1HeldItem.Hand.Equipment?.ToString() ?? "wrench");
        Text("player.inventory", JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()));
        Text("camera.options", JsonSerializer.Serialize(CameraSettingsStore.Current));
        if (_fusion is not null)
        {
            Text("fusion.selected", _fusion.Selected.ToString());
            Text("fusion.sources", JsonSerializer.Serialize(FusionConfiguration));
            foreach (var key in FusionWeaponSession.Weapons) Text("fusion.status." + key, JsonSerializer.Serialize(_fusion.Status(key)));
            Numbers("fusion.counts", _fusion.Tick, _fusion.ShotsFired, _fusionAcceptedHits, _fusionProjectiles.Count, _fusionEffects.Count);
            Text("fusion.lastDamage", JsonSerializer.Serialize(_fusionLastDamage));
            Text("fusion.cannon", JsonSerializer.Serialize(new
            {
                _fusion.CannonSession.State,
                Charge = _fusion.CannonFrame?.ChargeUpdates ?? 0,
                Beam = _fusion.CannonFrame?.BeamUpdatesRemaining ?? 0
            }));
            foreach (var (key, node) in _fusionProjectiles.OrderBy(p => p.Key.Generation).ThenBy(p => p.Key.Sequence))
                if (IsInstanceValid(node)) Vector($"fusion.projectile.{key.Generation}.{key.Sequence}", node.GlobalPosition);
        }
        if (_player is { } p && IsInstanceValid(p))
        {
            Vector("player.position", p.GlobalPosition);
            Vector("player.velocity", p.Velocity);
            values["player.facing"] = new([p.Rac1CurrentYaw, p.Rac1ControlYaw], Tolerance: .0001);
            Numbers("player.support", p.IsOnFloor() ? 1 : 0, p.Rac1RecoveredSupportGrounded ? 1 : 0);
            Text("player.phase", p.Rac1MovementPhase.ToString());
            Text("player.animation", p.AnimationState.ToString());
            Numbers("player.avatar", CurrentPlayerAvatarSourceSequence() ?? -1,
                _playerAvatarView?.CurrentFrame ?? -1, _playerAvatarView?.FramesPerSecond ?? 0);
            Text("player.step", p.Rac1SmallStepDecision);
            Text("player.action", JsonSerializer.Serialize(_rac1Nanotech.Actions.Probe()));
            Text("player.wrench", JsonSerializer.Serialize(p.Rac1WrenchFrame));
            Vector("camera.position", p.Camera.GlobalPosition);
            Vector("camera.rotation", p.Camera.GlobalRotation);
        }
        foreach (var instance in _rac1MobyRuntime.Instances.OrderBy(v => v.NativeClassId).ThenBy(v => v.InstanceIndex))
            Numbers($"moby.{instance.NativeClassId}.{instance.InstanceIndex}.state", instance.State.NativeState,
                instance.IsActive ? 1 : 0, instance.State.IsTerminalized ? 1 : 0);
        foreach (var node in _sceneResult?.DynamicObjectNodes ?? [])
            if (IsInstanceValid(node.Root))
            {
                string key = $"object.{node.Source.NativeClassId}.{node.Source.InstanceIndex}";
                Vector(key + ".position", node.Root.GlobalPosition);
                Vector(key + ".rotation", node.Root.GlobalRotation);
                Numbers(key + ".visible", node.Root.Visible ? 1 : 0);
            }
        foreach (var (id, enemy) in _rac1Enemies.OrderBy(v => v.Key))
            if (IsInstanceValid(enemy.Body))
            {
                Vector($"enemy.{id}.velocity", enemy.Body.Velocity);
                values[$"enemy.{id}.animation"] = new([enemy.Animation.Sequence, enemy.Animation.Position], Tolerance: .0001);
                Numbers($"enemy.{id}.lifetime", enemy.DeathTicks, enemy.RewardEmitted ? 1 : 0, enemy.Buried ? 1 : 0);
                if (enemy.Node.Source.NativeClassId == 1440 && _rac1Class1440 is not null)
                    Numbers($"enemy.{id}.health", _rac1Class1440.Get(id).Health);
                if (_rac1HostileNodes.ContainsKey(id)) Numbers($"enemy.{id}.health", _rac1Hostiles.Probe(enemy.Node.Source).Health);
            }
        Numbers("transient.counts", _rac1Projectiles.Count, _rac1PickupMotion.Count, _rac1EquipmentShots.Count,
            _rac1DoomAgents.Count, _rac1AmmoPickups.Count, _rac1NanotechClouds.Count);
        foreach (var (id, projectile) in _rac1Projectiles.OrderBy(p => p.Key))
        {
            Vector($"bomb.{id}.position", FromEffectNative(projectile.Motion.Position));
            Vector($"bomb.{id}.velocity", FromEffectNative(projectile.Motion.Velocity));
            Numbers($"bomb.{id}.state", projectile.Motion.NativeState, projectile.Motion.BlastAge);
        }
        foreach (var shot in _rac1EquipmentShots.OrderBy(s => s.Id))
        {
            Vector($"shot.{shot.Id}.position", shot.Position);
            Vector($"shot.{shot.Id}.step", shot.Step);
            Numbers($"shot.{shot.Id}.state", (int)shot.Weapon, shot.Age, shot.Grounded ? 1 : 0);
        }
        foreach (var agent in _rac1DoomAgents.OrderBy(a => a.Id))
            if (IsInstanceValid(agent.Body))
            {
                Vector($"doom.{agent.Id}.position", agent.Body.GlobalPosition);
                Vector($"doom.{agent.Id}.velocity", agent.Body.Velocity);
            }
        foreach (var (id, node) in _rac1PickupNodes.OrderBy(p => p.Key))
            if (IsInstanceValid(node)) Vector($"pickup.{id}.position", node.GlobalPosition);
        foreach (var crank in _rac1NovalisCrankNodes.Keys.Order()) Numbers($"crank.{crank}.progress", _rac1NovalisCranks.Progress(crank));
        CaptureSharedCheckpoint(values);
        return new(values);
    }
}
