# One Big Package agent guide

## Ground rules
- Production code is Godot 4 + C#.
- Retail NTSC-U game data and executable behaviour are authority for recovered native behaviour.
- Keep OBP-created design choices clearly separate from recovered retail behaviour.
- Never commit retail ISOs, executables, credentials, tokens, personal paths, captures, or unrelated machine data.

## Architecture
- `OBP.IO`: bounded/random-access IO and authority verification.
- `OBP.PS2`: shared PS2 formats/codecs.
- `OBP.PS2.Presentation`: narrow shared PS2-material → neutral-runtime presentation mapping only.
- `OBP.RAC1/2/3`: game-specific formats, evidence and native behaviour.
- `OBP.Runtime`: engine-neutral facts, identities and ports; no source-native formulas or orchestration policy.
- `OBP.Fusion`: engine-neutral, source-qualified gameplay orchestration and player-session policy; never native decoders/controllers.
- `OBP.Godot` + `game/`: scenes, physics contacts, input and presentation; the composition root wires registered source adapters.
- Godot must not become the source of truth for native game formats or gameplay rules.

## Player, world and actor ownership
- Follow the single [ownership and version-1 contract](docs/PLAYER_WORLD_CONTRACT.md). A destination selects world content, never the player's avatar/provider, animation/movement clock, attack route, health, input, HUD, pause menu, Quick Select or audio profile.
- One persistent player session owns player-global configuration/state. World leases own scene/physics transients and source-qualified actors; teardown cannot reset the player session. Source campaigns and their native payloads remain separate.
- Qualify content by its own source/build/asset identity. Reuse `SourceWeaponKey`, `WeaponTargetKey` and `PlayerAvatarIdentity`; do not alias equal native numbers across games or invent competing player inventories.
- World/source adapters may inspect origin for authored starts, missions, water, taxis, native actor context and provenance. Those checks cannot grant player-global capabilities. Explicit retail-parity/diagnostic modes are chosen by the caller, never inferred from world origin.
- Existing destination-driven player gates are frozen migration debt in `tests/OBP.Tests/host-ownership-policy.json`, with follow-up owners. Remove their entries when migrating; do not expand the baseline or add broad file exemptions. New origin-dependent player selection must fail the focused ownership tests.
- Narrow world/source `adapter` exceptions are fingerprinted too. A legitimate source-owned change needs a reviewed fingerprint update with a focused regression and retained ownership rationale; an adapter must not acquire player-global mutations.
- An unavailable source/provider or unrecovered capability needs an explicit failure/status. No silent capsule fallback in ordinary play; no placeholder avatar or gadget advertised as working. Stage-0 contracts do not mean the later host migrations are implemented.

## Working style
- `dev` is the protected integration branch. Never push directly to `dev`; work in a feature branch or isolated worktree and open a pull request targeting `dev`.
- Pull requests must pass the required GitHub checks and resolve review conversations. All merges are deliberate and manual; do not enable auto-merge.
- `dev` is the sole maintained integration/release branch; do not recreate a permanent `main` release pointer. Keep changes focused and avoid bookkeeping-only commits.
- Prefer small evidence-backed changes with deterministic tests, but do not create process for process's sake.
- When creating/moving scripts under `game/scripts` or `game/diagnostics`, move existing `.cs.uid` files alongside renamed scripts (`git mv`), or run `./tools/sync-godot-uids.ps1` (Linux: `bash tools/sync-godot-uids.sh`) to generate missing sidecars with the checksum-pinned Godot .NET editor. Commit the sidecars. The C#/.NET command skips importing and does not build the game when none are missing; `-Check` or `--check` validates without Godot. Do not manually invent UIDs.
- Run `./tools/test.ps1 -Configuration Release` on Windows or `./tools/test.sh Release` where supported; both check UID coverage before .NET tests, and the six UID-tooling regression tests run in xUnit.
- Run `dotnet format OneBigPackage.sln --verify-no-changes --no-restore` before integrating code changes.
- For changes that depend on retail bytes, run `./tools/test-retail.ps1` on an authorized local machine before calling them authority-validated.
- Keep generated retail evidence payload-free and reproducible.
