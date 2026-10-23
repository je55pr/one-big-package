using System.Globalization;

namespace OneBigPackage;

/// <summary>
/// OBP command-line options, taken from the args after Godot's <c>--</c>
/// separator (<c>OS.GetCmdlineUserArgs()</c>).
/// </summary>
public sealed record CommandLineArgs
{
    public string TestScene { get; init; } = "smoke";
    public bool TestSceneExplicit { get; init; }
    public int? CaptureFrame { get; init; }
    public string? CaptureOut { get; init; }
    public int? FixedSeed { get; init; }
    public string? RecordPlaytest { get; init; }
    public string? ReplayPlaytest { get; init; }
    public string? PlaytestReport { get; init; }
    public bool PlaytestAllowBuildChange { get; init; }
    public bool PlaytestSmoke { get; init; }
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
    public string? GcIso { get; init; }
    public int GcLevel { get; init; } = 1;

    /// <summary>Path to an R&amp;C1 retail ISO (composition lab source).</summary>
    public string? Rac1Iso { get; init; }

    /// <summary>Path to an Up Your Arsenal retail ISO (composition lab source).</summary>
    public string? UyaIso { get; init; }

    /// <summary>Historical GC planet/level alias resolved to a canonical <c>rac2:LEVEL*</c> destination.</summary>
    public string? Planet { get; init; }

    /// <summary>
    /// Canonical game-neutral destination id such as <c>rac2:LEVEL1</c>. The
    /// scene bootstrap resolves this through the registered world providers.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>Enter the GC destination selected by <see cref="Planet"/> / <see cref="GcLevel"/> directly.</summary>
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

    /// <summary>Boot the multi-world composition / fusion lab. Optional value = a saved composition JSON to load on start.</summary>
    public bool Compose { get; init; }

    /// <summary>Path to a saved composition document (implies <see cref="Compose"/>).</summary>
    public string? CompositionPath { get; init; }

    /// <summary>Composition capture view: "overview" | "top" | "a-only" | "b-only" | "overlay". Headless composition capture.</summary>
    public string? CompositionView { get; init; }

    /// <summary>Comma-separated <c>id=game:level</c> world specs to seed a composition when no <see cref="CompositionPath"/> is given (e.g. "a=gc:1,b=gc:3").</summary>
    public string? ComposeWorlds { get; init; }

    /// <summary>Run the anchor alignment solve on load (before any capture) and apply it to world B. "rigid" (default) or "scale".</summary>
    public string? ComposeSolve { get; init; }

    /// <summary>Composition lifecycle stress: build + tear down every world N times, logging node / orphan / memory counts, then quit.</summary>
    public int? ComposeReload { get; init; }

    public static CommandLineArgs Parse(string[] args)
    {
        var result = new CommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            result = a switch
            {
                "--test-scene" => result with { TestScene = Next() ?? result.TestScene, TestSceneExplicit = true },
                "--capture-frame" => result with { CaptureFrame = ParseInt(Next()) },
                "--capture-out" => result with { CaptureOut = Next() },
                "--fixed-seed" => result with { FixedSeed = ParseInt(Next()) },
                "--record-playtest" => result with { RecordPlaytest = Next() ?? string.Empty },
                "--replay-playtest" => result with { ReplayPlaytest = Next() ?? string.Empty },
                "--playtest-report" => result with { PlaytestReport = Next() },
                "--playtest-allow-build-change" => result with { PlaytestAllowBuildChange = true },
                "--playtest-smoke" => result with { PlaytestSmoke = true },
                "--movement-state-file" => result with { MovementStatePath = Next() },
                "--load-movement-state" => result with { LoadMovementState = Next() },
                "--replay-forward" => result with { ReplayForwardFrames = ParseInt(Next()) ?? -1 },
                "--replay-out" => result with { ReplayOut = Next() },
                "--replay-profile" => result with { ReplayProfile = true },
                "--movement-state-smoke" => result with { MovementStateSmoke = true },
                "--gc-iso" => result with { GcIso = Next() },
                "--rac1-iso" => result with { Rac1Iso = Next() },
                "--uya-iso" => result with { UyaIso = Next() },
                "--gc-level" => result with { GcLevel = ParseInt(Next()) ?? result.GcLevel },
                "--planet" => result with { Planet = Next(), DirectLoad = true },
                "--destination" => result with { Destination = Next() },
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
                "--compose" => result with { Compose = true },
                "--composition" => result with { Compose = true, CompositionPath = Next() },
                "--composition-view" => result with { CompositionView = Next() },
                "--compose-worlds" => result with { ComposeWorlds = Next() },
                "--compose-solve" => result with { ComposeSolve = "rigid" },
                "--compose-solve-scale" => result with { ComposeSolve = "scale" },
                "--compose-reload" => result with { ComposeReload = ParseInt(Next()) },
                _ => result,
            };
        }

        return result;
    }

    private static int? ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
}
