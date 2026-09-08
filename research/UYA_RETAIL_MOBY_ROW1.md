# Retail UYA row-1 Moby compatibility and render subset

This note records the current Moby class/placement compatibility result against the canonical UYA NTSC-U authority and the conservative subset admitted to the neutral retail world.

The source remains the canonical `SCUS-97353` retail image. Public-derived table/core semantics remain provenance hypotheses; the compatibility claims below are about observed retail bytes and unchanged shared readers, not native-loader proof of those semantic names.

## Class compatibility

Applying the unchanged GC Moby reader to retail row 1 produces:

- **227 / 227 unique declared Moby class entries**;
- **76 valid zero-local-core entries** under the pinned public format (`offset_in_asset_wad == 0`);
- **151 / 151 nonzero local-core candidate classes decoded**;
- **71,807 class vertices**;
- **63,820 class triangles**;
- **0 non-finite class positions**;
- **34 skinned decoded classes**, with the current GC bind-pose reconstruction applied to 31;
- deterministic class-geometry SHA-256 `822c3fed6c23eefd1859ea28eaa28b7ba130406d79246ed8212e02d2beef5319`.

Six decoded class objects have empty render geometry: `0, 1, 2, 71, 4677, 4679`. The last three are also the skinned classes for which the current bind-pose reconstruction is not applied. Empty parser output is kept distinct from zero-local-core data and from malformed data.

## Gameplay placement compatibility

The public-derived gameplay Moby block strongly matches the existing GC instance representation:

- **735 declared instances**;
- **735 decoded instances**;
- all complete `0x88` records accepted by the independent prerequisite census;
- no non-finite parsed scale/position/rotation components;
- every observed gameplay oClass exists in the complete declared class table;
- **307 instances** reference valid zero-local-core classes;
- deterministic placement SHA-256 `064c82f80b17c6757e3928628fe3a13b0f0f8d771f863b4a08c9d76d1ba064a2`.

The 22 oClasses previously described as “gameplay-only” are therefore not absent classes. They are declared entries whose local-core offset is zero under the pinned public format.

## Texture sentinel

Decoded Moby geometry observes byte value `0xff` outside the 143-entry row-1 Moby texture table. Pinned Wrench format behavior uses `0xff` for unused/no-texture class-local slots, so it is treated as an explicit **no-texture sentinel**, not as texture 255.

Across the decoded class set, 649 triangles reference that sentinel. There are **zero other texture IDs outside the confirmed Moby texture table**. In the currently placed row-1 render subset, no emitted triangles happen to use the sentinel.

## Conservative retail render subset

Pipeline `2827405961` / iid **454**, job `16352997661`, ran on `Jess-Laptop` and passed with **205 / 205 repository tests green**.

The neutral row-1 world admits only evidence-backed renderable Moby geometry:

- **427 / 735 Moby instances rendered**;
- **221,349 Moby render triangles**;
- **307 zero-local-core instances skipped**;
- **1 instance skipped because its decoded class has empty geometry**;
- **0 instances skipped for unresolved skinning** in this row;
- any unexpected missing class or non-sentinel out-of-range material remains a hard failure.

This policy does not claim zero-local-core Mobies are nonvisual globally. It only declines to invent level-local geometry where the sampled level contains none.

## Resulting row-1 world

With the conservative Moby subset included:

- render meshes: **387**;
- render triangles: **1,107,427**;
- collision triangles: **264,313**;
- generated world bytes: **70,402,078**;
- generated world SHA-256: `d325127c6806c9f9f2bf8cba18d0e5061e63fbc45a1a00b5d0f6a5c1fb3fdf61`.

The next compatibility question is generality: run the same strict world builder on additional retail table rows and preserve any divergence instead of weakening the gates.
