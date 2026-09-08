# Retail UYA multi-row neutral-world reconstruction census

This note records end-to-end `OBPWorld` reconstruction and actual viewer capture for four independent canonical UYA NTSC-U retail table rows.

The row/table interpretation remains a public-derived provenance lead. Direct retail evidence independently establishes the sampled table structure and that the raw main-header `+0x08` field equals the physical table index. Public Wrench names are included only as cross-reference labels.

## End-to-end result

Every sample passed the same strict path without per-row parser exceptions:

`retail ISO → candidate hidden table → level/core WAD-LZ → tfrags + textures + TIE/shrub/Moby classes → gameplay placements → collision → validated OBPWorld → Chrome/WebGL2 viewer → JPEG artifact`

| Table | Public-derived label | Tfrags | TIE placed | Shrub placed | Moby rendered / decoded | Render tris | Collision tris | World SHA-256 | Screenshot SHA-256 |
|---:|---|---:|---:|---:|---:|---:|---:|---|---|
| 1 | Veldin | 802 | 1,960 / 1,960 | 1,894 / 1,894 | 427 / 735 | 1,107,427 | 264,313 | `d325127c6806c9f9f2bf8cba18d0e5061e63fbc45a1a00b5d0f6a5c1fb3fdf61` | `b9912f9e2f3d8bbcb73b1249b389076a6fd64418df97e4dfb8441a6a2201942f` |
| 8 | Aquatos | 2,876 | 646 / 646 | 499 / 499 | 668 / 777 | 624,745 | 112,813 | `edf76282649074e14d400b3136e38922497065e99eb60a5dd15478e6fcd4fa44` | `96eae1d8444b59613e6128ef1772bd645a5291339f12c3f664155e4e278a7c26` |
| 20 | Final Boss | 383 | 595 / 595 | 1,105 / 1,105 | 117 / 200 | 584,395 | 236,309 | `c94e72d5e4a10b41d0c8bd8725fa7765b39ee7665d68e1411e50ed9aa7b9574c` | `6daade0669aea5b689f9c659dc752cc6f09d52c6641ea9e24b7092aa10704e10` |
| 50 | Bakisi Isles (Split-screen) | 525 | 365 / 365 | 85 / 85 | 182 / 198 | 152,075 | 222,264 | `57a3f57d2a35e58ea95bc6a076e09946d81084efb882668eae77277af002d307` | `044b66d2a7d1654483a9dd43f38cc16e6f7762d2c18a309db541c12c502738fd` |

Viewer captures for every row reported WebGL2 through ANGLE/Vulkan SwiftShader at a 1249×625 canvas and zero uninterpreted neutral instances.

## Moby safety gates exercised across samples

The renderer does not equate “declared instance” with “renderable local mesh.” It preserves three independent non-renderable reasons:

| Table | zero-local-core skipped | empty-geometry skipped | unresolved-skinning skipped |
|---:|---:|---:|---:|
| 1 | 307 | 1 | 0 |
| 8 | 97 | 1 | 11 |
| 20 | 82 | 1 | 0 |
| 50 | 16 | 0 | 0 |

Row 8 is particularly useful because it actively exercises the unresolved-skinning gate: eleven gameplay Mobies reference decoded skinned classes for which the current GC bind-pose reconstruction is not applied. They are omitted instead of being rendered in a guessed pose, while the rest of the world still validates and captures successfully.

All four rows retain the other Moby prerequisites established by the compatibility probe: complete gameplay-record decoding, no non-finite placement components, all referenced oClasses present in the declared class table, all nonzero local-core candidate classes decoded, and no unexpected texture IDs beyond the pinned `0xff` no-texture sentinel model.

## Capture pipelines

- table 1: pipeline iid **464**, job `16353075223`
- table 8: pipeline iid **468**, job `16353152022`
- table 20: pipeline iid **475**, job `16353205404`
- table 50: pipeline iid **476**, job `16353208425`

Each capture is retained locally under `C:\ChatGPT\OBP-Reports\uya-tableN-screenshot.jpg` and uploaded as a small GitLab CI artifact. Image bytes are no longer mirrored through normal CI logs.

## What this promotes

Across these four sampled worlds, OBP now has direct retail evidence that the current shared/compatibility path is sufficient to reconstruct and view:

- UYA tfrag terrain;
- tfrag, Moby, TIE and shrub textures;
- TIE class geometry and exact gameplay placement;
- shrub class geometry and exact gameplay placement;
- the conservative renderable subset of Moby class geometry and gameplay placement;
- shared RC collision;
- neutral coordinate conversion and `OBPWorld` validation;
- deterministic browser rendering on the self-hosted Windows runner.

This is materially stronger than parser-only compatibility because the decoded systems compose together into visible worlds across campaign and multiplayer-shaped rows.

## Remaining visual boundary

The next major visible omission is the **sky/environment path**. Public Wrench evidence says R&C1/GC use `RacGcSkyShellHeader`, whereas UYA/Deadlocked use a versioned `UyaDlSkyShellHeader` that adds shell rotation. Therefore OBP should not simply promote the existing GC sky reader unchanged without a UYA-specific prerequisite/compatibility study.
