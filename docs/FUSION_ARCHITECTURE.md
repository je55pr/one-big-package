# Fusion ownership and retained research

`OBP.Fusion` is the engine-neutral integration module: source-qualified gameplay
orchestration, persistent player-session policy, staged world entry, actor
registration, equipment bindings and provenance. Native parsers, item behaviour
and state machines remain in `OBP.RAC1/RAC2/RAC3`; Godot owns scenes, contacts,
input and presentation.

The [player/world/actor contract](PLAYER_WORLD_CONTRACT.md) is the ownership
authority. Fusion may reference Core and Runtime, not RAC modules, native
IO/PS2 decoders or Godot. Source adapters project native facts through neutral
ports; the host composition root registers those adapters. A destination never
selects the player's provider or grants player-global capabilities.

## Comparison viewer retirement (#295)

The free-fly comparison viewer, Godot scene/renderer, source loader, startup and
capture modes, placement/document models and JSON persistence have been removed.
The unused `FusionSession`/`FusionExhibit` planning descriptors were also retired;
the live `FusionSourceIdentity` remains for actors, Lab definitions and equipment
presentation bindings. Their source-qualified identity and JSON shape are intact.

The numerical `CompositionTransform`, `AnchorPair` and `PlanarAlignmentSolver`
remain because the original retail Veldin comparison and synthetic numerical
regressions independently consume them. They have no viewer or gameplay host.
See [the retirement/research note](WORLD_COMPOSITION.md) and the historical
[Veldin study](../research/VELDIN_CROSS_GAME_ALIGNMENT.md).

## Playable integration

The authored Fusion Lab uses the same `ObpPlayerSession` and `WorldEntrySession`
as native destinations. World leases own transient scenes, contacts and actors;
player configuration, health, loadout and progress persist independently.
Actor registrations and equipment presentation bindings use `FusionSourceIdentity`
and existing source-qualified keys. A descriptor is never proof of usability:
source modules still validate their native context, and unavailable capabilities
produce explicit results.

The former `OBP.Composition` assembly remains retired. No duplicate project,
comparison command router or compatibility viewer is maintained. Run the normal
portable/retail suites, source-free Godot bootstrap and reconciliation gate for
the retained player/world flow.
