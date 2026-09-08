# Going Commando PVars

**Authority:** `rac2-ntscu-v1.01` / `SCUS-97268`  
**Retail ISO SHA-256:** `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`

This document records native per-instance variable (`PVar`) structure used by Going Commando gameplay data. Retail bytes and loaded executable behaviour are authority; public archaeology is used only as corroboration and naming guidance.

## Status legend

- **CONFIRMED** — validated directly against the authority retail build.
- **CORROBORATED** — retail structure agrees with a pinned public implementation/name.
- **INFERRED** — strong structural/behaviour interpretation, not yet symbol-proved.
- **UNKNOWN** — unresolved.

## Gameplay block layout

**CONFIRMED.** In the decompressed GC gameplay lump:

| gameplay header | native block |
|---:|---|
| `+0x48` | Moby class list |
| `+0x4c` | static Moby instances |
| `+0x58` | PVar Moby-link fixups |
| `+0x5c` | PVar table |
| `+0x60` | PVar data |
| `+0x64` | PVar relative-pointer fixups |

The static GC Moby record is `0x88` bytes. Fields used by the archaeology probe:

| Moby offset | field |
|---:|---|
| `+0x10` | UID/raw instance identifier |
| `+0x14` | unresolved native field; kept raw |
| `+0x28` | `oClass` |
| `+0x68` | PVar table index (`<0` = none) |
| `+0x70` | mode bits |

A PVar table entry is two little-endian `s32`s: offset relative to the PVar data block, then byte size. Both fixup tables are pairs `{ s32 pvar_index; u32 offset_within_pvar; }` terminated by a negative `pvar_index`.

Reproducible parser: `reference-ts/packages/gc-pvars/src/index.ts`.

## Retail census

**CONFIRMED.** The all-level exact-authority census was reconfirmed on post-OBP branch commit `9b90f23c9bb67eb7d72a958a4fbbd0cf11f68b06` in pipeline `2827607142`, job `16354126201` after full SHA-256 verification. A later focused 4018/4021 census on post-main-sync commit `6db4a15d894ca3047fcc5fa04029155c49cec323` (pipeline `2827640439`, job `16354341562`) reproduced the same global totals and target-class footprints.

Across all 27 `LEVEL*.WAD` gameplay lumps:

- 17,422 static Mobies
- 976 distinct observed static Moby classes
- 14,344 static Mobies with resolved PVars
- 5,826 PVar Moby-link fixups total; 5,766 owned by static-Moby PVars
- 22,863 relative-pointer fixups; all owned by static-Moby PVars
- zero duplicate static-Moby PVar indices
- every static-Moby-owned fixup is in bounds for its owning PVar

This is a structural census. Camera/sound PVars are not included in the static-Moby ownership totals above.

## GC subvar header

Pinned public source: Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`, `src/instancemgr/pvar.cpp`. Wrench describes a `0x20`-byte GC PVar subvar header whose pointer fields include `TargetVars` at `+0x00` and a GC-specific block at `+0x08`.

The retail crate-family slice independently establishes:

- every authored class `500/501/505/511/512` instance has mode bit `0x20`;
- every such PVar is exactly `0x110` (272) bytes;
- every such PVar has native relative-pointer fixups at `+0x00` and `+0x08`;
- PVar dword `+0x00` is always `0x20`;
- PVar dword `+0x08` is always `0x50`;
- header dwords `+0x04,+0x0c,+0x10,+0x14,+0x18,+0x1c` are always zero.

Therefore the two substructure offsets are **CONFIRMED**; public names `TargetVars` and `GcVars08` are **CORROBORATED**, not retail symbol names.

## Crate-family authored fields

Authority probe: `reference-ts/tools/gc-pvar-family-probe.mjs`.

**CONFIRMED.** Across all 2,788 static instances of classes `500/501/505/511/512`, 66 of the 68 dwords in the `0x110`-byte PVar are identical across every class and level. The only authored dwords that vary are:

- `+0xC8`: `0` or `1`
- `+0xCC`: `0` or `1`

Class `500` and class `505` are entirely constant at those authored fields. The other classes vary only in a few level-specific groups:

| class | field | nonzero retail instances | where observed |
|---:|---:|---:|---|
| 501 | `+0xC8` | 16 / 131 | LEVEL2 only |
| 501 | `+0xCC` | 18 / 131 | LEVEL2 (16), LEVEL26 (2) |
| 511 | `+0xC8` | 32 / 423 | LEVEL2 (16), LEVEL11 (16) |
| 511 | `+0xCC` | 35 / 423 | LEVEL2 (16), LEVEL11 (16), LEVEL26 (3) |
| 512 | `+0xC8` | 16 / 96 | LEVEL11 only |
| 512 | `+0xCC` | 16 / 96 | LEVEL11 only |

LEVEL2 = Maktar Resort, LEVEL11 = Joba / Megacorp Games, LEVEL26 = Gorn Space Arena.

The previous conclusion that these were merely arena-correlated authored booleans has now advanced: loaded executable code **directly consumes both fields**. Oozla-family code provides a native `+0xC8` read; Maktar's loaded shared crate update directly reads `+0xCC` during initialization. Therefore `+0xC8/+0xCC` are **CONFIRMED live authored inputs**. Their exact gameplay semantics remain **UNKNOWN**.

## Loaded crate runtime fields

The loaded shared crate-family routines use additional words/bytes which are zero or constant in the static authored image but become mutable runtime state:

| offset | loaded behaviour |
|---:|---|
| `+0xA0/+0xA4` | linked Moby references traversed/copied/released |
| `+0xAC` | mutable flag word |
| `+0xB4` | mutable counter/timing-like word |
| `+0xC6` | runtime halfword; class 511 initializes to `100` |
| `+0xC8` | authored input, consumed by native code |
| `+0xCA` | runtime byte written to `1` on a class-505 transition path |
| `+0xCB` | runtime byte read/mapped by resource-emission logic |
| `+0xCC` | authored input, consumed by native code |

These offsets and accesses are **CONFIRMED**. Descriptive names beyond “link/flag/counter/input/selector” remain deliberately withheld.

See [`GC_CRATES.md`](GC_CRATES.md) for the loaded state-machine and helper provenance.

## Runtime class-3291 PVar

Class `3291` has no authored static PVar because it has zero authored static instances. Loaded Oozla code dynamically creates the class and initializes a runtime PVar.

**CONFIRMED loaded fields:**

- `+0x60` — amount passed by the constructor and later consumed by the indexed-count service;
- `+0x68` — resource/index value passed by the constructor and later used to select the indexed count;
- `+0x70` — initialized to integer `16`;
- `+0x6C/+0x78` — runtime floating-point state initialized by the constructor; exact semantics UNKNOWN.

The class-3291 update passes PVar `+0x68` and `+0x60` to loaded `0x002C9738`. That service indexes a 32-bit count table, adds the amount, and clamps the result to a per-index maximum where one exists. This makes `+0x68 = indexed resource/count slot` and `+0x60 = increment amount` **CONFIRMED by dataflow**, while the human names of individual indices remain UNKNOWN.

## Dormant class 502 branch

Loaded crate-family code explicitly tests oClass `502`, but the exact retail census finds:

- not present in any gameplay class table;
- zero static instances;
- zero static PVars.

Therefore class 502 is **CONFIRMED absent from authored GC gameplay data** even though shared runtime code retains support for the numeric class ID. Do not infer a GC class-502 identity solely from R&C1/public lineage.

## Notak class 4018 vertical fields

**CONFIRMED retail structure.** Class `4018` has exactly five static instances, all in LEVEL6 (Notak / Canal City). Every instance has a 64-byte PVar, mode `0`, and no PVar fixups.

For all five instances:

- PVar `+0x04` interpreted as `f32` equals the Moby's world-space Z coordinate exactly;
- PVar `+0x00` is either the same value or exactly 10 native units higher;
- the three `+10` instances also have authored `f32 +0x0C = 30.0`; the two equal-height instances have authored `+0x0C = 0.0`;
- authored `f32 +0x34 = 4.5` for every instance.

| Moby | position Z | `f32 +0x00` | `f32 +0x04` | `f32 +0x0C` | `f32 +0x34` |
|---:|---:|---:|---:|---:|---:|
| 657 | 47.0 | 47.0 | 47.0 | 0.0 | 4.5 |
| 658 | 57.0 | 67.0 | 57.0 | 30.0 | 4.5 |
| 659 | 52.0 | 62.0 | 52.0 | 30.0 | 4.5 |
| 660 | 57.0 | 67.0 | 57.0 | 30.0 | 4.5 |
| 661 | 50.5 | 50.5 | 50.5 | 0.0 | 4.5 |

Loaded Notak `lvl.vtbl` resolves class 4018 to update `0x00456BB0`. The function ends at `0x00456EDC`; the immediately following routines belong to other code and must not be conflated with the class-4018 update.

Within the actual class-4018 function body, retail code **CONFIRMS**:

- Moby `+0x68` is used as its PVar pointer;
- Moby `+0x20` dispatches a three-state (`0/1/2`) state machine;
- PVar `+0x00` is read as `f32` and written into the Z component of a temporary position/query vector;
- authored PVar `+0x34` (`4.5`) is read as `f32` and passed to a called helper;
- PVar `+0x10`, `+0x14`, `+0x18`, `+0x28`, `+0x2C`, and `+0x38` are also consumed by the loaded update in integer/float/reference-like paths.

This upgrades `+0x00` from a purely statistical height candidate to a **CONFIRMED live vertical-plane input**. The exact semantic name—water height, frozen height, target height, or another plane—remains **UNKNOWN**. The public Wrench `moby4018_therm_water` lead keeps a Thermanator/water-system identity **CORROBORATED**, not retail-symbol-confirmed.

Importantly, class 4018's own update has **not** yet been shown to read its authored `+0x04` or `+0x0C` fields. Their strong authored correlations remain CONFIRMED data facts, but their runtime consumers are still UNKNOWN.

Exact-authority loaded-code provenance: pipelines `2827627993` / `2827629753` (jobs `16354256903` / `16354269806`) on the same retail authority, plus post-main-sync vtable/xref pipeline `2827642036` job `16354351180`.

## Notak class 4021: neighboring but not yet linked

A focused post-main-sync census independently confirms class `4021` has exactly 11 static instances, all in LEVEL6, each with a 16-byte PVar, mode `0`, and no PVar fixups. All 11 authored PVars are byte-for-byte identical at dword granularity:

| offset | authored value |
|---:|---:|
| `+0x00` | `0` |
| `+0x04` | `0x42700000` = `60.0f` |
| `+0x08` | `103` |
| `+0x0C` | `0` |

All 11 instances share X/Y approximately `(192.438, 189.344)` and form a vertical stack from Z `60.573` to `76.534`, with varying scales.

Loaded Notak `lvl.vtbl` resolves class 4021 to `0x00456F08`, immediately after the class-4018 routine region. That class-4021 update has its own state `0/1/2` dispatch and reads **its own** PVar `+0x04` and `+0x0C`; those reads are not evidence that class 4018 consumes class-4018 `+0x04/+0x0C`.

An exact loaded-`.text` JAL-xref scan finds **zero direct calls** to either class-4018 update `0x00456BB0` or class-4021 update `0x00456F08`. There is therefore currently no direct-call evidence that 4018 and 4021 form one gameplay subsystem. Their adjacent vtable records/functions are a structural observation only.

Status: class 4021's structure and loaded update are **CONFIRMED**; its gameplay identity and relationship to 4018 are **UNKNOWN**. Do not call it a water/Thermanator component without additional evidence.

## Reproduction

Bounded authority probes are explicit opt-ins through the project `obp-local` runner, including:

```text
run_mode = local-gc-moby-census
run_mode = local-gc-pvar-family
run_mode = local-gc-vtbl-probe
run_mode = local-gc-overlay-probe
```

They verify the exact retail ISO hash and emit only bounded structural/disassembly summaries; retail payloads remain local. The vtable workflow's loaded-code xref targets are parameterized through `gc_vtbl_xref_targets` so subsystem call-graph tests do not require one-off CI edits.

## Next questions

1. Map the indexed resource table used by class 3291 to named inventory/resource slots.
2. Resolve exact `+0xC8/+0xCC` semantics now that their native reads are proven.
3. Identify incoming event/caller semantics for the crate transition helpers and state numbers `0..6`.
4. Determine crate collision removal, respawn and persistence/save behaviour.
5. Determine the semantic meaning and consumer of class-4018 `+0x04/+0x0C`, and identify the exact event transitions for its state `0/1/2` machine.
6. Identify class 4021 independently before asserting any relationship to class 4018.
