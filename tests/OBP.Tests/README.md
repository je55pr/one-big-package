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

## Keeping tests readable (#429)

These are screening questions for new and refactored tests, not size limits. A long class that is one coherent
contract (one static native routine, one sequential scenario, one census) is fine as it is.

- **One contract per class.** Name the class after the subject and the contract it pins. Split a class when it mixes
  unrelated scenarios, or when its helpers are used by only one group of its tests. Do not split just because a file is
  long.
- **One reason to fail per test.** If a failure would not say which contract broke, split the test. A retail witness that
  checks several unrelated tables belongs in separate tests that share a fixture.
- **Keep builders next to their tests.** Put synthetic builders used by several classes in a `<Subject>Fixtures.cs`
  static class (for example `Rac3Class5821Fixtures`). Give builders named parameters for the one field a test cares
  about, and keep the interesting mutation visible in the test (`WithPvar(source, pvar => ...)`) instead of burying it in
  the builder. Promote a helper only when identical copies already exist in several classes; do not add base classes or
  generic builder frameworks.
- **Import expensive retail data once.** Use an `IClassFixture` that imports the level or world once per class and
  exposes it as `null` when the ISO variable is unset (`Rac1KerwanRetailFixture`, `Rac2OozlaWorldImportFixture`). Tests
  only read the shared value and build their own session or mutable state.
- **Retail gating stays explicit.** Each retail test starts with its own  on the matching   variable. When a class, or a family of classes split from one, repeats that preamble many times, a small
  -style helper () with the same  and message is fine. Do not add a
  repository-wide helper just to shorten the preamble. Restructuring never changes whether a test skips, fails or
  passes.
- **Preserve evidence.** Keep native provenance comments, addresses, offsets, exact retail constants and theory rows
  verbatim, and keep each comment next to the assertion it justifies.
- **Names are IDs.** Moving a test to another class changes its xUnit display ID, and renaming it changes it again.
  Move first; rename only for a reason, and check that docs, tools and workflows do not reference the old ID.
- **Prove a refactor kept every case.** Compare `dotnet test --list-tests` before and after (the only differences should be
  the moved IDs and any deliberate splits), compare the retail and portable pass/skip counts, and check that no
  assertion line disappeared.
