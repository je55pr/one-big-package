# Playable Fusion Lab greybox (#254)

The Fusion Lab is an original OBP destination under **World Select**. It
uses the same persistent player session and staged world lifecycle as original
planets, with its real `obp:fusion-lab` identity. Ordinary play uses the selected
genuine R&C1 Ratchet and requires that source. Missing selected assets produce a
visible entry failure. For asset-free greybox inspection, explicitly pass
`--diagnostic-player` (capture wrapper: `-DiagnosticPlayer`) to select the capsule.
See the [ownership contract](PLAYER_WORLD_CONTRACT.md).

## Contract

The lab layout is authored in game/data/fusion_lab.json (schema version 1).
OBP.Fusion/FusionLabDefinition.cs validates unique room and station ids,
position bounds, source identity provenance, explicit adapter ids, north-south
door geometry and graph reachability. FusionLabWorldFactory creates original
untextured boxes with collision triangles, emitting an ordinary RuntimeWorld.
The same WorldHost and normal PlayerHost own it. No new controller, native
parsers, giant OBPGame partial, guessed PVars or imported copyrighted assets.

Walk entrance -> southern passage -> loadout hub -> testing corridor ->
test chamber, then turn around and walk back. Doors are real collision openings,
not triggers/teleports. Stations deliberately remain labelled UNAVAILABLE
until adapters, catalogues and mixed encounter work land in follow-ups.

## Smoke/inspection

- Menu: World Select -> OBP FUSION LAB.
- Developer launch: --test-scene fusion-lab (selected avatar source required).
- Asset-free launch: --test-scene fusion-lab --diagnostic-player.
- First-frame capture: ./tools/capture.ps1 fusion-lab -DiagnosticPlayer -Frame 120.
- Automated traversal: ./tools/capture.ps1 fusion-lab -DiagnosticPlayer -Frame 1500.
  In capture mode only, a scripted input provider drives the ordinary
  PlayerHost from entrance through hub and test chamber, reverses, then
  unloads and re-enters via World Select. Require the log line
  [fusion-smoke] PASS and capture-complete exit code 0. No teleporting.
- Interactive check: verify normal controller, collision, pause/resume,
  return to World Select and re-entry. Live manual pause checks are not
  replaced by the automated traversal smoke.
- Cross-world session check: ./tools/player-session-smoke.ps1 -Rendered, with all
  three authorized source environment variables configured. It checks retained
  avatar/profile, selected foreign weapon, wrench hand ownership, health and
  camera preferences, repeated native/custom re-entry, missing avatar source and
  failed staged load. Lab entries also exercise walking and jumping with each of
  the wrench, Bomb Glove, Lancer and Shock Cannon selected. It is a lifecycle
  fixture, not cross-world replay admission or weapon-effect validation.
- Run portable FusionLabDefinitionTests, ArchitectureLayeringTests and build
  game/OBP.Game.csproj. The authored topology and failure cases are
  deterministic without native assets. The old comparison viewer was retired
  under #295; it is separate from this playable destination.

## Future integrations

The [three-source native effect station](EFFECT_HOST.md) uses the existing
Developer page for start/reset and the `native-effects` reset key. Its authored
recipe references live in `game/data/effects_experiment.json`; native parameters
remain with their source adapters. It retires only effects, independently of
the mixed encounter and persistent player session.

#255 supplies universal equipment, #256 mixed actor/combat adapters, #257
reset/experiment UI, #258 mechanics gallery and #259 pause/loadout menu.
Additional rooms and stations should be described in authored data; no
source-owned gameplay should be reinterpreted as a generic behaviour.
