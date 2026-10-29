# Root ownership and decomposition (#333 / #334)

`OBPGame` is the Godot composition root. It may construct owned components,
receive engine lifecycle/input callbacks and delegate through narrow ports.
New gameplay policy, native encounter state, source algorithms and diagnostic
assertions belong in their actual owners. Existing world/session/actor/operation
owners remain authoritative; extraction must not create replacement sessions or
another all-capabilities manager.

The [diagnostic scenario contract](DIAGNOSTIC_SCENARIOS.md) defines focused
observation/action ports and the bounded external startup proof for #335.

Godot C# node scripts still need `partial` for source generation. Ordinary
components do not become nodes merely to make them independently owned. Moving
a file without removing privileged access is not encapsulation. Preserve script
UIDs, scene associations, native authority, callback phase/order and all existing
CLI/profile/save/replay wire identities.

## Frozen source and actual scope

The starting commit is `eea806399f864f30949d665701d39d510549a7ac`.
The filename census in #333 is correct: 121 `OBPGame*.cs` files, 26,779 file
lines, 755 direct methods, 457 direct fields (312 mutable declarations).
The semantic scan additionally finds an existing `OBPGame` fragment in
`game/diagnostics/ControllerSmoke.cs`: one internal controller fixture method.
It is explicitly assigned to #339's input/clock diagnostics migration.

The complete starting scope is therefore **122 files, 27,362 file lines,
756 direct methods, 457 direct fields, 44 exposed direct members and 2,654
distinct cross-file root references**. The latter counts unique
consumer-file/consumer-type/root-symbol edges, including 28 external typed
root accesses. It does not count runtime calls. Lines include other types in
those files; nested helpers are excluded from direct root member totals.
Public/internal/protected fields, properties and methods count as exposed;
Godot callbacks are included. These are measurements, not size quotas.

## CI guard and migration ledger

The #336 registry/evidence migration retires two root fragments and eight
historical declaration/member permissions. Its measured root is 120 files,
27,311 file lines, 755 direct methods, 457 fields (312 mutable), 44 exposed
members and 2,651 distinct cross-file references. The evidence reporter owns
its check collection and assertions; four exact temporary composition entries
retain the field/delegating bridges for #338/#344 callers and expire in #344.
Legacy scenario invocation callbacks remain until their individual Phase 1
suites migrate. This is a bounded intermediate measurement, not completion of
the diagnostic extraction programme.

The #337 startup/menu/pause extraction then removes five more diagnostic
fragments and 19 historical member/declaration permissions. The root has
115 files, 746 direct methods and 456 fields (311 mutable); the public root
surface remains 44 members. The removed review input-guard field is owned by
the disposable engine fixture. Four exact composition entries bind narrow UI
ports and delegate the retained bootstrap/nested callers; their removal owners
are #340/#343 and #346/#348. All UI assertions and scenario control flow live
outside the root. The frozen origin ledger is unchanged.

The #338 player/profile/audio extraction retires four diagnostic fragments,
their 19 historical declaration/member permissions and the old unbounded audio
trace field permission. The root has 111 files, 733 direct methods, 456 fields
(311 mutable) and 44 exposed members. One fixture-composition method and one
bounded collector field are exact temporary #338 entries, removed with the
player/world adapter wiring by #359 and the audio owner by #354. The independently
owned capability sampler is supervised; its remaining trilogy caller delegates
until #344. Synthetic R&C1 collision construction is external too, behind its
retained #339 delegation. These dependencies do not mean the later partials
have already been retired.

The #339 movement extraction removes all nine assigned diagnostic partials and
the privileged fragment in `ControllerSmoke.cs`; its ordinary UI driver stays.
The measured root has 101 files, 23,470 file lines, 717 direct methods, 456
fields (311 mutable), 44 exposed members and 2,431 cross-file references.
Movement, native air/ground/contact, water, tether, environmental restart and
controller-weapon assertions consume copied facts and focused fixture actions.
One input/clock driver owns logical actions, simulated keys/buttons and phase
waits. Synthetic geometry, development settings, relocated enemies and drowning
subscriptions have scoped cleanup. Forty-six historical declaration/member
permissions retire; twenty exact composition delegations remain for later
diagnostic, audio, controller-UI and movement-reproduction owners. Their expiry
owners are recorded individually; the origin ledger does not expand.

The #340 encounter extraction retires seven more diagnostic partials and 52
historical declaration/member permissions. The root now has 94 files, 20,255
file lines, 680 direct methods, 456 fields (311 mutable), 44 exposed members
and 2,291 cross-file references.
Enemy, Novalis, Aridia, frog/queen and Veldin/combat suites consume immutable
facts and fixture actions instead of privileged root access. Actor handles use
the existing source-qualified world-generation identity and reject retired
worlds. Nine exact composition entries bind these fixtures or delegate the four
remaining wait/input/placement helpers; their expiry owners are #341, #342,
#345 and #359. Two expired movement delegations are removed. The frozen origin
ledger remains unchanged.

The #341 weapon extraction retires five more diagnostic partials and 31
historical declaration/member permissions. The measured root has 89 files,
19,049 file lines, 659 direct methods, 456 fields (311 mutable), 44 exposed
members and 2,145 cross-file references. Wrench, Bomb Glove, arsenal, idle/audio
and mixed-source weapon acceptance now use copied source-qualified facts and
focused equipment fixtures. Shared movement input/clock ownership includes
the original fusion recording keys and simulated stick axes. Six exact
composition/delegation entries expire with #342, #344, #352 and #367; the
expired #340 placement delegation is removed. The frozen origin is unchanged.

The #342 campaign/mission/loot extraction retires four more diagnostic partials
and 23 historical declaration/member permissions. The measured root has 85
files, 18,420 file lines, 640 direct methods, 456 fields (311 mutable), 44
exposed members and 2,084 cross-file references. Campaign save/travel/checkpoint,
the read-only mission observer, crate and placed-bolt suites use immutable
observations and focused fixture actions. Two exact composition entries expire
with the campaign owner #372 and pickup owner #369. The two remaining logical
attack delegations are removed; source-native rules and the frozen origin stay
with their existing owners.

The #343 world-route extraction retires the Novalis route/music and Metropolis
taxi partials and eight historical declaration/member permissions. The measured
root has 82 files, 17,921 file lines, 636 direct methods, 456 fields (311 mutable),
44 exposed members and 2,065 cross-file references. Scoped adapters expose copied
mechanism, vehicle/passenger/camera and music facts; independent scenarios retain
the existing native thresholds, input and frame phases. Repeated entry, temporary
source detachment during queued teardown and scene/audio/hull retirement have
separate test ports. Two exact factory entries expire with #370/#371. The obsolete
retail-navigation delegation is removed; the frozen origin is unchanged.

The #344 recording/report extraction retires five more partials and 17 historical
declaration/member permissions. After integrating the independently merged
source-effects and shrub presentation changes, the root has 77 files, 17,409 file
lines, 624 direct methods, 456 fields (312 mutable), 44 exposed members and 1,976 cross-file
references. Independent recording, reconciliation, bug-report, movement-state
and trilogy suites consume focused fixtures and immutable observations. The
cached capture harness replaces the readonly evidence reference: its mutable
nullable reference accounts for the one mutable-field increase. Its owned
reporter retains the ordered acceptance collection across capture completion.
Five exact composition permissions expire with recording #356, movement repro
#358 and player/world wiring #359; five old evidence/recording bridge permissions
retire. The frozen origin ledger is unchanged.

The #345 CLI/Lab extraction retires the four assigned partials and 27 historical
declaration/member permissions, plus 13 expired movement/encounter invocation
bridges. The root now has 73 files, 16,961 file lines, 603 methods, 446 fields
(302 mutable), 44 exposed members and 1,937 cross-file references. Independent
Lab, crate-observation, shot-list and stress components own their control/state;
their engine fixtures copy scene/world facts and bind existing operations.
The native R&C2 fresh-bolt session remains source-owned and is held by the
existing world resource lease, separate from the optional diagnostic probe.
Three exact composition entries expire with Lab navigation #347, capture
presentation #356 and final world-entry composition #376. Both old Lab origin
permissions retire; the frozen origin does not expand. Ordinary Lab presentation
retains room/station labels and hides development status/help captions.

The #346 source/setup extraction retires both assigned root partials and their
30 historical declaration/member permissions, plus the expired startup-visibility
bridge and its external access. The root now has 71 files, 16,612 file lines,
592 methods, 435 fields (292 mutable), 43 exposed members and 1,879 cross-file
references. One source coordinator owns the library, bounded stamp/probe cache,
explicit ordinary/diagnostic admission, recovery and consumer invalidation; its
small controller owns the setup view and generation-scoped picker callbacks.
The player session/profile remains with its existing owner and saving is suspended
before repair tears down the world. Changed sources retire pending verification,
actor caches and equipment preparation; explicit successful repair prepares them
again. Unchanged files retain the existing cached authority verification rather
than a full-disc hash on each launch. Ordinary canvases hide source preparation
status, and UI observations include sibling application overlays.
Eight exact composition/compatibility entries expire with navigation #347,
profile #349 and final world-entry composition #376. The frozen root origin does
not expand; three existing source-world adapter fingerprints change only to read
the same source library through its owner, with their native rationale retained.
The source coordinator and physical storage have no engine dependency; focused
source-free tests exercise admission, recovery, replacement and profile-suspension
state. Engine startup/source-loss, UI and replay checks retain the existing wire
formats, native input phases and explicit nonzero failures.

The #347 navigation extraction retires the three title/destinations/Lab root
partials. The root now has 68 files, 16,205 file lines, 589 methods, 426 fields
(283 mutable), and 43 exposed members, down from 71 files, 592 methods and
435 fields (292 mutable) after #346; the exposed root API remains unchanged.
One explicit coordinator reference replaces the title, browser, Lab overlay,
and R&C1 planet-map pause/UI fields. The coordinator receives 25 focused
composition ports (one Godot host node and 24 read/action delegates) rather
than privileged root access. R&C1 travel admission still uses its existing
campaign session, authored Lab geometry belongs to the active world scene,
and `WorldSceneLifetime` remains the sole world transition owner. Generation
checks retire stale title/browser and deferred preparation callbacks; rejected
destinations leave the current browser interactive. The native world-adapter
ledger relocates the existing provider/travel/collision exceptions without
granting player-global authority. All three retired script UIDs are removed,
with newly generated sidecars for the focused owners. Existing bootstrap,
diagnostic, profile and input entrypoints remain thin root delegations pending
final composition #376.

The #348 modal ownership extraction retires `OBPGame.ControllerSettings.cs`
and `OBPGame.QuickSelect.cs` and their UID sidecars, replacing 13 old modal
root state fields with one `UiModalCoordinator` composition reference (12 net
fields retired). The root census is now 66 files, 16,035 lines, 590 methods,
414 fields (271 mutable), and 43 exposed members; before #348 it was 68 files,
16,205 lines, 589 methods, 426 fields (283 mutable), and 43 exposed members.
`UiModalCoordinator` alone owns pause leases, controller-settings visibility,
return focus, mouse mode, held-button suppression, live Quick Select input
capture and wheel gesture timing. `ModalEquipmentPorts` routes equipment
mutations through the existing player equipment API, while the native retail
menu and ordinary universal catalog retain separate presentation policies.
The root only composes the already-existing menu read models and forwards
stable public entrypoints. Reviewed temporary #348 root composition bindings
expire with #376; the frozen origin/access ledger does not grow.

The #349 persistent player/profile extraction retires the two root partials
`OBPGame.PlayerSession.cs` and `OBPGame.Profile.cs`, moving lifecycle
state to `PlayerSessionHost` and profile recovery/atomic persistence to
`ProfileCoordinator`. Compared with #348's 66 root files, 16,035 lines,
590 methods, 414 fields (271 mutable) and 43 exposed members, the root now
has **64 files, 15,910 lines, 589 methods, 399 fields (256 mutable) and 43
exposed members**. The 15 direct-field reduction consists of relocated native
player/session/profile state, with two application-level composition references
and read-only projections replacing legacy cross-partial callers.

`PlayerSessionHost` owns the single player/native controller pair, world-entry
session, selected entry mode and animation/control identity for the process
lifetime. `ProfileCoordinator` owns the loaded profile, recovered/blocked write
state, source provenance, opaque campaign contributor tracking, retained unknown
fields, last committed destination, save-failure diagnostics and the 10-second
world-active save clock. Both expose only bounded state/control operations to
the root; native equipment laws and R&C1 campaign capture/restore remain in
their existing source modules and explicit host ports. Avatar source staging and
HUD rendering stay world-bound. Staged/failed world entry must not reset the
persistent player or claim the profile destination. The existing save envelope,
versions, atomic write guard, source mismatch rejection and retail-parity file
isolation are unchanged.

All 36 historical direct members from the two retired partials are recorded as
retired; 30 exact, token-hashed root composition/owner-backed compatibility
projections (including two writable projections) are temporary and expire with #376. Their narrow consumers should
migrate to owned ports, rather than growing the root bridge. The frozen legacy
source/owner ledger is unchanged. The source-free owner tests exercise five
committed world entries, world unload, rejected preparation/activation,
unchanged player and native state, version/build
rejection, blocked saves, absent profile, world-only autosave and opaque campaign
preservation.

The #350 avatar/equipment presentation extraction retires
`OBPGame.PlayerAvatar.cs` and `OBPGame.EquipmentPresentation.cs` plus their
Godot UID sidecars. `PlayerAvatarPresentationHost` owns validated source-stamped
avatar and R&C1 hand asset caches, world-scoped visual nodes, native animation
controller and fixed 60-Hz clock, pose sampling, hand attachments and event
unsubscription. `PlayerEquipmentPresentationBinder` owns the selected R&C1
presentation bank, weapon model/socket admissions and per-world effect material
and mesh cache. World entry decodes both avatar and selected equipment data
into pending candidates without retiring the currently active presentation.
A rejected or superseded pre-commit preparation discards those candidates while
the live avatar, 60-Hz clock and animation callbacks remain intact. The next
world publishes them only after the previous world has begun its committed
retirement. On teardown, the avatar detaches from `PlayerHost`, restores the
fallback visual and releases references to world nodes; the committed source
avatar can be reused for a same-source destination. Identity or stamp changes
also force a refresh of the selected equipment bank, including a replacement ISO
at the same path. The persistent player session is not invalidated. No synthetic
assets replace missing retail data.

Compared with #349 (64 files, 15,910 lines, 589 methods, 399 fields / 256
mutable), the four-configuration root census is **62 files, 15,767 lines,
588 methods, 380 fields / 238 mutable, 43 exposed members**. The retired
original fields are replaced by two readonly composition references, not new
player-global feature state. Original weapon decisions/physics callback order,
selected bank audio cues, native source animation/skeleton sampling, equipment
icons, world-entry profile identity and saved/replay formats stay with their
existing owners. Existing host callbacks (including native weapon presentation)
remain recorded as expiry-scoped composition bridges for #351/#354/#375/#376.
Original root member retirements, relocated bridge hashes, and exact exceptions
are recorded in the versioned architecture ledger; the frozen origin is unchanged.

The #354 audio extraction retires `OBPGame.Audio.cs` and
`OBPGame.Rac1PlayerAudio.cs`, including their UID sidecars. From #350's **62 files,
15,767 lines, 588 methods, 380 fields / 238 mutable, 43 exposed members**, the
root becomes **60 files, 15,569 lines, 575 methods, 366 fields / 232 mutable,
43 exposed members**. One composition reference replaces the native cue/reaction
sessions, five playback hosts, three loop IDs, pitch random state, bank context
and diagnostic collections. Twenty-five historical member/declaration permissions
and sixteen old composition permissions retire; nine focused owner/port bindings
remain for equipment #352 and final composition #376. The frozen origin and
world-origin ledger membership remain unchanged; the existing source-contact
adapter fingerprint only follows the same native water sound into its new owner,
with a regression forbidding source binding or player-service selection there.

`Rac1PlayerAudioPresenter` borrows the existing equipment bank and binds its admitted
source/build/file stamp to the current world lease. Its unbound owner is composed
at startup before static-camera diagnostics; ordinary audio uses the committed
avatar source stamp of its prepared bank. Godot's playback adapter owns
the player/pack/wrench/animation/reaction voices, while `WorldHost` retains world
music and equipment one-shots and now presents the representative integration cue.
Source loss and world retirement clear cue cursors, voices and the binding; exit
disposes all five channels immediately after world audio retirement. Death stops
movement loops, then held equipment, before scheduling the native reaction. Pause
retains suspended voices/cursors, so resume consumes no extra pitch randomness.
Diagnostics receive copied voice facts and bounded, source/world-qualified start,
stop and native-silence events rather than walking the player's audio nodes.
The rendered retail player-audio smoke's 57 ordered playback records, including
pitch rates, match the pre-extraction run at `0189349` exactly. The rendered
arsenal smoke at fixed 60 FPS matches all 125 playback records, including its
world reload, and the Novalis music/lifetime smoke matches all nine records.
Equipment one-shots resolve the current lease's `WorldHost` through a focused
playback port; retaining an earlier world's host would lose sound after reload.
Portable presenter
tests exercise the same pitch/sample sharing, pause, wrench, death, retirement,
missing-bank and disposal contracts; authored audio facts and native timing stay
with their existing source owners.

Broader retail scene limitations were checked against unchanged `0189349`:
the arsenal fixture emits the same nine existing skeletal-layer sampling errors
while its assertions pass in both revisions. The Novalis swim fixture fails
the existing surface/underwater-air-meter assertion in both revisions, so the
complete audio-shutdown script cannot be reported as passing. Its Oozla exit,
three Oozla switches and repeated Novalis route probes pass without audio leaks;
the independent music/lifetime fixture also covers source detachment and re-entry.
Release validation passes the portable suite (3,075 passed, 262 skipped) and the
authorized retail suite (3,322 passed, 15 optional oracle checks skipped), pinned
UID coverage and `dotnet format --verify-no-changes --no-restore`. The Godot app
build passes with warnings as errors; mixed-source weapon fixtures and a fresh
same-build input recording/replay retain their original source-identity gates.

The #355 report UI extraction retires `OBPGame.BugReports.cs`, moving its script
UID to `BugReportUiCoordinator`. The root changes from 62 files, 15,767 lines,
588 methods, 380 fields (238 mutable), 43 exposed members and 1,675 cross-file
references to 61 files, 15,397 lines, 572 methods, 370 fields (229 mutable),
43 exposed members and 1,663 cross-file references. One coordinator reference
replaces the 11 bug-report fields; all 29 historical declaration/member
permissions retire. Two exact composition entries retain the owner reference
and the existing bootstrap modal observation until #376. The existing #344
capture-fixture binding now consumes the report owner's narrow diagnostic
interface; the frozen origin and external-access ledger do not grow.

`BugReportUiCoordinator` owns forms, inbox rows/selection, confirmations and
child dialogs. Its nine composition ports bind the Godot host, overlay parent,
recording writer, resume/notice callbacks, build and physics identity checks,
frame waits and failure exit. Its five-member diagnostic interface exposes
copied UI/report snapshots, the local evidence root and focused UI actions;
neither mutable views nor the report store escape through that interface.
`BugReportCapture` preserves startup/profile/campaign provenance separately from
view lifetime. The existing `BugReportInbox` and `PlaytestRunWriter` retain
publication, recording and archive authority; report/marker IDs, manifests,
source stamps, F7/F8 input and native recording phases stay unchanged.

Closing or disposing the coordinator cancels view work, retires dialogs and
prevents late callbacks from updating another inbox. Export/import temporary
files and owned reproduction children are cleaned up on cancellation/failure,
including interactive children. Back/Cancel remains available during work;
a saving form explicitly labels its close action as continuing the save.
An admitted report save is supervised independently of the view and drains
before capture finalization during host shutdown; failed shutdown writes still
require a nonzero exit. Portable regressions cover late callbacks, committed
saves, child cancellation/deadlines, failed replacement writes and cancellation
after a temporary manifest write. The retained engine scenario also cancels an
active child, reopens its inbox and performs both same-build report replays.

Submitted report prose is atomically retained by the existing inbox before a
save can outlive its view. Successful publication removes that pending draft.
Capture startup restores pending reports from their original completed capture,
preserving session/marker/source identity and copying the tape without rewriting
input or observations. Unfinished or unwritable captures retain their drafts and
remain explicitly pending; committed-save failures still reach host supervision
and require the existing nonzero exit. Recovery/publication retries and explicit
session deletion have portable regressions, with no new root fields or schemas
for saved reports or portable archives.

The #359 actor-composition extraction retires `OBPGame.Actors.cs`,
`OBPGame.ActorAdapters.cs` and `OBPGame.ActorContacts.cs`. Against #350, the root
changes from 62 files, 15,767 lines, 588 methods, 380 fields (238 mutable),
43 exposed members and 1,675 cross-file references to 59 files, 15,340 lines,
560 methods, 379 fields (238 mutable), 43 exposed members and 1,611 cross-file
references. The source-cache and actor-world fields become one lazy owned-host
reference. The established public placement-reset entrypoint remains a
delegation; 37 historical declaration/member permissions retire and four exact
composition entries expire with #376.

`ActorWorldHost` privately owns registration, views, bodies, placed identities,
reward/audio transients and world retirement. `ActorPlacementPreparation` owns
the world's preparation barrier, stable source/class/id activation order,
provenance checks and explicit retry status. Retiring that owner cannot activate
a late import; `ActorSourceCache` retains attachment-owned decoded data and
observes canceled/failed native reads through `HostOperationScope`. Source
invalidation retires placed actors and player attack identities, disables their
bodies and clears pending activation without resetting the persistent player.

`ActorNativeAdapters` supplies typed R&C1/R&C2/R&C3 damage and update bindings;
existing source controllers retain native formulas, masks, amounts, reactions
and 60-Hz updates. `ActorCombatContactService` owns registered geometry, ray and
sphere queries, blast/wrench dispatch and receipt routing through the existing
`ActorRuntimeSet`. Shared OBP geometry retains the two original segment-test
tolerances. Source-qualified factories explicitly declare their deterministic
replay witness; the current admitted adapter IDs and source-world families are
preserved without a root family switch or a replay schema change.

The host receives nine world/source/lifetime ports, six contact ports and three
source-specific native binding records. Its eleven-member diagnostic interface
returns copied population/placement facts and controlled cache, construction,
invalidation and retirement witnesses. Diagnostics no longer receive the actor
host's dictionaries, runtime registry, body map or source cache. Existing scene,
native-session and source-context fixture composition remains exact: the four
player/world/trilogy bridges formerly expiring with #359 now expire with final
composition #376; the five R&C1 encounter bridges expire with their actual
source owners #361/#362/#363/#368. Their existing permissions remain narrow,
and the frozen origin/access ledger does not grow. The R&C1 voice adapter's
fingerprint changes only to call its owned voice scope; a focused regression
retains rejection of player-equipment selection in that source adapter.

The authorised mixed-encounter scenario reproduces the pre-extraction digest
`B690142A3C5695161F095AD1D5F67F2EFB28B119CFAE0E1E5DC60B69E5C8C085` for
its sampled identities, positions, native states/sequences, health and alive
flags. The same scenario retains ordinary cross-game contacts, independent
resets, failed construction rollback, source invalidation/re-admission, world
retirement, source-cache reuse and persistent-ammunition acceptance.

With #354 integrated after #359, the combined four-configuration census is
57 files, 15,142 lines, 547 methods, 365 fields (232 mutable), 43 exposed
members and 1,582 cross-file references. Actor contacts retain their owned
service; its accepted wrench-feedback port delegates to the player audio
presenter. Only that existing composition binding and the two player diagnostic
bindings need combined fingerprints; #359's remaining composition expiry stays
with #376 and the frozen origin ledger remains unchanged.

With #355 integrated after #354/#359, the combined four-configuration census is
56 files, 14,772 lines, 531 methods, 355 fields (223 mutable), 43 exposed
members and 1,570 cross-file references. Report startup follows the unbound
audio owner's composition; admitted report saves drain before capture
finalization and audio/world disposal. Both owners retain their independent
composition entries and retired permissions; the frozen origin ledger remains
unchanged.

The #356 capture/replay extraction retires `OBPGame.CaptureDiagnostics.cs`,
`OBPGame.CaptureSession.cs` and `OBPGame.Playtest.cs`, plus the privileged
recorder and report-control fragments. The recorder's existing UIDs move with
its scripts; the ordinary bookmark launch UI remains with #358. Against the
combined #355 census above, the root has **51 files, 13,601 lines, 511 methods,
353 fields (221 mutable), 43 exposed members and 1,448 cross-file references**.
The migration retires 138 historical declaration/member permissions and the
old diagnostic-harness field permission. Nine exact composition entries bind
copied snapshots and existing actions; the surviving diagnostic/shot factories
contain wiring only and expire with final composition #376. The frozen origin
ledger does not expand. The world-provenance metadata adapter moves to the
read-only capture snapshot binding with its rationale retained.

`CaptureReplayCoordinator` owns startup/header admission, seed/profile handoff,
source-qualified adapters, command/fact serialization and checkpoint hashes.
Its typed snapshots can be exercised without Godot or unrelated UI. The external
`PlaytestDriver` retains the original input order and process/physics priorities
(-10,000/+10,000); `GodotCapturePresentation` retains timer and post-draw capture
phases. Capture version 1/2, profile/bookmark schemas, native tolerances and the
same-build gate are unchanged. Recording pins attachment stamps and rejects
replacement before admitting another step/checkpoint or bookmark. File paths
remain private admission data and never enter headers or archive evidence.

Critical startup, capture, shot and persistence tasks remain in the existing
`HostOperationScope`. Optional hashing has a distinct
`SourceVerificationCoordinator` scope and still uses one
`SourceVerificationOperation` per attachment. Replacement retires that operation;
generation-qualified failures cannot cancel or publish into its successor.
Shutdown cancels and drains both scopes before source/audio/world disposal.
The coordinator owns the cached diagnostic harness; the report UI continues to
borrow its existing writer through the #355 ports.

Portable regressions cover command order, copied native/shared facts, source and
adapter admission, initial controller reset, startup rejection, metadata wire
fields and optional verification cancellation/replacement. A fresh comparison
against `b0f3aa6c090da169dcf9bc987e1e2b5e24b09bc4` keeps all 1,057 scripted steps
and 13 checkpoints identical, including logical inputs, command/gameplay order
and checkpoint hashes. Assembly/application build identities and wall-clock input
timestamps are expected to change between fresh builds/processes; these are
excluded only from that comparison, never from ordinary replay admission.
The rendered Oozla metadata sidecar is byte-identical to the baseline.

The #357 replay-driver boundary completes the standalone extraction begun by
#356: `PlaytestEndDriver` has its own script/UID and neither historical nested
driver/report partial returns. `GodotPlaytestHost` exposes window size/focus,
simulation readiness, input attachment, quit and an owned UI suspension. The
driver receives copied capture facts plus focused report/diagnostic actions;
it receives neither the root, a player node nor the diagnostic harness.

Each input lease detaches on player tree exit, replacement, completion, failure
and driver removal. A retained callback returns neutral input without consulting
a retired scene or consuming the successor's tape. The driver lifetime spans
intentional destination transitions and cancels its diagnostic tasks on failure
or removal. Their existing run contexts still own input and temporary worlds.
Admitted tape/bookmark writes remain in `HostOperationScope` until durable
completion; retired drivers cannot publish their UI continuations. Driver
termination closes its report view, canceling active child-process replays while
retaining admitted report saves under their existing persistence owner. UI suspension
restores the captured nodes, pause menu, focus, pointer and navigation state on
resume or termination. Finalization is idempotent, including removal before host
shutdown. Callback priorities, command/event order, scenario IDs and all existing
report/bookmark/replay schemas remain unchanged.

Against #353's merged result `42ebc9c` (final head `d440722`), the root remains **46 files, 457 methods,
294 fields (177 mutable), 43 exposed members and 1,134 cross-file references**;
the narrowed composition adds five lines, for **12,451 lines**. One existing
composition fingerprint moves to #357, still expiring with #376. No origin,
access or historical member permissions expand. Portable regressions cover
retained callbacks and driver cancellation independently of host shutdown; the
authorised reconciliation gate exercises the mixed-source route, profile restart,
bookmark prefixes and fresh-process bug-report replays through the new adapter.

Validation for #357 on that merged base: 3,260 portable Release tests pass with
283 retail-dependent skips; 3,528 authorised NTSC-U tests pass with 15 optional
oracle skips, using `DOTNET_PROCESSOR_COUNT=2`. All 17 reconciliation phases pass,
including exact mixed-source replay, profile restart, bookmark prefixes and six
fresh-process report replays. The focused ownership/input/cancellation/report
suite passes all 105 checks. UID coverage, solution formatting and the game build
with warnings as errors pass. Engine acceptance is headless; human visual/audible
review is not claimed.

The #351 equipment extraction retires `OBPGame.PlayerEquipment.cs` and its UID,
including 14 historical declaration/member permissions. The root changes from
56 files, 14,772 lines, 531 methods, 355 fields (223 mutable), 43 exposed members
and 1,570 cross-file references to 55 files, 14,661 lines, 526 methods, the same
field/exposed-member counts and 1,529 references. `PlayerSessionHost` owns one
`PlayerEquipmentFacade` for its lifetime, including explicit parity projections.
The facade uses the existing native inventory, hand, weapon and assignment
contracts and receives copied presentation/context observations. It does not
own another inventory, cache, node, subscription or clock. World retirement
retains it and the player state; application exit releases its scene callbacks.
Prepared models and admitted presentation bindings are separate read-model facts;
unimplemented, unowned, missing-source/build and absent-world-context states
retain their individual reasons. Missing presentation bindings remain disabled,
while contextual gadgets retain their desired assignments. The existing slot,
equip, gadget and unequip command IDs and configuration identity stay unchanged.
Seven public compatibility delegations expire with #376. Seven existing
#350/#354 presentation composition bridges move into the main root file with
their #352 expiry retained; four existing bindings now read through the facade.
The immutable origin and player/world ownership ledger do not expand.

With #356 integrated, the combined root has 50 files, 13,494 lines, 506 methods,
353 fields (221 mutable), 43 exposed members and 1,409 cross-file references.
Capture command actions reach the same equipment facade through the retained
public delegations; the facade's recording callback uses `CaptureReplayCoordinator`.
Both retired equipment/capture-session partials stay absent. The combined ledger
retains both migrations' composition boundaries and retired permissions without
expanding the frozen origin.

The #360 R&C1 world extraction retires `OBPGame.Rac1Gameplay.cs` and
`OBPGame.Rac1Presentation.cs`. After reconciling the #351 equipment and
#356 capture/replay extractions at `3d91db1`, the root moves from 50 files,
13,494 lines, 506 methods, 353 fields (221 mutable) and 1,409 cross-file references
to 48 files, 13,096 lines, 495 methods, 324 fields (201 mutable)
and 1,258 references. Its exposed surface remains 43 members. Fifty-one historical permissions retire; eight exact temporary
composition bindings replace them. The campaign reference expires with #372;
the remaining module, player projection and HUD bindings expire with #376.
The frozen origin/access inventory does not expand.

`GodotWorldResources` owns one `Rac1WorldModule`, native `Rac1WorldContext`
and scene-only presentation adapter per lease. The module preserves the existing
configure/reset/render callback order; source controllers and the player's
physics callbacks retain their native 60-Hz rules. Typed mechanic/action ports
allow later enemy, loot, weapon and area owners to replace the retained
composition callbacks without acquiring privileged root access. The actor bridge
resolves the active hostile dictionary lazily, then binds it to that world's
factory; construction before world entry never captures a retired dictionary.

The persistent `Rac1CampaignHost` retains the original source campaign and parity
wallet, separately from `PlayerSessionHost` and its ordinary native player state.
World retirement detaches player/source events, releases mechanic bindings and
clears only that lease's views and projectiles. Repeated disposal cannot invoke
the old callbacks against a replacement world. Failed staging still leaves the
live lease untouched. `Rac1WorldContextTests` checks shared Moby/reward identity
and isolation across repeated authored IDs; persistent-player and engine
reconciliation checks cover continuity, original-world checkpoints, HUD and
same-build replay. Source builds, native IDs and save/replay formats are unchanged.

The #361 enemy-host extraction retires `OBPGame.Rac1Enemies.cs`,
`OBPGame.Rac1Hostiles.cs`, `OBPGame.Rac1NovalisBirdbots.cs`,
`OBPGame.Rac1NovalisPeckbots.cs` and `OBPGame.Rac1Seekers.cs`. Their UIDs move
with the owned host/query and family scripts. Against merged #357 (`d7fb403`,
final head `0c25712`), the root moves from **46 files / 12,451 lines / 457 methods /
294 fields (177 mutable) / 1,134 references** to **41 files / 11,381 lines /
424 methods / 281 fields (169 mutable) / 957 references**. The exposed surface
remains 43 members. Fifty-four historical member/declaration permissions retire;
one exact enemy-composition factory replaces them. Fourteen existing composition
fingerprints now bind owned queries/commands. The encounter diagnostic factory
expires with final composition #376; the frozen origin/access census does not
expand.

Each `Rac1WorldModule` owns one `Rac1EnemyHost`. Toad/class-749, class-1440,
birdbot, peckbot and seeker adapters retain their existing source-native
controllers and authored registration/damage admission. The family registry
dispatches the same ascending instance order. `Rac1EnemyTickClock` preserves
the 60-Hz accumulation, frame-counter phase, early defeat return and final
birdbot-projectile phase. Native state, attack, reaction, damage/reward amounts
and replay/factory identities remain source-owned and unchanged. The existing
Godot capsule, sleep, flame and birdbot-projectile approximations retain their
explicit provenance; the host frame counter is not claimed as the recovered
retail global update phase.

The contact roster admits only matching source-qualified R&C1 placements. A
retired host rejects old nodes, callbacks and equal native IDs in a successor
lease. Placed-actor bindings capture their exact owner rather than a root-wide
dictionary; retired observations report retirement and late damage/ticks cannot
dispatch into another world. Contact, flame, projectile and registration cleanup
attempt every owned release even when one family fails. Actor-scene resources
still own placed bodies, and the existing source-versioned actor cache owns
imported animation data; original-world presentation remains with its scene.

Frog/queen, Aridia and amoeboid behavior stays with the existing #362/#363/#368
owners. Focused neighbor ports preserve their configure/reset/tick order without
copying native algorithms into a shared base class. The temporary composition
binds existing neighbor state resets and source context only; it supplies no root
reference and grants no player-global capability. Ownership regressions cover
source identity, retained contacts, failure isolation, clock/reset phases and
rejection of player-provider selection in both migrated source adapters.

Validation for #361: the final portable Release suite passes 3,273 tests with
283 retail-dependent skips. The authorised NTSC-U suite passes 3,540 tests with
15 optional oracle skips, using `DOTNET_PROCESSOR_COUNT=2`. All 17 reconciliation
phases pass, including mixed-source reset/source continuity, exact recordings,
profiles, bookmarks and fresh-process reports. Veldin enemies, Novalis
peckbots/birdbots, Aridia and frog/queen engine checks pass; seeker worlds 9 and
15 each pass fresh-process record/replay. Eleven final ownership/contact/clock
checks cover failure isolation and retirement after replacement within one scene.
UID coverage, solution formatting and the game build with warnings as errors
pass. Engine acceptance is headless; human visual/audible review is not claimed.

The #352 shared equipment extraction retires `OBPGame.FusionWeapons.cs` and moves
its UID to `FusionWeaponHost.cs`. Against `dd97c87`, the root moves from **48 files,
13,096 lines, 495 methods, 324 fields (201 mutable), 43 exposed members and
1,258 cross-file references** to **47 files, 12,543 lines, 462 methods,
297 fields (179 mutable), 43 exposed members and 1,168 references**. One lazy
composition reference replaces the 28 original fields. Sixty-three historical
permissions and seven expired equipment presentation bridges retire. Four exact
owner/factory/coordinate-copy entries and the rebound diagnostic factory expire
with final composition #376; existing native action bindings remain focused
callbacks. The frozen origin and player/world ownership ledgers do not expand.

`FusionWeaponHost` borrows the persistent native session and existing equipment
facade. `FusionWeaponSourceHost<TAssets>` owns cancelable preparation, the existing
`FileVerificationCache` and main-thread resource upload for an attached source
set. Unchanged sources retain those assets across destinations; source changes
retire voices, effects and views before disposing textures. A late canceled
worker cannot publish into a replacement source lifetime. Missing assets and
upload failures keep their explicit equipment status.

Each `GodotWorldResources` owns a disposable fusion renderer binding, installed
after native world initialization resets and before avatar attachment. Retirement
clears projectile/beam/spread/audio nodes, native transient sessions and fixture
state once; an old binding cannot act on a replacement world. The source owner
is independent of this lease. Native ammunition and the held-model animation
snapshot persist with the player; death/source invalidation clear use snapshots
and world effects without refilling equipment. Application exit releases sources
and the preparation notice in a finally boundary. `Rac1FusionCampaignIsolation`
owns the explicit test campaign/wallet/gold-bolt snapshot and restores it
idempotently; ordinary persistent-session admission returns before that path.
Damage, cadence, charge, source animations/audio and the existing replay/config
identities remain with their original native/session owners. The unresolved
spread renderer and held Cannon clip choice retain their OBP provenance.

Validation for #352: the source-free Release suite passes 3,241 tests with 283
retail-dependent skips; the authorized NTSC-U suite passes 3,509 tests with 15
optional oracle skips. Both use `DOTNET_PROCESSOR_COUNT=2`. All 17 reconciliation
phases pass, including five-stop continuity, profiles, bookmarks and fresh-process
bug-report replay. The complete fusion suite passes asset absence, source
invalidation, cancellation, transient cleanup and normal return. Its 1,271-step,
three-marker recording replays with 14 matched checkpoints and no build override;
the initial and 13 later checkpoint payloads also match unchanged `dd97c87`
exactly. UID coverage, solution format verification and the game build with
warnings as errors pass. Engine acceptance is headless; human visual/audible
review is not claimed.

The #353 player damage/lifecycle extraction builds on merged #352 (`4670f2a`) and retires
`OBPGame.PlayerHealth.cs`, preserving its UID on `GodotPlayerLifecycleHost.cs`.
The root changes from **47 files / 12,548 lines / 462 methods / 297 fields
(179 mutable) / 1,168 references** to **46 files / 12,446 lines / 457 methods /
294 fields (177 mutable) / 1,134 references**, retaining 43 exposed members.
One composition reference replaces the four damage-world/service-clock fields.
Eleven historical permissions and the old HUD composition bridge retire; three
exact owner/factory bindings expire with final composition #376. The frozen
origin and player/world ownership ledgers remain unchanged.

`PlayerLifecycleBridge` borrows the existing persistent native health/session
services. Its disposable damage-world lease owns source-qualified registrations,
actor liveness guards and retirement. `GodotWorldResources` owns that lease's
Godot binding, which disconnects the player-service callback and retires ordinary
retry UI. Captured tick/retry callbacks reject a replacement world/player, and
delayed disposal cannot end a newer lease. Retiring a scene does not heal or
replace the player. The service clock remains with the lifecycle owner across
world visits. Typed sample/hurt/death/respawn events drive copied health, HUD,
audio and retry presentation through focused ports; source producers still use
the existing PlayerAttackIntent/admission policy.

Ordinary respawn delegates to the existing staged world-entry/camera path and
restores the previous session on a rejected or throwing stage. Native parity
checkpoint/death decisions remain with the source campaign and native nanotech
controllers behind the existing R&C1 lifecycle adapter; the shared bridge does
not infer or invoke recovered parity restarts. The ordinary retry panel has its
own owner, while diagnostics retain a copied observation of either retry view.
Application exit retires subscriptions even when another cleanup step throws.

Validation for #353: 3,253 source-free Release tests pass with 283 retail-dependent
skips; 3,521 authorized NTSC-U tests pass with 15 optional oracle skips, using
`DOTNET_PROCESSOR_COUNT=2`. All 17 reconciliation phases pass, covering native
entry, mixed-source continuity, profiles, recordings, bookmarks and fresh-process
report replays. The focused lifecycle smoke passes repeated world visits, source
loss/recovery, health retention, rejected staging, canceled entry and drained
callbacks. Seven bridge cases and three ownership regressions cover stale
producer/lease rejection, mixed-source intake, death notification, respawn
rollback and retained ammunition/wallet. UID coverage, solution formatting and
the game build with warnings as errors pass. Engine validation is headless.

`RootBoundaryArchitectureTests` runs with the ordinary source-free test suite.
The reusable Roslyn implementation/CLI is `tools/OBP.ArchitectureTool`.
It scans all C# source below `game/`, excluding build/import outputs, binds
references and identifies the class declaration rather than its filename.
It evaluates the actual game's MSBuild `DefineConstants` (including implicit
.NET framework constants) for Debug/Release on Windows/LinuxBSD, the supported
desktop targets. All four compilations contribute to the permission check;
an inactive conditional branch in one build is not a retirement. Reports retain
per-configuration symbols/census; headline totals and member/reference keys
cover the distinct union of all four builds. File lines are counted once.
Escaped identifiers are decoded, and external consumer identities include
their namespace/containing type/generic arity rather than only a short name.

The guard rejects:

- New root partials, including empty declarations under unrelated filenames.
- New root members, including readonly collections, properties, native
  `Tick`/`Configure`/formula methods and nested privileged diagnostic types.
- Changes to declaration signatures, attributes, inheritance or primary constructors that
  would hide new state/access behind an existing name.
- New external typed root references, subclasses, aliases or member accesses.
- Undocumented/stale composition exceptions and unrecorded member retirements.

The JSON ledger retains the immutable starting inventory, primary migration
owner and rationale. Its canonical origin hash is pinned in the auditor.
`RetiredMembers` and `RetiredAccess` remove permissions from that inventory;
record removals in the same migration that removes the old code. Reintroducing
a retired member fails. Never expand/reseed the origin to accept new work.
The retained origin is historical evidence, not an active broad file exemption.

An unavoidable temporary root composition bridge needs an exact entry in
`Composition`: member key, implementing issue, ownership rationale, follow-up
issue in `ExpiresWith`, and the reported token hash. The hash ignores comments
and formatting but detects body growth. Remove the entry when its bridge moves.
Normal PR review assesses whether the bridge actually composes/observes rather
than acquiring feature policy. This is not a separate permission process.

Existing method bodies are deliberately not frozen: incremental fixes and
migration wiring remain possible. Native formula implementations newly added
to the root fail the same no-new-member rule. This structural check cannot prove
the provenance of arbitrary arithmetic or prevent every reflection/dynamic
bypass. Source authority, layer review and the existing ownership/naming guards
still apply. Do not use local functions, untyped objects or reflection to evade
the owner boundary.

## Reproducing the measurements

From the repository root, without retail sources:

```text
dotnet run --project tools/OBP.ArchitectureTool -c Release -- check .
dotnet run --project tools/OBP.ArchitectureTool -c Release -- census .
```

The census writes JSON to stdout with per-file/direct-member totals, member
keys/hashes and bound cross-file reference edges. Redirect it to an ignored
local evidence directory to compare revisions. Paths are repository-relative;
no assets or machine paths are emitted. Keep the audited commit and report
revision with the evidence. Compare exposed ports and ownership as well as
field/line totals; a readonly owner reference can be a useful replacement for
many unrelated state fields.

The existing native CI job publishes this as the `root-ownership-census`
artifact for 14 days, alongside the portable guard tests.

Each migration PR reports its owner/lifetime, narrow API, before/after census,
disposal behavior, affected scenario evidence and source compatibility. Keep
native clocks/input/capture phases unchanged. Preserve every diagnostic and
move architecture checks with its new owner. Fresh same-build replay validates
each revision; older recordings carry different assembly IDs and require
explicit cross-build investigation/trace comparison without weakening the
same-build gate.
