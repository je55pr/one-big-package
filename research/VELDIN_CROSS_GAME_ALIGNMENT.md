# R&C1 / UYA Veldin cross-game alignment

Authority:

- R&C1 NTSC-U `SCUS-97199`, native level 0.
- UYA NTSC-U `SCUS-97353` / `rac3-ntscu-original`, native table 1.

This note records a local retail-only archaeology pass investigating whether the two Veldin reconstructions can be related by one rigid planar transform. It deliberately distinguishes evidence of strong local layout resemblance from proof of shared lineage or one coordinate frame for the complete maps.

## Question and boundary

`OBP.Composition.PlanarAlignmentSolver` can exactly solve a rigid planar transform once equivalent landmarks are known. The archaeological problem is therefore not the solver. It is establishing equivalent landmarks without choosing points because they happen to make an attractive fit.

No transform from this note is admitted into production runtime or Godot presentation. Candidate object correspondences below remain archaeological hypotheses unless stated otherwise.

## Ratchet-centred collision evidence

The retail class-0 Ratchet placement was used only as a translation anchor. Collision point clouds were converted to OBP Y-up coordinates, centred on Ratchet, clipped by planar radius, deterministically down-sampled to at most 320 points, and compared with a 70%-trimmed bidirectional 3D Chamfer score. R&C1 levels 0-18 were scanned against UYA table 1 in 5-degree rotation steps.

| radius | R&C1 level 0 rank | best coarse rotation | trimmed distance | next-best level |
|---:|---:|---:|---:|---:|
| 80 | 4 | 10 deg | 5.572 | level 5 at 4.279 |
| 120 | 1 | 0 deg | 6.589 | level 7 at 7.317 |
| 160 | 1 | 5 deg | 7.051 | level 7 at 7.780 |
| 180 | 1 | 5 deg | 6.646 | level 7 at 8.040 |
| 200 | 1 | 5 deg | 7.492 | level 7 at 8.216 |
| 220 | 1 | 5 deg | 7.932 | level 7 at 8.636 |
| 260 | 2 | 5 deg | 9.477 | level 7 at 9.179 |

The stable level-0 win from radius 120 through 220 is evidence that the UYA table-1 neighborhood around Ratchet resembles R&C1 Veldin more strongly than the other R&C1 levels under this metric. The loss at radius 80 and near-tie/loss at radius 260 are equally important: this is not evidence for one unchanged whole-map transform.

## Shape-only object matching probe

A separate scratch-only heuristic compared placed retail Moby/TIE/shrub model geometry using deterministic area-weighted surface sampling. Model matching did not use authored world positions. As a control, R&C1 Moby class 13 ranked UYA class 13 first with a substantial score gap, showing the descriptor could recover at least one known cross-game identity.

For placed static geometry, several strong mutual nearest-neighbor shape pairs appeared. One relevant pair was R&C1 shrub class `467` to UYA shrub class `3364` with descriptor score `0.01367`. This score is specific to the exploratory descriptor and is not a native semantic field or production contract.

A placement selected under the earlier approximately-5-degree terrain hypothesis landed only about `0.29` world units apart. That initially looked like a strong landmark candidate. A broader falsification pass rejected treating it as independent confirmation.

## Rotation-blind placement check

To remove the terrain-angle prior, all authored placements of shrub `467` and shrub `3364` were centred on their respective Ratchet positions. The R&C1 placements within 80 planar units were then rotated through all 360 degrees in 0.25-degree steps and scored against all UYA class-3364 placements by nearest-neighbor distance.

The best 70%-trimmed placement-cloud score occurred around **31 degrees**, not 5 degrees:

- approximately 31 deg: trimmed mean `2.635`, median `3.738`, 6 source placements within 3 units;
- 5 deg: trimmed mean `5.747`, median `7.453`, 4 source placements within 3 units;
- 0 deg: trimmed mean `6.643`, median `8.403`, 3 source placements within 3 units.

Because UYA class `3364` has many authored placements, nearest-neighbor coincidence is easy to manufacture. The 0.29-unit candidate therefore cannot be used as an independent anchor merely because its model geometry also resembles R&C1 class `467`.

## Candidate-anchor solver result

The repository solver was run on Ratchet plus three candidate shrub correspondences. Solver direction here is R&C1 local space into UYA local space, both in OBP Y-up coordinates.

Using Ratchet plus candidate shrub `467[14] -> 3364[254]` only gives an exactly determined two-anchor rigid fit:

- rotation: `-8.057710` deg;
- RMS over the two fitted anchors: `0.014481`;
- candidate `467[17] -> 3364[253]`, held out from that fit: residual `1.698666`.

Adding that second shrub candidate to the fit changes the result substantially:

- rigid rotation: `-18.108433` deg;
- rigid RMS over three fitted anchors: `0.640324`;
- candidate `467[13] -> 3364[245]`, held out: residual `5.382812`.

Allowing uniform scale does not rescue a single clean transform:

- fitted scale: `0.975826238`;
- rotation remains `-18.108433` deg;
- fitted RMS: `0.636754`;
- held-out residual: `5.145397`.

These numbers are pinned by `VeldinAlignmentRetailTests` so later parser or coordinate-basis changes cannot silently turn this exploratory evidence into a different conclusion.

## Current conclusion

The authority data supports **strong local layout resemblance around the Veldin start area**, especially at roughly 120-220 world-unit collision neighborhoods. That resemblance is consistent with reuse or shared ancestry, but does not prove either. It does **not** currently support one production-ready rigid transform for complete R&C1 Veldin -> UYA Veldin alignment.

Safe interpretation:

- keep R&C1 level 0 and UYA table 1 as independently reconstructed worlds;
- use `OBP.Composition` for neutral comparison only;
- do not bake the exploratory 5-degree, -8-degree, -18-degree, or scaled transforms into either importer;
- require future landmarks to be established independently of the transform they are intended to validate;
- treat disagreement between local neighborhoods as evidence of level editing/deformation unless stronger native evidence shows otherwise.

A useful next archaeology pass would look for sparse, uniquely placed static landmarks whose semantic equivalence can be established from model structure, instance metadata, executable behavior, or another independent source. Those anchors can then be held out from terrain registration entirely and tested with `PlanarAlignmentSolver`.

## Reproduction boundary

`VeldinAlignmentRetailTests` is the committed, payload-free reproduction hook for the candidate-anchor solver results. It requires both `OBP_RAC1_ISO` and `OBP_UYA_ISO` and therefore runs only under the authorized local retail gate.

The collision-cloud ranking and exploratory mesh descriptor were local archaeology probes, not production algorithms. Their summarized metrics are retained here for provenance; no retail geometry, textures, executables, or extracted payloads are committed.
