# Going Commando gameplay Moby catalogue

**Authority:** `rac2-ntscu-v1.01` / `SCUS-97268`  
**Retail ISO SHA-256:** `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`

This is the semantic catalogue layer above the structural census in [`GC_PVARS.md`](GC_PVARS.md). Only classes with reproducible evidence are named or prioritized. The machine-readable slice is `research/generated/gc_moby_catalogue.json`.

## Confidence

- **CONFIRMED** — direct retail bytes/executable/behaviour.
- **CORROBORATED** — public identity agrees with retail evidence but native code has not independently named the object.
- **INFERRED** — strong structural/behaviour interpretation.
- **UNKNOWN** — unresolved.

## First interactive family

| oClass | retail instances | levels observed | PVar | mode | rel-ptr fixups | identity |
|---:|---:|---|---:|---:|---|---|
| 500 | 1,979 | 18 levels | `0x110` | `0x20` | `+0x00,+0x08` | **CORROBORATED:** Bolt Crate |
| 501 | 131 | 13 levels | `0x110` | `0x20` | `+0x00,+0x08` | **UNKNOWN** |
| 502 | 0 | none | none | — | — | **UNKNOWN:** dormant/shared-code branch only |
| 505 | 159 | 7 levels | `0x110` | `0x20` | `+0x00,+0x08` | **UNKNOWN** |
| 511 | 423 | 19 levels | `0x110` | `0x20` | `+0x00,+0x08` | **UNKNOWN** |
| 512 | 96 | 11 levels | `0x110` | `0x20` | `+0x00,+0x08` | **UNKNOWN** |
| 3291 | 0 | none; class-table entry all 27 | runtime only | — | — | **INFERRED:** generic indexed collectible/resource object |
| 4018 | 5 | LEVEL6 only | `0x40` | `0` | none | **CORROBORATED:** Thermanator/water lead |
| 4021 | 11 | LEVEL6 only | `0x10` | `0` | none | **UNKNOWN** |

### oClass 500 — Bolt Crate

Pinned Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`, `docs/asset_system.md`, explicitly uses `gc.moby_classes.500` as its R&C2 bolt-crate example. Retail independently confirms a strong crate-family runtime footprint. Therefore `500 = Bolt Crate` remains **CORROBORATED**.

The loaded-runtime picture has advanced substantially:

- Oozla classes 500/501/511 share loaded update `0x00391080`;
- Maktar classes 500/501/505/511 share `0x003A52B8`;
- Endako classes 500/501/511/512 share `0x003AB1E8`;
- the shared routine is a seven-state Moby state machine using Moby `+0x20` and the PVar at Moby `+0x68`;
- loaded transitions reach a dynamic indexed-resource emission path which constructs class 3291 objects;
- authored PVar `+0xC8/+0xCC` are both consumed by native runtime code.

The per-level addresses differ because the level executable overlay is loaded/relocated per level; these addresses are evidence for those specific overlays, not universal fixed ABI addresses.

Still UNKNOWN for a native-faithful production crate implementation:

- exact meanings/entry conditions of states `0..6`;
- exact native hit/health threshold for class 500;
- which indexed resource IDs/amounts correspond specifically to the Bolt Crate's reward;
- collision removal/effect ordering;
- persistence, reload and respawn behaviour.

### oClasses 501 / 505 / 511 / 512

**CONFIRMED:** these are both a common authored PVar family and a common loaded-runtime update family where they are present. The exact-address probes above independently cover class 505 on Maktar and class 512 on Endako.

Wrench's R&C1 underlay gives analogous R&C1 identities for several matching numbers, but those are not GC identities and are deliberately not promoted here.

PVar `+0xC8/+0xCC` are the only authored dwords that vary among these classes. Both have loaded native reads, while their exact semantics remain UNKNOWN.

### oClass 502

**CONFIRMED negative result:** the loaded crate state machine tests numeric class ID 502, but class 502 is not listed in any of the 27 retail gameplay class tables and has zero authored static instances/PVars. Treat it as dormant/shared lineage code unless a dynamic creation path is independently found.

### oClass 3291

**CONFIRMED loaded relationships:** class 3291 is present in every level's class table but has zero authored instances; Oozla resolves its loaded update to `0x003D37A0`; `0x003D3E28` dynamically constructs it, installs that update, and initializes PVar `+0x60` with an amount and `+0x68` with an indexed resource/count slot.

Its collection path addresses a 56-entry count domain. The update reads `0x00139688 + index*4`, calls `0x002C9738(index, amount)`, reads the same slot again, and only emits collection feedback when the count increased. A byte mapping at `0x00139568` selects `0xE0`-byte descriptors at `0x002637A0`; descriptor `+0x8E` supplies the capacity used by the add/clamp and resource-selector code.

Pinned Wrench independently defines `Ammo[56]`, which strongly **CORROBORATES** an ammo/inventory-resource interpretation of this runtime domain. Exact slot names remain UNKNOWN. Class 3291 should not be universally called a bolt pickup.

The previously considered `index=-1 = bolts` interpretation is explicitly superseded: loaded helpers replace `-1` with the runtime-selected index at `0x0019B068` before table access.

### oClass 4018

**CONFIRMED:** five instances, all in Notak / Canal City, each with a 64-byte PVar. PVar `f32 +0x04` equals instance world Z; `f32 +0x00` is either the same height or +10.0. Loaded Notak `lvl.vtbl` resolves class 4018 to update `0x00456BB0`, a three-state (`0/1/2`) routine. Its own update directly reads PVar `+0x00` as a floating-point vertical-plane input and consumes constant authored `+0x34 = 4.5` plus several other fields. It has **not** yet been shown to read its authored `+0x04/+0x0C` fields.

Pinned public Wrench contains a `moby4018_therm_water` lead, so a Thermanator/water identity is **CORROBORATED**. Exact state meanings and plane semantics remain UNKNOWN. See [`GC_PVARS.md`](GC_PVARS.md).

### oClass 4021

**CONFIRMED:** 11 instances, all in Notak, each with an identical 16-byte PVar `{0, 60.0f, 103, 0}` and no fixups. All 11 share X/Y approximately `(192.438, 189.344)` and form one vertical stack from Z `60.573` to `76.534` with varying scales.

Loaded Notak `lvl.vtbl` resolves class 4021 to `0x00456F08`, immediately after class 4018's loaded routine. It has its own state `0/1/2` dispatch. Exact loaded-JAL xref scans find zero direct calls to either update and no 4018↔4021 direct-call edge. Their adjacency therefore does **not** prove that they are one subsystem; class 4021's identity and relationship to 4018 remain UNKNOWN.

## Structural all-level baseline

The exact-authority census finds:

- 17,422 static Mobies
- 976 distinct observed classes
- 14,344 static Mobies with PVars
- zero duplicate static-Moby PVar indices

The catalogue should continue to be generated from this machinery rather than becoming a hand-maintained 976-row table.

## Runtime evidence checkpoint

The initial loaded-runtime set was established on post-OBP commit `9b90f23c9bb67eb7d72a958a4fbbd0cf11f68b06`. Post-main-sync exact-authority runs additionally established the Notak 4018/4021 split and the 56-slot resource table on commits `6db4a15d...` / `7bcfb568...`. All cited authority jobs verified the full retail ISO SHA-256.

## Next promotion gate

For crates, the next useful promotion is to disassemble the loaded `0x00392158` emission helper far enough to recover the exact class-500 constructor index/amount rules, then trace persistence/reload. For class 4018, the next gate is to identify the consumer/meaning of authored `+0x04/+0x0C` and the events driving its three-state update. Class 4021 should be identified independently rather than assumed to be a water-system sibling.
