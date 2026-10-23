<p align="center">
  <img src="docs/assets/branding/obp-logo.png" alt="One Big Package logo" width="520">
</p>

# One Big Package

One Big Package (OBP) is a Godot 4 and C# project reconstructing the original PlayStation 2 games *Ratchet & Clank*, *Going Commando* and *Up Your Arsenal*.

It reads data from locally supplied game discs and implements their formats and gameplay systems in a shared runtime. **The project is still in development and is not a complete playable trilogy.**

## Current status

- **Ratchet & Clank (2002):** Most active gameplay work. Player movement, weapons, enemies, pickups, campaign state and other systems are implemented to varying degrees.
- **Going Commando (2003):** World loading, presentation and some game systems are implemented. Gameplay is incomplete.
- **Up Your Arsenal (2004):** World loading, presentation and some game systems are implemented. Gameplay is incomplete.

See [issues](https://github.com/je55pr/one-big-package/issues) and [pull requests](https://github.com/je55pr/one-big-package/pulls) for current work.

## Getting started

On Windows, with the .NET 8 SDK installed, run from the repository root:

```powershell
./tools/bootstrap-dev.ps1
./tools/play.ps1
```

The bootstrap script downloads the project's pinned Godot .NET version and builds the project. If no game source is configured, the app opens the Game Sources screen.

OBP requires your own supported retail game files to load original levels. **No disc images, game assets or executables are included in this repository.**

## Testing

```powershell
# Build and unit tests
./tools/test.ps1 -Configuration Release

# Tests that require locally supplied game data
./tools/test-retail.ps1
```

Pull requests also run .NET tests, CodeQL analysis and an asset-free Godot startup test. Some R&C1 gameplay can be recorded and replayed; see [movement and replay documentation](docs/MOVEMENT_REPRO_STATES.md).

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
