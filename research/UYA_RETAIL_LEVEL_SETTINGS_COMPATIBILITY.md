# Retail UYA level-settings compatibility

The existing `gc-level-settings` reader describes the shared GC/UYA/DL `0x5c` first-part structure. This note records direct canonical UYA retail corroboration before those values are promoted into the UYA neutral world.

Pipeline `2827483935` / iid 520, job `16353416647`, ran on the self-hosted `Jess-Laptop` runner and passed all four sampled rows after a green repository check.

## Four-row result

| row | public-derived label | settings offset | first-part SHA-256 | background RGB | fog RGB | fog near/far | death height |
|---:|---|---:|---|---|---|---|---:|
| 1 | Veldin | 208 | `ee1af5460d97f904da9e356790a37528fbe51d9d4e252023ea095530cf41f03f` | `128,128,128` | `125,115,80` | `51200 / 204800` | 60 |
| 8 | Aquatos | 176 | `3b2fe4e8cdf0a5a85d70b6e51017bc53d9541ce9de801817008f412eb6f02986` | `0,0,107` | `0,0,106` | `0 / 230400` | 0 |
| 20 | Final Boss | 176 | `d72af854507d2a2f16a09e05dd7f03d8918bfc7930b2af7fa5d983f29c8aebc6` | `16,8,0` | `0,0,0` | `0 / 256000` | 0 |
| 50 | Bakisi Isles (Split-screen) | 176 | `ef7fd92e6531fd6a76b871865b90c24eaffb06494eb7f0818302dcef9f346c06` | `60,64,50` | `75,70,60` | `54272 / 245760` | 185 |

The labels remain public-derived cross-references; the rows, offsets, hashes and values above are retail evidence.

All sampled colour channels are valid `0..255` values, all decoded floating-point fields are finite, and the existing shared reader accepts every sampled first part without special-casing UYA. The sampled rows are non-spherical.

## Environment implication

These values explain a visible omission in the first UYA screenshots. The sampled sky headers themselves have zero base colour, while the level-settings blocks carry strong level-specific atmosphere:

- Veldin: neutral grey clear colour with warm brown fog;
- Aquatos: deep blue clear/fog colour;
- final-boss row: almost-black warm clear colour;
- Bakisi split-screen: muted olive/grey clear and fog colours.

Therefore UYA sky promotion should be paired with these settings rather than rendering the recovered sky shells against the viewer's generic fallback background.

## Boundary

This promotes binary compatibility of the first `0x5c` settings part for sampled retail UYA levels. It does not yet claim native executable provenance for every semantic name in the structure, nor does it promote later settings blocks. The neutral world retains the source values and the viewer's current fog-distance scale interpretation remains explicitly provisional.
