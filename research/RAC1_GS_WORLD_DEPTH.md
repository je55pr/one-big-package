# RAC1 retail GS world depth evidence (issue #416)

Status: **retail frame and draw-stream state captured; individual tree-root draw correlation pending**. Preserve the existing verified #415 tree-base fix.

## Captured evidence

The tester reached the Veldin tree from issue #414 in the authentic R&C1 NTSC-U game, SCUS-97199, on 2026-10-27. They **also saw apparent camera-dependent Z-fighting in PCSX2 at that tree**. This is an emulator observation, not proof that original PS2 hardware showed the defect.

Private data, deliberately NOT committed:
- PCSX2 screenshot and matching GS dump: Ratchet & Clank_SCUS-97199_20261027213131.png / .gs.zst in the PCSX2 snaps directory
- Parsed summary: captures/retail-gs-416/draw-state.json

The source-independent tool tools/rac1-gs-dump-audit.py reads GS dump packets and GIF packed A+D register writes. Five synthetic, portable tests cover the packet parser and depth-state decoding. It does not simply assume that the boot-time register values remain active during gameplay.

## Streamed GS state (not just startup state)

The capture reports R&C1 SCUS-97199, CRC CE4933D0, GS state version 9, captured image 640 x 480, four frame boundaries, and 7,060 GIF transfers.

All four analysed frame periods produce identical observed distributions, with **81,299 GIF vertex writes** per frame:

| Depth/alpha state | Vertex writes per frame |
| --- | ---: |
| GEQUAL, depth write on, alpha test GEQUAL, alpha-fail=3 | 79,507 |
| GEQUAL, depth write on, other alpha-test states | 1,622 |
| ALWAYS, various alpha/depth-write modes | 170 |

Therefore **81,129 / 81,299** vertex writes use an inclusive GEQUAL depth comparison with depth writes enabled. Representative actual streamed values include TEST_1 = 0x000000000005360B, TEST_1 = 0x000000000005380B, and ZBUF_1 = 0x00000000010000D8. The latter TEST register has a threshold of 128, alpha comparison GEQUAL, alpha-fail mode 3, depth-test GEQUAL and depth testing enabled; the ZBUF register permits depth writes.

A GIF vertex write is not a pixel, a triangle or necessarily a draw call. The report groups consecutive writes by observed state. It has **not** associated any given batch with the 119 class-501 / texture-13 tree-root triangles. The exact draw-state for that object remains unproven until packet/texture identification is resolved.

## Source comparison and meaning

The original-game native rendering port describes world ordering sky -> tfrag -> TIE -> shrubs -> Mobys; see https://github.com/RAC-Native/rac1-native-game/blob/main/docs/rendering.md . Retail class-501 texture-13 GIF material data enables blending and carries TEX0/TEX1/CLAMP/MIPTBP, but does not itself override GS TEST or ZBUF. Original executable initialization likewise enables GEQUAL testing and writes.

**The retail-emulator flicker means inclusive depth comparison is NOT a universal Z-fighting cure**. The result might be genuinely retail-authentic, depend on PCSX2 software versus hardware renderer and its depth conversion, or vary with platform/precision. The current captured frame cannot distinguish these.

OBP's class-501 0.005-unit shim from PR #415 is human-verified to improve the visual. Do not remove it before an equally effective, evidence-backed renderer replacement exists.

## Controlled PCSX2 comparison

PCSX2 2.6.3 produced a retail resume state at the captured tree. Two isolated
portable profiles reuse that privately saved state and independently cloned
memory cards, verified against the original card files by SHA-256. The normal
PCSX2 profile and original memory cards were not modified.

The independent renderers are PCSX2 GSRendererType VK = 14 (Vulkan) and
SW = 13 (software), both at native resolution (upscale_multiplier = 1).
The first test load showed a 'memory card removed' warning because the card
was absent from the clone; copying the original cards into both isolated
profiles cleared that warning. Both now display the same stationary tree
scene with broadly similar appearance.

**Jess's moving-camera result (27 October 2026): the same tree-base ground
flickering remains visible in the isolated PCSX2 software renderer.** It was
also visible in the Vulkan/normal retail-emulator view. This excludes an
exclusively Vulkan hardware-renderer defect from the observed reproductions,
but does not determine whether original physical PS2 hardware also flickers.
The comparison was observational, not a scripted pixel-identical camera path.

The present source evidence is consistent with essentially coplanar retail
geometry having imperfect depth resolution, even under GEQUAL and 24-bit
depth writes. Recreating source draw order alone is therefore not established
as a sufficient fix. The human-verified, slightly raised OBP overlay (#415)
remains a deliberate rendering improvement.

## Next experiments

1. Correlate the native class-501 texture-13 packet with actual streamed GS
   vertex batches using texture VRAM identity; inspect TEST_1, ZBUF_1,
   ALPHA_1 and GIF PRIM at that **specific** draw. Our live frame-level
   register counts are not per-object proof.
2. Optionally collect matched input/movie captures on PCSX2 hardware and
   software renderers if a quantitative comparison becomes necessary; current
   Jess observations already establish both paths flicker.
3. Evaluate a reusable world-overlay policy experimentally in Godot against
   class-500 and class-501 decorations, without weakening the established
   #415 visual fix, source geometry provenance, or collision.
4. Separate source parity from intentional visual improvement, and do not
   substitute a flickering reproduction for a stable user-approved result.

Raw retail dumps, savestates, textures and screenshots stay under ignored private capture storage, never source control.
