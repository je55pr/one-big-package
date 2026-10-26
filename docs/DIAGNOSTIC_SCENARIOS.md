# Diagnostic scenario contracts (#335)

Standalone diagnostics live outside `OBPGame`. A scenario declares its stable,
versioned ID, exact required source/build authorities, setup policy, destination
when needed, timeout, maximum frame waits and nonzero failure exit code. Its
constructor receives only the focused observation/action ports it needs.
`DiagnosticRunContext` supplies cancellation, phase waits and temporary resource
ownership; it cannot look up the root or arbitrary services.

## Admission and lifetime

The explicit test driver admits a scenario. Ordinary startup cannot implicitly
run it. Asset-free fixtures require no source configuration; explicit source
diagnostics name their own authorities. Ordinary gameplay scenarios still
require all three real source/build authorities, regardless of destination.
An absent provider or mismatched/unavailable source produces `Unavailable`
with a nonzero exit code. Sources are checked again after world preparation.
Setup is delegated to the existing world owner through a lease, rather than
introducing another world/session manager.

The caller retains `HostOperationScope` ownership and awaits the runner. A run
links caller shutdown, its deadline and the prepared world's lifetime. It
awaits the scenario before releasing temporary resources in reverse order,
then disposes the world lease. Cancellation, deadline/frame-budget exhaustion,
unavailable capabilities, scenario exceptions and cleanup exceptions all
produce explicit non-success results. Foreign cancellation exceptions remain
failures. Cleanup errors are retained even if another failure already occurred.

Bounds are cooperative: scenarios and bound actions must honor cancellation,
and frame waits consume the declared budget. The runner does not abandon a
still-running task or attempt to abort synchronous native work. Existing source
reads retain their safe drain semantics. Source algorithms and authority remain
in their source-owned adapters.

## Observations and explicit actions

Separate ports describe scene/world/camera, player/pose/HUD, actors,
equipment/audio, navigation/UI, logical input and replay evidence. Snapshots
copy values and use immutable collections; they expose no Godot nodes, root
reference, backing dictionaries or mutable native controller objects. Existing
source-qualified identities remain intact. An unavailable observation throws
`DiagnosticUnavailableException`; fabricated empty/default native evidence is
not a substitute.

Write operations are developer/test capabilities bound by the explicit driver.
They are not public mutation hooks on the production root. Navigation and fresh
setup continue through the existing application/world owners. Input leases
change logical Godot actions only and release only their owned simulated
presses. Multiple leases share a simulation owner and preserve one another's
contributions, even when disposed out of order. Effective physical input is
never read back and re-injected as a synthetic press. Legacy direct-`Input`
diagnostic drivers must remain mutually exclusive with this binding until
their migration routes them through the same owner; the engine cannot recover
ownership of unregistered API presses. These ports never move the desktop
pointer or change mouse capture mode.

Phase 0 binds scene observations, the clock and logical input. The startup
pilot uses only scene observations and the clock. The other domain contracts
are intentionally unbound until the relevant Phase 1 migration supplies and
tests its narrow adapter. They do not claim that the existing 44 diagnostic
partials have already been extracted. The Phase 1A registry below supplies
world-smoke dispatch without an all-capabilities host or service locator.

## Registry migration (#336)

`WorldSmokeHarness` registers the original 21 first-world smoke flags and their
legacy source gates/order in `DiagnosticScenarioRegistry`. Normal launch selects
none. Unknown names and missing entry points fail explicitly. The remaining
startup/session/replay/tool entry points join the registry in their own
#337-#345 migrations; they are not silently treated as already extracted.

Retained root suites currently bind invocation-only `LegacyDiagnosticScenario`
callbacks. `DiagnosticScenarioDispatch` supervises all selected tasks through
`HostOperationScope` and the bounded runner. `ApplicationQuitDeferral` collects
each legacy callback's exit request in its own async flow; execution and cleanup
finish before the dispatcher commits the process exit. Results are observed as
they complete; a failure cancels and drains its siblings, including interactive
loops. Failures override passes, and exceptions after a legacy pass cannot
become successful exits. Deferral does
not capture unrelated callbacks or late children after the scope is disposed.
The temporary cancellation-only bridge lets retained waits observe the run
token, preserving the actual process/physics/post-draw signal phase. Owned
cancellation is rethrown through legacy catch-all handlers; bounded cleanup
waits deliberately finish without the retired run token. These small mechanical
changes are necessary before registering legacy callbacks as shutdown work.
#339 and the individual suite migrations remove the bridge as their helpers
move to explicit context waits. The asset-free engine probe cancels a real
frame-driven interactive loop while its scene remains alive, checks signal
unsubscription, and verifies fail-fast cancellation/drain of a waiting sibling.

`ExistingDiagnosticWorld` validates the initial world without owning or disposing
its scene. Legacy application-scoped suites deliberately survive their own world
travel/respawn and remain linked to host shutdown. The real scene leases and
persistent player owner remain authoritative. Future world-local suites bind
their actual world lifetime rather than inheriting that legacy scope blindly.

`AcceptanceEvidence` owns ordered assertions and the unchanged version-1 JSON
schema. A retained profile/reconciliation run has one reporter; its temporary
root composition field and three delegating methods are exact #336 exceptions
expiring with #344 (the profile caller moves in #338). Neither assertions nor
mutable check collections remain implemented as root partials.

## Startup, ordinary UI and live pause (#337)

`UiDiagnosticHarness` registers the three existing startup/menu/source-loss
commands and the two nested Veldin/live-world pause checks. `OrdinaryUiScenario`,
`SourceSetupScenario`, `StartupVisibilityScenario` and the two retail pause
scenarios own their assertions outside `OBPGame`. Their ports return copied
screen, focus, visible-text, panel, pixel, player-continuity and campaign-travel
facts. Scenarios never receive a root reference, node tree, mutable inventory or
source library. Dedicated menu actions and a source-repair fixture exercise the
existing production paths; native campaign operations remain in `OBP.RAC1`.

The ordinary UI and live-source-loss checks intentionally request repair exit
**2 on success**. `DiagnosticProcessScenario` defers the production repair quit
until assertions and cleanup finish, requires that expected policy outcome, and
maps an assertion or cleanup failure to the original failure exit 1. This does
not grant ordinary play a diagnostic source exemption. The repair checks still
require all three primary authorities at admission and verify that persistent
player/profile state survives source loss. Profile evidence compares SHA-256
digests of the same files instead of retaining their payloads in scenarios.

`GodotRetailPauseFixture` owns the temporary 960x720 review viewport, exact
injected-event input guard and logical pointer state. Its disposable lease
closes pause, frees the guard and restores the caller's viewport/input state;
each admitted synthetic press is released in `finally`, including cancellation.
The original process-frame counts, 600-frame page bounds, native elapsed
transition timing, six-panel/Items-grid/focus checks, opacity thresholds and
capture paths remain. Headless runs verify structure and navigation; rendered
pixel evidence is sampled only with a renderer. Startup visibility still
requires nine actual post-draw signals and explicitly reports unavailable
headlessly. No automated check needs desktop pointer capture.

Four exact temporary composition entries retain the UI fixture factory and
three bootstrap/nested-call delegations. The #340/#343 callers and the
#346/#347/#348 source/navigation/pause ownership migrations remove them; none
contains a diagnostic assertion. All five audited root partials and their
historical member permissions are retired.

## Exact frame phases

`Process` and `Physics` await the corresponding SceneTree frame-start signal,
before node callbacks. They do not mean that all node processing has completed.
`PostDraw` awaits `RenderingServer.FramePostDraw` itself. All use
`GodotAsyncWait`, preserving inline signal continuation and disconnecting on
completion or cancellation. A headless renderer reports post-draw evidence as
unavailable rather than substituting a process frame. Future replay migrations
must retain their existing priority and before/after-callback semantics.

## First external scenario and validation

`SourceFreeStartupScenario` moves the existing asset-free CI bootstrap assertion
behind copied scene facts. It waits the same six process frames and checks that
the loaded scene has the source bootstrap, UI and source manager. The explicit
driver is `SourceFreeStartupProbe`; it owns its operation and observes scene
retirement. The existing entry point and evidence remain:

```text
godot --headless --path game --script res://tests/ci_bootstrap_smoke.gd -- --test-scene picker
[godot-ci-smoke] PASS: OBPGame loads and Game Sources opens without retail data
```

The GDScript entry point retains clean-import/scene checks, failure exit 1,
successful follow-on Bug Report menu checks and final exit 0. Production root
members and all other smoke dispatch stay unchanged. Portable tests cover
admission, source/build mismatches, deterministic timeout/frame exhaustion,
world/caller cancellation, cleanup and exceptions. The existing complete
17-phase reconciliation gate remains the compatibility check for gameplay,
profiles, bookmarks and fresh same-build replay; older recordings are never
silently accepted as same-build evidence.

The explicit CI probe also checks the engine input binding using a temporary
F24 action and Godot-only key events: releasing physical input must leave no
synthetic press, and disposing one simulation must preserve another owner.
The fixture removes its events/action in `finally` and sends no desktop input.

Portable coverage includes the engine-free diagnostic files linked into
`OBP.Tests`, using exact source-path permissions. Godot adapters/probes and
unrelated diagnostic files remain excluded; the report does not claim engine
integration coverage. Path normalization and source/payload privacy checks
still apply to these linked files.
