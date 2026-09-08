# Ratchet & Clank 1 — first neutral OBP world slice

Authority: `rac1-ntscu-original` / SCUS-97199. Retail bytes remain outside Git.

This checkpoint proves the first end-to-end R&C1 world slice without pretending the outer game format is Going Commando's level-WAD format:

```text
raw retail disc
  -> R&C1 disc index @ LBA 1500
  -> native 0x2434 level header
  -> outer positional range 0
  -> R&C1 11-range level-data directory
  -> native core index + GS RAM + WAD-LZ core data
  -> shared inner tfrag / collision / PS2 texture codecs
  -> neutral OBPWorld
```

The importer intentionally stops at the concepts currently supported by direct retail evidence: static terrain, terrain material references and collision. Empty OBP arrays for instances/splines/volumes/spawns mean **unrecovered**, not absent from the native game.

## First target: native level 0

Native level 0 was selected as the initial vertical-slice target because its recovered core is structurally ordinary, all relevant shared inner codecs validate without exceptions, and it is early in the native catalogue. No planet/location name is assigned here until the retail/executable mapping is independently established.

Deterministic authority summary is committed as `research/generated/rac1_level0_world_summary.json`.

Key results:

- native table slot: `0`
- native header LBA: `1885903`
- native header size: `0x2434`
- outer positional range 0: LBA `1885908`, `6562` sectors
- core-index bytes: `32160`
- WAD-LZ core data: `9,515,621 -> 16,766,912` bytes
- decompressed core-data SHA-256: `a849d0554f38d7627c9a8f019de2dd6b983c24c85f113e1b94e2bdf686b910a4`

Static terrain:

- tfrag block: `0x000000..0x113f40`
- `460` native tfrags
- `24,758` recovered render vertices
- `24,520` recovered triangles
- sum of native per-tfrag triangle declarations: **24,520 exactly**
- `78` distinct referenced terrain texture IDs
- Y-up bounds: `(86.6689453125, 5.9990234375, 67.9599609375)` .. `(233.083984375, 43.5087890625, 307.4794921875)`

Terrain textures:

- `78` native table entries
- all `78` IDs are referenced by terrain on this level
- dimensions: `46 x 128x128`, `30 x 64x64`, `2 x 32x32`
- every pixel/palette range validates against the recovered core-data/GS-RAM ranges
- decoded pixels use the shared 8-bit indexed PS2 path already independently checked across all R&C1 levels

Collision:

- native core range: `0x1fe7c0..0x2c1700`
- `5,783` octants
- `109,751` vertices
- `86,184` triangles
- native type bytes: `{9, 10, 12, 31}` — meanings intentionally unresolved
- native Z-up bounds: `(67.9375, 70.125, 10.1875)` .. `(207.9375, 311.1875, 82.0625)`
- OBP Y-up bounds: `(67.9375, 10.1875, 70.125)` .. `(207.9375, 82.0625, 311.1875)`

Combined first-slice OBP bounds:

- min `(67.9375, 5.9990234375, 67.9599609375)`
- max `(233.083984375, 82.0625, 311.1875)`

## Importer behaviour

`packages/rac1-world` assembles the recovered native structures into `OBPWorld`. `packages/importer-rac1` now implements `importWorld()` for the exact supported authority build instead of throwing.

Important constraints:

- request `levelId` is a **native integer level ID**, resolved through the retail-generated catalogue rather than assumed to equal a filename/planet number;
- core WAD-LZ data is decompressed once per import and shared by terrain/collision/texture decoding;
- all native terrain texture table entries become OBP materials, including entries not currently referenced by rendered triangles;
- native texture `type`, palette slot and both trailing 16-bit words remain in provenance notes;
- native collision type bytes remain per triangle and are not semantically renamed;
- texture RGBA is decoded, but this first browser-neutral world slice embeds only an average `debugRgba` preview rather than introducing a Node-only PNG dependency into the importer. Full image transport can be generalized separately.

## R&C1 -> GC evolution classification at this milestone

| Concept | R&C1 evidence | GC comparison | Relationship |
|---|---|---|---|
| outer level storage | raw-disc index + native 0x2434 header + positional sector ranges | ISO-visible `LEVELn.WAD` family | same purpose, redesigned outer format |
| level-data directory | 11 native ByteRanges in outer range 0 | GC/UYA data-lump header has a different exposed container path | same purpose / related inner organization, different outer access |
| core compression | WAD-LZ on all 19 levels / 38 regional streams tested | WAD-LZ | identical codec |
| core-index prefix | 0xbc bytes with several field positions now retail-correlated | GC 0xbc `LevelCoreHeader` | obvious shared/ancestral structure; semantics still promoted field-by-field |
| static terrain | same 0x40 tfrag table/header + VIF grammar, 20,016/20,016 retail fragments validated | GC tfrags | identical binary structure for recovered path |
| collision | same octree decoder succeeds 19/19 | GC collision | identical binary structure for recovered path |
| terrain texture entries | same 0x10 entries + linear 8-bit pixels + RGBA32 CLUT | GC level textures | identical recovered binary structure |
| complete world container | R&C1 raw-disc hierarchy above | GC level-WAD hierarchy | redesigned |

This is deliberately narrower than saying “R&C1 uses the GC level-core format”. The independently validated inner structures may be binary-identical while the native world/container architecture around them is not.

## Production-native C# expansion — sky + placed static world

The production C# path has now advanced beyond the historical first terrain/collision slice above. Retail R&C1 reaches the neutral `RuntimeWorld` through shared inner codecs for tfrags, collision, level textures, sky, Tie packets and Shrub packets, while retaining R&C1-specific outer containers, Tie class headers and gameplay placement records.

Level 0 now contains:

- 78 tfrag material meshes / 24,520 triangles;
- 7 sky material groups / 2,366 triangles / 8 sky textures;
- 131 placed Tie material meshes / 396,708 triangles;
- 70 placed Shrub material meshes / 327,841 triangles;
- 33 placed Moby material meshes / 149,474 bind/rest-pose triangles;
- 900,909 render triangles total;
- 86,184 collision triangles;
- 1,114 native Tie placements, 1,697 native Shrub placements and 296 native Moby placements;
- expanded Y-up world bounds `(2.5745086669921875, -5.954422950744629, -56.46558770118281)` .. `(352.2431854769455, 101.66961976741732, 427.795684825629)`.

The placed-world values independently match the mature TypeScript reference implementation exactly for mesh grouping, triangle counts and bounds.

### All-level production gate

`Rac1WorldImport.Build()` survives all native levels 0..18 directly against the authority ISO. The permanent retail-gated test checks finite render/collision geometry, index ranges, texture linkage, bounds, environment generation and ship spawn generation on every level.

The deterministic sanitized census is committed as `research/generated/rac1-native-world-census.json`. Across all 19 levels it records:

- 33,040,382 production render triangles;
- 9,761,535 placed Moby bind/rest-pose render triangles;
- 3,686,240 collision triangles;
- 44,712 Tie placements;
- 25,572 Shrub placements;
- 16,232 Moby placements, of which 15,359 link to decoded geometry;
- 1,804 Tie class occurrences;
- 551 Shrub class occurrences;
- 2,968 payload-bearing Moby class occurrences / 22,227 high-LOD packets.

Those placement/class totals exactly reproduce the independent retail archaeology censuses. No retail bytes, decoded texture pixels or proprietary screenshots are stored in the census.

## Production provider / Godot proof

R&C1 now has the same neutral production-provider seam as Going Commando. `Rac1DestinationCatalogue` exposes exactly the 19 retail-validated native level ids as `rac1:LEVEL0..18`; planet/location labels, native engine ids and container names remain deliberately unresolved rather than invented. `Rac1WorldProvider` resolves only canonical catalogue entries and passes their retail source directly to `Rac1WorldImport`.

The application composition root registers R&C1 and GC together, so source attachment, the Worlds browser, direct destination capture and interactive entry all use the same `IObpWorldProvider -> RuntimeWorld -> RuntimeWorldScene` path.

A deterministic direct-provider Godot run of native level 1 validated the player-facing end of that path:

- provider import: 383 RuntimeWorld meshes / 1,118,293 render triangles;
- Godot presentation: 382 render meshes / 1,117,525 triangles (the deliberately omitted untextured sky backdrop accounts for the difference);
- 383 decoded textures;
- one 182,331-triangle native collision body;
- native ship point `(167.2265625, 61.15489196777344, 125.84651947021484)` enters the recovered collision footprint;
- the debug player grounded on that collision, moved, jumped and landed again under normal runtime physics.

The proprietary screenshot is intentionally not committed. Only these structural observations are retained.

Level 0 is retained as the deterministic wide static-world baseline, but its recorded ship point `(20,20,20)` is the sole native level whose X/Z lies outside the currently decoded core-collision AABB. It is therefore not used as evidence for player grounding; levels 1..18 all place the recorded ship horizontally inside the recovered core collision footprint. OBP preserves the retail level-0 value instead of fabricating a replacement spawn.

## Native Moby bind/rest-pose promotion

Production C# now decodes and places R&C1 Moby high-LOD geometry without borrowing GC's skeleton shortcut. The R&C1 decoder uses the retail-validated 0x48 class generation and its distinct 0x20-byte vertex-table header (8 × u32), including the seven-entry vertex-id pipeline, persistent cross-packet native vertex cache, duplicate-cache emissions and persistent GS texture state.

Bind/rest-pose recovery deliberately uses the stored `s16 * classScale / 1024` positions for both rigid and animated classes. No R&C1 skeleton affine/inverse-bind interpretation is applied at this milestone; animated classes therefore appear in their validated base surface only.

Across all 19 authority levels the permanent C# census matches the independent TypeScript archaeology exactly:

- 2,968 payload-bearing Moby class occurrences;
- 22,227 high-LOD packets;
- 1,920,636 emitted class vertices including duplicate-cache emissions;
- 1,933,983 class triangles;
- 38 geometry-free payload occurrences;
- 16,232 gameplay placements;
- 15,359 placements linked to decoded geometry: 9,122 rigid-class and 6,237 animated-class placements;
- 873 unlinked placements, all explained by zero-asset class-table entries;
- zero nonzero-payload decode gaps.

Independent placed-world cross-checks also agree exactly: level 0 links 286 placements to 149,474 Moby triangles, while level 1 links 918 placements to 527,505 Moby triangles.

The all-level `Rac1WorldImport.Build()` gate remains 19/19 green with Moby geometry enabled. Full Moby animation is intentionally a separate future phase because R&C1 skeleton rotation/inverse-bind semantics are not safely interchangeable with GC.

### Godot bind-pose visual proof

A deterministic native level-1 provider capture with the Moby promotion enabled imports 523 RuntimeWorld meshes / 1,645,798 triangles. Of those, 140 material groups / 527,505 triangles are placed Moby bind/rest-pose geometry. Godot presents 522 meshes / 1,645,030 triangles after the same deliberately omitted untextured sky backdrop; only 144 Moby triangles use the untextured fallback tint.

The native ship/collision path remains healthy in this populated world: the debug player grounds, follows the scripted movement, jumps and lands. The local capture visibly adds crates, machinery/props and other dynamic-world objects to the previously terrain/Tie/Shrub-only scene. The proprietary PNG remains local and uncommitted.
