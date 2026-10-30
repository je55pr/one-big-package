# OBP architecture

Production is Godot 4 + C#. Retail NTSC-U bytes and executable behaviour are
authority; public tools are cross-checks. Source evidence and deliberate OBP
design stay separate. Creative direction is in [PROJECT_VISION.md](PROJECT_VISION.md).

## Layer ownership

| Layer | Owns |
|---|---|
| `OBP.Core` | Shared provenance, identity and math |
| `OBP.IO` | Bounded/random-access single-file reads, streaming hashes, authority verification |
| `OBP.PS2` | Shared ISO/ELF, compression, texture, packet and collision formats, plus `Presentation/` adapters for neutral runtime materials and supported skeletal assets |
| `OBP.RAC1/2/3` | Source-specific containers, interpretation, recovered behaviour and provenance |
| `OBP.Runtime` | Engine-neutral world/player/actor facts, identities, snapshots and ports; no native formulas or orchestration policy |
| `OBP.Godot` | Neutral data to Godot meshes, materials, collision, audio and presentation objects |
| `game/` | Scene lifecycle, physics contacts, input, UI/camera presentation and composition-root adapter wiring |
| `OBP.Fusion` | Engine-neutral source-qualified gameplay orchestration and player-session policy, world-entry contracts, composition/placement |
| `tests/` and `tools/` | Deterministic imports, probes and contract verification |

`ArchitectureLayeringTests` guards project references/imports. Godot adapters
do not become native parsers; RAC modules do not depend on Godot; Runtime does
not acquire source-game numeric state machines. Share low-level codecs only
where retail evidence supports the common structure. Preserve unknown fields.

The binding source/player/world/actor ownership rules, dependency graph, version-1
ports and staged migration ownership are in [PLAYER_WORLD_CONTRACT.md](PLAYER_WORLD_CONTRACT.md).
Destination origin chooses world content, not player-global services. Source-native
controllers stay in RAC modules; Fusion invokes neutral ports and applies explicitly
labelled OBP policy. The Godot composition root wires those implementations without
copying their rules. The contract PR establishes these seams; the linked follow-ups
replace the current host couplings. Architecture tests freeze that migration debt
and reject new player gates while allowing narrow world/campaign adapters.

## Naming and migration order

Source-owned C# types, filenames and namespace segments use `Rac1`, `Rac2`,
`Rac3`; the source assembly roots remain `OBP.RAC1`, `OBP.RAC2`, `OBP.RAC3`.
For example, `GcLancerSession` / `GcLancerSession.cs` becomes
`Rac2LancerSession` / `Rac2LancerSession.cs`; `IUyaMobyDamageConsumer` becomes
`IRac3MobyDamageConsumer`. Use source-neutral names only for genuinely shared
contracts in the owning Core/Runtime/Fusion/PS2 layer. `Rac1RatchetNanotechSession`
must not become `UniversalHealth` merely because an OBP player uses that policy.

Going Commando, Up Your Arsenal, GC/UYA retail evidence labels, native ISA names
and historical research/reference terminology retain their actual meaning.
This is a C# ownership rule, not a global text replacement. Native class numbers,
disc hashes, Godot UIDs and stored IDs such as `rac2:LEVEL1`, `rac3:TABLE1` and
source wire strings remain stable. Real source identity uses `ObpSourceGame`;
authored worlds require a distinct typed world identity, not a guessed retail
origin. Validated parse/format adapters own external strings. Do not add internal
aliases to keep two competing source vocabularies alive.

Source-facing C# contracts now use `ObpSourceGame`, including actor/weapon keys,
equipment, native targets, authored entity metadata and shared profile sources.
`RuntimeWorld.Game` and composition placements use `WorldContentIdentity`, which
contains either that source enum or an explicit authored `obp:<id>`. The Fusion
Lab is authored `obp:fusion-lab`, with no native source game. Its established
world wire tag remains `fusion`; destination IDs remain `obp:fusion-lab`.

`SourceIdentityWire` is the validated parse/format boundary. Canonical source
strings remain `rac1`/`rac2`/`rac3`; existing comparison-viewer labels such as
`gc`, `R&C1` and `Up Your Arsenal` are accepted on input and written canonically.
Unknown labels fail rather than becoming `Synthetic`. Migrated string JSON
properties opt into their string converter individually; already enum-backed
archive/avatar/replay properties keep their existing numeric JSON encoding.
The v1/v2 `PlaytestWeaponConfiguration` source map is an explicit wire-only
compatibility DTO; its keys stay strings and are formatted from typed runtime
maps. This migration breaks the old free-string C# construction API deliberately,
without changing profile/replay versions, native IDs, builds, destinations or
Godot resources. `SourceIdentityArchitectureTests` prevent new string identity
contracts and raw source routing; the ownership guard also recognises identities
passed separately from their world.

`SourceNamingPolicyTests` checks public types, using aliases and source projects.
Nested source records inherit their enclosing type's context, so names such as
`Rac2LancerSession.State` stay useful. Neutral `RuntimeWorld`, `Moby`, and shared
PS2/ISA types do not need a game prefix. Existing noncanonical public declarations
are exact migration entries in `source-naming-legacy.json`; each entry must
disappear when its declaration is renamed. New legacy aliases or moving one
into a different file fail the check. The census is a reviewed policy artifact;
changing it to admit new migration debt is prohibited and requires review just
as changing the check does. A genuine historical/retail exception must
name its exact declaration and reason, retain its layer ownership, and include
a focused guard regression; a broad namespace/file exemption is insufficient.
The existing `GcUyaMoby`, `GcUyaMobyPose`, `GcUyaGadgetPose` shared PS2 geometry
vocabulary and their named tests are retained as exact historical-format
exceptions. They describe the already evidenced cross-source codec, not a new
Rac2/Rac3 gameplay branch. New cross-source APIs should use neutral names.

The cleanup programme [#307](https://github.com/je55pr/one-big-package/issues/307)
uses separate PRs with manual review/merge:

The app project, assembly and C# namespace are `OBP.Game`. Direct builds use
`dotnet build game/OBP.Game.csproj`; replace the former
`game/OneBigPackage.csproj` path in local commands. The supported `tools/play`
and launch wrappers keep their existing entry points. The solution remains
`OneBigPackage.sln`, the Godot directory remains `game/`, and the game brand,
scene/script UIDs, resource paths and user-data location are unchanged. Existing
recordings still parse; the renamed assembly is a different build for replay
compatibility checks, just as any changed assembly is.

1. [#289](https://github.com/je55pr/one-big-package/issues/289) establishes this policy and guard.
2. After policy merge, [#290](https://github.com/je55pr/one-big-package/issues/290)
   migrates source identity while retaining wire formats.
   [#174](https://github.com/je55pr/one-big-package/issues/174) owns the single
   strict CLI parser and can proceed once its interface assumptions agree.
   The isolated UYA admission/audit fixes #175/#176 precede source renaming.
3. [#291](https://github.com/je55pr/one-big-package/issues/291) and
   [#292](https://github.com/je55pr/one-big-package/issues/292) rename source
   symbols/files mechanically after typed contracts. Coordinate shared host edits.
4. [#293](https://github.com/je55pr/one-big-package/issues/293) moves the app to
   `OBP.Game`; #294 migrates public source flags with explicit old aliases and
   uses #174's parser. #295 decides the comparison viewer's fate before flags
   are removed. These are later migrations, not exceptions for new source APIs.

Do not overlap these host-file renames with #308/#309 ownership/actor extraction.
`OBP.Fusion` means cross-game integration. The legacy comparison viewer and its
CLI/document path were retired under #295. Naming-only changes
must leave native gameplay and all existing external contracts unchanged.

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
not a universal mission VM. Existing Godot partials still wire and coordinate
these sessions; the reconciliation contract assigns engine-neutral gameplay
coordination to Fusion and retains scene/contact plumbing in the host. The
current mixed responsibilities have explicit migration owners. Neither layer
defines native constants or invents shared class semantics.

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
retain their own selector contracts. The selected avatar's actual source/provider
must resolve independently of destination origin. The reconciliation contract
selects genuine R&C1 Ratchet as the initial ordinary profile on every world;
The shared entry path resolves that selected provider before unloading the world;
a capsule requires explicit diagnostic mode. #268 owns complete profile admission.
GC/UYA skins are not admitted just because their skeletons are compatible.
Missing selected assets must fail visibly. Movement is authoritative
outside the animation mapping. Neutral skeleton metadata is optional until
its transform semantics are independently established.

## Host lifecycle and presentation

The [host ownership map](OBPGAME_HOST_OWNERSHIP.md) records the field census and
staged #308 extraction. `WorldSceneLifetime` owns candidate/live scene resources
and deferred navigation; Fusion retains player/entry policy and source adapters
retain authored starts. Root world facts are read-only views of that owner.

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
