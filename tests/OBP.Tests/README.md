# OBP.Tests layout

`OBP.Tests` is a single xUnit project. Every test type keeps the `OBP.Tests` namespace, so folders are only a
navigation aid: moving a file between folders never changes a test name or its discovery.

| Folder | What lives there |
|---|---|
| `Architecture/` | Static audits and ownership gates: layering, source-naming and host-ownership policy, Roslyn scans of `src/` and `game/`, and the `Rac1*Ownership` guards. |
| `Core/` | Engine-neutral libraries: `OBP.IO`, `OBP.PS2` codecs and formats, shared runtime types, source identity/library, and cross-game skeletal and material evidence. |
| `Fusion/` | `OBP.Fusion` orchestration: player session and capabilities, equipment, world entry/provider contracts, Fusion weapons, profiles. |
| `GodotHost/` | `OBP.Game` / `OBP.Godot` host behaviour: scene lifetime, actor and effect hosts, HUD, player avatar presentation, source setup and verification coordinators, command line. |
| `Diagnostics/` | Diagnostic scenarios, capture, replay, playtest recording and bug reporting. |
| `Rac1/` | Ratchet & Clank (2002), split into `Campaign/`, `Enemies/`, `Menus/`, `Player/`, `Taxi/`, `Weapons/` and `World/` (levels, mobys, particles, foliage, Novalis). `Rac1VerifiedDisc.cs` is the shared retail-disc helper. |
| `Rac2/` | Going Commando. |
| `Rac3/` | Up Your Arsenal. |
| `Tooling/` | Repository tooling: coverage report, Godot UID tools, launcher argument quoting, native process capture. |
| `Helpers/` | Shared synthetic-data helpers. |

The project root keeps only the project file, `GlobalUsings.cs`, `RepoPaths.cs` and the JSON policy files that
architecture tests read by fixed path (`host-ownership-policy.json`, `reconciliation-capabilities.json`,
`root-boundary-policy.json`, `source-naming-legacy.json`).

Pick the folder for the subsystem under test, not for the types the test happens to use. Retail-gated tests sit beside
their portable siblings and skip when the matching `OBP_*_ISO` variable is unset.
