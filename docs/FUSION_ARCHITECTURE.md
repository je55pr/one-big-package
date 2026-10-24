# Fusion ownership and comparison migration (#260)

OBP.Fusion is the engine-neutral integration module: source-qualified session and
exhibit descriptions, provenance, and reusable transform/placement math.
It deliberately does not implement native AI or convert source-specific mechanics.
Native parsers, item behaviours and state machines remain in OBP.RAC1/RAC2/RAC3.
Shared input/world concepts remain in OBP.Runtime; Godot presentation and UI
remain in the game host and OBP.Godot.

## Legacy census and disposition

* Retained neutral transforms and placement: CompositionTransform, WorldPlacement,
  WorldComposition and deterministic CompositionJson moved into OBP.Fusion.
  The existing version 1 compositions/*.json is byte-compatible.
* Retained diagnostic archaeology: AnchorPair and PlanarAlignmentSolver moved
  into OBP.Fusion. The free-fly CompositionLab, CompositionWorldLoader,
  CompositionView, CLI --compose flags, capture script and original research
  stay operational. They are not ordinary Fusion Lab gameplay.
* Retired: the standalone OBP.Composition assembly, project file and all project
  references. The former code was a viewer, not an ordinary-world dependency.
* Reproducibility: research/VELDIN_CROSS_GAME_ALIGNMENT.md, payload-free authored
  comparison JSON and tools/capture-veldin-comparison.ps1 remain available.
  Runtime native source data and local ISO paths are never persisted here.

## Integration contract

#254 owns authorable room layout, the new playable scene host and the existing
normal Ratchet pipeline. #255 owns the equipment catalog, #256 live combat
adapters, #257 reset/sandbox controls, #258 exhibits, #259 pause/loadout UI.
The shared FusionSession/FusionExhibit/FusionSourceIdentity are adapter-neutral:
a source-qualified id and explicit adapter may be described, but it is the game
host's job to resolve and validate actual source availability and behaviours.

Regression: build the solution and run portable OBP.Tests; --compose remains a
separate optional diagnostic path when local original-game inputs are present.
