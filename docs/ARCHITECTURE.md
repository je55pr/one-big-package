# OBP architecture

Production is Godot 4 + C#. Retail NTSC-U bytes and executable behaviour are
authority; public tools are cross-checks. Source evidence and deliberate OBP
design stay separate. Creative direction is in [PROJECT_VISION.md](PROJECT_VISION.md).

## Layer ownership

| Layer | Owns |
|---|---|
| `OBP.Core` | Shared provenance, identity and math |
| `OBP.IO` | Bounded/random-access file and split reads, streaming hashes, authority verification |
| `OBP.PS2` | Shared ISO/ELF, compression, texture, packet and collision formats |
| `OBP.RAC1/2/3` | Source-specific containers, interpretation, recovered behaviour and provenance |
| `OBP.Runtime` | Evidence-justified engine-neutral world, player, presentation, audio and gameplay concepts |
| `OBP.PS2.Presentation` | Narrow shared PS2-material to neutral-runtime mapping; no game-specific semantics |
| `OBP.Godot` | Neutral data to Godot meshes, materials, collision, audio and presentation objects |
| `game/` | Application lifecycle, input, UI, cameras and host coordination |
| `OBP.Composition` | World composition schema, transforms and anchor solving |
| `OBP.Cli` / tests | Deterministic imports, probes and contract verification |

`ArchitectureLayeringTests` guards project references/imports. Godot adapters
do not become native parsers; RAC modules do not depend on Godot; Runtime does
not acquire source-game numeric state machines. Share low-level codecs only
where retail evidence supports the common structure. Preserve unknown fields.

## Sources and destination providers

`TrilogySourceDefinitions` supplies source-owned authority identities to the
engine-neutral `ObpSourceLibrary`. Attachment performs bounded ISO/SYSTEM.CNF/
ELF/serial probing plus exact expected payload length. Recognized serial alone
does not admit an unsupported revision. Full cryptographic verification is an
explicit streaming operation, not a startup multi-gigabyte read.

`user://sources.json` stores game, expected build and local path. Restore reopens
and re-identifies each file; saved metadata is not proof. Forget removes only
the attachment. Retail bytes remain in user-owned files outside Git.

Source availability, destination discovery and loadable-provider availability
are distinct. `ObpDestination` preserves destination/build/game/native identity
separately from display names. `ObpWorldProviderRegistry` resolves the correct
`IObpWorldProvider`, whose native importer builds `RuntimeWorld` on demand.
No exported world JSON or handmade planet scene is required.

## World and entity boundary

`RuntimeWorld` holds decoded render/collision, environment and authored objects.
`Ship` and optional `PlayerStart` are distinct source facts. Hosts prefer a
proven player start and otherwise use the supported fallback. Coordinate and
material interpretation belongs to source providers, not scene builders.

`RuntimeDynamicObject` is immutable authored identity/payload/presentation
capability. `RuntimeEntityState` is a replaceable live snapshot of stable
identity, neutral transform, Active/Inactive presence and animation role.
PVar bytes, native state numbers, health, AI and transition reasons remain
source-owned. Visibility is not gameplay lifetime. See
[the lifecycle contracts](../research/TRILOGY_ENTITY_LIFECYCLE.md).

`RuntimeWorldScene` builds static, dynamic, animated and collision presentation
through focused adapters behind one Build entry point. GC/RAC1/RAC3 world-import
coordinators compose their native decoders without pushing formats into Godot.

## Source-game gameplay sessions

RAC1 sessions own Moby registration/state, class controllers, damage events,
Nanotech, equipment, actions, pickups, persistence, motion and camera laws.
`Rac1MobyRuntimeSession` dispatches native class updates/damage; class-local
controllers own their predicates and consequences. `Rac1GameplayDamageEvent`
keeps source, owner, target, amount, flags and marker distinct. Damage transport
publishes synchronously before the target consequence. Terminal lifetime is
separate from an optional native terminal-state byte.

The host supplies contacts and presentation facts. `Rac1PlayerContactResult`
retains raw/effective material semantics, support identity, anchor carry and
conveyor channels. `Rac1MobyPersistenceSession` is a per-level UID boundary,
not a universal mission VM. Godot gameplay partials coordinate these sessions;
they do not define native constants or invent shared class semantics.

## Player movement, camera and animation

`PlayerControlIntent` carries unconditioned normalized signed axes, button
edges and a neutral control basis. Source movement applies its conditioner
and emits native displacement per 60-Hz update; Godot supplies contact and
converts displacement to engine motion. Applying the RAC1 movement base in
GC/UYA worlds is an OBP design choice, not proof of equal retail controllers.
Development fly/reset/diagnostics are separate from ordinary movement law.

Camera snapshots and obstruction facts are neutral. Source modules own recovered
recurrences and selectors; unsupported native camera channels remain explicit
host presentation policy. A host camera vector does not establish attack facing.

`PlayerAvatar` clips are model-local, with native timing, attachment tracks and
optional skeleton metadata. Placed `RuntimeAnimatedMesh` frames are world-space;
the two contracts cannot be interchanged. Source avatar providers parse assets;
source animation controllers resolve numeric/native selection into neutral
presentation. `PlayerAvatarView` consumes the resolved sampled pose and cannot
invent source selectors or advance a physics-owned clock during rendering.

RAC1 exposes a verified Ratchet carrier and source-aware animation controller.
Its ordinary locomotion, crouch, stop and landing clocks are documented in
[the animation reference](../research/RAC1_LOCOMOTION_ANIMATION.md). GC/UYA
retain their own selector contracts; a source without an admitted renderable
avatar fails closed instead of borrowing RAC1 assets. Movement is authoritative
outside the animation mapping. Neutral skeleton metadata is optional until
its transform semantics are independently established.

## Host lifecycle and presentation

`ApplicationLifecycle` owns explicit quit requests with a reason/exit code.
Desktop close remains distinct from Back/navigation; Escape from gameplay or
source/selector UI must not accidentally become process exit. Automated gates
must reach their own completion; closing their window is cancellation.

Opt-in smoke dispatch belongs in `game/diagnostics/`; the world loader invokes
one diagnostic boundary after successful loading. Staged combat/contact fixtures
and ordinary-play routes establish different claims. See
[RETAIL_TESTING.md](RETAIL_TESTING.md) for commands and gate boundaries, and
[GODOT_PRESENTATION.md](GODOT_PRESENTATION.md) for scene/material/UI details.

## Evidence, storage and design

Bounded source readers and streaming hashes avoid loading complete ISOs into
memory. Derived caches must be rebuildable; caches, render visibility and host
saves do not become retail authority. Commit only payload-free evidence and
keep current native/source tests alongside the relevant behaviour.

Unknown native semantics stay unknown until recovered. Cross-game campaign,
progression or remix choices belong above the native model and must be labelled
as OBP design. Keep implementation status in code/tests and Git history,
technical source contracts in research, and speculative ideas in brainstorming.
