# Going Commando crate archaeology

**Authority:** `rac2-ntscu-v1.01` / `SCUS-97268`  
**Retail ISO SHA-256:** `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`

This document narrows the first interactive-object milestone to the native crate family. Retail bytes and the **loaded level overlay** are authority; public archaeology is corroboration only.

## Confidence

- **CONFIRMED** — direct retail-authority relationship.
- **CORROBORATED** — retail agrees with pinned public archaeology.
- **INFERRED** — strong interpretation, not yet behaviour-proved or symbol-named.
- **UNKNOWN** — unresolved.

## Identity and authored structural family

`oClass 500 = Bolt Crate` is **CORROBORATED** by pinned Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` (`docs/asset_system.md`) and the retail footprint.

Classes `500/501/505/511/512` are **CONFIRMED** to share the authored `0x110`-byte PVar layout, mode `0x20`, and relative-pointer fixups at PVar `+0x00/+0x08`. Class 500 is authored 1,979 times across **18** retail level files. See [`GC_PVARS.md`](GC_PVARS.md).

The packed GC Moby instance field at `+0x14` is the authored Bolt reward value. The authority build gives class 500 **24 distinct values from 1 through 1000**; on Oozla, 189 of 190 class-500 instances carry `13` and one carries `14`. Oozla's loaded static-Moby population path directly copies this field into live Moby `+0xB4` (details below), so OBP exposes it as `MobyInstance.Bolts`. Pinned Wrench independently names the same authored field `bolts`.

Class `502` is an important negative result: loaded crate code explicitly retains a class-502 branch, but the exact-authority census finds class 502 **not listed and not authored in any of the 27 retail gameplay files**. It is therefore a **CONFIRMED dormant/shared-code branch in this build's authored data**; dynamic reachability remains UNKNOWN.

### Static instance -> live Moby bridge

Oozla's loaded retail population code closes the authored/runtime identity directly. It reads gameplay pointer `+0x4C`, enters the Moby block, consumes the `0x10`-byte block header, and then walks the packed static records. In the per-record path:

- `0x002F73C0`: `lw $s4,0($s2)` reads authored record `+0x10` (`uid`);
- `0x002F73C8`: `lw $v0,0($s2)` reads authored record `+0x14`, then `0x002F73D4` preserves it in `$s1`;
- the runtime Moby address is formed from the live pool base plus `index << 8`, confirming a `0x100`-byte live-Moby stride;
- `0x002F769C` calls live-Moby initializer `0x00305FE0`;
- `0x002F76A8`: `sh $s4,0xB2($s3)` writes the authored UID into live `+0xB2`;
- `0x002F76C8`: `sh $s1,0xB4($s3)` writes the authored `+0x14` value into live `+0xB4`.

Thus the mapping is **CONFIRMED retail dataflow**. The loader stores the low 16 bits into the live halfword fields; all observed class-500 UID/reward values used here fit that representation. Separately, live-Moby initialization at `0x0030608C` writes the native class ID to live `+0xAA`.

## Loaded runtime authority: one crate-family update routine per level

The old fixed-address boot-ELF route was wrong for gameplay because the level overlay replaces those regions. The correct route is `lvl.vtbl` -> loaded `.text`.

Exact-authority vtable probes confirm that authored members of the crate family share one update routine inside each loaded level:

| loaded level | authored family members checked | shared loaded update |
|---|---|---:|
| LEVEL1 / Oozla | 500, 501, 511 | `0x00391080` |
| LEVEL2 / Maktar | 500, 501, 505, 511 | `0x003A52B8` |
| LEVEL8 / Endako | 500, 501, 511, 512 | `0x003AB1E8` |

The changing address is expected: each level supplies its own executable overlay. Do **not** treat `0x00391080` as a universal GC crate address.

The Oozla copy is a **CONFIRMED seven-state Moby state machine**:

- Moby `+0x20` is used as the state byte and dispatched through seven cases (`0..6`);
- Moby `+0x68` supplies the PVar pointer;
- the routine explicitly tests class IDs `500/501/502/505/511/512`;
- class-specific transitions/timers and linked-Moby state are handled inside the shared routine.

The exact gameplay names of states `0..6` remain **UNKNOWN**.

### Class-500 break predicate

The common update calls loaded `0x00312B68(moby, 0x05830001, 0)` before dispatching the state. That helper resolves an event record owned by the Moby and returns it only when `(event.flags & 0x05830001) != 0`. The caller then explicitly discards the exact flag word `0x01000000`.

In state 1, the normal class-500 path performs one further test: `0.0 < event[+0x2C]`. If that comparison succeeds it immediately calls `0x003922B0` and then `0x00392158` in the same update. There is no class-500 HP decrement or multi-hit threshold in this state. Therefore the native break predicate is **CONFIRMED** as a qualifying owned event with a positive `+0x2C` scalar; the exact public semantic name of the event structure/scalar is still UNKNOWN.

`0x003922B0` later writes Moby state `3` and timer byte `15`. On the next state-3 update, normal class 500 follows authored PVar `+0xC8`; all 190 Oozla Bolt Crates have `+0xC8 == 0`, which routes directly to loaded `0x00306250`. That helper moves the Moby into native sentinel state `0xFD` or `0xFE` and runs its deactivation path. Thus Oozla's normal Bolt Crate is **CONFIRMED to leave active gameplay immediately after the short break transition**, although the exact debris/effect presentation is not yet reconstructed.

## Runtime PVar field use

Loaded retail code upgrades several formerly structural fields into behaviour-backed facts:

| PVar offset | retail runtime evidence | status |
|---:|---|---|
| `+0xA0/+0xA4` | linked Moby pointers are read, copied, released and traversed by the crate-family helpers | **CONFIRMED runtime links** |
| `+0xAC` | flag word manipulated during transitions | **CONFIRMED runtime state** |
| `+0xB4` | counter/timing-like word incremented/read in the state machine | **CONFIRMED runtime state**, exact meaning UNKNOWN |
| `+0xC6` | runtime halfword read by helpers; class 511 initializes it to `100` | **CONFIRMED runtime field**, exact meaning UNKNOWN |
| `+0xC8` | authored `0/1` field read by loaded native crate code | **CONFIRMED live authored input**, exact meaning UNKNOWN |
| `+0xCA` | written to `1` on one class-505 transition path | **CONFIRMED mutable runtime flag**, exact meaning UNKNOWN |
| `+0xCB` | read by the resource-emission helper and mapped to candidate type values | **CONFIRMED runtime selector input**, exact meaning UNKNOWN |
| `+0xCC` | authored `0/1` field read by the loaded Maktar crate update during state initialization | **CONFIRMED live authored input**, exact meaning UNKNOWN |

Thus the arena-correlated `+0xC8/+0xCC` bytes are no longer merely statistical curiosities: both are consumed by native runtime code. Their precise challenge/arena semantics are still UNKNOWN.

## Reward paths: Bolt Crates are separate from indexed resources

Loaded Oozla code now proves that `0x00392158` contains **two distinct reward paths**. Class 500's normal Bolt Crate path does **not** construct class 3291 when PVar `+0xC6 == 0`.

### Class-500 Bolt reward path

For the class-500 branch, loaded `0x00392158` computes a small integer from a progression-like global plus `0x003104E8(2)`, then calls:

```text
0x00392158
  -> 0x0030F920(crate, 0x100, pieceBudget)
  -> 0x003177C8(..., pieceBudget)
  -> 0x00378270(..., denomination)
  -> creates one denomination-specific runtime pickup object
```

`0x003104E8(n)` is **CONFIRMED** to be an integer modulo-RNG helper: it obtains the native RNG value, masks it to 15 bits, divides by `n`, and returns the remainder. Therefore the call with `2` returns exactly `0` or `1`.

The progression-like input at `gp + 0x3AC` is still semantically **UNKNOWN**, but its effect on the third argument is **CONFIRMED**:

| progression-like input | base | `pieceBudget` after RNG(2) |
|---:|---:|---:|
| `< 11` | 2 | 2 or 3 |
| `11..20` | 1 | 1 or 2 |
| `>= 21` | 0 | 0 or 1 |

The important correction is what this value controls. `0x003177C8` first chooses the reward value, then — when the class-500 path's `0x8` flag is set — uses `pieceBudget` to cap how many **physical denomination pickup objects** it emits. It greedily partitions the selected value using denominations:

`1000, 500, 100, 50, 20, 5, 1`.

For each denomination it emits at most the remaining physical-piece budget. Any value left after that budget is exhausted is added to `0x001A7A14`. A later reward event drains `ceil(pool / 50)` from that global, adds the drained amount into its new reward magnitude, and stores `pool - drained` back. Therefore `0x001A7A14` is **CONFIRMED as a deferred-reward reservoir**; it is not an immediate direct-credit path.

Thus the small progression/RNG parameter is **CONFIRMED to be a physical pickup-count budget, not the Bolt reward amount**. The reason later progression reduces the number of physical pieces is still UNKNOWN.

`0x00378270` is the denomination-object constructor used by this path. Its denomination input selects seven runtime object resources/classes through a generic object allocator:

| denomination | allocator selector |
|---:|---:|
| 1 | 13 |
| 5 | 14 |
| 20 | 15 |
| 50 | 16 |
| 100 | 17 |
| 500 | 18 |
| 1000 | 19 |

The selector/value relationship and per-object initialization are **CONFIRMED retail dataflow**. Interpreting 13..19 specifically as Bolt-denomination pickup identities is **INFERRED with strong native support** from their exclusive denomination mapping in this reward path.

#### Base reward scaling

Loaded helper `0x002D9528` establishes the normal reward scaler. Retail code reads a packed 4-bit selector from `0x0019B4A8 + contextIndex*0x400 + floor(uid/2)` (low/high nibble chosen by UID parity). Selector bit 3 chooses one of two 8-byte percentage banks; the low three bits choose the percentage within that bank. The helper returns the integer quotient:

`scaledValue = percentage[selector & 7] * mobyRewardValue / 100`.

Retail independently proves live Moby `+0xAA` is `oClass`. The scaler consumes the nearby live fields `+0xB2` as its UID selector input and `+0xB4` as the reward-value input. The static-to-live population path is now traced directly: authored `+0x10` (`uid`) is copied to live `+0xB2`, and authored `+0x14` (`bolts`) is copied to live `+0xB4`. Therefore the scaler's inputs are **CONFIRMED** to be the authored UID and Bolt reward value.

The authored-value percentage banks are loaded `.lit` bytes at `0x001A89C8` and `0x001A89D0`: `[100,50,40,30,25,20,15,10]` and eight `100` values. The earlier emission branch uses the adjacent banks at `0x001A89D8` and `0x001A89E0`: `[100,50,40,30,25,20,15,10]` and `[100,30,10,10,10,10,10,10]`. All 27 retail level overlays contain the same 32 bytes. Oozla callers at `0x0030FD64/0x0030FD74` select the authored pair before calling `0x002D9528`; `0x0030FB48/0x0030FB64` select the emission pair.

This corrects an earlier MIPS address-decoding error. `addiu` sign-extends its 16-bit immediate, so `lui 0x1A; addiu -0x4B58` resolves to `0x0019B4A8`, not `0x001AB4A8`, and `lui 0x1B; addiu -0x7638` resolves to `0x001A89C8`, not `0x001B89C8`. The real selector region is zero-filled in the retail executable image and is mutated at runtime. The previous `144/190` Oozla selector census sampled unrelated bytes and is discarded. Exact runtime selector-state initialization and context identity remain **UNKNOWN**.

A further local layer check closes a misleading boot-code lead: when Oozla is loaded, virtual address `0x0029B3E0` lies inside the level overlay `.data`, while `0x002DF3F8` is overwritten by the level overlay `.text`. Boot-image routines at those addresses therefore cannot be treated as live in-level selector initializers. OBP's current fresh-showcase path deliberately chooses selector `0` because it is a native-valid encoding backed by the zero-filled executable image; this is an **OBP harness initial condition**, not a claim about selector state for arbitrary retail saves or revisit history.

The scaled value is additionally multiplied by `max(1, byte[0x001A7A32])` in the normal path. Retail code advances that byte up to a maximum of 20 using a subordinate counter at `0x001A7A33`. The multiplier mechanics are **CONFIRMED**; interpreting these two bytes as GC's Challenge Mode bolt multiplier/sublevel is **INFERRED with strong behavioural support** until a retail symbol or independent GC structure names them.

After the deferred-reservoir release is added, `0x0030F920` converts the resulting magnitude into an integer centre and chooses a symmetric random range: values below 2 have zero spread; values from 2 to under 8 use spread 1; values at least 8 use approximately 25% spread (converted with the native float-to-int helper). `0x003177C8` then selects inclusively from `[centre-spread, centre+spread]` using the native modulo RNG. The control flow is **CONFIRMED**; exact host reproduction of the PS2 FPU conversion mode is intentionally not asserted yet.

### Generic indexed-resource branch

When the Bolt-specific condition does not apply, `0x00392158` follows the separate indexed-resource path:

```text
crate-family loaded state/transition path
  -> 0x003922B0   transition/effect/link helper (semantic name INFERRED)
  -> 0x00392158   indexed-resource branch
  -> 0x0030F570   choose an indexed resource with remaining capacity
  -> 0x003D3E28   dynamic resource-object constructor
  -> creates oClass 3291
  -> installs update 0x003D37A0
  -> class-3291 update
  -> 0x002C9738(index, amount)
  -> bounded indexed counter increment
```

This branch reads PVar `+0xCB`, maps candidate resource values, and repeatedly calls `0x003D3E28`. Calling it an indexed-resource emission/drop branch is **INFERRED** from the complete dataflow. Class 3291 is **not** the class-500 Bolt pickup path.

### `0x003922B0`

**CONFIRMED structure:** this routine manipulates linked Mobies, PVar flags/state, effects and Moby state, and has class-specific paths for the crate family. It sets one transition state and participates immediately before reward emission.

A destruction/break-transition role is **INFERRED**, not yet promoted to CONFIRMED because its incoming hit/event semantics are not fully named.

## Runtime-only class 3291

The static census and loaded code agree:

- class `3291` is listed in every retail level's Moby class table;
- it has zero authored static instances and zero authored static PVars;
- Oozla `lvl.vtbl` resolves its loaded update to `0x003D37A0`;
- loaded function `0x003D3E28` dynamically creates oClass 3291 through the runtime Moby allocator;
- constructor PVar `+0x60` carries an **amount** and `+0x68` carries an **index** into the indexed count system;
- the class-3291 update later passes those same values to `0x002C9738`.

The collection path is now more specific. Immediately before the add/clamp call, the update reads the selected count from `0x00139688 + index*4`. It calls `0x002C9738(index, amount)`, reads that same slot again, and produces collection feedback only if the value increased. The feedback path receives the actual `after-before` delta.

This is **CONFIRMED** dataflow. A generic indexed collectible/resource interpretation remains **INFERRED with strong native support**; do not universally label class 3291 as a bolt pickup.

## 56-slot indexed resource system

Exact-authority Oozla disassembly at `0x002C96B0..0x002C981C` and `0x0030F570..` establishes the table structure used by class 3291 and crate resource selection:

- current 32-bit counts begin at `0x00139688`, indexed by `index * 4`;
- a parallel byte mapping begins at `0x00139568`;
- each mapping byte selects a descriptor record of size `0xE0` at base `0x002637A0`;
- descriptor halfword `+0x8E` is the per-entry maximum/capacity used by add/subtract/select logic;
- the selector scans **56 indices (`0..55`)**;
- another per-index byte table at `0x001A7AF8` gates candidates;
- the selector only counts/selects enabled entries whose current count is below the mapped maximum;
- effective exclusions visible in the 0..55 scan include indices `31` and `45` (the code also compares against `61`/`77`, which are outside this loop's range).

`0x002C9738` adds an amount to a selected count and clamps to the mapped descriptor maximum when nonzero. Nearby loaded code implements the complementary bounded subtraction/get paths.

Pinned Wrench `data/memcard/types_gc.h` independently describes `Ammo[56]` alongside 56-entry gadget/item arrays. The exact 56-entry match is **CORROBORATION** for an ammo/inventory-resource interpretation of this runtime table, not a retail symbol name. The exact slot-to-weapon/resource mapping is still UNKNOWN.

### Correction: `index = -1`

Earlier archaeology considered `index=-1` a possible bolt sentinel because it appeared adjacent to the indexed count table. The loaded helper bodies disprove that simple interpretation.

Multiple count helpers explicitly test `index == -1` and replace it with the runtime value stored at `0x0019B068` before indexing the tables. Therefore:

- **CONFIRMED:** `-1` means “use a runtime-selected/current index” in these helpers;
- **NOT CONFIRMED:** `-1 = bolts`;
- any Bolt Crate reward identity/amount must be proved from the crate emission helper's actual constructor arguments or a separate bolt-specific path.

This correction supersedes all earlier adjacency-based `-1 = bolts` speculation.

## Resource selection service

Loaded `0x0030F570` scans the 56-slot system described above, chooses among enabled entries with remaining capacity, and writes a selected index to the caller-provided output. It also uses loaded RNG helper `0x003104E8` while choosing among candidates.

The selection mechanics are **CONFIRMED**. Calling the table an ammo table is **CORROBORATED/INFERRED**, while individual resource names and the crate-family rules that feed this selector remain UNKNOWN.

## Critical boot-ELF correction retained

Earlier archaeology followed fixed addresses in `SCUS_972.68`. That was not runtime authority because the selected level's custom executable overlay overwrites non-core `.lit`, `.bss`, `.data`, `lvl.vtbl`, `lvl.camvtbl`, `lvl.sndvtbl` and `.text` regions.

The following old boot-image interpretations remain explicitly superseded and must not be used as implementation specifications:

- `0x003120C8` as a crate destruction transition;
- `0x00311F70` as a crate reward/drop helper;
- `0x003202E8` as the runtime class-3291 constructor;
- `0x0031FC60` as the runtime class-3291 update;
- `0x0026F740` as the runtime resource service.

The loaded addresses documented above replace those stale runtime claims.

## Exact-authority provenance

All runs below verified the full retail ISO SHA-256 in the same job:

- pipeline `2827600082`, job `16354081451` — Oozla crate-family full loaded update body;
- pipeline `2827602355`, job `16354095087` — transition/emission/3291 loaded helper slices;
- pipeline `2827606692`, job `16354123617` — complete `0x003918D0` callback + class-3291 constructor bridge;
- pipeline `2827607142`, job `16354126201` — all-level census including dormant class 502;
- pipeline `2827609920`, job `16354148745` — Maktar/Endako family vtable confirmation and `+0xCC` read;
- pipeline `2827610817`, job `16354155119` — indexed resource add/clamp service and resource selector;
- pipeline `2827647584`, job `16354385800` — post-main-sync bounded Oozla count/selector/class-3291 disassembly, including the 56-slot structure and `-1` correction.

On 2026-09-08, the static-to-live Moby bridge was re-derived locally on Jess-Laptop from the same exact authority build using the bounded level-overlay tooling. The retail bytes remain local; only instruction addresses, field relationships and derived facts are recorded here.

## Local deterministic interaction proof

The Godot development harness can focus the first preserved class-500 object and feed it the known-good debug event (`flags=0x00000001`, scalar `1.0`). Target acquisition is explicitly a host/debug convenience; the break decision itself goes through `GcCrateInteraction.ShouldBreakClass500`.

On Jess-Laptop, the original paired LEVEL1 captures at frame 120 target `level:1:moby:31` / UID `73`:

- `tools/capture.ps1 -CrateFocus ...` records `Visible=true`, `Broken=false`;
- `tools/capture.ps1 -CrateFocus -CrateAutoStrike ...` records `PvarC8=0`, `Route=Deactivate`, `Visible=false`, `Broken=true`.

A later deterministic fresh-showcase capture extends that path through reward planning and collection. For UID `73`, authored `bolts=13`, explicit selector `0`, reward-multiplier byte `0` (native `max(1, byte)` therefore gives effective multiplier `1`), progression-like input `0`, and native-valid RNG-domain choice `1`, RAC2 gameplay uses the recovered reward centre `13` as the deterministic showcase value and partitions it as `5+5+1 physical, 2 deferred`. The Godot host renders those three physical denominations as deliberately non-native gold placeholder orbs; the scripted debug player walks through their `Area3D` triggers and the RAC2 session records `11` collected, `0` outstanding, `2` deferred. The capture harness now evaluates metadata after frame settling, so its gameplay state matches the photographed frame.

This proves a complete development loop from preserved retail crate identity through the recovered class-500 break predicate, authored reward value, percentage/denomination/deferred rules, physical host pickups and collection accounting. The gold-orb appearance, generous trigger radius, neutral reward-multiplier byte, deterministic progression/RNG inputs, reward-centre choice and selector-zero initialization are **OBP showcase choices**. Native Bolt pickup model/scatter/magnet presentation, exact PS2 RNG sequence, arbitrary-save selector context/history, and real game wallet/save integration remain unreconstructed.

## What is proved now

**CONFIRMED:** authored crate-family PVar structure; class-500 population; per-level shared crate-family update vtables; seven-state loaded state machine; class-500 event mask/exclusion/positive-scalar break predicate; class-500 break transition to state 3 and Oozla `+0xC8==0` deactivation path; runtime consumption of authored `+0xC8/+0xCC`; runtime `+0xC6/+0xCA/+0xCB` use; live `Moby+0xAA = oClass`; static Moby `+0x10 -> live +0xB2` UID mapping; static Moby `+0x14 -> live +0xB4` Bolt-reward mapping; class-500's separate `0x0030F920 -> 0x003177C8` reward path; percentage-scaling helper `0x002D9528`; four overlay-backed reward percentage banks at `0x001A89C8..0x001A89E7`; packed selector addressing at `0x0019B4A8 + contextIndex*0x400 + floor(uid/2)` and UID-parity nibble selection; zero-filled selector bytes in the retail executable image; deferred reservoir add/release at `0x001A7A14`; modulo RNG at `0x003104E8`; progression-tier effect on the class-500 physical-piece budget; denomination decomposition `1000/500/100/50/20/5/1`; denomination-to-selector mapping `19..13`; dynamic class-3291 creation on the separate indexed-resource branch; class-3291 amount/index PVar fields; 56-slot indexed count structure; add/subtract/capacity mechanics; class-3291 successful-collection delta check; `-1` runtime-index substitution; class-502 authored absence.

**CORROBORATED:** `oClass 500 = Bolt Crate`; public `bolts` naming for the now retail-proved `+0x14` reward field; public TargetVars structural lead; public `Ammo[56]` matches the runtime selector's 56-slot domain.

**INFERRED:** `0x003922B0` is a break/destruction transition helper; selectors `13..19` are Bolt-denomination pickup identities; `0x001A7A32/+0x33` are the Challenge Mode bolt multiplier/sublevel; class 3291 is a generic indexed collectible/resource object; the 56-slot counter system is ammo/inventory-resource state.

Still **UNKNOWN:** exact names of states `0..6`; public semantic name of the native break-event structure and its `+0x2C` scalar; exact `+0xCC/+0xC6/+0xCA/+0xCB` semantics; broader meaning of `+0xC8` outside the proved Oozla deactivation branch; semantic identity of progression-like `gp+0x3AC`; exact runtime population/history of the packed reward-selector blocks and semantic identity of the active `contextIndex`; exact 56-slot resource names; debris/effect presentation; save/reload/persistence/respawn rules; exact GC identities of classes 501/505/511/512.
