# R&C1 Veldin smoke boundaries

Two Godot-host smokes cover different questions. Keep their results separate.

## Ordinary-play Veldin gate

`tools/rac1-veldin-play-smoke.ps1` launches `rac1:LEVEL0` through the normal
source/destination provider path with no campaign persistence enabled. The host
therefore begins from the same ephemeral opening state used by ordinary local
development: current level 0, no admitted travel destinations, Wrench equipped,
Bomb Glove owned with six rounds, four Nanotech, and no prior crate/projectile
session mutations.

The smoke then drives only the public player input boundary. It verifies:

- natural full-forward Veldin fall, recovered death state, R-key respawn and
  return to the authored class-0 start;
- recovered type-0 horizontal camera orbit and recovered vertical manual orbit
  through the ordinary camera InputMap actions while Ratchet remains stationary;
- ordinary keyboard selection of the Bomb Glove and one primary-action fire,
  proving the opening six-round grant is usable by observing the normal 6 -> 5
  inventory transition;
- visible class-749 translation under the supported runtime hostile session;
- natural pursuit/attack marker damage, including a real 4 -> 3 Nanotech hit;
- a Wrench selection/action against the hostile after gameplay itself establishes
  contact range;
- practical class-500 contact reached by ordinary movement. A test-side
  collision-grid route chooses reachable terrain and may press the normal jump
  action after sustained lack of planar progress; it never writes Ratchet's
  transform. The final Wrench action must increase the live destroyed-crate count.

The gate intentionally does **not** teleport Ratchet or enemies, write checkpoint,
death or attack state, mutate ammo, or invoke consequence handlers in place of
input. The route planner is test policy only and is not a recovered navigation AI.

CLI flag: `--rac1-veldin-play-smoke`. The historical `--rac1-combat-smoke` flag
is retained as an alias for this honest gate so old invocations no longer select
the staged harness.

## Synthetic combat host-contract gate

`tools/rac1-combat-contract-smoke.ps1` selects
`--rac1-combat-contract-smoke`. Its implementation lives in
`OBPGame.Rac1CombatContractSmoke.cs`. This is explicitly a lower-level synthetic
integration harness: it may stage transforms and call narrow internal seams to
isolate wrench/contact, projectile, presentation, witness-gating and lifecycle
contracts.

A pass from the synthetic contract gate is useful for regression isolation, but
must not be cited as proof that a player can reach the same setup from an
ordinary local-dev start. End-to-end playability claims belong only to the
ordinary-play Veldin gate above.

## Retail boundary

Both smokes exercise production code backed by the repository's retained R&C1
evidence, but neither converts OBP host policy into retail evidence. In
particular, the current Wrench spatial admission dimensions and the smoke route
planner remain host/test policy. Native consequences stay separated from those
host-side contact choices as documented in `../research/RAC1_WRENCH_COMBAT.md`.
