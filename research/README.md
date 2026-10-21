# Retail research references

Retail bytes/executable behaviour are authority. Public comparisons remain
labelled; OBP host/design policies stay separate. Current implementation and
validation live in code/tests and Git, not completed-task reports.

## Source identity and shared formats

- [Input provenance](INPUT_PROVENANCE.md), [build confidence](BUILD_PROBING.md), [disc layouts](RETAIL_TRILOGY_DISC_LAYOUT.md).
- [External-source policy](EXTERNAL_SOURCE_POLICY.md), [public comparisons and packet boundaries](PUBLIC_FORMAT_ARCHAEOLOGY.md).
- [Material semantics](MATERIAL_SEMANTICS.md), [atmosphere](WORLD_ATMOSPHERE.md), [world effects](WORLD_EFFECTS.md).
- [Entity lifetime](TRILOGY_ENTITY_LIFECYCLE.md), [common movement authority](RATCHET_MOVEMENT_BASE.md).
- [Generated evidence manifest](generated/MANIFEST.md): current producers, frozen witnesses and authority inventories. Generated evidence is not disposable.

## Ratchet & Clank

| Area | References |
|---|---|
| Disc/core data | [Disc index](RAC1_DISC_INDEX.md), [overlay mapping](RAC1_LEVEL_OVERLAY.md), [settings](RAC1_LEVEL_SETTINGS.md), [instances](RAC1_INSTANCES.md) |
| Graphics | [Terrain](RAC1_STATIC_GEOMETRY.md), [textures](RAC1_TEXTURES.md), [sky](RAC1_SKY.md), [Mobies](RAC1_MOBY.md), [skinning](RAC1_MOBY_SKINNING.md), [Moby animation/audio](RAC1_MOBY_ANIMATION_SOUND.md) |
| Player | [Movement](RAC1_PLAYER_MOVEMENT.md), [animation and handoffs](RAC1_LOCOMOTION_ANIMATION.md), [avatar](RAC1_PLAYER_AVATAR.md), [camera](RAC1_CAMERA_ARCHAEOLOGY.md) |
| Contact/traversal | [Collision and ledges](RAC1_LEDGE_TRAVERSAL_AND_COLLISION_PROFILE.md), [contact carry](RAC1_CONTACT_MOTION_CARRY.md), [surfaces](RAC1_SURFACE_MOVEMENT.md), [airborne packs/flips](RAC1_AIRBORNE_AND_MOVEMENT_EQUIPMENT.md), [traversal equipment](RAC1_TRAVERSAL_EQUIPMENT.md), [Swingshot pendulum](RAC1_SWINGSHOT_PENDULUM.md) |
| Combat/equipment | [Wrench actions](RAC1_WRENCH_ACTIONS.md), [wrench model](RAC1_WRENCH_VISUAL.md), [Bomb Glove](RAC1_BOMB_GLOVE.md), [inventory](RAC1_WEAPON_INVENTORY.md), [fire framework](RAC1_WEAPON_FIRE_FRAMEWORK.md), [contact records](RAC1_PROJECTILE_HIT_PATTERNS.md) |
| Working arsenal | [Additional weapons](RAC1_ADDITIONAL_WEAPONS.md), [ordinary arsenal](RAC1_ARSENAL.md), [pose/audio contracts](RAC1_WEAPON_PRESENTATION_AUDIO_FIXES.md) |
| Gameplay/populations | [Crates/bolts](RAC1_CRATES_PICKUPS.md), [Veldin enemies](RAC1_VELDIN_ENEMIES.md), [common hostile state](RAC1_HOSTILE_COMMON_STATE.md), [attack contracts](RAC1_ENEMY_ATTACK_PATTERNS.md), [Aridia enemies](RAC1_ARIDIA_ENEMIES.md), [Kerwan taxis](RAC1_METROPOLIS_TAXIS.md) |
| Census/progression | [Authored Mobies](RAC1_AUTHORED_MOBY_CENSUS.md), [class families](RAC1_CLASS_FAMILIES.md), [NPC/interactables](RAC1_NPC_INTERACTABLE_CENSUS.md), [travel](RAC1_CAMPAIGN_TRAVEL.md), [checkpoints](RAC1_CHECKPOINTS.md), [start](RAC1_VELDIN_SPAWN.md), [health/death/restart](RAC1_DEATH_RESPAWN.md) |

## Going Commando

| Area | References |
|---|---|
| Disc/core | [Loading/RC2.HDR](GC_LEVEL_LOADING.md), [catalogue](GC_LEVEL_CATALOGUE.md), [names](GC_PLANET_NAMES.md), [WAD](GC_LEVEL_WAD.md), [core](GC_LEVEL_CORE.md), [settings](GC_LEVEL_SETTINGS.md) |
| Graphics/contact | [Tfrags](GC_TFRAG.md), [TIEs/shrubs](GC_TIES.md), [textures](GC_TEXTURES.md), [Mobies](GC_MOBY.md), [collision](GC_COLLISION.md), [coordinates/chunks](GC_COORDINATES_AND_CHUNKS.md), [sky](GC_SKY.md), [lighting](GC_LIGHTING.md) |
| Gameplay/player/audio | [PVars](GC_PVARS.md), [Moby catalogue](GC_MOBY_CATALOGUE.md), [crates](GC_CRATES.md), [Aranos](GC_ARANOS_OPENING.md), [player selectors and retained leads](GC_PLAYER_ANIMATION_STATES.md), [attacks](GC_WRENCH.md), [audio](GC_AUDIO.md) |

## Up Your Arsenal

- [Disc discovery and boot identity](UYA_RETAIL_TOC.md).
- [Native world formats/import and evidence](UYA_PRODUCTION_IMPORTER.md).
- [Moby animation](UYA_MOBY_ANIMATION.md).
- [Player states, selection and movement witnesses](UYA_PLAYER_ANIMATION_SELECTION.md).
- [Veldin class-runtime recovery](UYA_VELDIN_GAMEPLAY.md).

## Using the project

[Architecture](../docs/ARCHITECTURE.md), [testing](../docs/RETAIL_TESTING.md),
[saved repros](../docs/MOVEMENT_REPRO_STATES.md), [weapon controls](../docs/RAC1_WEAPONS_TESTING.md),
and [Veldin composition alignment](VELDIN_CROSS_GAME_ALIGNMENT.md).
Speculative design stays in [brainstorming](../docs/brainstorming/README.md).
