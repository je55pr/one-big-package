# R&C1 native Moby updater atlas

Status: reproducible, offline, payload-free discovery for the pinned
`rac1-ntscu-original` / `SCUS-97199` retail disc. This report and the
corresponding JSON catalogue are **registration evidence**, not automatic
recovery of gameplay semantics.

## What is now known

Every one of the 19 native level overlays has a directly readable registration
table in its fourth serialized section. It contains 12-byte rows:

| Row field | Meaning |
|---|---|
| `+0x00 u32` | Native Moby class ID; cross-checked against that level's authored class table |
| `+0x04 u32` | Mapped native per-class update routine address, or zero |
| `+0x08 u32` | A third pointer/word, **preserved without assigning a callback meaning** |

The table terminates with class `0xffffffff` and a zero update pointer.
The remainder of the same serialized section contains other native pointers/data
and **must not** be interpreted as additional class rows.

This structure was found from retail byte correlations with independently known
update functions (classes 500, 749, 1781, 750 and 774), then validated
across the full disc and against the previously frozen authored Moby census.

| Metric | Whole-game result |
|---|---:|
| Class registration rows (per-level occurrences) | 3,077 |
| Rows with a mapped nonzero updater | 3,037 |
| Registered rows with zero updater | 40 |
| Authored class-table rows | 3,641 |
| Authored rows absent from registration tables | 564 |
| Authored placed Moby instances | 16,232 |
| Instances with a class registration | 15,116 |
| Instances without a class registration | 1,116 |

Therefore **93.1% of authored Moby placements are associated with a native
class registration**. This does not imply their behaviour is already decoded
or that unregistered classes are inert: they may use other engine mechanisms.

## 22 October 2026: common-class identities resolved

The first atlas deliberately recorded updater addresses without assigning semantics.
The four most widespread candidates have since been traced against the original
retail PVars, native code and matching NTSC-U decompilation. **505 = explosive
crate, 605 = buried Metal Detector target, 805 = checkpoint marker, 1060 =
pulsing-colour effect.** See [Common Moby behaviours](RAC1_COMMON_MOBY_BEHAVIOURS.md)
for the retail-backed contracts and the independent level-2 checkpoint writer
correlation. The following is retained as the initial discovery list.

## Valuable cross-level candidates

These are prioritization *leads*, not confirmed semantics. Instance numbers
are summed from the independently recovered authored census.

| Candidate | Authored placements | Levels | Why investigate |
|---|---:|---:|---|
| Class 505 | 608 | 18 | Shares one update routine with classes 500/501/502/511 wherever registered; its code contains explicit class-505 branches. Cannot simply turn it into an ordinary Bolt Crate. |
| Class 605 | 358 | 18 | Large repeated population; identify the update semantics and rendering/lifetime roles. |
| Class 1060 | 334 | 4 | Dense regional population; determine whether one source routine can activate an entire family. |
| Class 805 | 97 | 18 | Smaller but nearly ubiquitous; investigate a potential recurring interaction. |
| Class 750 | 8 | several | Class-owned destination admission is independently proven; completing its interaction could unblock progression across multiple levels despite the small population. |

High-frequency classes 500, 13, 511, 502 and 501 already have substantial
crate/pickup implementations and should not automatically outrank the above.

**Class 505 deserves special caution.** The original LEVEL1 routine at
`0x002ea178` handles the shared crate family but branches specifically on
class `0x1f9` at `0x002ea38c`, `0x002ea42c`, `0x002ea598`, and
`0x002ea630`. The class-specific conditions, state effects, reward policy and
damage consequences need separate analysis before adding it to
`Rac1BoltCrate.CrateClasses`.

## First end-to-end gameplay use

The existing data path had already recovered Veldin class 1781 (red plant):
33 authored models, native sequence 1's 20 frames, a 30-FPS playback rate,
and a selector requiring player distance **strictly below 1.0 world unit** plus
motion **strictly above 1/30 unit per 60-Hz update**. A recovered native
animation-completion flag returns the plant to rest.

`Rac1RedPlantSession` now owns this bounded, class-specific activation and
one-shot lifetime, and the Godot gameplay host wires it to existing imported
objects and their source-backed animated mesh. The host supplies actual player
distance/displacement and infers completion from the 40-update source duration;
it does **not** claim to emulate the original live Moby animation-flags writer.
No new animation art, universal proximity triggers or speculative classes were
introduced. Portable and opt-in retail tests exercise the contract.

## Reproduce

Use the same authorized retail ISO as the OBP source manager:

```powershell
python tools/rac1-moby-updater-atlas.py --iso $env:OBP_RAC1_ISO --check
python -m unittest discover -s tests/tooling -p test_rac1_moby_updater_atlas.py
dotnet test tests/OBP.Tests/OBP.Tests.csproj -c Release --filter FullyQualifiedName~Rac1RedPlantSessionTests
```

The Python script verifies the full ISO SHA-256, independently bounds each
overlay, validates the registration table against the authored class census,
checks that each nonzero updater points inside mapped executable bytes,
and produces `research/generated/rac1-moby-updater-atlas.json`.

The report contains only class IDs, addresses, counts, hashed overlay
identities, third-word values, and relationships. It contains no executable
instructions, native game models, PVar bytes, ISO payloads or user-specific
paths. `--skip-sha` is for local development only and must not be used to claim
the pinned retail authority was verified.

## Follow-up

1. Class 505: recover the native state-specific damage/activation path and
   effects from its explicit branches, then implement and test it across several
   worlds using the existing crate/damage/bolt/audio systems.
2. Class 750: follow the registered update's 12-state dispatch to its exact
   player interaction and destination-admission conditions; integrate with
   existing `Rac1CampaignRuntimeSession`.
3. Add verified class-specific PVar schemas and input/output dependencies to
   this atlas as each routine is decoded. Keep unproven fields opaque.
4. Only generalize a dispatcher, event bus, helper or interaction policy when
   two independent native routines demonstrate that it is truly shared.
