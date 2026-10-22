# Frequently placed R&C1 enemy families

**Authority:** the local original US R&C1 `SCUS-97199` disc, all 19 native
gameplay authored-instance tables, matched overlay updater registrations
and the original NTSC-U update code. External cross-check: Lombyte
`lombyte-project/lombyte` at reference commit `1e40bcb`. No external
game source was copied into OBP; all counts, PVars and model/animation
availability were independently rechecked using the authorised ISO.

## Ranked families

| Family | Native class | Retail placements | Levels | Per-instance PVar | Native update function |
|---|---:|---:|---|---:|---|
| Amoeboid, large | 572 (`0x23c`) | 30 | 1:5, 5:13, 11:12 | 688 bytes | `FUN_L01_002edca0` |
| Amoeboid, medium | 865 (`0x361`) | 63 | 1:10, 5:29, 11:24 | 688 bytes | same |
| Amoeboid, small | 866 (`0x362`) | 134 | 1:20, 5:66, 11:48 | 688 bytes | same |
| **Amoeboids total** | **3 classes** | **227** | **3 levels** | Same family | `FUN_L01_002eec68` damage, `FUN_L01_002ef350` spawn |
| Frogs | 827 (`0x33b`) | **193** | 6:169, 10:24 | 656 bytes | `FUN_L06_002e8678` |
| Seekers | 193 (`0x0c1`) | **170** | 9:81, 15:89 | 624 bytes | `FUN_L09_002c5990` |
| Scoutbots | 252 (`0x0fc`) | **148** | 8:96, 14:52 | 624 bytes | `FUN_L08_002d2af0` |
| Puffoids | 1246 (`0x4de`) | **126** | 11:126 | 704 bytes | `FUN_L11_00314318` |

The original class-1181 census shows 124 visually represented placements
on level 9 with a 112-byte PVar and only one animated sequence. Its status
as a hostile enemy was not proven, so **do not count 1181 as a verified
enemy family**.

For these six enemy families (counting the Amoeboid variants separately),
the source material includes visible high-LOD meshes, native skeletons
and several authored animation clips. Availability of models and clips does
not by itself prove gameplay behaviour.

## Major finding: Amoeboids are not 227 initially active enemies

The three class IDs are variants of the **same update routine**. When the
original large Amoeboid is damaged, its hit routine can find a free
medium-variant reserve, spawn two medium creatures and retire the parent;
medium variants can similarly release smaller variants. The source
`FUN_L01_002ef350` selects an originally **state-12** dormant Moby of the
next class from its native group, then copies spawn position, facing, scale,
persistence and other state from the parent.

Retail original PVar facts from all 227 placements:

- All **30 large** sources have `+0x230 = 1`, so the native initialization
  enters its active state 1 (rather than the spawn-reserve branch).
- All **63 medium and 134 small** sources have `+0x230 = 0`, but
  an additional `+0x258` source override **forces 7 small sources active**
  even when their ordinary spawn flag is zero. Of the remaining 190,
  **186** have `+0x25c = -1` and enter the dormant source reserve
  (native state 12); the other **four** use native clip volume **44**
  and start in gated state 13. Overall: **37 active on load**, **4 gated**
  and **186 dormant**, across all 227 original placements.
- The dormant pool must **not** be treated as 186 normal enemies or
  animated/solid stationary monsters. This makes a naive general-purpose
  enemy registration especially damaging to retail fidelity.
- Authored `+0x240` and `+0x244` are path-following inputs and vary
  across level encounters. The source's path/target search and
  group-specific pool admission must precede a trustworthy full
  implementation.

Next development work: reconstruct native group membership and pool
selection, then integrate all three variants' dormant, active, split and
return-to-path states. This has greater class-coverage leverage than
simply activating class 866.

## Frogs: largest single missing enemy class

The shared `frog_update` handles **193** class-827 placements on two
levels. The original PVar carries:

- `+0x1f8 s32`: authored path index. **167** sources use `-1`
  (no source path); **26** use an indexed authored path.
- Distinct initial possession, lives/respawn and mode selectors in
  `+0x200/+0x202` and `+0x240/+0x242`. Treat their 16-bit values as
  native selectors, not as one four-byte integer.
- Native state 8 for free roaming, 9 for approach, 10 for alternate
  approach, 11/12 for source attack windup/contact, 13 for retreat,
  and 15/16 for special modes. Their code calls common pathing,
  ground-probe, damage and animation functions.

This is a high-volume **next** target, but the actual campaign also
uses special carried and respawning frogs. Implementing all instances as
identical simple melee enemies would erase those distinctions.

## Seekers: first fully hosted family in this pass

Class-193 Seeker has the most self-contained movement among the first
candidates, with a source update shared by **Gaspar (level 9, 81)** and
**Quartu (level 15, 89)**. In both original sources:
`+0x180` path sentinel is `0` and `+0x164` target-mode is `0`.
Their native initializer sets **3 health points**, **7.75 units/sec**
travel speed and animation sequence **1**. The observed transitions:

1. State 0 initializes; state 1 enters chasing state 2.
2. Chasing uses source horizontal movement hysteresis: stop below
   **1.4** native units, resume above **1.9**.
3. Source hover correction follows Ratchet's height at an offset of
   **0.8** units when moving, **0.2** when stopped, with original
   asymmetric upward/downward factors **0.1** and **0.05**.
4. Reaching less than **6** planar units enters state 3 with original
   animation sequence **3**, then animation completion starts state 4
   with sequence **4** and its **180-game-tick** countdown. The original
   state 7 emits an explosion and removes the Seeker.

**Implemented now:** engine-neutral `Rac1SeekerSession` with those
native transitions, source PVar edits, player-wrench/projectile damage
dispatch, and Godot animated/colliding presentation and chase motion.
The source frame rates for sequence **1, 3 and 4** are valid and checked
on both planets. Original clips **2** and **8** contain unsupported
infinite/zero frame-rate fields, so the Godot animation baker rejects
those specific clips rather than constructing fake timing.

**Still incomplete:** exact steering acceleration from the PS2 common
angle integrator, source frame-scale calibration, native player-impact
damage query, knockback/fatal falling, sound and explosion effects, and
the original target/decoy or group-reset interactions. Host movement
sweep and its 42-unit sleeping optimisation are explicit engine
adapters. Death currently terminalizes the Moby without recreating
the original aerial knockback/debris. This is **partial functional
coverage**, not a declaration of full retail fidelity.

Host smoke verification: Gaspar registers **81** animated/colliding
original Seekers and Quartu registers **89**, using the original
high-LOD geometry, source animation skeletons and native placements.

The previous approximate family-exposure estimate of **27/813 class IDs**
and **8,031/16,232 placements** advances to **28/813 (3.4%)** and
**8,201/16,232 (50.5%)** when class 193 is counted. The separate Amoeboid
PVar parser is **research only** and does not add its three classes to
functional coverage. None of these counts measures retail completeness.

## Scoutbots and Puffoids

The recovered Scoutbot state machine has idle/wander, approach,
return-home, an animation-frame-gated projectile attack, and
explosion/removal states. The source's home/target distance and
vertical limits are explicit. It is a good second single-class AI
candidate once source projectile creation and pathing can be routed
through the shared host.

The 126 Puffoids on level 11 share a source updater that supports
path-following, wandering, jumping, moving-carrier relationships,
attack, knockback, and distinct mode values. Because it is one
level, its benefit is mostly area completeness rather than
cross-level reusability.

## Suggested development order

1. **Finish Seekers:** playtest actual chase and damage, implement
   native contact and death collision callbacks. This first
   implementation establishes the cross-level animated enemy pathway
   on two planets.
2. **Amoeboid family:** recover the original grouped spawn-pool
   allocation and correct initial dormant visibility. Three class
   types and 227 placements become available as one system.
3. **Frogs:** 193 placements, then handle special carried/path/
   respawn selectors rather than flattening their behaviour.
4. **Scoutbots:** 148 native patrol/attack actors, with source-guided
   projectile effects.
5. **Puffoids:** 126 level-11 actors, after moving-carrier/path
   infrastructure is available.

This ranking is for *functional gameplay* and source fidelity; merely
drawing those original models would inflate class coverage without
making their gameplay authentic.
