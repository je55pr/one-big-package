# Shared profile, replay and reconciliation gate

Ordinary OBP play uses one persistent player across R&C1 Veldin, GC Oozla,
UYA Veldin and the authored Fusion Lab. These are OBP integration policies;
native movement, ammunition, damage and actor behaviour remain source owned.
See [the ownership contract](PLAYER_WORLD_CONTRACT.md) and
[the equipment/actor evidence](TRILOGY_EQUIPMENT_ACTORS.md).

## Ordinary profile

`user://obp-profile.json` is the version-1 `obp/shared-player/v1` envelope.
It contains qualified avatar/controller/service identities, health, wallet,
preferences, selected and held equipment, slots, native player contributors,
last committed destination, and source build/digest dependencies. Equipment
ownership, native ammunition and enabled gadgets stay in the existing player
contributor; there is no second inventory.

Source campaigns are separate tagged contributors. `rac1/campaign` retains the
native R&C1 campaign/inventory/wallet envelope under its own schema. Unknown
campaign contributors and fields survive saves. Native checkpoints whose retail
serialization is unrecovered are not advertised as durable checkpoints.

The existing `user://rac1-campaign.json` stays isolated and untouched. Open that
save explicitly with `--rac1-campaign-save PATH` for its original R&C1 parity
mode. It is never silently interpreted as the shared player's inventory or
campaigns for other games. Recordings and replays do not write ordinary profiles.

Saving occurs after committed world changes, every ten seconds of ordinary play,
and on clean exit. Same-directory atomic replacement follows a disk flush;
`.bak` keeps the previous valid profile. A truncated/corrupt primary can recover
that backup; replacing corrupt data retains a `.rejected-*` copy. An unsupported
newer envelope or unavailable required player contributor is retained without
automatic replacement. Status is visible in Game Sources and the ordinary pause
menu. Failed world preparation cannot publish a new destination.

Use `--profile PATH` for an isolated profile and `--no-profile` for an ephemeral
ordinary session. Automated runs do not touch the default profile. Missing
sources retain desired equipment and dependencies, expose the unavailable reason
and release the unavailable weapon's hand. Unsupported models/gadgets cannot
activate or substitute a capsule. Newer mandatory player state requires its
adapter; it is preserved rather than partially applied.

## Input tapes, reports and bookmarks

Version-2 gzip JSONL tapes retain the shared profile, exact source digests,
world/build, scenario seed and admitted native/session adapters. Existing
version-1 R&C1 tapes decode into their original explicit parity mode. Source
builds, engine, platform, physics backend and 60-Hz clock must match. The existing
explicit build-change diagnostic override affects assembly identity only.

The tape captures logical player input, raw UI input, session commands, world
transitions, ordered qualified shot/damage facts and adapter clocks. Checkpoints
compare player state, UI focus, animation, camera, ammunition/projectiles and
qualified actor observations. Unknown live native actor adapters are refused;
the recorder does not invent AI snapshots. The Lab uses the three existing
evidence-backed deterministic native actor contexts.

`tools/record-playtest.ps1 -Destination rac2:LEVEL1` records ordinary Oozla;
`rac3:TABLE1` and `obp:fusion-lab` are also admitted. Attach the three supported
sources via their environment variables. F8 marks a tape, F9 finishes it.
The existing Bug Capture workflow provides F8 report forms and the F7 inbox on
all these destinations. Reports show exact source prerequisites, retain completed
prefixes immediately, and export through the existing local ZIP workflow. They
contain no source paths, disc images, proprietary assets or form keystrokes.

F12 during an ordinary recording or Bug Capture saves a version-3 universal
bookmark and a completed sibling replay prefix. Stand still on the ground to
include the selected player's pose observation. Move the two files together.
`--load-movement-state BOOKMARK.json` replays the exact history to the saved step;
human play can then continue in that isolated reproduction process. Loading from
the pause menu opens that reproduction separately. Without an input history,
ordinary F12 explicitly explains why a universal bookmark is unavailable. It
does not pretend to restore native AI from a pose. Legacy version-1/2 R&C1
bookmarks remain supported in their original parity mode.

## Reproducible gate

On an authorised Windows machine, set `OBP_RAC1_ISO`, `OBP_GC_ISO`, and
`OBP_UYA_ISO`, then run:

```powershell
./tools/test-reconciliation.ps1
```

Every engine process, including report replays, is headless. The gate runs the
portable/retail tests, format and UID checks, C# build, five-world pose/controller
fixture, native mixed-actor/combat/audio/lifecycle fixture, ordinary save/restart,
five-stop selection/firing capture and exact fresh-process replay, GC/UYA/Lab
bookmarks, and each destination's F8/F7 inbox/export/two-process report replay.
The persistent capture route is Veldin → Oozla → UYA Veldin → Lab → Veldin.
The separate equivalent-input pose/audio fixtures deliberately restore the same
initial player state on an explicitly authored contact deck for comparison.

Evidence goes to a new ignored `captures/reconciliation/<id>/` directory:
`summary.json`, observed and declared capability matrices, profile restart
assertions, native pose traces, input tape, bookmarks, report ZIPs and replay
comparison results. Results come from assertions and observed state. Skipping
tests/build labels the result `partial`; it cannot manufacture a full PASS.
The declared matrix is [reconciliation-capabilities.json](../tests/OBP.Tests/reconciliation-capabilities.json).
Portable CI checks its registrations and traverses the same qualified route with
synthetically authored providers; the ownership audit remains active.
The last two migration exceptions are removed: the ordinary Metal Detector
shortcut follows its unavailable catalog entry, and checkpoint source admission
only returns a native placement to the caller's explicit parity policy. A focused
regression verifies that this source adapter cannot select a player service.

Automation proves state, clocks, geometry/attachment traces, audio intents and
cleanup. A separate human check should confirm visible attachment/blending,
effects, audible sound and controller feel across the route, then restart once
to check the saved loadout. This script does not claim a rendered/audible review
or close parent #264; its dependencies and that final review remain explicit.
