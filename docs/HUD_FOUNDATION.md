# HUD foundation

Status: foundation contract, engine-independent state adapter, R&C1 projection and a non-interactive Godot renderer are implemented. This document defines the smallest player-HUD presentation model needed by the current playable slices. Retail-faithful artwork, layout and timing remain unresolved.

## Purpose and ownership

The HUD is an observer. Gameplay authority stays where it already lives: source-game/session code owns health, economy, inventory, damage, pickups, interaction eligibility and every transition that changes those values. The HUD consumes a read-only projection of that state.

The intended dependency direction is:

```text
source-game / OBP gameplay authority
        -> engine-independent HUD snapshot + transient events
        -> game host presentation adapter
        -> Godot controls, layout, animation and input glyphs
```

The reverse direction is forbidden. A HUD widget must never spend ammo, add Bolts, apply damage, choose a weapon, decide whether an interaction is legal, or parse native payloads/PVars to discover gameplay state.

This follows the repository-wide boundary in [ARCHITECTURE.md](ARCHITECTURE.md): native/game-specific meaning stops before Godot, while presentation remains replaceable. A non-Godot host should be able to render the same snapshot.

## Current baseline

The repository already has useful state, but it is intentionally uneven and mostly source-specific:

- R&C1 owns bounded Nanotech/life state in `Rac1RatchetNanotechSession`; the witnessed Veldin reset value is four, not a trilogy-wide maximum-health claim.
- R&C1 owns a minimal wrench/item-10 inventory plus equipped selection and item-10 ammo in `Rac1WeaponInventory`; `Rac1BombGloveSession` proves a 40-round Bomb Glove capacity and one-round accepted-shot cost.
- R&C1 and GC have bounded Bolt-crate sessions that can emit/collect proven denominations. Their current `CollectedBolts` counters are slice/session telemetry, not a reconstructed trilogy wallet or save economy.
- Ordinary contextual gameplay-prompt state is not yet represented as an authoritative runtime concept.
- `DebugPlayer.PlayerHud`, `OBPGame._worldHud`, `GetRac1GameplayHudLine()` and `GetCrateDebugHudLine()` are developer telemetry. They expose positions, controller tuning, native states and smoke-test facts that do not belong in a shipping player HUD.
- `UiTheme` is an OBP application theme. It does not establish retail HUD fonts, colours, panel shapes, spacing or animation timing.

Relevant retail-backed evidence is recorded in [RAC1_NANOTECH_DAMAGE_DEATH.md](../research/RAC1_NANOTECH_DAMAGE_DEATH.md), [RAC1_WEAPON_INVENTORY.md](../research/RAC1_WEAPON_INVENTORY.md), [RAC1_BOMB_GLOVE.md](../research/RAC1_BOMB_GLOVE.md), [RAC1_CRATES_PICKUPS.md](../research/RAC1_CRATES_PICKUPS.md) and [GC_CRATES.md](../research/GC_CRATES.md).

## Minimal snapshot

A future shared contract should be conceptually equivalent to:

```text
HudSnapshot
  Epoch
  Revision
  Health?            HudHealth
  Bolts?             HudCurrency
  CurrentWeapon?     HudWeapon
  Feedback[]         HudFeedbackEvent
  ContextPrompt?     HudContextPrompt
```

`Epoch` changes when the observed gameplay session is replaced, such as a source/world/session reset. `Revision` increases within one epoch. Together they let a presenter reject stale snapshots and de-duplicate transient events without using wall-clock time as gameplay truth.

Every major field is optional. Unsupported or unresolved state is **absent**, never fabricated as zero, empty, full or “default”. That rule matters immediately for GC/UYA, where a polished-looking zero-Bolt counter or health bar would imply authority that does not yet exist.

The snapshot is an atomic observation after a gameplay update. Presentation may interpolate its own pixels, but it must not combine values sampled from different gameplay revisions.

## Health / Nanotech

`HudHealth` needs only:

| Field | Meaning |
|---|---|
| `Current` | Current authoritative health/Nanotech quantity. |
| `Capacity?` | Optional meaningful display capacity, only when the owning gameplay model actually knows one. |
| `UnitKey` | Stable presentation semantic such as `nanotech`; not a native address or source enum. |
| `LifeState?` | Optional projected alive/dead state when gameplay owns that distinction. |

For the current R&C1 slice, `Current` comes from `Rac1RatchetNanotechSnapshot.Nanotech` and `LifeState` from that same snapshot. The witnessed Veldin `RespawnNanotech == 4` must **not** automatically populate `Capacity`: research explicitly limits four to the witnessed initial/respawn state and leaves upgrades/broader rules unresolved.

A HUD must not infer death solely from `Current == 0`. The gameplay session owns life-state transitions, including environmental-death sequencing. Likewise, the HUD must not heal on respawn; it only observes the post-respawn snapshot.

## Bolts

`HudCurrency` needs:

| Field | Meaning |
|---|---|
| `CurrencyKey` | Stable semantic, initially `bolts`. |
| `Balance` | Authoritative player-facing balance for the observed session. |

Pickup amounts do not belong in `Balance` until the gameplay/economy owner has credited them. A presenter may animate a pickup event toward the counter, but the displayed settled total is always the authoritative `Balance`.

The existing R&C1 and GC crate sessions prove denominations and collection consequences, but neither currently constitutes the full player economy. Their debug `CollectedBolts`, GC deferred value, outstanding-pickup count and payout selectors stay out of the normal HUD contract unless a future gameplay session deliberately promotes the relevant value to an authoritative wallet.

This also prevents the HUD from “fixing” economy gaps by summing visible pickups itself.

## Current weapon and ammo

`HudWeapon` needs:

| Field | Meaning |
|---|---|
| `PresentationKey` | Stable weapon/item presentation identity, independent of native memory layout. |
| `NameKey?` | Optional localized/display-name key when naming provenance is established. |
| `Ammo?` | Optional `HudAmmo`; absent for ammo-free weapons or when ammo semantics are unknown. |

`HudAmmo` contains `Current` and optional `Capacity`. Zero is a real current value; absence means “not applicable / not represented”, not zero.

For the current R&C1 slice, equipped native ids 8 and 10 are retail-backed inventory state. The wrench maps to an ammo-less presentation. Item 10 maps to the Bomb Glove presentation where that corroborated name is accepted, with current ammo owned by `Rac1WeaponInventory`; the Bomb Glove descriptor-backed capacity of 40 may populate `Capacity`. Selection and firing remain R&C1 gameplay operations, never HUD actions.

The foundation intentionally excludes inventory lists, quick-select ordering, weapon experience, upgrades, purchase state and acquisition. Those need their own recovered or explicitly designed gameplay models before HUD presentation can consume them.

## Transient pickup and damage feedback

Persistent values and transient feedback are different channels. The HUD should not detect a pickup by noticing `Bolts.Balance` increased, or detect damage by noticing health decreased: loads, respawns, scripted state replacement and future reconciliation could produce the same deltas without the same player-facing event.

`HudFeedbackEvent` therefore needs:

| Field | Meaning |
|---|---|
| `EventId` | Stable unique id within the current `Epoch`. |
| `Kind` | At minimum `Pickup` or `Damage`. |
| `ResourceKey?` | Optional semantic such as `bolts`, `nanotech` or `ammo`. |
| `Delta?` | Optional signed authoritative quantity associated with the event. |
| `SubjectKey?` | Optional item/source presentation identity when the authority can name one safely. |

Gameplay or a thin source-specific projection creates the event at the admitted transition. The presenter remembers consumed `EventId` values for the current epoch and chooses how to animate them.

Animation lifetime, easing, counter tweening, screen tint, shake, floating text, icon scale and sound synchronization are presentation policy unless retail evidence later promotes exact behaviour. They must not be smuggled into gameplay state merely to make the HUD animate.

For today's R&C1 slice, a collected crate pickup can supply a retail-backed denomination delta, and the admitted class-749 hit can supply a one-Nanotech damage delta. The event's *meaning/value* can therefore be backed even while its visual treatment remains OBP-created.

## Contextual prompt

The minimal normal-play model allows one current `HudContextPrompt`:

| Field | Meaning |
|---|---|
| `PromptId` | Stable identity for de-duplication/replacement. |
| `ActionId` | Semantic input action requested by gameplay, for example `interact`; never a physical key/button. |
| `MessageKey` | Localizable presentation message owned by the gameplay/design layer. |
| `SubjectKey?` | Optional target/item presentation identity. |
| `Progress?` | Optional authoritative hold/channel progress only when gameplay owns such a mechanic. |

Prompt eligibility belongs to gameplay/context logic. The HUD must not ray-cast, measure proximity, inspect native classes or decide that a target is interactable.

The physical key/button glyph is also **not** prompt gameplay data. The host/input layer resolves `ActionId` against the currently active binding/device and passes the resulting glyph or label to the renderer. This keeps keyboard/gamepad swaps out of source-game rules.

No ordinary gameplay prompt authority exists in the current baseline, so `ContextPrompt` is null today. The contract is an OBP-created seam for future recovered or designed interactions, not evidence that the retail games shared one prompt system.

## Provenance: data versus appearance

At this foundation stage, **no integrated player-HUD layout, icon set, typography, animation timing or screen placement is claimed retail-faithful**. Retail evidence currently backs selected gameplay values and transitions, not the final HUD pixels.

| HUD concern | Current provenance | Rule |
|---|---|---|
| R&C1 Nanotech quantity and admitted alive/dead consequence | Retail-backed, bounded | Present the observed value; do not generalize Veldin four into a trilogy max. |
| R&C1 equipped ids 8/10 and item-10 ammo consumption | Retail-backed, bounded | Project selection/ammo only; do not infer quick-select UI or full inventory. |
| Bomb Glove 40-round capacity | Retail-backed descriptor fact | May be displayed when the Bomb Glove mapping is active. |
| R&C1 Bolt pickup denominations/credit | Retail-backed, bounded | Feedback may carry the credited value; canonical wallet still needs an authority owner. |
| GC physical payout/collection values | Retail-backed core with explicit fresh-session choices | Keep current harness counters/debug selectors out of a normal wallet HUD. |
| Current gold-orb GC pickup mesh/scatter/radius | OBP placeholder | Never present as recovered retail pickup appearance/behaviour. |
| Damage/pickup popups, flashes and timing | OBP default until recovered | Values may be backed; visual response is not. |
| Prompt layout, wording style and binding glyph treatment | OBP default until recovered | Gameplay supplies eligibility/action/message semantics; renderer supplies pixels/glyphs. |
| Fonts, colours, panels, spacing and responsive layout | OBP application design | `UiTheme` is not retail evidence. |
| Current debug HUD strings | Development-only | Do not reuse as the shipping HUD contract. |

A later retail-HUD archaeology lane may replace individual OBP defaults with recovered assets/layout/timing. Each promotion should cite its authority and leave unrelated defaults labelled as OBP design.

## Per-game projection today

### R&C1

A thin adapter can already observe `Rac1RatchetNanotechSession` and `Rac1WeaponInventory` without moving their rules. It can emit bounded pickup/damage feedback at the existing accepted transitions.

Bolts should remain absent as a canonical balance until the gameplay layer owns a real player wallet rather than only the current crate-session total. A development fixture may deliberately expose the session counter for testing, but it must be labelled as fixture/debug state.

### Going Commando

The current class-500 crate work is a development harness. Its collected/deferred/outstanding values are useful deterministic telemetry, not a normal player-HUD economy model. Normal `Bolts`, `Health`, `CurrentWeapon` and `ContextPrompt` therefore remain absent unless another gameplay owner supplies them.

### Up Your Arsenal

The current production importer does not expose the player health/economy/weapon/prompt state required here. All normal HUD fields remain absent rather than borrowing R&C1/GC assumptions.

## Update and reset semantics

1. Gameplay updates first.
2. A source-specific or shared gameplay adapter projects one immutable `HudSnapshot`.
3. The host gives that snapshot to the renderer.
4. The renderer may animate toward it, but the snapshot remains the truth for text/numbers/visibility.
5. On session replacement, `Epoch` changes and old feedback events are discarded.
6. On ordinary updates, `Revision` advances monotonically; repeated revisions must be idempotent to render.

If a field changes from present to absent, the corresponding HUD element disappears. The presenter must not retain the last known value as though it were still authoritative.

## Suggested implementation boundary

Neutral data types and the epoch/revision/event lifecycle adapter live in `OBP.Runtime/Presentation/HudState.cs`, outside Godot controls. `OBP.RAC1/Presentation/Rac1HudProjection.cs` translates the existing Nanotech and weapon inventory owners into that neutral shape; the application host publishes state changes and resets the epoch at world teardown. Bolt collection currently contributes only a transient pickup event because the crate-session total is not a canonical wallet.

`game/` can now map neutral presentation keys and semantic actions to Godot labels, textures, animation and live input glyphs without moving gameplay authority into controls. `OBP.Runtime` does not depend on `OBP.RAC1/2/3`; dependency flow remains source game -> neutral projection -> host.

## Deterministic validation

The model should be testable without Godot:

- snapshot projection preserves exact current values and optional/absent semantics;
- R&C1 Veldin four is not silently asserted as a universal capacity;
- ammo-free weapons produce no ammo object, while real zero ammo remains `Current == 0`;
- event ids de-duplicate pickup/damage feedback within an epoch;
- epoch replacement drops stale transient events;
- absent GC/UYA authority does not synthesize zero counters;
- prompt action semantics do not contain physical device bindings.

Representative Godot world smokes now exercise the same lifecycle against retail-backed reconstructed worlds: R&C1 LEVEL0 publishes its bounded Nanotech and equipped-weapon projection, while GC LEVEL1 and UYA TABLE1 start fresh epochs with unsupported normal HUD fields absent. Capture metadata is the deterministic state oracle for these smokes; the rendered pixels still validate OBP presentation choices unless and until a retail-backed visual contract is recovered.

## Non-goals

This foundation does not:

- recreate the R&C1, GC or UYA retail HUD artwork;
- choose a final cross-game health/economy/inventory reconciliation;
- define quick-select wheels, vendors, weapon upgrades or ammo acquisition;
- make crate-session Bolt totals into a save-game wallet;
- discover interactable targets in UI code;
- standardize source-game native ids, PVars, save layouts or health rules;
- turn developer overlays/telemetry into player-facing design;
- claim the current Godot HUD layout, artwork or animation defaults are retail-faithful.

The narrow goal is a trustworthy glass pane over gameplay state: enough structure to render health/Nanotech, Bolts, current weapon/ammo, transient pickup/damage feedback and one contextual prompt without letting the glass pane become the game.
