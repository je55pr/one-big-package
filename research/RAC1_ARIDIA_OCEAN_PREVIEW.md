# Aridia class-854 sand-sea grid preview

Tracking [#232](https://github.com/je55pr/one-big-package/issues/232) and
[#477](https://github.com/je55pr/one-big-package/issues/477). Depends on
the executable-overlay reader introduced in PR #479.

## Finding

Aridia's native LEVEL2 class-854 ocean draw records include an extra pointer at
**record +0x38** to a packed array of **one byte per 7x7 draw cell**.
All referenced bytes in the retail NTSC-U overlay are 0 or 1:

| Native source record | Grid cells | Enabled |
|---|---:|---:|
| 0x1f3c00 | 12 x 10 | 57 |
| 0x1f3c60 | 6 x 4 | 8 |
| 0x1f3cd0 | 6 x 6 | 31 |
| 0x1f3d30 | 6 x 5 | 20 |
| 0x1f3d90 | 3 x 6 | 17 |
| 0x1f3dd0 | 2 x 1 | 2 |
| 0x1f3e10 | 1 x 1 | 1 |

**136 of 231** cells are enabled. The existing +0x34 pointer retains the
0x200-byte VIF packets; +0x38 is independent cell-selection data.
The first five flags arrays lie in initialised sections immediately preceding
their corresponding records. This was checked against the local retail ISO
and is guarded by retail-backed tests; source assets are never committed.

## Opt-in visual experiment

The new `Rac1AridiaOceanPreview` takes the source record X/Y positions as
**grid origins** (not centres), uses the native 7x7-cell dimensions and step
sizes, omits cells whose byte flag is zero, and uses the actual level-local
Fx texture **40** as its first appearance. A 136-cell 8x8-vertex grid becomes
**13,328** host triangles. The whole pipeline uses the existing neutral
`RuntimeMesh` / `RuntimeTexture` path; no collision proxy or player
mechanic is changed.

The origin placement performed substantially better than centre placement
in a *diagnostic*, collision-centroid proximity check (80/136 enabled cell
centres within 10 units of Aridia class-3 mud sample centroids versus
33/136 centre-based). That is evidence for testing the orientation, not
a proof of the source's VU vertex calculation.

**This is deliberately disabled by default.** On an authorised Windows
checkout with local source images configured:

```powershell
$env:OBP_RAC1_OCEAN_PREVIEW = '1'
./tools/shots.ps1 -Game rac1 -List tools/shots/aridia-ocean.json
Remove-Item Env:\OBP_RAC1_OCEAN_PREVIEW
./tools/shots.ps1 -Game rac1 -List tools/shots/aridia-ocean.json -SkipBuild
```

Compare `captures/shots/rac1-level2-ocean-top.png` and
`rac1-level2-ocean-orbit.png` from the two runs. A source-backed
world can also be loaded by the usual player capture; only the
composition-root R&C1 path enables this developer environment switch.

## Explicit limitations

The flat constant-height plane and UV repetition per 7x7 cell are
**OBP presentation approximations**, not recovered native wave/texture output.
Likewise, the alpha blend-mode selection is provisional. Aridia's retail
VU0 cell deformation, VU1 program-7 STQ/fog/depth, animation between native
Fx frames 40..103, per-frame actor-driven visibility and clipping are
not yet reproduced. Early top-down captures showed a large unwanted
sheet when *every* cell was drawn and the header was treated as centre
coordinates; the masks and origin correction eliminated that particular
layout error, but the remaining appearance still needs native comparison.

This source-owned opt-in is not generalised automatically to Gaspar,
Pokitaru, Oozla or Florana. A later shared liquid renderer should take
neutral **native-backed** geometry, texture frames, material state and
animation data from independent game-specific adapters.

The default retail import must remain pixel-identical to the existing
behaviour (no preview meshes) until a faithful reconstruction is verified.
