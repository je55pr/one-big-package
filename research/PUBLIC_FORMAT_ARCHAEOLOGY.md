# Public format archaeology — surviving cross-source notes

This note keeps only the public-source findings that still add something after the retail-backed Going Commando work merged to `main` at `d5dc8224`.

**Authority rule:** exact retail bytes and executable behaviour are authoritative. Public tools are used to suggest layouts, explain already-observed bytes, identify cross-game variants, and expose places where independent implementations disagree.

## Pinned public sources

### Wrench

Repository: `chaoticgd/wrench`

Pinned commit: `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`

Wrench is the broadest public PS2-series implementation found in this pass and includes both read and write paths for many native formats.

### noclip.website

Repository: `magcius/noclip.website`

Pinned commit: `6b16cfda00ef5af3ee2a66d8b928bb0bf700e5b6`

Its `src/RatchetAndClank/` implementation is useful as a separately written TypeScript reader/renderer that actually consumes tfrags, TIEs, shrubs, mobies, collision, skies and gameplay data.

### Historic index

`RatchetModding/rac-modding-resources` is useful for locating older utilities and documentation, but those are supporting references rather than authority.

## What retail work has superseded

Do **not** use this branch's earlier public-only notes as authority for structures that are now decoded from `rac2-ntscu-v1.01`. Current retail-backed notes on `main` include:

- `GC_LEVEL_WAD.md`
- `GC_LEVEL_CORE.md`
- `GC_COLLISION.md`
- `GC_TFRAG.md`
- `GC_TEXTURES.md`
- `GC_TIES.md`
- `GC_SHRUBS_MOBIES.md`
- `GC_MOBY.md`
- `GC_SKY.md`
- `GC_LEVEL_SETTINGS.md`
- `GC_PLANET_NAMES.md`

The earlier claim that GC native level data should be thought of as a separate hidden raw-sector database was also too strong. Retail evidence shows real ISO-9660 `LEVEL<n>.WAD`, `AUDIO<n>.WAD` and `SCENE<n>.WAD` files plus `/RC2.HDR`, an engine-facing LBA directory that addresses those assets by disc sector. Both views matter.

## Remaining useful discrepancies and compatibility notes

### noclip's GC pills pointer is wrong

The GC gameplay pointer table is independently described by Wrench and by noclip's own explanatory comment as:

```text
0x64 relative-pvar-pointer fixups
0x68 cuboids
0x6c spheres
0x70 cylinders
0x74 pills
```

But noclip's returned GC object reads `shapesPills` from `0x64`. Treat that as a concrete parser bug, not an alternate layout. If OBP expands gameplay parsing, `0x74` is the public cross-checked pills candidate and should still be checked against retail bytes before being promoted to authority.

### Wrench knows a GC `0x68` top-level level-header variant

The authority NTSC-U v1.01 disc contains `0x60` level headers across all 27 `LEVEL<n>.WAD` files. Wrench additionally supports a GC-specific `0x68` variant with separate NTSC/PAL gameplay ranges; noclip's game-2 reader accepts only `0x60`.

Therefore the `0x68` form is **future build/revision compatibility evidence**, not a reason to complicate the current authority parser prematurely. If another GC revision or region is added, probe the header size before assuming the v1.01 layout.

### GC can mix RAC1 and RAC2 Moby class codecs

Both Wrench and noclip dispatch some Going Commando Moby classes through the older RAC1 mesh path based on class-local header state. This matches the remaining `force_rac1` / class-byte `0x0b` investigation already noted in `GC_MOBY.md`.

The architectural lesson survives even after the main parser matured: **game identity is not always sufficient to select a native subcodec.** Keep class/header-local dispatch possible.

### Compatible recompression is not byte identity

Wrench explicitly documents its compressor as producing compatible streams rather than reproducing Insomniac's encoder byte-for-byte. A round-trip that changes compressed bytes is not, by itself, evidence of semantic corruption. Validate decompressed content and runtime/reader behaviour instead.

### Minimal VIF support should remain deliberately minimal

The retail-backed GC geometry parsers prove that OBP can recover current tfrags, TIEs, shrubs and Mobies without emulating VU programs. That does **not** make `packages/ps2-vif` a general PS2 VIF interpreter.

A notable public-reader gap is `STCYCL` filling mode where `WL > CL`. Before genericising the VIF layer to other games/formats, catalogue the actual command/cycle combinations on the relevant authority inputs. See `PS2_GEOMETRY_PIPELINE.md`.

### Do not generalise GC texture/sky details across all games

Examples already visible in public code:

- GC's ordinary indexed texture pixels are linear; Deadlocked adds a pixel-layout swizzle.
- RAC/GC use one sky-shell header family; UYA/DL use another that includes shell rotation/angular-velocity data.

Shared high-level asset families do not imply identical packed layouts.

## Remaining authority checks worth doing

1. Confirm the class-local RAC1/RAC2 Moby selector against representative retail GC classes, especially remaining parse failures / Museum content.
2. Decode the level-settings chunk-plane suffix and settle the exactly-on-plane `> 0` versus `>= 0` disagreement documented in `GC_COORDINATES_AND_CHUNKS.md`.
3. If another GC region/revision becomes an authority input, catalogue top-level header sizes and specifically look for Wrench's `0x68` form.
4. Catalogue VIF command and `STCYCL` modes across authority games before extending `packages/ps2-vif` beyond the packet shapes already exercised.
5. Finish any hero-collision-group semantics from retail/executable evidence rather than inheriting public names blindly.

## Provenance policy

When a public structure helps explain retail bytes, record which source supplied the hypothesis and which retail observation promoted it. Preserve unknown/native fields where practical instead of turning reverse-engineered names into permanent semantics too early.
