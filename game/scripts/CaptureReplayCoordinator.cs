using System.Reflection;
using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Game.Diagnostics;
using OBP.RAC1.Camera;
using OBP.RAC1.Progression;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed record CaptureEnvironment(string Engine, string Platform, string PhysicsEngine, int PhysicsHz);
internal sealed record CaptureHeaderSnapshot(string Destination, string Build, int Width, int Height,
    JsonElement Campaign, JsonElement CameraOptions, PlaytestWeaponConfiguration? Weapons, ObpProfile? Profile);
internal sealed record CaptureReadiness(bool Preparing, string[] ActorProblems, bool Lab, int Placed, int Expected, bool PlayerAndWorld);
internal sealed record CaptureEffectReadiness(bool Ready, string? Failure);
internal sealed record CaptureEffectPorts(Func<CaptureEffectReadiness> Prepare, Func<string?> Adapter,
    Action<SortedDictionary<string, PlaytestValue>> Checkpoint);
internal sealed record CaptureCommandActions(Func<string, bool> Destination, Func<string, bool> Equip,
    Func<string, bool, bool> Gadget, Func<int, string?, bool> QuickSlot, Func<bool> Unequip, Func<string, bool> Effects);
internal sealed record CaptureReplayPorts(Func<bool> OrdinaryPlayer, Func<long> NativeTick,
    Func<CaptureHeaderSnapshot> Header, Func<CaptureCheckpointSnapshot> Checkpoint,
    Func<CaptureSharedCheckpoint> Shared, Func<IReadOnlyList<ObpBuildIdentity>> Sources,
    Func<IReadOnlyList<string>> ActorAdapters, Func<CaptureReadiness> Readiness,
    CaptureEffectPorts Effects, CaptureCommandActions Commands, Func<string, bool> RegisteredDestination,
    Func<IReadOnlyList<VerificationSourceStamp>> SourceStamps);

internal interface ICaptureReplaySession
{
    PlaytestRunReader? Reader { get; }
    bool WaitingForEntry { get; }
    bool PreparingWorld { get; }
    bool HasEntered { get; }
    bool Finished { get; }
    bool BugInputSuspended { get; }
    PlaytestRunWriter BugWriter { get; }
    void RecordSessionCommand(PlaytestCommand command);
    void RecordGameplayFact(PlaytestGameplayFact fact);
    void WorldEntered();
    void Save();
    void RequestBookmark(string path);
    bool RejectMovementLoad();
    void ResumeAfterBugOverlay();
    void ShowBugNotice(string notice);
    void SuspendBugCaptureForQuit();
    Task PrepareBugCaptureQuitAsync();
}

/// <summary>Recording policy and wire observations, independent of UI, Godot callbacks and optional disc hashing.</summary>
internal sealed class CaptureReplayCoordinator(CaptureReplayPorts ports, CaptureEnvironment environment, CaptureBuildIdentity build)
{
    private ObpProfile? _startingProfile;
    private bool _controllersInitialized;
    private IReadOnlyList<VerificationSourceStamp>? _recordingSources;
    private CaptureDiagnosticHarness? _diagnostics;
    internal CommandLineArgs Arguments { get; private set; } = new();
    internal ICaptureReplaySession? Recorder { get; private set; }
    internal CaptureBuildIdentity Build => build;
    internal CaptureEnvironment Environment => environment;
    internal bool IncludesEffects => EffectCaptureContract.Includes(Arguments, Recorder?.Reader?.Header);
    internal CaptureDiagnosticHarness Diagnostics(Func<CaptureDiagnosticHarness> create) => _diagnostics ??= create();

    internal CommandLineArgs Initialize(CommandLineArgs args, Func<string> legacyCampaignPath,
        Func<CaptureReplayCoordinator, PlaytestRunReader?, Rac1CameraOptions?, ICaptureReplaySession> install)
    {
        Arguments = args;
        if (!args.PlaytestRequested) return args;
        PlaytestRunReader? reader = null;
        try
        {
            if (args.RecordPlaytest is not null && args.ReplayPlaytest is not null ||
                args.RecordPlaytest == string.Empty || args.ReplayPlaytest == string.Empty ||
                args.LoadMovementState is not null || args.CaptureFrame is not null || args.ShotsPath is not null ||
                args.ReplayForwardFrames.HasValue || args.StressSwitch is not null || WorldSmokeHarness.IsRequested(args))
                throw new InvalidDataException("Choose one playtest mode, starting from an ordinary level entry.");
            if (environment.PhysicsHz != 60)
                throw new InvalidDataException("Playtest runs require --fixed-fps 60 before --. Use the record/replay wrappers.");
            reader = args.ReplayPlaytest is { } path ? new PlaytestRunReader(path) : null;
            Rac1CameraOptions? camera = null;
            if (reader is not null)
            {
                var header = reader.Header;
                if (header.Weapons is { } weapons)
                {
                    bool isolatedLoadout = weapons.LoadoutId == FusionWeaponSession.LoadoutId;
                    if ((!isolatedLoadout && weapons.LoadoutId != FusionWeaponSession.DefaultLoadoutId) || weapons.DamagePolicy != FusionDamagePolicy.Id ||
                        isolatedLoadout && header.Destination != "rac1:LEVEL0")
                        throw new InvalidDataException("Recording requires an unsupported cross-game loadout or damage policy.");
                    args = args with { FusionTest = isolatedLoadout };
                }
                else if (args.FusionTest)
                    throw new InvalidDataException("Recording has no fusion loadout; remove --fusion-test.");
                ValidateDestination(header.Destination);
                header.VerifyEnvironment(environment.Engine, environment.Platform, environment.PhysicsEngine, environment.PhysicsHz);
                if (!build.Matches(header) && !args.PlaytestAllowBuildChange)
                    throw new InvalidDataException("Recording was made with a different build; explicitly allow build changes to investigate it.");
                if (header.Version == PlaytestRunHeader.LegacyVersion)
                {
                    var envelope = header.Campaign.Deserialize<Rac1CampaignSaveEnvelope>() ?? throw new InvalidDataException("Recording has no starting campaign.");
                    _ = Rac1CampaignSavePolicy.RestoreOrDefault(envelope);
                    string isolated = legacyCampaignPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(isolated)!);
                    File.WriteAllText(isolated, header.Campaign.GetRawText());
                    args = args with { RetailParity = true, Rac1CampaignSavePath = isolated };
                }
                else _startingProfile = ObpProfile.Decode(System.Text.Encoding.UTF8.GetBytes(header.PlayerProfile!.Value.GetRawText()));
                args = args with { Destination = header.Destination, FixedSeed = header.Seed, NoProfile = true };
                camera = header.CameraOptions.Deserialize<Rac1CameraOptions>()!.Validate();
            }
            else
            {
                ValidateDestination(args.Destination);
                args = args with { FixedSeed = args.FixedSeed ?? 1440 };
            }
            Arguments = args;
            Recorder = install(this, reader, camera);
            return args;
        }
        catch
        {
            reader?.Dispose();
            _startingProfile = null;
            throw;
        }
    }

    internal ObpProfile? TakeStartingProfile()
    {
        var profile = _startingProfile; _startingProfile = null; return profile;
    }

    internal void PrepareControllers(Action reset)
    {
        if (Recorder is not { WaitingForEntry: true } || !ports.OrdinaryPlayer() || _controllersInitialized) return;
        reset(); _controllersInitialized = true;
    }

    internal string ReportPath => Path.GetFullPath(Arguments.PlaytestReport ?? Arguments.ReplayPlaytest + ".report.json");

    internal void WriteStartupFailure(Exception error)
    {
        if (Arguments.ReplayPlaytest is null) return;
        string output = ReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Result = "invalid", Error = error.Message }));
    }

    private void ValidateDestination(string? destination)
    {
        if (destination != "obp:fusion-lab" && (destination is null || !ports.RegisteredDestination(destination)))
            throw new InvalidDataException("Recording requires a registered source-qualified world.");
    }

    internal IReadOnlyList<PlaytestAdapter> ReplayAdapters()
    {
        var adapters = new SortedSet<string>(StringComparer.Ordinal) { "obp/shared-player/v1", "rac1/ordinary-player/v1", "obp/weapon-controllers/v1" };
        if (IncludesEffects && ports.Effects.Adapter() is { } effect) adapters.Add(EffectCaptureContract.AdapterPrefix + effect);
        foreach (string adapter in ports.ActorAdapters()) adapters.Add(adapter);
        return adapters.Select(id => new PlaytestAdapter(id, 1, 60)).ToArray();
    }

    internal bool WorldReady()
    {
        var ready = ports.Readiness();
        if (ready.Preparing) return false;
        if (IncludesEffects)
        {
            var effects = ports.Effects.Prepare();
            if (effects.Failure is { } failure) throw new InvalidDataException(failure);
            if (!effects.Ready) return false;
        }
        if (ready.ActorProblems.Length > 0) throw new InvalidOperationException("Capture requires admitted actor contexts: " + string.Join("; ", ready.ActorProblems));
        if (ready.Lab && ready.Placed != ready.Expected) return false;
        _ = ReplayAdapters();
        return ready.PlayerAndWorld;
    }

    internal void RecordCommand(PlaytestCommand command)
    {
        if (ports.OrdinaryPlayer()) Recorder?.RecordSessionCommand(command);
    }

    internal void RecordGameplayFact(string kind, object facts)
    {
        if (ports.OrdinaryPlayer()) Recorder?.RecordGameplayFact(new(kind, ports.NativeTick(), JsonSerializer.SerializeToElement(facts)));
    }

    internal void ApplyCommand(PlaytestCommand command)
    {
        command.Validate();
        var actions = ports.Commands;
        bool accepted = command.Kind switch
        {
            "destination" => actions.Destination(command.Value),
            "equip" => actions.Equip(command.Value),
            "gadget" => actions.Gadget(command.Value, command.Enabled),
            "quick-slot" => actions.QuickSlot(command.Slot, command.Value.Length == 0 ? null : command.Value),
            "unequip" => actions.Unequip(),
            "effects" => actions.Effects(command.Value),
            _ => false,
        };
        if (!accepted) throw new InvalidDataException("Recorded session operation is unavailable: " + command.Kind + " " + command.Value);
    }

    internal void ValidateProvenance(PlaytestRunHeader header)
    {
        if (header.Version != PlaytestRunHeader.LegacyVersion) header.VerifySessionSources(ports.Sources(), ReplayAdapters());
    }

    internal PlaytestCheckpoint CaptureCheckpoint()
    {
        ValidateRecordingSources();
        var values = CaptureCheckpointBuilder.BuildValues(ports.Checkpoint());
        if (ports.OrdinaryPlayer())
        {
            CaptureCheckpointBuilder.AddShared(values, ports.Shared(), ReplayAdapters());
            if (IncludesEffects) ports.Effects.Checkpoint(values);
        }
        return new(values);
    }

    internal PlaytestRunHeader CaptureHeader()
    {
        _recordingSources = ports.SourceStamps().OrderBy(source => source.Game).ToArray();
        var state = ports.Header(); bool ordinary = ports.OrdinaryPlayer();
        return new(ordinary ? PlaytestRunHeader.CurrentVersion : PlaytestRunHeader.LegacyVersion, state.Destination, state.Build,
            build.AssemblyId, environment.Engine, environment.Platform, environment.PhysicsEngine, environment.PhysicsHz,
            Arguments.FixedSeed!.Value, state.Width, state.Height, DateTimeOffset.UtcNow,
            ordinary ? JsonSerializer.SerializeToElement(new { }) : state.Campaign, state.CameraOptions, CaptureCheckpoint())
        {
            Assemblies = build.Assemblies,
            Weapons = state.Weapons,
            PlayerProfile = ordinary ? JsonSerializer.Deserialize<JsonElement>(state.Profile!.Encode()) : null,
            Sources = ordinary ? ports.Sources().OrderBy(source => source.Game).ToArray() : [],
            Adapters = ordinary ? ReplayAdapters() : [],
            ApplicationVersion = build.ApplicationVersion,
        };
    }

    internal void ValidateRecordingSources()
    {
        if (_recordingSources is not null && !_recordingSources.SequenceEqual(ports.SourceStamps().OrderBy(source => source.Game)))
            throw new InvalidDataException("Recording source attachment changed; start a fresh recording with the current sources.");
    }
}

internal sealed record CaptureBuildIdentity(Guid AssemblyId, IReadOnlyDictionary<string, Guid> Assemblies, string ApplicationVersion)
{
    internal bool Matches(PlaytestRunHeader header) => header.AssemblyId == AssemblyId && header.MatchesAssemblies(Assemblies);
    internal static CaptureBuildIdentity Read(Assembly application)
    {
        var result = new SortedDictionary<string, Guid>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(); pending.Enqueue(application);
        while (pending.TryDequeue(out var assembly))
        {
            if (!result.TryAdd(assembly.GetName().Name!, assembly.ManifestModule.ModuleVersionId)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(a => a.Name?.StartsWith("OBP.", StringComparison.Ordinal) == true))
                pending.Enqueue(Assembly.Load(reference));
        }
        return new(application.ManifestModule.ModuleVersionId, result,
            application.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
    }
}
