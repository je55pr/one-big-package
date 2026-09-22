# R&C1 Wrench visual and player attachment

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). Static disc/executable evidence is primary; one retained Veldin savestate was used to identify the live owner/model relationship and was reduced to addresses, ids and transforms only. No retail payload is committed.

## Asset identity

The ordinary held Wrench is a **separate Moby**, not triangles embedded in Ratchet class 0.

The native item descriptor table has 0x4c-byte entries. Item `8` is the Wrench, and item-8 descriptor `+0x10` is class id `0x47` / **71**. The ordinary model-loader caller around `0x0020eb30` defaults to item 8, reads that descriptor field and passes class 71 to the common model loader at `0x00245770`.

A retained neutral Veldin frame independently shows:

- live Ratchet: class 0 at `0x01845e80`;
- live Wrench: class 71 at `0x01858a80`;
- Wrench `Moby+0x24` model pointer: `0x006d8000`;
- Wrench `Moby+0xb8`: `0x01845e80`, directly owning/linking the object to Ratchet;
- Wrench native sequence: `1`.

Level 0's ordinary Moby class table keeps class 71 at slot 12 with **asset offset zero** and texture slot 0 mapped to Moby texture **15**. Runtime table `0x00197300` is slot-indexed; slot 12 is patched to `0x006d8000`. The generic live-Moby initializer at `0x0024ea10` then copies the slot-selected model pointer to `Moby+0x24`.

This explains why the normal level-class decoder correctly preserves class 71 as meshless even though retail visibly renders the Wrench: class 71 is a global resident loaded into the level's runtime model table.

## Retail class source

The global-class source registry contains class 71 as its first entry, with source pointer `0x01698230`. That source begins with WAD-LZ and declares compressed size `0x5628` (22,056 bytes). The standard OBP `WadLz` decoder expands it to 79,376 bytes.

The source is also recoverable statically from the disc. Level-0 range-0 slot 10 is the already-established core-data WAD; after its normal outer decompression, the exact class-71 compressed source begins at core-assets offset **`0x00f90230`**. Runtime maps the core asset buffer at `0x00708000`, so `0x00708000 + 0x00f90230 = 0x01698230`, matching the live registry pointer exactly.

Payload-free compressed-source SHA-256:

`6923ebb76c882fd58f9a04220a722a3f9e18c720109e28aaac4aecd08a633306`

Decoded class-71 shape:

| property | retail result |
|---|---:|
| high-LOD packets | 9 |
| joints | 12 |
| vertices | 837 |
| triangles | 922 |
| class-local sequence slots | 20 |
| neutral live sequence | 1 |
| Veldin Moby texture | 15 |

Class **110** is also publicly named as a Wrench and has the same 9-packet / 12-joint broad structure on Veldin, but direct retail comparison rejects it as the ordinary held model: its native vertex coordinates differ throughout. Production therefore does not substitute class 110 for the zero-offset class-71 entry.

## Attachment and action coupling

The class-71 live update is `0x002a84c8`. It maintains the Ratchet owner link at `Moby+0xb8`, calls helper `0x002a7c70`, and directly branches on player action **`0x13`**, the separately recovered ordinary wrench action. This proves the held object participates in the player action path rather than being unrelated scenery.

The exact Ratchet skeleton joint/socket and per-frame native attachment transform are **not yet recovered**. In particular, the evidence above does not justify naming a hand joint merely from proximity.

The current Godot presentation therefore makes the boundary explicit. It renders the real retail class-71 mesh and texture, parents it beneath the animated Ratchet view, and uses a host-only neutral root-relative transform calibrated from the retained neutral frame. The fallback node is named `Rac1RetailWrench_HostAttachmentFallback`, and its provenance string is `host-neutral-root-relative-fallback`.

The retained neutral calibration is:

- Ratchet position `(154.7710419, 120.5826263, 29.484375)`, native yaw `1.111041784`;
- class-71 position `(154.9961548, 120.3641052, 29.9795303)`;
- resulting Ratchet-local native offset `(-0.09594126, -0.29870149, 0.49515533)`.

This fallback follows Ratchet's player root while Ratchet plays admitted sequence 23, but it does **not** pretend to reproduce an unresolved animation-driven hand socket. Weapon-selection presentation is still stateful: the class-71 view is visible for Wrench and hidden when the existing Bomb Glove item 10 is equipped.

## Production boundary

`Rac1WrenchAsset` follows the retail level-core gadget directory at header `+0x80/+0x84`, selects native class 71, validates that entry's fixed-build compressed size and SHA-256, decompresses it with the existing WAD-LZ codec, validates the recovered geometry/skeleton/sequence shape, and resolves its texture through class 71's actual zero-offset class-table entry. No extracted mesh, texture, WAD or savestate bytes are stored in Git.

`Rac1WrenchAssetTests` freezes the canonical Veldin source offset, class-71 mesh counts, neutral sequence slot, texture id 15 and retail identity constants. The managed R&C1 combat smoke also asserts that the class-71 view is parented to the animated Ratchet view and visible during both admitted sequence-23 wrench swings, hidden while Bomb Glove is equipped, and visible again after re-equipping the Wrench.
