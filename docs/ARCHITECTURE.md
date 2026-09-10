# OBP architecture

One Big Package's production architecture is **native Godot 4 + C#**. `reference-ts/` is temporary archaeology/equivalence code where native parity is incomplete; browser storage and WebGL are not product constraints.

This document is intentionally separate from [`PROJECT_VISION.md`](PROJECT_VISION.md): architecture should enable future trilogy fusion without quietly deciding story order, progression or cross-game rules.

## Core layering

```text
user-supplied retail ISO / files
        |
        v
OBP.IO
  native bounded random access, file/split readers, hashing, authority identity
        |
        v
OBP.PS2
  ISO-9660, SYSTEM.CNF, ELF32, shared PS2 compression/geometry/texture helpers
        |
        v
OBP.RAC1 / OBP.RAC2 / OBP.RAC3
  game-specific native structures, parsers and provenance
        |
        v
OBP.Runtime
  engine-independent runtime concepts and game-facing neutral data
        |
        v
OBP.Godot
  neutral/runtime data -> Godot meshes, textures, collision and scene objects
        |
        v
game/
  application shell, lifecycle, input, cameras, UI, audio/presentation
```

`OBP.Core` contains shared provenance/math concepts used beneath those layers. `OBP.Cli` and `OBP.Tests` exercise the engine-independent stack without requiring Godot.

### Hard boundary: Godot is the host, not the parser

Nothing in `game/` should need to understand a GC WAD header, R&C1 disc index or UYA native class table. Native Ratchet parsing belongs in ordinary C# libraries where it can be unit-tested and compared deterministically with retail evidence.

Likewise, game-specific decoders should not manufacture Godot nodes. Their output should terminate in native/neutral OBP data, which the adapter/runtime layer can present.

The merged A–H GC baseline still has some transitional shapes around `GcWorldImport`; active runtime work is moving those through a genuinely game-neutral `RuntimeWorld`. That is the direction of travel, not a reason to put more game-specific knowledge into Godot.

## Rule 1: preserve before interpreting

Game-specific native decoders normalise source data into shared OBP structures while retaining provenance. Unknown gameplay instances stay unknown until evidence supports a semantic type.

The shared representation is not any one game's native level format, and native archaeology must not be forced into a cross-game meaning merely because the runtime wants a convenient abstraction.

A useful pipeline is:

```text
native evidence
    -> faithful game-specific representation
    -> neutral runtime concept where justified
    -> optional OBP orchestration/design
```

not:

```text
native evidence -> one prematurely chosen trilogy-wide meaning
```

## Rule 2: retail builds remain authority

Retail bytes and executable behaviour establish what the selected source builds actually did. Source manifests record game, stable build ID, region, serial, revision, size and cryptographic identity. No original game data belongs in Git.

Public tools such as Wrench/noclip are evidence and cross-checks, not authority over contradictory retail data. OBP-created behaviour is allowed, but it belongs above the native-evidence layer and must be labelled as OBP design.

## Rule 3: local-first, bounded native IO

Multi-gigabyte images are ordinary seekable sources, not giant in-memory blobs.

The production source path is conceptually:

```text
local file / validated numbered split set
        |
        v
IRandomAccessReader
        |
        +--> bounded range/subrange readers
        +--> streamed hashing / authority verification
        |
        v
ISO-9660
        |
        +--> SYSTEM.CNF / boot ELF identification
        |
        v
game-specific native containers/assets
        |
        v
neutral/runtime world data
```

`OBP.IO.FileRandomAccessReader`, `SubRangeReader` and `ConcatenatedRandomAccessReader` provide the native implementation. `SplitParts` validates numbered split inputs. Hashing and identity checks stream data rather than reconstructing an entire image in RAM.

The earlier browser `File`/`Blob` and concatenated-reader code remains useful in `reference-ts`, but it is no longer an architectural requirement for the product.

## Shared PS2 boundary

`OBP.PS2` owns formats and helpers that are demonstrably shared below the individual game boundary, including:

- ISO-9660 filesystem access;
- PS2 boot identification / `SYSTEM.CNF`;
- ELF32 parsing and virtual mapping;
- WAD-LZ where retail evidence confirms the common codec;
- shared collision, VIF and texture helpers where compatibility is independently established.

A codec being shared does **not** imply that the enclosing R&C1, GC and UYA containers are the same. Outer layout and provenance remain game-specific.

## Per-game decoder boundary

`OBP.RAC1`, `OBP.RAC2` and `OBP.RAC3` own game-specific native structures and assembly logic.

Going Commando currently has the deepest merged C# implementation. Its native path covers level WAD/core data, tfrags, textures, TIEs, shrubs, Mobies, sky, settings, collision and ship spawn. `GcIsoLoad` is the engine-free façade for identifying/verifying a supported disc and loading a level.

R&C1 and UYA archaeology may prove compatibility first in research/reference tooling. Stable findings should be promoted into the C# libraries only with deterministic equivalence/authority tests. Do not weaken a GC parser merely to make another game pass; either prove the shared structure or add the correct game-specific boundary.

## Runtime boundary

`OBP.Runtime` is where native reconstruction becomes engine-independent game-facing data. It should contain concepts useful regardless of renderer/engine, for example:

- runtime meshes/material references;
- collision geometry/query data;
- environment/lighting state;
- spawns and transforms;
- future entity/state/gameplay concepts once their semantics are justified.

This layer should be capable, in principle, of being consumed by a non-Godot front end without teaching that front end the retail file formats.

## Godot boundary

`OBP.Godot` converts runtime/native-neutral data into Godot presentation objects:

- `ArrayMesh` / `MeshInstance3D`;
- `Image` / `ImageTexture` / materials;
- `StaticBody3D` / collision shapes;
- cameras and engine-facing scene helpers.

`game/` owns application lifecycle and player-facing concerns such as the file picker, planet/world selection, input, debug/player controller, HUD and eventual audio/UI/gameplay presentation.

The current `DebugPlayer` is explicit provisional OBP runtime behaviour. Its movement values are useful for traversing collision but are **not** claimed to reconstruct native Ratchet physics.

## Deterministic validation is part of the architecture

One of the most valuable browser-era practices survives the migration: every major decoder/runtime step should be testable without manual play.

Current mechanisms include:

- xUnit tests over synthetic and retail-gated fixtures;
- TypeScript/C# equivalence checks for already-understood formats;
- `OBP.Cli` deterministic import summaries;
- Godot command-line capture modes producing screenshots plus metadata;
- explicit local probes against exact retail authorities on an authorized development machine.

A visual runtime feature should, where practical, have a repeatable capture or state assertion so an agent can change, run and inspect it without relying on a human play session for every iteration.

## Retail-authority agent bridge

Retail-backed archaeology is run explicitly on an authorized local machine against user-owned sources. Portable CI must not depend on retail payloads, and only bounded payload-free evidence may be committed.

## Storage / cache boundary

Production OBP no longer depends on OPFS, IndexedDB or CacheStorage. Retail source images remain user-owned local files outside the repository.

Derived caches may be added where they materially improve load time, but they must be rebuildable from verified source data and should not become a second source of truth. The exact native cache/save layout remains a runtime design decision rather than an archaeology assumption.

The old `reference-ts/packages/storage` implementation remains useful historical/reference code only.

## Gameplay/design boundary

Recovered native campaign machinery — story flags, mission gates, encounters, vendors, saves, etc. — should be represented faithfully enough to understand and reproduce it. The shared runtime must avoid assuming that the final OBP game preserves the original orchestration unchanged.

That separation lets archaeology remain trustworthy while the creative design remains open:

```text
source game truth -> faithful native model -> deliberate OBP adaptation/remix
```

When a cross-game rule is chosen, document it as an OBP design decision and keep the underlying source evidence accessible.

## Current engineering principle

Prefer a small evidence-backed capability with deterministic tests over a broad abstraction that guesses what the fused game will need. Preserve provenance, keep retail data local, keep game-native parsing out of Godot, and promote shared concepts only when the evidence justifies sharing them.

## Player-avatar runtime boundary

Playable character presentation is a separate runtime concern from world import. `OBP.Runtime/Player` carries stable avatar/model identity, decoded surface textures, per-surface UV/index groups, bounds/origin/axis metadata, and multiple animation clips in **model-local space**. Each clip has a neutral semantic role plus per-frame durations, so native variable-rate timing survives without flattening it to a guessed constant FPS. Source-game sequence numbers stop at the provider boundary and never enter `OBP.Runtime` or game code.

This differs from `RuntimeAnimatedMesh`: its `Frames` are already **OBP Y-up world-space** positions for a particular placed world object. Hosts must place/remap every `PlayerAvatar` clip themselves rather than treating its local frames as `RuntimeAnimatedMesh` data.

Source-game providers own retail parsing. `Rac1PlayerAvatarProvider` reads the user-owned R&C1 authority independently of any loaded `RuntimeWorld`, decodes `Rac1RatchetAvatar`, and resolves Ratchet's Moby textures from explicit canonical native level 0. Its admitted neutral clip set now includes standing, locomotion start and sustained locomotion, two deliberately indistinguishable stop variants, stationary/moving jump, crouch and direction-specific crouch turns, and primary attack. The two stop clips share one neutral role because retail evidence admits both as stop transitions but does not yet establish the 5-vs-6 selection predicate.

`PlayerAvatarView` implements `IPlayerAnimationStateSink` as presentation policy over those clips. Idle selects standing; Walk and Run currently share the admitted sustained-locomotion clip as an explicit OBP presentation choice; JumpRise chooses moving or stationary jump from launch context; Fall preserves that same airborne clip and clock origin; Land hands directly back to standing or locomotion because no separate retail landing selector was witnessed; and Attack is a one-shot that returns to the previous ground context. Physics and movement remain authoritative outside this presentation mapping.

Skeleton metadata is optional at this boundary. R&C1 currently leaves it absent because the integrated avatar exposes native hierarchy/affine records, but no neutral bind/animation-transform meaning has yet been established strongly enough to publish as shared runtime semantics.
