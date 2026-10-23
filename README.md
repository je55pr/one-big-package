<p align="center">
  <img src="docs/assets/branding/obp-logo.png" alt="Ratchet & Clank: One Big Package" width="730">
</p>

<p align="center">
  <strong>Three PS2 adventures. One evolving reconstruction.</strong>
</p>

<p align="center">
  Original-game research meets a shared Godot 4 + C# runtime.
  <br>
  Built from evidence. Tested against the games. Powered by your own discs.
</p>

<p align="center">
  <a href="https://github.com/je55pr/one-big-package/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/je55pr/one-big-package/actions/workflows/ci.yml/badge.svg?branch=dev"></a>
  <a href="https://github.com/je55pr/one-big-package/actions/workflows/codeql.yml"><img alt="CodeQL" src="https://github.com/je55pr/one-big-package/actions/workflows/codeql.yml/badge.svg?branch=dev"></a>
  <a href="https://github.com/je55pr/one-big-package/issues"><img alt="Development status: work in progress" src="https://img.shields.io/badge/status-active_reconstruction-E28A35"></a>
</p>

<p align="center">
  <a href="#where-things-stand">Progress</a> ·
  <a href="#get-started">Get started</a> ·
  <a href="#testing-and-replays">Testing</a> ·
  <a href="#how-obp-is-built">Architecture</a> ·
  <a href="#development-and-research">Research</a>
</p>

---

> [!IMPORTANT]
> **OBP is a work-in-progress reconstruction, not a finished trilogy port.** Gameplay and compatibility are incomplete. You must supply your own legally obtained game discs or ISO images; this repository does not include retail game data.

## What is One Big Package?

**One Big Package (OBP)** aims to bring the original PlayStation 2 *Ratchet & Clank*, *Going Commando* and *Up Your Arsenal* into one shared game foundation.

The goal is not simply to draw the planets. OBP reconstructs the underlying formats, game systems, player controls, world interactions and presentation from **original-game evidence**, while keeping new engine-specific choices explicitly identifiable.

| Principle | What it means |
| --- | --- |
| **Retail-backed** | Game-specific behaviour is researched against supported original PS2 releases, not guessed from screenshots. |
| **One shared foundation** | Three game-specific providers feed an engine-neutral runtime, hosted by Godot. |
| **Reproducible** | Automated .NET and Godot checks, retail-backed validation and recorded-playtest replays catch regressions. |
| **No bundled game data** | Disc images, original textures, audio, executables and extracted caches stay on the owner's machine. |

## Where things stand

| Game | Current work | Reality check |
| --- | --- | --- |
| **Ratchet & Clank (2002)** | The deepest gameplay reconstruction: movement, cameras, weapons, pickups, Nanotech, enemies, campaign state and world interactions | Playable and testable slices; many behaviours are still being recovered |
| **Going Commando (2003)** | Original data/world loading, presentation and selected game-specific systems | Not a complete playable campaign |
| **Up Your Arsenal (2004)** | Original data/world loading, presentation and selected game-specific systems | Not a complete playable campaign |

All three can be loaded through the same destination/provider architecture. That **does not** mean their gameplay is feature-complete. R&C1 is the active proving ground for deeper original behaviour.

Explore the [open issues](https://github.com/je55pr/one-big-package/issues) and [pull requests](https://github.com/je55pr/one-big-package/pulls) to see what is being researched and implemented now.

## Get started

**Windows development setup:** install the .NET 8 SDK, then run these commands from the repository root in PowerShell:

```powershell
./tools/bootstrap-dev.ps1
./tools/play.ps1
```

The bootstrap downloads OBP's pinned Godot .NET editor and builds/imports the project. Without an attached game source, OBP opens the **Game Sources** screen. You can supply your own supported retail images locally; none are downloaded by this project.

To run the portable test suite:

```powershell
./tools/test.ps1 -Configuration Release
```

Looking for the internals? See [Architecture](docs/ARCHITECTURE.md), [Project vision](docs/PROJECT_VISION.md) and the [research index](research/README.md).

## Testing and replays

OBP treats a passing build and accurate original behaviour as two different claims.

| Check | Where it runs | Covers |
| --- | --- | --- |
| Native CI | GitHub Actions, every PR | .NET formatting, build and unit tests |
| Godot smoke | GitHub Actions, every PR | Asset-free Godot startup and Game Sources UI |
| CodeQL | GitHub Actions | Static security analysis |
| Retail tests | Authorised local machine | Original disc-backed decoding and behavioural validation |
| Playtest replay | Authorised local machine | Recorded user inputs and state-observation divergence |

```powershell
./tools/test-retail.ps1
```

The retail test suite requires locally configured source images. It is **not** part of public PR CI. Recorded R&C1 playtests can also be replayed in a fresh Godot process; see [replay documentation](docs/MOVEMENT_REPRO_STATES.md) and [retail testing](docs/RETAIL_TESTING.md).

## How OBP is built

```text
Your own PS2 sources
        ↓
OBP.IO → OBP.PS2 → OBP.RAC1 / OBP.RAC2 / OBP.RAC3
                           ↓
                     OBP.Runtime
                           ↓
                       OBP.Godot
                           ↓
                         game/
```

Game-specific decoding and original gameplay rules stay out of the Godot presentation layer. Common abstractions belong in the neutral runtime **only when evidence supports sharing them**.

The full dependency and evidence boundaries are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Development and research

OBP uses **`dev` as its protected integration branch**. Development happens on feature branches with pull requests targeting `dev`. CI checks and review conversations must be resolved before an intentional, manual merge. Direct pushes and automatic merges are not part of this workflow.

- [Agent/developer guide](AGENTS.md): repository rules, tests and source-data boundaries.
- [Research index](research/README.md): original-game archaeology, evidence and open questions.
- [Project vision](docs/PROJECT_VISION.md): goals, scope and deliberately unresolved choices.
- [External source policy](research/EXTERNAL_SOURCE_POLICY.md): use of public decompilation projects and licence boundaries.
- [Third-party notices](THIRD_PARTY_NOTICES.md): licences and attribution for referenced or integrated code.

**Rights and scope:** OBP is an unofficial, independently developed fan project, not affiliated with or endorsed by Sony Interactive Entertainment or Insomniac Games. Original game assets and software are not distributed here. You are responsible for providing your own legally obtained source material.
