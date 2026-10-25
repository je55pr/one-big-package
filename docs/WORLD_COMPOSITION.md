# Retired world comparison viewer

The legacy CompositionLab alignment/comparison viewer was removed under #295.
The playable [Fusion Lab](FUSION_LAB.md) remains an ordinary OBP destination;
it uses the same player session and world-entry lifecycle as native worlds.

The viewer scene, host, world loader, comparison renderer, world-placement
documents and their JSON serializer are no longer shipped. Saved comparison
documents are no longer read by the application. This retirement does not change
game profiles, native campaign payloads, replay schemas or destination IDs.

These discontinued options now fail explicitly as unknown OBP arguments:

- `--compose`
- `--composition`
- `--composition-view`
- `--compose-worlds`
- `--compose-solve`
- `--compose-solve-scale`
- `--compose-reload`

The former composition parameters of `tools/capture.ps1` were removed too.
Normal destination capture, movement/replay tooling and the playable Lab remain
supported through their existing entry points.

## Retained original-game research

[Veldin cross-game alignment](../research/VELDIN_CROSS_GAME_ALIGNMENT.md) records
the original quantitative retail study. Its findings and retail witness are
retained; the historical viewer instructions are not a supported launch path.

`VeldinAlignmentRetailTests` still checks the real retail landmark comparison.
`CompositionTransform`, `AnchorPair` and `PlanarAlignmentSolver` remain as the
small, engine-neutral numerical helpers used by that research and its synthetic
invariant tests. They do not load worlds, own gameplay state or expose a viewer.
Run `tools/test-retail.ps1` with authorised trilogy inputs to reproduce the
retail witness. No disc bytes or machine-specific paths are stored here.
