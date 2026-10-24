# Fusion ownership and comparison migration (#260, #265)

OBP.Fusion is the engine-neutral integration module: source-qualified gameplay
orchestration, persistent player-session policy, world-entry contracts, scenario
and exhibit descriptions, provenance, and reusable transform/placement math.
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

The [source/player/world/actor contract](PLAYER_WORLD_CONTRACT.md) is the single
ownership authority. Fusion may reference Core and Runtime, not RAC modules,
native IO/PS2 parsers or Godot. Native controllers implement neutral ports;
the host composition root supplies their adapters to Fusion. Cross-game admission,
target routing and player policy belong here, not in scene callbacks or native formulas.

#254's room layout/greybox and #241's weapons are merged. #265 establishes version-1
contracts and CI guards. #266/#267 implement `ObpPlayerSession` and
`WorldEntrySession`, with one host staging/activation path for native worlds and
the Lab. Source-native state and host preference adapters project into frozen
snapshots without adding RAC or Godot dependencies to Fusion. Complete profile
admission, movement/health policy, the equipment catalog, mixed combat and ordinary
pause UI remain #268/#269/#270, #255, #256 and #259. See the canonical contract's
file/member ownership table before editing shared host partials.
The shared FusionSession/FusionExhibit/FusionSourceIdentity are adapter-neutral:
a descriptor is not proof of usability. The composition root resolves source
handles and native adapter registrations; source modules validate native context,
and Fusion owns cross-source admission/routing policy. Scene code supplies contacts
and presentation rather than choosing behaviour from world origin.

Regression: build the solution and run portable OBP.Tests; --compose remains a
separate optional diagnostic path when local original-game inputs are present.
