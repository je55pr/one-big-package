using System.Globalization;

namespace OBP.Game;

/// <summary>
/// OBP command-line options, taken from the args after Godot's <c>--</c>
/// separator (<c>OS.GetCmdlineUserArgs()</c>).
/// Source flags are --rac1-iso/--rac2-iso/--rac3-iso. The legacy --gc-iso and
/// --uya-iso aliases share their canonical slot and conflict validation here.
/// </summary>
public sealed record CommandLineArgs
{
    public string TestScene { get; init; } = "smoke";
    public bool TestSceneExplicit { get; init; }
    public int? CaptureFrame { get; init; }
    public string? CaptureOut { get; init; }
    public int? FixedSeed { get; init; }
    public string? RecordPlaytest { get; init; }
    public bool RecordEffects { get; init; }
    public string? ReplayPlaytest { get; init; }
    public string? PlaytestReport { get; init; }
    public bool PlaytestAllowBuildChange { get; init; }
    public bool PlaytestSmoke { get; init; }
    /// <summary>Explicit, ephemeral three-game weapon demo on fresh R&amp;C1 Veldin.</summary>
    public bool FusionTest { get; init; }
    public bool FusionSmoke { get; init; }
    public bool EffectsSmoke { get; init; }
    public bool RetailParity { get; init; }
    public bool DiagnosticPlayer { get; init; }
    public bool DiagnosticSources { get; init; }
    public bool SourceSetupCheck { get; init; }
    public bool OrdinaryUiSmoke { get; init; }
    public string? OrdinaryUiCase { get; init; }
    public bool PlayerSessionSmoke { get; init; }
    public bool PlayerCapabilitiesSmoke { get; init; }
    public bool TrilogyGameplaySmoke { get; init; }
    public bool ReconciliationSmoke { get; init; }
    public string? ProfileSmoke { get; init; }
    public string? AcceptanceOut { get; init; }
    public string? ProfilePath { get; init; }
    public string? SourceConfigPath { get; init; }
    public bool NoProfile { get; init; }
    public string? PlayerCapabilitiesOut { get; init; }
    public bool NativeParityRequested => RetailParity || PlaytestSmoke || FusionTest || FusionSmoke || DefaultWeaponsSmoke ||
        Rac1CampaignPersistenceRequested || Rac1VeldinPlaySmoke || Rac1MovementContactSmoke || Rac1StartupVisibilitySmoke ||
        Rac1CombatContractSmoke || Rac1WrenchSmoke || Rac1CrateSmoke || Rac1EnemySmoke || Rac1FrogSmoke ||
        Rac1BombGloveSmoke || Rac1NovalisSwimSmoke || Rac1NovalisRouteSmoke || Rac1NovalisPeckbotSmoke ||
        Rac1NovalisBirdbotSmoke || Rac1NovalisMusicSmoke || Rac1MetropolisTaxiSmoke || Rac1WeaponsSmoke ||
        Rac1PlacedBoltSmoke || Rac1CampaignSmoke || Rac1MissionPlaySmoke || Rac1PlayerAudioSmoke || MovementStateSmoke ||
        MovementCase is "environmental-death" or "swim";
    public bool DefaultWeaponsSmoke { get; init; }
    public bool BugCapture { get; init; }
    public bool BugReportSmoke { get; init; }
    public string? BugReportInbox { get; init; }
    public string? ReplayBugReport { get; init; }
    public string? BugReportId { get; init; }
    public bool PlaytestInspectMarker { get; init; }
    public bool PlaytestRequested => RecordPlaytest is not null || ReplayPlaytest is not null;
    public string? MovementStatePath { get; init; }
    public string? LoadMovementState { get; init; }
    public int? ReplayForwardFrames { get; init; }
    public string? ReplayOut { get; init; }
    public bool ReplayProfile { get; init; }
    public bool MovementStateSmoke { get; init; }
    public bool ControllerSmoke { get; init; }
    public bool Rac1PlayerAudioSmoke { get; init; }

    /// <summary>Path to a Going Commando retail ISO.</summary>
    public string? Rac2Iso { get; init; }
    public int Rac2Level { get; init; } = 1;

    /// <summary>Path to the supported R&amp;C1 retail ISO.</summary>
    public string? Rac1Iso { get; init; }

    /// <summary>Path to the supported Up Your Arsenal retail ISO.</summary>
    public string? Rac3Iso { get; init; }

    /// <summary>Historical GC planet/level alias resolved to a canonical <c>rac2:LEVEL*</c> destination.</summary>
    public string? Planet { get; init; }

    /// <summary>
    /// Canonical game-neutral destination id such as <c>rac2:LEVEL1</c>. The
    /// scene bootstrap resolves this through the registered world providers.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>Enter the GC destination selected by <see cref="Planet"/> / <see cref="Rac2Level"/> directly.</summary>
    public bool DirectLoad { get; init; }

    /// <summary>Skip the interactive title screen on an ordinary application launch.</summary>
    public bool SkipTitle { get; init; }

    /// <summary>Comma-separated GC planet tokens — lifecycle stress test retained for the GC regression harness.</summary>
    public string? StressSwitch { get; init; }

    /// <summary>Run the full multi-gigabyte SHA-256 authority verification after loading (default: serial/size source checks only).</summary>
    public bool VerifyHash { get; init; }

    /// <summary>Overlay the decoded octree collision as a translucent debug mesh.</summary>
    public bool CollisionDebug { get; init; }

    /// <summary>
    /// Comma-separated <see cref="OBP.Godot.DebugOverlay"/> layers to enable on
    /// load for a deterministic capture — e.g. <c>kindtint,collisionwire</c> or
    /// <c>isolate:moby,worldbounds</c>. F1..F7 toggle them interactively.
    /// </summary>
    public string? Overlay { get; init; }

    /// <summary>Path to a JSON <see cref="OBP.Runtime.Presentation.ShotList"/> — run every named shot in one process, then quit.</summary>
    public string? ShotsPath { get; init; }

    /// <summary>Planet / level token for <see cref="ShotsPath"/> when the list does not pin its own world.</summary>
    public string? ShotsWorld { get; init; }

    /// <summary>Directory for <c>--shots</c> output (default: <c>captures/shots</c> relative to the working dir).</summary>
    public string? ShotsOut { get; init; }

    /// <summary>Open the world inspector on load (picks the crosshair centre).</summary>
    public bool Inspect { get; init; }

    /// <summary>Spawn the debug player next to the first animated moby instead of the ship point (MobySequence showcase).</summary>
    public bool AnimFocus { get; init; }

    /// <summary>Render only the animated mobies (no world geometry) with a static camera framed on them — the clean MobySequence showcase.</summary>
    public bool AnimSolo { get; init; }

    /// <summary>Debug harness: spawn beside the first preserved class-500 crate.</summary>
    public bool CrateFocus { get; init; }

    /// <summary>Debug harness: feed one qualifying native break event to the focused/first class-500 crate.</summary>
    public bool CrateAutoStrike { get; init; }

    /// <summary>Run the ordinary-input-only R&amp;C1 Veldin playability smoke and quit.</summary>
    public bool Rac1VeldinPlaySmoke { get; init; }

    /// <summary>Run the R&amp;C1 Veldin movement/contact-only ordinary-input smoke and quit.</summary>
    public bool Rac1MovementContactSmoke { get; init; }

    /// <summary>Exercise cold startup visibility, then enter ordinary R&amp;C1 Veldin through the bootstrap/provider path.</summary>
    public bool Rac1StartupVisibilitySmoke { get; init; }

    /// <summary>Run the lower-level synthetic R&amp;C1 combat host-contract smoke and quit.</summary>
    public bool Rac1CombatContractSmoke { get; init; }

    /// <summary>Exercise wrench input, animation, flight and staged contact fixtures.</summary>
    public bool Rac1WrenchSmoke { get; init; }

    /// <summary>Exercise staged retail crate, fragment, bolt and resource-pickup fixtures.</summary>
    public bool Rac1CrateSmoke { get; init; }
    public bool Rac1EnemySmoke { get; init; }
    public bool Rac1FrogSmoke { get; init; }
    public bool Rac1BombGloveSmoke { get; init; }
    public bool Rac1NovalisSwimSmoke { get; init; }
    public bool Rac1NovalisRouteSmoke { get; init; }
    public bool Rac1NovalisPeckbotSmoke { get; init; }
    public bool Rac1NovalisBirdbotSmoke { get; init; }
    public bool Rac1NovalisMusicSmoke { get; init; }
    public bool Rac1MetropolisTaxiSmoke { get; init; }
    public bool Rac1WeaponsSmoke { get; init; }

    /// <summary>Exercise authored world bolt resting, attraction and collection fixtures.</summary>
    public bool Rac1PlacedBoltSmoke { get; init; }

    /// <summary>Run the two-pass evidence-backed R&amp;C1 campaign travel/persistence smoke.</summary>
    public bool Rac1CampaignSmoke { get; init; }
    /// <summary>Ordinary-input mission actors, discovery, travel and durable reload; no injected unlocks.</summary>
    public bool Rac1MissionPlaySmoke { get; init; }

    /// <summary>Explicitly opt this host session into R&amp;C1 campaign persistence.</summary>
    public bool Rac1CampaignPersist { get; init; }

    /// <summary>Override the OBP host campaign-state file. Supplying a path also opts into persistence.</summary>
    public string? Rac1CampaignSavePath { get; init; }

    public bool Rac1CampaignPersistenceRequested =>
        Rac1CampaignPersist || !string.IsNullOrWhiteSpace(Rac1CampaignSavePath);

    /// <summary>Run the cross-game Godot analogue/input smoke against the selected destination and quit.</summary>
    public bool MovementSmoke { get; init; }
    public string? MovementCase { get; init; }

    public static CommandLineArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var result = new CommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next()
            {
                if (i + 1 >= args.Length || string.IsNullOrEmpty(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(a + " requires a value.");
                return args[++i];
            }
            int NextInt(int minimum = int.MinValue, int maximum = int.MaxValue)
            {
                string value = Next();
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new ArgumentException(a + " requires a 32-bit integer.");
                if (number < minimum || number > maximum)
                    throw new ArgumentException($"{a} requires a value from {minimum} through {maximum}.");
                return number;
            }
            string SourceIso(string? existing, string flags)
            {
                string value = Next();
                if (existing is not null && existing != value)
                    throw new ArgumentException(flags + " specify conflicting source paths.");
                return value;
            }

            result = a switch
            {
                "--test-scene" => result with { TestScene = Next(), TestSceneExplicit = true },
                "--capture-frame" => result with { CaptureFrame = NextInt() },
                "--capture-out" => result with { CaptureOut = Next() },
                "--fixed-seed" => result with { FixedSeed = NextInt() },
                "--record-playtest" => result with { RecordPlaytest = Next() },
                "--replay-playtest" => result with { ReplayPlaytest = Next() },
                "--playtest-report" => result with { PlaytestReport = Next() },
                "--playtest-allow-build-change" => result with { PlaytestAllowBuildChange = true },
                "--playtest-smoke" => result with { PlaytestSmoke = true },
                "--fusion-test" => result with { FusionTest = true },
                "--fusion-smoke" => result with { FusionTest = true, FusionSmoke = true },
                "--effects-smoke" => result with { EffectsSmoke = true },
                "--record-effects" => result with { RecordEffects = true },
                "--default-weapons-smoke" => result with { DefaultWeaponsSmoke = true },
                "--bug-capture" => result with { BugCapture = true },
                "--bug-report-smoke" => result with { BugReportSmoke = true },
                "--bug-report-inbox" => result with { BugReportInbox = Next() },
                "--replay-bug-report" => result with { ReplayBugReport = Next() },
                "--bug-report-id" => result with { BugReportId = Next() },
                "--playtest-inspect-marker" => result with { PlaytestInspectMarker = true },
                "--movement-state-file" => result with { MovementStatePath = Next() },
                "--load-movement-state" => result with { LoadMovementState = Next() },
                "--replay-forward" => result with { ReplayForwardFrames = NextInt(1, 3600) },
                "--replay-out" => result with { ReplayOut = Next() },
                "--replay-profile" => result with { ReplayProfile = true },
                "--movement-state-smoke" => result with { MovementStateSmoke = true },
                "--rac2-iso" or "--gc-iso" => result with { Rac2Iso = SourceIso(result.Rac2Iso, "--rac2-iso/--gc-iso") },
                "--rac1-iso" => result with { Rac1Iso = SourceIso(result.Rac1Iso, "--rac1-iso") },
                "--rac3-iso" or "--uya-iso" => result with { Rac3Iso = SourceIso(result.Rac3Iso, "--rac3-iso/--uya-iso") },
                "--gc-level" => result with { Rac2Level = NextInt() },
                "--planet" => result with { Planet = Next(), DirectLoad = true },
                "--destination" => result with { Destination = Next() },
                "--retail-parity" => result with { RetailParity = true },
                "--diagnostic-player" => result with { DiagnosticPlayer = true },
                "--diagnostic-sources" => result with { DiagnosticSources = true },
                "--source-setup-check" => result with { SourceSetupCheck = true },
                "--ordinary-ui-smoke" => result with { OrdinaryUiSmoke = true, OrdinaryUiCase = Next() },
                "--player-session-smoke" => result with { PlayerSessionSmoke = true },
                "--player-capabilities-smoke" => result with { PlayerCapabilitiesSmoke = true },
                "--trilogy-gameplay-smoke" => result with { TrilogyGameplaySmoke = true },
                "--reconciliation-smoke" => result with { ReconciliationSmoke = true },
                "--profile-smoke" => result with { ProfileSmoke = Next() },
                "--acceptance-out" => result with { AcceptanceOut = Next() },
                "--profile" => result with { ProfilePath = Next() },
                "--source-config" => result with { SourceConfigPath = Next() },
                "--no-profile" => result with { NoProfile = true },
                "--player-capabilities-out" => result with { PlayerCapabilitiesOut = Next() },
                "--direct" => result with { DirectLoad = true },
                "--skip-title" => result with { SkipTitle = true },
                "--stress-switch" => result with { StressSwitch = Next() },
                "--verify-hash" => result with { VerifyHash = true },
                "--collision-debug" => result with { CollisionDebug = true },
                "--overlay" => result with { Overlay = Next() },
                "--shots" => result with { ShotsPath = Next() },
                "--shots-world" => result with { ShotsWorld = Next() },
                "--shots-out" => result with { ShotsOut = Next() },
                "--inspect" => result with { Inspect = true },
                "--anim-focus" => result with { AnimFocus = true },
                "--anim-solo" => result with { AnimSolo = true },
                "--crate-focus" => result with { CrateFocus = true },
                "--crate-auto-strike" => result with { CrateAutoStrike = true },
                "--rac1-veldin-play-smoke" => result with { Rac1VeldinPlaySmoke = true },
                "--rac1-movement-contact-smoke" => result with { Rac1MovementContactSmoke = true },
                "--rac1-startup-visibility-smoke" => result with { Rac1StartupVisibilitySmoke = true },
                // Historical alias now names the honest ordinary-play gate rather than the
                // synthetic staged-contact harness it previously selected.
                "--rac1-combat-smoke" => result with { Rac1VeldinPlaySmoke = true },
                "--rac1-combat-contract-smoke" => result with { Rac1CombatContractSmoke = true },
                "--rac1-wrench-smoke" => result with { Rac1WrenchSmoke = true },
                "--rac1-bomb-glove-smoke" => result with { Rac1BombGloveSmoke = true },
                "--rac1-novalis-swim-smoke" => result with { Rac1NovalisSwimSmoke = true },
                "--rac1-novalis-route-smoke" => result with { Rac1NovalisRouteSmoke = true },
                "--rac1-novalis-peckbot-smoke" => result with { Rac1NovalisPeckbotSmoke = true },
                "--rac1-novalis-birdbot-smoke" => result with { Rac1NovalisBirdbotSmoke = true },
                "--rac1-novalis-music-smoke" => result with { Rac1NovalisMusicSmoke = true },
                "--rac1-metropolis-taxi-smoke" => result with { Rac1MetropolisTaxiSmoke = true },
                "--rac1-weapons-smoke" => result with { Rac1WeaponsSmoke = true },
                "--controller-smoke" => result with { ControllerSmoke = true },
                "--rac1-player-audio-smoke" => result with { Rac1PlayerAudioSmoke = true },
                "--rac1-crate-smoke" => result with { Rac1CrateSmoke = true },
                "--rac1-enemy-smoke" => result with { Rac1EnemySmoke = true },
                "--rac1-frog-smoke" => result with { Rac1FrogSmoke = true },
                "--rac1-placed-bolt-smoke" => result with { Rac1PlacedBoltSmoke = true },
                "--rac1-campaign-smoke" => result with { Rac1CampaignSmoke = true },
                "--rac1-mission-play-smoke" => result with { Rac1MissionPlaySmoke = true },
                "--rac1-campaign-persist" => result with { Rac1CampaignPersist = true },
                "--rac1-campaign-save" => result with { Rac1CampaignSavePath = Next() },
                "--movement-smoke" => result with { MovementSmoke = true },
                "--movement-case" => result with { MovementSmoke = true, MovementCase = Next() },
                // Existing diagnostic scenes also consume these automation markers.
                "--automation" or "--smoke" => result,
                _ => throw new ArgumentException("Unknown OBP argument: " + a + ". OBP options follow Godot's -- separator."),
            };
        }

        if (result.MovementCase is not (null or "full" or "airborne" or "environmental-death" or "landing" or "swim" or
            "slope-jump" or "small-step" or "swingshot" or "traversal-contact" or "controller-weapons"))
            throw new ArgumentException("--movement-case requires a known movement regression name.");
        if (result.RecordPlaytest is not null && result.ReplayPlaytest is not null)
            throw new ArgumentException("--record-playtest cannot be combined with --replay-playtest.");
        if (result.RecordEffects && (result.RecordPlaytest is null || result.NativeParityRequested))
            throw new ArgumentException("--record-effects requires --record-playtest with the ordinary shared player.");
        if (result.BugCapture && (result.PlaytestRequested || result.ReplayBugReport is not null))
            throw new ArgumentException("--bug-capture cannot be combined with --record-playtest, --replay-playtest or --replay-bug-report.");
        if (result.ReplayForwardFrames.HasValue && result.LoadMovementState is null)
            throw new ArgumentException("--replay-forward requires --load-movement-state.");
        if (result.PlaytestRequested && (result.LoadMovementState is not null || result.CaptureFrame.HasValue ||
            result.ShotsPath is not null || result.ReplayForwardFrames.HasValue || result.StressSwitch is not null))
            throw new ArgumentException("--record-playtest/--replay-playtest require ordinary entry without --load-movement-state, --capture-frame, --shots, --replay-forward or --stress-switch.");
        if (result.EffectsSmoke && !result.TestSceneExplicit && result.Destination is null) result = result with { TestScene = "fusion-lab", TestSceneExplicit = true };
        return result;
    }
}
