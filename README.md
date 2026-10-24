<p align="center">
  <img src="docs/assets/branding/obp-logo.png" alt="One Big Package logo" width="520">
</p>

# One Big Package

One Big Package (OBP) is a Godot 4 and C# project to combine the original PlayStation 2 games *Ratchet & Clank*, *Going Commando* and *Up Your Arsenal* into one game.

The first step is reconstructing the original games' levels and systems from user-supplied discs. These are the building blocks for the combined game, rather than three separate campaigns being the final goal.

## Goals

The long-term aim is to let content and progression cross the boundaries between the three games. For example:

- **One connected galaxy:** Travel between planets from all three games on a shared galaxy map, with one save for equipment, bolts, collectibles and mission progress.
- **Equipment across games:** Keep weapons from the whole trilogy, use later gadgets or Clank abilities on earlier planets, and work out how upgrades and vendors fit together.
- **Reasons to revisit planets:** Return to familiar locations with new equipment, routes, characters and missions.
- **Combined encounters:** Mix enemies from different games, including arena challenges that draw on the full weapon collection and enemy roster.

The original mechanics and encounters are being recovered first, so they can be preserved or deliberately adapted. The final campaign structure, balance and progression have not been decided yet.

**OBP is still in development and is not a complete playable trilogy.**

## Current status

- **Ratchet & Clank (2002):** Most active gameplay work. Player movement, weapons, enemies, pickups, campaign state and other systems are implemented to varying degrees.
- **Going Commando (2003):** World loading, presentation and some game systems are implemented. Gameplay is incomplete.
- **Up Your Arsenal (2004):** World loading, presentation and some game systems are implemented. Gameplay is incomplete.

See [issues](https://github.com/je55pr/one-big-package/issues) and [pull requests](https://github.com/je55pr/one-big-package/pulls) for current work.

R&C1 Quick Select includes the GC Lancer and UYA Shock Cannon by default alongside
owned R&C1 weapons. Their shared assets prepare during Game Sources setup and
stay loaded across level changes. The original game sources are required; see
[cross-game weapon controls and limits](docs/FUSION_WEAPONS_TESTING.md).

## Trilogy content catalogue (prototype)

This is a **small preview**, not a full inventory or an audited completion percentage. Expand a category to explore sample content from the original trilogy and its current OBP implementation. The finished catalogue could grow here without making the README long to scroll through.

**Status guide:** 🎮 **Playable** = working in the stated scope, not necessarily retail-complete; 🚧 **Partial** = working pieces with known gaps; 📖 **Documented** = source research exists but implementation has not been audited here; ⚪ **Unassessed** = not verified for this preview. A linked tracking issue describes intended work, **not** proof of completion.

<details>
<summary><strong>🔫 Weapons</strong> (3 examples)</summary>

| Weapon | Original game | Original-game reconstruction | Cross-game availability | Evidence |
| --- | --- | --- | --- | --- |
| Bomb Glove | R&C1 | 🎮 R&C1 throw, ammo and blast | ⚪ Other worlds unassessed | [Weapon test notes](docs/FUSION_WEAPONS_TESTING.md) |
| Lancer | Going Commando | 🚧 Native firing subset; upgrades not covered | 🎮 Selectable and fires against supported R&C1 targets | [Fusion test notes](docs/FUSION_WEAPONS_TESTING.md) |
| Shock Cannon | Up Your Arsenal | 🚧 V5 firing/charge subset; upgrades not covered | 🎮 Selectable and fires against supported R&C1 targets | [Fusion test notes](docs/FUSION_WEAPONS_TESTING.md) |

</details>

<details>
<summary><strong>🧰 Gadgets &amp; traversal</strong> (3 examples)</summary>

| Gadget | Original game | Original-game reconstruction | Cross-game availability | Evidence |
| --- | --- | --- | --- | --- |
| Swingshot | R&C1 | 📖 Native traversal and target families documented | ⚪ Unassessed | [Traversal research](research/RAC1_TRAVERSAL_EQUIPMENT.md) |
| Gravity Boots | Going Commando | ⚪ Unassessed | ⚪ Unassessed | — |
| Refractor | Up Your Arsenal | ⚪ Unassessed | ⚪ Unassessed | — |

</details>

<details>
<summary><strong>👾 Enemies &amp; encounters</strong> (4 examples)</summary>

| Enemy / encounter | Original game | Original-game reconstruction | Cross-game encounters | Evidence |
| --- | --- | --- | --- | --- |
| Opening Veldin melee creatures (class 749) | R&C1 | 🚧 Live behaviour and animation, with known gaps | ⚪ Unassessed | [Veldin enemies](research/RAC1_VELDIN_ENEMIES.md) |
| Opening Veldin flame robots (class 1440) | R&C1 | 🚧 Live behaviour and animation, with known gaps | ⚪ Unassessed | [Veldin enemies](research/RAC1_VELDIN_ENEMIES.md) |
| Thugs-4-Less enemies | Going Commando | ⚪ Unassessed | ⚪ Unassessed | — |
| Tyrhrranoids | Up Your Arsenal | ⚪ Unassessed | ⚪ Unassessed | — |

</details>

<details>
<summary><strong>🪐 Planets &amp; level-specific content</strong> (3 examples)</summary>

| Destination | Original game | Current reconstruction scope | Cross-game gameplay | Evidence |
| --- | --- | --- | --- | --- |
| Veldin (opening) | R&C1 | 🚧 World, player and selected enemies active; not complete | ⚪ Unassessed | [Veldin enemies](research/RAC1_VELDIN_ENEMIES.md) |
| Oozla | Going Commando | 🚧 World loading/presentation; gameplay incomplete | ⚪ Unassessed | [GC world loading](research/GC_LEVEL_LOADING.md) |
| Veldin | Up Your Arsenal | 🚧 World loading/presentation; gameplay incomplete | ⚪ Unassessed | [UYA world import](research/UYA_PRODUCTION_IMPORTER.md) |

</details>

<details>
<summary><strong>🎮 Shared gameplay systems</strong> (5 examples)</summary>

These are examples of **reconciliation work being tracked**, not completed-feature claims.

| System | Desired scope | Tracking |
| --- | --- | --- |
| One player avatar and animation clock | Same Ratchet across every destination | [#268](https://github.com/je55pr/one-big-package/issues/268) |
| Universal equipment and loadouts | All three games' source-qualified equipment | [#255](https://github.com/je55pr/one-big-package/issues/255) |
| Unified pause, HUD and Quick Select | Consistent player-facing interface | [#259](https://github.com/je55pr/one-big-package/issues/259) |
| Mixed-source actors and combat | Enemies and effects from different games coexist | [#256](https://github.com/je55pr/one-big-package/issues/256) |
| Shared saves and source-owned world state | Persistent OBP progress without conflating source state | [#273](https://github.com/je55pr/one-big-package/issues/273) |

</details>

## Getting started

On Windows, with the .NET 8 SDK installed, run from the repository root:

```powershell
./tools/bootstrap-dev.ps1
./tools/play.ps1
```

The bootstrap script downloads the project's pinned Godot .NET version, builds the project and syncs Godot C# script identity sidecars. If no game source is configured, the app opens the Game Sources screen.

When adding a `.cs` file under `game/scripts` or `game/diagnostics`, run `./tools/sync-godot-uids.ps1` (Linux: `bash tools/sync-godot-uids.sh`). This uses the pinned Godot .NET editor to generate any missing `.cs.uid` files. Commit the generated sidecars with your scripts. Run `./tools/sync-godot-uids.ps1 -Check` (Linux: `bash tools/sync-godot-uids.sh --check`) for read-only validation. The small .NET UID tool runs without a Godot import or game build when sidecars already exist. If a script moves, move its `.cs.uid` with it to preserve its identity. Duplicate, malformed or orphaned UIDs require manual correction rather than automatic replacement.

OBP requires your own supported retail game files to load original levels. **No disc images, game assets or executables are included in this repository.**

## Testing

```powershell
# Build and unit tests
./tools/test.ps1 -Configuration Release

# Tests that require locally supplied game data
./tools/test-retail.ps1
```

Pull requests run .NET tests (including strict C# UID sidecar coverage), CodeQL analysis and an asset-free Godot startup test. The Godot smoke also checks that the preceding pinned headless import left the game working tree unchanged. The six .NET xUnit UID-tooling regressions run with the ordinary test suite. Some R&C1 gameplay can be recorded and replayed; see [movement and replay documentation](docs/MOVEMENT_REPRO_STATES.md).

## Project layout

| Directory | Contents |
| --- | --- |
| `src/OBP.IO`, `src/OBP.PS2` | Disc access and PS2 file formats |
| `src/OBP.RAC1`, `src/OBP.RAC2`, `src/OBP.RAC3` | Game-specific formats and behaviour |
| `src/OBP.Runtime` | Engine-independent runtime |
| `src/OBP.Godot`, `game/` | Godot integration and application |
| `research/` | Reverse-engineering notes and evidence |
| `tests/`, `tools/` | Tests and development scripts |

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Project vision](docs/PROJECT_VISION.md)
- [Research index](research/README.md)
- [Retail testing](docs/RETAIL_TESTING.md)
- [Development guidelines](AGENTS.md)
- [External source policy](research/EXTERNAL_SOURCE_POLICY.md)

Development uses feature branches and pull requests into the protected `dev` branch. Merges are manual.

This is an unofficial fan project and is not affiliated with Sony Interactive Entertainment or Insomniac Games.
