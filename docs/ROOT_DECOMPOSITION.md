# Root ownership and decomposition (#333 / #334)

`OBPGame` is the Godot composition root. It may construct owned components,
receive engine lifecycle/input callbacks and delegate through narrow ports.
New gameplay policy, native encounter state, source algorithms and diagnostic
assertions belong in their actual owners. Existing world/session/actor/operation
owners remain authoritative; extraction must not create replacement sessions or
another all-capabilities manager.

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

`RootBoundaryArchitectureTests` runs with the ordinary source-free test suite.
The reusable Roslyn implementation/CLI is `tools/OBP.ArchitectureTool`.
It scans all C# source below `game/`, excluding build/import outputs, binds
references and identifies the class declaration rather than its filename.

The guard rejects:

- New root partials, including empty declarations under unrelated filenames.
- New root members, including readonly collections, properties, native
  `Tick`/`Configure`/formula methods and nested privileged diagnostic types.
- Changes to declaration signatures, inheritance or primary constructors that
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
