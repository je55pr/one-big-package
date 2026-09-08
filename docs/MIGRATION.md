# Native runtime migration — completed A–H baseline

OBP's production runtime has moved from the original browser/TypeScript application to a **native desktop game built with Godot 4 + C#**. The A–H migration milestone was merged into `main` on 2026-09-07.

The TypeScript implementation was not discarded. It is preserved under [`../reference-ts/`](../reference-ts/) as an archaeology and equivalence oracle, with [`TS_REFERENCE_BASELINE.md`](TS_REFERENCE_BASELINE.md) recording the baseline used during the port.

For the living project snapshot, use [`CURRENT_STATE.md`](CURRENT_STATE.md). This file records the migration decision and its acceptance gates.

## Architecture established by the migration

```text
retail ISO / game files
    -> OBP.IO         bounded native random access, splits, hashing
    -> OBP.PS2        ISO-9660, SYSTEM.CNF, ELF32, shared PS2 helpers/codecs
    -> OBP.RAC1/2/3   game-specific native structures/parsers
    -> OBP.Runtime    engine-independent runtime concepts
    -> OBP.Godot      runtime/native-neutral data -> Godot objects
    -> game/          application shell, scenes, UI, input, presentation
```

`OBP.Core` holds shared provenance/math concepts. Native Ratchet parsing is deliberately kept out of `game/` and `OBP.Godot` so it can be tested without launching the engine.

## Repository layout

| Path | Purpose |
|---|---|
| `OneBigPackage.sln` | native solution |
| `src/OBP.*` | engine-independent and Godot-adapter C# libraries |
| `game/` | Godot 4 C# application |
| `tests/OBP.Tests/` | xUnit suite |
| `reference-ts/` | preserved TypeScript archaeology/equivalence implementation |
| `research/`, `docs/` | retail archaeology, provenance and project documentation |
| `tools/` | bootstrap/build/test/play/capture wrappers |
| `.tools/` | downloaded pinned toolchain binaries; git-ignored |

## What was ported for the milestone

The migration established native equivalents for the core path required to open a supported Going Commando disc and walk around reconstructed world data:

- `OBP.IO`: seekable bounded readers, subranges, concatenated numbered splits, streaming SHA-256 and authority verification;
- `OBP.PS2`: ISO-9660, `SYSTEM.CNF`, ELF32, WAD-LZ, shared texture/VIF/collision helpers;
- `OBP.RAC1/3`: authority identities/manifests;
- `OBP.RAC2`: GC level WAD/core parsing, settings/instances, textures, tfrags, TIEs, shrubs, Mobies, sky, collision, world assembly and disc-level load/verify façade;
- `OBP.Cli`: deterministic `test-import` summaries;
- `OBP.Godot`: world meshes/textures/collision and camera presentation;
- `game/DebugPlayer`: provisional capsule movement for traversing reconstructed collision;
- deterministic Godot capture modes for smoke, picker, world, collision and player validation.

The native codecs were ported incrementally against the preserved TypeScript implementation and retail-derived deterministic hashes/counts rather than rewritten from memory.

## A–H acceptance gates

| Gate | Goal | Result |
|---|---|---|
| **A** | Godot C# project launches; deterministic smoke capture; pinned toolchain | **done** |
| **B** | Native IO + ISO + build verification matches authority data | **done** |
| **C** | Native WAD-LZ matches the TypeScript/retail baseline | **done** |
| **D** | Native GC import reproduces deterministic Oozla counts | **done** |
| **E** | Oozla renders directly in Godot from C# | **done** |
| **F** | Native file picker opens/identifies a real GC ISO | **done** |
| **G** | Decoded native collision exists as Godot collision | **done** |
| **H** | Debug capsule spawns at retail ship data and walks/jumps on decoded collision | **done** |

The resulting milestone is genuinely native end to end:

```text
Godot executable
  -> local retail GC ISO
  -> native C# identification/import
  -> native C# reconstructed world
  -> Godot render/collision
  -> debug capsule traversal
```

Oozla is the principal deterministic baseline. The merged path also handles unchunked levels such as Aranos, accepts arbitrary GC level IDs, provides fly/noclip and respawn for exploration, and retains deterministic screenshots/metadata.

## Toolchain

The repository pins the Godot build in `tools/godot-toolchain.json`.

- Godot: `4.7.2-stable` Mono/.NET build pinned by the repository.
- C# projects: `net8.0`; newer compatible SDKs may build them as well.

### Windows development loop

```powershell
./tools/bootstrap-dev.ps1
./tools/build.ps1
./tools/test.ps1
./tools/capture.ps1 smoke
./tools/capture.ps1 -GcIso $env:OBP_GC_ISO
./tools/capture.ps1 -Picker -GcIso $env:OBP_GC_ISO
./tools/capture.ps1 -CollisionDebug -GcIso $env:OBP_GC_ISO
./tools/capture.ps1 -Player -GcIso $env:OBP_GC_ISO
./tools/play.ps1
```

### Linux / sandbox loop

```bash
./tools/bootstrap-dev.sh
./tools/build.sh
./tools/test.sh
./tools/capture.sh smoke
GC_ISO="$OBP_GC_ISO" ./tools/capture.sh
```

## Deterministic capture contract

`game/scripts/OBPGame.cs` supports command-line test/capture options for smoke scenes, the picker, retail GC import, player mode, level selection, hash verification and collision debugging. Captures are written beneath `captures/` and remain git-ignored because screenshots derived from retail assets are not repository content.

The migration deliberately retained the old project's strongest development habit: important native/runtime changes should be reproducible by automated tests or deterministic captures rather than only by manual play.

## Constraints retained after migration

- Retail bytes/executable behaviour remain authority.
- Unknown native fields stay uninterpreted until evidence supports them.
- OBP-created runtime behaviour is labelled as such.
- No Sony/Insomniac assets, executables or disc images belong in Git.
- Multi-gigabyte inputs remain seekable/range-readable sources, not giant in-memory buffers.
- TypeScript is a reference implementation, not a second product runtime.
- Campaign chronology and final trilogy progression remain design questions, not architecture constraints.

## What the migration deliberately did not solve

A–H proved the native foundation; it did not claim native-perfect gameplay. Ratchet character animation/movement, enemies/AI, weapons, story logic, trilogy progression, economy, audio archaeology, cross-game fusion, perfect GS rendering and spherical-world player behaviour remain later systems work.

Since A–H completed, active development has already moved into generic GC planet switching and deeper world fidelity on a dedicated branch. Track that work in [`CURRENT_STATE.md`](CURRENT_STATE.md) rather than extending this historical migration checklist indefinitely.
