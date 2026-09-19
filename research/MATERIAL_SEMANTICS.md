# Native material / render-state semantics

This note freezes the bounded material evidence recovered for the PS2 Ratchet & Clank trilogy.
It deliberately stops below full GS emulation. The production contract is the small set of
material inputs that the existing geometry decoders can prove and preserve.

No retail payload bytes are checked in. The executable fixtures in
`tests/OBP.Tests/RcMaterialStateTests.cs` synthesize only the documented field layouts.

## Structural authority

The packet layouts match the open-source Wrench structural model
(`chaoticgd/wrench`, observed at commit `1b48f4d1ed02de9e05b57802ab518f9ec39c23df`):

- `src/engine/tfrag_low.h`: `TfragTexturePrimitive`, five 0x10-byte A+D-shaped records.
- `src/engine/tie.h`: `TieAdGifs`, the same five records in TIE-specific order.
- `src/engine/shrub.h`: `ShrubTexturePrimitive`, four 0x10-byte records with the packet offset at +0x0c.
- `src/engine/moby_packet.h`: `MobyTexturePrimitive`, four 0x10-byte records with secret-index words.
- `src/engine/gif.h`: GIF tag PRE/PRIM fields and the PRIM alpha-blend-enable bit.

PS2SDK (`ps2dev/ps2sdk`, observed at commit `d317f8f0a2a413db38c5ef2b46ba927996983e0a`)
provides the GS `ALPHA` equation and selector encoding used below:
`(C1 - C2) * A / 128 + C3`, with source/destination/zero colour selectors and
source/destination/fixed alpha selectors.

Wrench's shrub writer is especially important evidence: it explicitly states that these words
are runtime-fixup inputs and not necessarily literal GS register images. It writes shrub S/T wrap
as 0/1 in the low/high CLAMP words and MMIN as the plain TEX1 high word. OBP therefore decodes
only those semantics that have a demonstrated engine-side encoding and preserves weaker fields raw.

## Recovered contract

| Family | Texture record | Proven semantic decode | Preserved but unresolved |
| --- | --- | --- | --- |
| tfrag | 0x50 | texture id | TEX1 words, CLAMP words, runtime address bytes |
| TIE | 0x50 | texture id; GS ALPHA equation when tail address is 0x42 | TEX1 words, CLAMP words, non-ALPHA tail state |
| shrub | 0x40 | texture id, S/T repeat-or-clamp, MMIN, LOD-K raw | other mip state, runtime address bytes |
| Moby | 0x40 | texture selector, MMIN, LOD-K raw | CLAMP words, runtime address bytes |
| shrub GIF tag | 0x10 | primitive type and alpha-blend enable when PRE=1 | alpha-test equation |

`RcMaterialState` preserves the raw TEX1 and CLAMP words even when their interpretation is not
proven. It also preserves the A+D-shaped address byte instead of requiring it to name the apparent
register: retail R&C1 Moby data proves those bytes can be runtime placeholders. Unknown is an
intentional semantic value, not a parse failure.

The shared Moby grammar also has negative texture selectors:
`-1 = none`, `-2 = chrome`, and `-3 = glass`. These are classified by
`RcMobyMaterial` and now survive both the GC/UYA and R&C1 packet paths.
## Alpha, translucency, additive blending

The recovered shrub GIF tags prove whether PRIM requested alpha blending. TIE retail data goes
further: GC and UYA use tail address `0x42` (`ALPHA_1`) in the fifth 0x10-byte material slot,
so `RcGsAlphaState` decodes the native blend equation directly. R&C1 Level 1 instead uses
tail address `0x36` there, demonstrating a real generation difference rather than a universal
five-register template.

The pinned retail census is payload-free:

- R&C1 Level 1 TIE: 593 material records, all 593 tail `0x36`, no decoded ALPHA equation.
- GC Level 1 TIE: 373 material records, all 373 tail `0x42`, all standard source-alpha equations.
- UYA row 1 TIE: 417 material records, all 417 tail `0x42`; 389 standard source-alpha,
  2 additive fixed-alpha, and 26 equations intentionally left unknown.

For the known equations, `SourceAlpha` is `(Cs-Cd)*As+Cd`; `AdditiveSourceAlpha` is
`Cs*As+Cd`; fixed-alpha variants replace `As` with the register's fixed coefficient.
The decoder reports `Unknown` for any other selector tuple instead of inventing a blend label.

No recovered path here identifies a `TEST_1` alpha-test equation or threshold. Existing
`MaterialModel.ResolveAlpha` texture-content classification therefore remains a presentation
fallback, not evidence for the original PS2 cutout/test mode.

## Emissive / unlit

TIE and Moby class structures contain fields named `glow_rgba` and `mode_bits` in the structural
authority, but their render semantics are not established by the recovered packet path. This slice
does not assign an emissive or unlit meaning to those fields.

Likewise, the current brightness-derived runtime emission is explicitly a presentation heuristic.
It is not promoted into the native material contract.
## Vertex colour modulation

Tfrag geometry carries native four-byte RGBA records. `RcTfrag` already preserved RGB for the
established baked-lighting path; it now also preserves the fourth byte losslessly as
`Mesh.VertexAlpha`. Keeping alpha separate avoids silently changing the runtime RGB colour-array
shape while retaining the native evidence for later render-state work.

No equivalent per-vertex colour modulation is asserted for TIE, shrub, or Moby by this slice.
Their existing normals / lighting inputs remain separate from the material-state contract.

## Game and layer boundaries

The reusable PS2 semantics live in `OBP.PS2.Graphics.RcMaterialState` and the shared geometry
codecs. Packet ownership remains where the native formats already live:

- `RcTfrag`, `RcTie`, and `RcShrub` expose decoded material state beside triangle identity.
- `GcUyaMoby` and `Rac1Moby` preserve per-switch Moby state and packet-to-packet inheritance.
- RAC2 tfrag/TIE/shrub facades forward the shared material evidence instead of erasing it.
- UYA's source-game asset layer retains TIE/shrub material state; Moby negative selectors remain
  native mesh evidence while the legacy mapped texture-id surface stays untextured.
- no Godot or runtime material policy is embedded in these native decoders.
## Verification boundary

`RcMaterialStateTests` is payload-free and pins:

1. shrub wrap/filter/LOD field interpretation;
2. tfrag/TIE raw sampler preservation without guessed semantics;
3. Moby chrome/glass/none selectors and filter evidence;
4. GIF PRE/PRIM alpha-blend-enable decoding;
5. preservation of runtime-placeholder address bytes;
6. standard, additive, and fixed-alpha GS equation classification.

`MaterialRetailEvidenceTests` pins the compact R&C1/GC/UYA TIE censuses above. Retail-gated
geometry tests remain the authority for whole-file decoding when local ISOs are available. This
task intentionally does not vendor or derive redistributable retail payloads.
