using System.Globalization;

namespace OneBigPackage;

/// <summary>
/// OBP command-line options, taken from the args after Godot's <c>--</c>
/// separator (<c>OS.GetCmdlineUserArgs()</c>).
/// </summary>
public sealed record CommandLineArgs
{
    public string TestScene { get; init; } = "smoke";
    public int? CaptureFrame { get; init; }
    public string? CaptureOut { get; init; }
    public int? FixedSeed { get; init; }
    public string? TestLevel { get; init; }

    /// <summary>Path to a Going Commando retail ISO — retained for compatibility with existing GC harnesses.</summary>
    public string? GcIso { get; init; }
    public int GcLevel { get; init; } = 1;

    /// <summary>Path to an R&amp;C1 retail ISO (composition lab source).</summary>
    public string? Rac1Iso { get; init; }

    /// <summary>Path to an Up Your Arsenal retail ISO (composition lab source).</summary>
    public string? UyaIso { get; init; }

    /// <summary>Planet / level token for the legacy GC selector-free path: "oozla", "endako", "8", "LEVEL19".</summary>
    public string? Planet { get; init; }

    /// <summary>
    /// Canonical game-neutral destination id such as <c>rac2:LEVEL1</c>. The
    /// scene bootstrap resolves this through the registered world providers.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>Skip the legacy GC planet selector and load <see cref="Planet"/> / <see cref="GcLevel"/> directly.</summary>
    public bool DirectLoad { get; init; }

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
                "--test-scene" => result with { TestScene = Next() ?? result.TestScene },
                "--test-level" => result with { TestLevel = Next() },
                "--capture-frame" => result with { CaptureFrame = ParseInt(Next()) },
                "--capture-out" => result with { CaptureOut = Next() },
                "--fixed-seed" => result with { FixedSeed = ParseInt(Next()) },
                "--gc-iso" => result with { GcIso = Next() },
                "--rac1-iso" => result with { Rac1Iso = Next() },
                "--uya-iso" => result with { UyaIso = Next() },
                "--gc-level" => result with { GcLevel = ParseInt(Next()) ?? result.GcLevel },
                "--planet" => result with { Planet = Next(), DirectLoad = true },
                "--destination" => result with { Destination = Next() },
                "--direct" => result with { DirectLoad = true },
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
