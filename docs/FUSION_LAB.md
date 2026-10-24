# Playable Fusion Lab greybox (#254)

The Fusion Lab is an original OBP destination under **World Select**. It
needs no disc source to enter: the unmodified PlayerHost remains controllable,
with a clearly marked debug capsule when no verified native R&C1 avatar is
available. With an attached supported R&C1 source, the ordinary avatar
presentation provider is attempted, and on failure the capsule is retained
without pretending that a substitute mesh is retail content.

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
- Developer launch: --test-scene fusion-lab, no source required.
- First-frame capture: ./tools/capture.ps1 fusion-lab -Frame 120.
- Automated round trip: ./tools/capture.ps1 fusion-lab -Frame 1500.
  In capture mode only, a scripted input provider drives the ordinary
  PlayerHost from entrance through hub and test chamber, reverses, then
  unloads and re-enters via World Select. Require the log line
  [fusion-smoke] PASS and capture-complete exit code 0. No teleporting.
- Interactive check: verify normal controller, collision, pause/resume,
  return to World Select and re-entry. Live manual pause checks are not
  replaced by the automated traversal smoke.
- Run portable FusionLabDefinitionTests, ArchitectureLayeringTests and build
  game/OneBigPackage.csproj. The authored topology and failure cases are
  deterministic without native assets. The old --compose viewer remains
  a separate research tool.

## Future integrations

#255 supplies universal equipment, #256 mixed actor/combat adapters, #257
reset/experiment UI, #258 mechanics gallery and #259 pause/loadout menu.
Additional rooms and stations should be described in authored data; no
source-owned gameplay should be reinterpreted as a generic behaviour.
