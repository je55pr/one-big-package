# Trilogy gameplay entity lifecycle boundary

## Scope

This note records the evidence used by issue #107 to separate authored Moby identity,
source-game mutable state and engine-neutral live presentation/lifetime state.
Retail NTSC-U behaviour remains authority. The shared contract deliberately contains
only semantics demonstrated by more than an abstract framework exercise.

## Neutral boundary

`RuntimeDynamicObject` remains the immutable authored definition delivered by a world
provider: source game, native class/instance identity, optional native UID, model and
interaction ids, authored transform/render surfaces, opaque source payloads and admitted
animation capability.

`RuntimeEntityState` is a replaceable live snapshot. It repeats stable identity so a
snapshot cannot be applied to the wrong authored object, then carries only:

- current engine-neutral transform;
- `Active` / `Inactive` presence;
- current neutral object-animation role.

Opaque payload bytes never enter the live snapshot. Native state numbers, PVar fields,
health, AI state and reasons for activation/deactivation stay in source-game code until
separate cross-game evidence justifies a shared semantic.

## R&C1 witness: Veldin red plant

R&C1 class 1781 provides a non-destructive live-state witness. Its authored placement
identity and render model remain unchanged while retail selects native sequence 1 when
the recovered distance/motion predicate passes, then returns to sequence 0 when native
animation flag `0x02` is observed. #53 already proved the clip, selector and timing.

`Rac1MobyAnimationProvider.AdvanceRedPlantEntityState` therefore consumes the R&C1-owned
selector inputs and changes only `RuntimeEntityState.Presentation.AnimationRole` between
neutral `Rest` and `Reaction`. Presence and identity remain unchanged. Native sequence
ids, thresholds and selector flags do not cross into `OBP.Runtime` or Godot.

## Going Commando witness: class-500 Bolt Crate

GC class 500 provides the destructive lifetime witness. The authored dynamic object
preserves its native `0x88` instance record and resolved PVar as opaque payloads. R&C2
code owns their layout and extracts UID, authored bolt count and PVar `+0xC8`.

Recovered class-500 behaviour enters its break transition from native state 1 through
state 3 after the admitted positive-damage predicate. The normal state-3 route uses
PVar `+0xC8`: zero enters the recovered deactivate helper; nonzero selects native state
6. `GcClass500Lifecycle` keeps states 1/3/6 and the PVar rule in `OBP.RAC2`, projecting
only the zero route to neutral `Inactive`. State 6 currently remains neutral `Active`;
no additional shared meaning is invented for it.

## UYA comparison and negative result

UYA already preserves authored Moby class/index identity, compatible UID evidence,
transforms, linked render models and opaque PVars through `RuntimeDynamicObject`.
That corroborates the authored half of the boundary across a third source game.

However, current UYA work has not recovered a native lifecycle/state selector with
semantics comparable to the R&C1 plant or GC crate. Its admitted animation previews are
explicit showcase admissions rather than native state-selection claims. Therefore #107
does not add a UYA lifecycle projection, generic native-state field, health field or
property bag. This absence is intentional evidence discipline, not missing plumbing.

## Godot/application boundary

`RuntimeWorldScene.DynamicObjectNode` owns the current neutral `RuntimeEntityState` and
applies only neutral effects: presence controls node visibility and the live transform
controls the node transform. It validates authored identity before every application.
Godot does not inspect PVars, native state numbers or source-game payload layouts.

The existing GC crate debug harness now asks `GcClass500Lifecycle` for the source-game
transition and applies its neutral snapshot. It no longer decides `Root.Visible = false`
from PVar semantics itself. Source-specific reward logic remains source-specific.

Future crate, enemy and mechanism work should extend source-game controllers first and
project only independently justified neutral effects. A shared field is not warranted
merely because two games both contain an integer at some native offset.
