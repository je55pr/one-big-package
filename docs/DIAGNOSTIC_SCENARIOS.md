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
change logical Godot actions only, restore their prior strengths on disposal,
and never move the desktop pointer or change mouse capture mode.

Phase 0 binds scene observations, the clock and logical input. The startup
pilot uses only scene observations and the clock. The other domain contracts
are intentionally unbound until the relevant Phase 1 migration supplies and
tests its narrow adapter. They do not claim that the existing 44 diagnostic
partials have already been extracted. There is no all-capabilities container
or replacement global scenario registry in this change; #336 owns dispatch.

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
