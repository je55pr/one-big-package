# Ratchet & Clank: One Big Package

**One Big Package (OBP)** is a reverse-engineering and game-runtime project exploring how the original PS2 **Ratchet & Clank**, **Going Commando**, and **Up Your Arsenal** can be reconstructed into one shared runtime and, eventually, one deliberately combined game.

The production runtime is now a **native desktop application built with Godot 4 + C#**. The earlier TypeScript/browser implementation is preserved under [`reference-ts/`](reference-ts/) as an archaeology, equivalence and comparison oracle; it is no longer the product runtime.

The long-term design remains intentionally open. OBP may preserve, interweave or substantially remix original story, level and progression structure. Retail data and executable behaviour remain the authority for what the source games actually did, and OBP-created design choices must stay distinguishable from recovered native behaviour.

See [`docs/PROJECT_VISION.md`](docs/PROJECT_VISION.md) for the creative direction and [`docs/CURRENT_STATE.md`](docs/CURRENT_STATE.md) for the implementation snapshot.

## What works today

The merged native baseline can already:

- build and test the layered C# solution (`OBP.Core`, `OBP.IO`, `OBP.PS2`, `OBP.RAC1/2/3`, `OBP.Runtime`, `OBP.Godot`, CLI and tests);
- open and identify the supported Going Commando NTSC-U v1.01 retail image (`SCUS-97268`);
- verify authority by streamed SHA-256 without loading a multi-gigabyte image into memory;
- parse ISO-9660, `SYSTEM.CNF`, ELF32 and WAD-LZ through bounded random-access readers;
- reconstruct Going Commando level WAD/core data, tfrags, textures, TIEs, shrubs, Mobies, sky, settings and octree collision in native C#;
- render reconstructed GC world data directly in Godot from user-supplied retail bytes;
- create Godot collision from the decoded native collision and spawn a provisional `CharacterBody3D` debug capsule at decoded ship data;
- walk, jump, respawn and fly around reconstructed worlds with deterministic capture support;
- load both chunked and unchunked GC levels through the native importer path.

The original browser/TypeScript implementation remains useful because many C# ports are checked against the same synthetic fixtures and retail-derived deterministic baselines. It should be treated as executable research/reference code, not as an alternative production architecture.

R&C1 and UYA archaeology are advancing on dedicated specialist branches. Their branch-local findings should not be mistaken for capabilities already merged into `main`; [`docs/CURRENT_STATE.md`](docs/CURRENT_STATE.md) keeps that distinction explicit.

## Native architecture

```text
user-supplied retail ISO / files
        |
        v
OBP.IO          bounded native random access, splits, hashing
        |
        v
OBP.PS2         ISO-9660, SYSTEM.CNF, ELF32, shared PS2 codecs
        |
        v
OBP.RAC1/2/3    game-specific native formats and provenance
        |
        v
OBP.Runtime     engine-independent runtime concepts
        |
        v
OBP.Godot       thin Godot adapter
        |
        v
game/           application, input, UI, cameras, audio/presentation
```

Godot is the host, not the parser. Native Ratchet formats belong in ordinary testable C# libraries rather than Godot scene scripts.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and the completed migration record in [`docs/MIGRATION.md`](docs/MIGRATION.md).

## Development commands

The repository pins its Godot toolchain. C# projects target .NET 8 and are also verified with newer compatible SDKs.

### Windows

```powershell
./tools/bootstrap-dev.ps1
./tools/build.ps1
./tools/test.ps1
./tools/play.ps1
./tools/capture.ps1 smoke
```

Retail GC development can additionally use the `-GcIso`, `-GcLevel`, `-Player`, `-CollisionDebug` and picker/capture options documented in [`docs/MIGRATION.md`](docs/MIGRATION.md).

### Linux / CI-style shell

```bash
./tools/bootstrap-dev.sh
./tools/build.sh
./tools/test.sh
./tools/capture.sh smoke
```

### TypeScript reference implementation

```bash
cd reference-ts
npm install
npm run check
```

## Retail-authority development bridge

A project-scoped self-hosted Windows GitLab runner tagged `obp-local` can execute opt-in bounded archaeology jobs directly against Jess's locally stored retail images. Normal pipelines default to **no jobs**, so hosted compute and the local machine are used only when explicitly selected.

The three local retail images have been full-hash verified against the repository manifests. Specialist branches can add task-specific `obp-local` probes and commit only bounded, sanitized evidence; retail payloads stay local.

See [`docs/LOCAL_RUNNER.md`](docs/LOCAL_RUNNER.md).

## Repository guide

- [`docs/PROJECT_VISION.md`](docs/PROJECT_VISION.md) — what OBP is trying to become, with unresolved design questions kept explicit.
- [`docs/CURRENT_STATE.md`](docs/CURRENT_STATE.md) — merged implementation state plus clearly labelled active-branch snapshots.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — native runtime/import boundaries and evidence rules.
- [`docs/MIGRATION.md`](docs/MIGRATION.md) — record of the completed browser → native A–H migration milestone.
- [`docs/LOCAL_RUNNER.md`](docs/LOCAL_RUNNER.md) — self-hosted retail-authority workflow and CI selectors.
- [`docs/brainstorming/README.md`](docs/brainstorming/README.md) — non-binding cross-game design exploration.
- [`research/README.md`](research/README.md) — index to retail archaeology, public cross-checks and generated evidence.
- [`research/BUILD_PROBING.md`](research/BUILD_PROBING.md) — source identification and confidence rules.
- [`docs/AGENT_SANDBOX_NOTES.md`](docs/AGENT_SANDBOX_NOTES.md) — fallback guidance for constrained ChatGPT sandboxes.

## Source authority and copyright boundary

Development uses selected original NTSC-U retail builds as primary mechanical authorities, with regional/revision builds retained as comparison sources and possible sources of intentional content/fixes. Going Commando currently targets NTSC-U v1.01 (`SCUS-97268`) rather than Greatest Hits v2.00 as its primary authority.

OBP does **not** contain Sony/Insomniac assets, executables or disc images. Users supply supported game data locally. Source bytes remain outside Git; only code, tests, research, non-infringing metadata and rebuildable derived state belong in the repository.

See [`research/INPUT_PROVENANCE.md`](research/INPUT_PROVENANCE.md) for the source policy.
