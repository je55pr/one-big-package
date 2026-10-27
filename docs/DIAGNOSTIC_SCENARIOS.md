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

## Player, profile and source audio (#338)

`PlayerSessionDiagnosticHarness` registers persistent-player, capability and
profile/restart runs. The world registry retains the R&C1 audio alias, ordering
and source/first-world gates, binding a typed scenario instead of a root callback.
All four audited player diagnostic partials are retired. Scenarios receive copied
immutable state/identity/pose/audio facts and focused session, menu, equipment,
source-attachment and health fixture actions. They never receive a native
controller, source library, mutable actor map, Godot node or root reference.

Application-scoped player tests survive their own travel, failed preparation and
temporary source removal. Detachment leases restore attachments in cleanup.
Restore points keep native component payloads private. Camera options, input
filters, physics subscriptions, artificial decks, the audio floor, bus mute,
FPS/joypad settings and owned pause state have explicit cleanup. Profile commands
retain the caller's explicit isolated test profile and version-1 acceptance
reporter. Automated runs do not select the ordinary default profile; the scripts
create their test profiles under ignored capture directories and verify restart.

`PlayerCapabilityTrial` keeps the original 240 native ticks, input sequence,
every-sixth-tick sample, field order and pose digest. Sampling stays in
`DevelopmentPhysicsCompleted` and completes inline. Its retained trilogy caller
uses a bounded supervised invocation until #344 migrates that suite. Deferred
navigation and sampling cancel/drain before releasing input or scene resources.

The bounded audio collector stamps the actual source, build, level and native
tick; overflow invalidates evidence. Played cues remain distinct from
source-selected silence. The old synthetic audio floor had R&C1 material numbers
without their authority, making the current contact adapter correctly reject
them and the old command fail its surface-category check on clean dev. Its
authored geometry now declares the R&C1 material authority. Neutral or other-game
materials cannot be interpreted as R&C1. The shared surface helper remains behind
the traversal delegation until #339 retires that partial. Audio resets retain
three settled native-ground updates sampled after physics, then 30 physics frames.

Retail voice program 54, referenced by sequence 25, has four audible and four
silent alternatives. The third-swing assertion requires a native program outcome,
including its admitted silent branch, which never counts as playback. Other cue
counts, PCM, looping, pause, catch, protected-hit and death checks remain. A retail
regression verifies the original alternatives. Native sound decoding,
probability and gameplay stay with their existing source owners.

Two exact temporary root entries compose these fixtures and the collector.
Player/UI/equipment/audio owners and #359's actor bridge remove the factory;
#354 removes the collector binding. Reporter bridges still expire with #344.
Frozen-origin permissions never expand.

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

## Movement, water and traversal extraction (#339)

The nine movement partials and the controller weapon-input fragment are now
standalone suites. `MovementDiagnosticDriver` retains the original physics and
process frame-start signals, three consecutive completed support observations,
conditioned-stick inverse and controller edge timing. Its leases release only
owned logical actions and simulated in-application keys/buttons. The asset-free
engine probe cancels a real physics-frame subscription and verifies that both
input types and the subscription are released while the scene remains alive.

Suites receive immutable movement/animation/world facts and narrow development,
geometry, water, environmental-death or equipment actions. Native controllers,
scene nodes and backing actor maps stay inside the engine adapters. Temporary
surfaces, pack/fly settings, displaced combat actors and drowning listeners are
scoped. Native constants, authored routes, numerical tolerances and existing
report/replay fields are retained.

The existing `--movement-smoke`, `--rac1-novalis-swim-smoke`, Veldin/combat and
controller commands retain their grouped checks. `--movement-case` additionally
selects `full`, `small-step`, `slope-jump`, `airborne`, `landing`,
`traversal-contact`, `swingshot`, `environmental-death`, `controller-weapons` or
`swim`. Individual cases require Veldin, except `swim`, which requires Novalis.
The fall and swim cases explicitly select the same native-parity mode as their
existing source commands. Swimming stages its own pool without a preceding
ground trial. Unknown case names fail at CLI parsing. Automated engine runs use
`--headless` and isolated profiles/app data.

Remaining source suites, native audio and movement reproduction use exact,
temporary root delegations to compatibility helpers until their recorded
migration owners remove them. They do not regain access to movement assertions.

## R&C1 encounters and Veldin acceptance (#340)

The seven enemy, Novalis birdbot/peckbot, Aridia, frog and Veldin/combat partials
are standalone suites, with queen checks separated from the frog suite. Their
existing command names, dispatch order, failure codes, native 60-Hz sequencing
and acceptance tolerances remain. Copied observations cover authored placement,
activation, animation, contact, damage, rewards and camera ownership. Existing
`ActorInstanceKey` handles qualify native IDs by source, world and generation;
foreign-source and retired-world handles fail explicitly.

Engine fixtures retain native sessions, PVars, scene nodes and actor maps.
Temporary geometry, player/actor pose, the isolated shark-recycle session,
camera ownership, damage listeners and simulated input have scoped cleanup.
Cleanup skips retired scene actors and does not release another camera owner.
The synthetic combat command remains a host-contract fixture; it does not
claim to be an ordinary retail playthrough. Native rules and controllers stay
with their existing source owners.

Validation found a pre-existing approach-fixture bug: a desired conditioned
diagonal direction was sent as raw stick axes, so native deadband conditioning
reduced the requested run to walking. The fixture now uses the existing
conditioned-stick inverse, with its original 180-frame and distance limits.
Portable regressions check the resulting native run speed/direction and input
release on timeout/cancellation. Production movement is unchanged.

Authorised headless checks cover Veldin, Novalis, Aridia and frog/queen levels
6 and 10, including actual level retry and unload/reload. Optional enemy image
capture still requires a rendered post-draw signal and reports unavailable in
headless mode. The seventeen newly linked engine-free files have exact coverage
path permissions; engine fixtures remain outside portable coverage.
