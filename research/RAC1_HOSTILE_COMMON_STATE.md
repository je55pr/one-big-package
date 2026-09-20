# R&C1 common hostile / Moby state archaeology

Authority: retail NTSC-U original, `SCUS-97199`; ISO SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`; executable SHA-256 `e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9`.

This note separates mechanisms that are demonstrably common to the R&C1 Moby engine from behavior that belongs to one loaded class script. The representative hostile remains class `749`; Veldin class `500` crates and class `1781` red plants are deliberately used as non-hostile controls.

## Common live-Moby mechanisms

The fixed Veldin witness uses the already-recovered live pool at `0x01845e80` with a `0x100`-byte stride. Cross-class runtime evidence now pins these fields as common live-Moby state:

- `Moby+0x20 u8`: native state byte. Class 500, class 749 and class 1781 all dispatch or expose their native state there.
- `Moby+0x74 ptr`: class update routine. All 103 class-500 Mobies point to `0x002cf218`; all 16 class-749 Mobies point to `0x002d4610`; all 33 class-1781 Mobies point to `0x002e3ca8`. Those routines independently match the existing crate, hostile and red-plant archaeology.
- `Moby+0xa4 u8`: native damage-record slot. `0xff` means no queued record.
- `Moby+0xa6 u16`: native class id, already independently proven by the Veldin population path.

Do **not** promote `Moby+0x78` as a universal PVar field. It is a valid PVar pointer for all fixed-witness class-500 and class-749 objects, but every class-1781 object leaves `+0x78` null. The safe contract is class-bound: a class script may own `+0x78`, and class 749 demonstrably does.

The fixed witness contains 103 class-500 objects, 16 class-749 objects and 33 class-1781 objects. Its state census is retained in `research/generated/rac1-hostile-common-state.json`; notably class 749 has two terminal `0xfd` objects with PVar health `0.0`, while its fourteen non-terminal objects have health `1.0`.

## Common damage queue, class-specific health

The engine damage-record path is shared; hit points are not.

The compact constructor at `0x00259bc8` reads the victim's `Moby+0xa4` slot, addresses the 64-entry / `0x40`-byte record pool, writes native damage at record `+0x2c`, writes the victim Moby pointer at record `+0x34`, and assigns the chosen slot back to victim `Moby+0xa4`. This is the same damage-record family already retained by wrench archaeology.

Loaded Veldin has 11 direct callers of lookup routine `0x0025a420` and three callers of consumer `0x0025a478`. Both the class-500 update and the class-749 update use the pair:

| Caller | lookup `0x25a420` | consumer `0x25a478` |
|---|---:|---:|
| class 500 update | `0x002cf2ec` | `0x002cf340` |
| class 749 pre-dispatch | `0x002d51d8` | `0x002d5200` |

Class 749 then performs its own consequence. It passes PVar `+0x20` into the common consumer, loads that field as an `f32`, subtracts the returned native damage, stores the result back to PVar `+0x20`, and writes native state `12` to `Moby+0x20`. It then clears `Moby+0xa4` to `0xff`. The health scalar and state-12 hit/death reaction are therefore **class-749 script semantics**, not a generic Moby-health contract.

This distinction matters for reuse: a future hostile can reuse the engine damage-record transport only after its own consequence path is recovered. It must not inherit class 749's PVar `+0x20` health merely because it consumes the same record format.

## Common terminalizer

Loaded routine `0x0024eb68` is a common Moby terminalization helper. It selects `0xfd` or `0xfe` from a pool-side pointer comparison, writes that value to `Moby+0x20`, then enters common cleanup via `0x00250b20`.

The loaded Veldin image contains 70 calls to `0x0024eb68`. Two independent gameplay paths prove it is not class-749-specific:

- class 500 calls it from `0x002cf820`;
- class 749 state 12 calls it from `0x002d5060`.

This promotes native states `0xfd` / `0xfe` as engine-owned terminal Moby states and makes the existing neutral projection to inactive presentation defensible. It does **not** prove immediate memory deallocation at the call, so “terminalize / enter inactive lifetime” is the supported wording rather than “free the object now.”

## Class-749 target and locomotion script

Class 749's loaded update is `0x002d4610`. Its `Moby+0x20` dispatch table covers native states 0 through 12; the states already exposed by `Rac1Class749Hostile` map directly to loaded entries:

- state `5` -> `0x002d4a9c`: search/roam. It calls `0x00261630` with PVar `+0x240` and reads PVar `+0x1c4`.
- state `6` -> `0x002d4b88`: pursue target. It calls class-local helper `0x002d54c8` with destination PVar `+0x180`, then applies the recovered attack-range and facing tests.
- state `7` -> `0x002d4c74`: attack. Native marker `34.0` reaches the generic damage-record path through `0x002599e8`.
- state `8` -> `0x002d4da0`: return home. It reuses `0x002d54c8`, this time with PVar `+0x1d0` home position, and the recovered strict `1.5` home-distance boundary.
- state `12` -> `0x002d501c`: hit/death reaction. Its completion path reaches common terminalizer `0x0024eb68`.

The loaded image has only one call to state-5 helper `0x00261630`, from class 749, and five calls to `0x002d54c8`, all within the class-749 overlay. These are therefore useful **class-local locomotion primitives**, not evidence for universal hostile movement constants.

### Target acquisition

PVar `+0x1c0` is the class-749 target-Moby pointer. In the pre-dispatch path, a null `+0x1c0` loads global `0x001413d0` and stores it into that field. In the fixed Veldin witness that global contains `0x01845e80`, the live class-0 Ratchet Moby. Eleven of sixteen class-749 PVars already hold that exact player pointer and the other five are null; no class-749 PVar points at another Moby.

State 6 dereferences the target Moby for facing while pursuing PVar `+0x180`. This closes the target identity without turning the surrounding script into a generic engine target-acquisition service.

### Activation/range boundary

PVar `+0x1c4` gates multiple class-749 transitions. State 5 remains in its search/roam state when the field equals `2`, and the already-frozen state machine uses changes away from that sentinel to enter its targeted state. The loaded main update reads `+0x1c4` eight times but contains no direct store to that offset.

The fixed live witness also rules out interpreting `+0x1c4` as a direct player-distance threshold. Excluding terminal Mobies, status `0` occurs from about `84.16..139.89` world units from Ratchet while status `2` occurs from about `31.27..122.31`; the ranges overlap substantially. Distance may still participate upstream, but this field is not itself a simple “inside radius” result.

Accordingly, the **consumer** is recovered but the writer and semantic name are not. No universal “aggro radius” is promoted. The attack range (`< 2.0`), attack-retention boundary (`<= 1.5`), facing threshold, and return-home range (`< 1.5`) remain class-749 behavior. A future hostile class must independently match these structures before sharing them.

## Reusable boundary

The implementation boundary is now:

- **engine/common:** `0x100` live-Moby stride; state byte `+0x20`; per-class update dispatch `+0x74`; damage slot `+0xa4`; class id `+0xa6`; damage-record construction/lookup/consumption; common `0xfd/0xfe` terminalizer;
- **class-bound field usage:** live `+0x78` when a specific class proves it as its PVar pointer;
- **class 749 only:** PVar health `+0x20`, target destination `+0x180`, target Moby `+0x1c0`, unresolved status `+0x1c4`, home `+0x1d0`, state meanings 5/6/7/8/12, locomotion helpers and all range/facing/animation timing;
- **still evidence-gated:** the writer/meaning of class-749 PVar `+0x1c4`, any universal activation radius, and whether another hostile shares class 749's state/PVar layout.

This is intentionally narrower than a generic “enemy component.” The common layer is Moby scheduling, damage transport and lifetime; hostile policy remains script-owned until a second hostile independently proves a shared family.

## Reproduction

Run:

`py -3.12 tools/rac1-hostile-common-probe.py --savestate "<authorized SCUS-97199 Veldin savestate>" --zstd-dll "<zstd.dll>" --out research/generated/rac1-hostile-common-state.json`

The probe re-verifies loaded instruction words, the class-749 0..12 dispatch table, cross-class update pointers and populations, class-749 health/target invariants, shared damage helper call sites, common terminalizer call sites, and the absence of a direct `+0x1c4` store in the class-749 main update. The generated JSON contains hashes, addresses, words, counts and derived values only; no retail payload is committed.
