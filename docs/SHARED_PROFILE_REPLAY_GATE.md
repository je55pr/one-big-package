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

## Historical reconciliation gate (retired)

The former multi-world automated reconciliation runner and its capability matrix
are preserved in the [pre-removal tests tag](https://github.com/je55pr/one-big-package/tree/tests). They are not
available on current dev, and CI does not claim cross-world behavioural
validation. The historical findings above remain useful design context.

For current investigation, record and replay actual gameplay with
`tools/record-playtest.ps1` and `tools/replay-playtest.ps1`, then check
presentation, audio, controller feel, saves and world transitions manually.
