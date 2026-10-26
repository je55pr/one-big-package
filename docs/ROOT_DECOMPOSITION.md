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
The measured root has 101 files, 23,466 file lines, 717 direct methods, 456
fields (311 mutable), 44 exposed members and 2,431 cross-file references.
Movement, native air/ground/contact, water, tether, environmental restart and
controller-weapon assertions consume copied facts and focused fixture actions.
One input/clock driver owns logical actions, simulated keys/buttons and phase
waits. Synthetic geometry, development settings, relocated enemies and drowning
subscriptions have scoped cleanup. Forty-six historical declaration/member
permissions retire; twenty exact composition delegations remain for later
diagnostic, audio, controller-UI and movement-reproduction owners. Their expiry
owners are recorded individually; the origin ledger does not expand.

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
