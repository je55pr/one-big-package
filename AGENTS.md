# One Big Package agent guide

## Ground rules
- Production code is Godot 4 + C#.
- Retail NTSC-U game data and executable behaviour are authority for recovered native behaviour.
- Keep OBP-created design choices clearly separate from recovered retail behaviour.
- Never commit retail ISOs, executables, credentials, tokens, personal paths, captures, or unrelated machine data.

## Architecture
- `OBP.Core`: shared provenance, identity and math.
- `OBP.IO`: bounded/random-access IO and authority verification.
- `OBP.PS2`: shared PS2 formats/codecs; `src/OBP.PS2/Presentation/` contains narrow PS2-to-neutral-runtime adapters for materials and supported skeletal assets. Keep runtime dependencies confined to that folder; native formats, source selection and game-specific policy remain in their owning layers.
- `OBP.RAC1/2/3`: game-specific formats, evidence and native behaviour.
- `OBP.Runtime`: engine-neutral facts, identities and ports; no source-native formulas or orchestration policy.
- `OBP.Fusion`: engine-neutral, source-qualified gameplay orchestration and player-session policy; never native decoders/controllers.
- `OBP.Godot` + `game/`: scenes, physics contacts, input and presentation; the composition root wires registered source adapters.
- Godot must not become the source of truth for native game formats or gameplay rules.

## C# source names and stable boundaries
- New source-owned namespaces/types/files use `Rac1`, `Rac2`, or `Rac3`; source projects remain `OBP.RAC1/RAC2/RAC3`. For example, add `Rac2LancerSession`, not a second `GcLancerSession` path. Interfaces use `IRac2...` / `IRac3...` as appropriate. Source-neutral types belong in their actual shared layer; do not rename native-only mechanics to misleading `Universal...` types.
- Use canonical Rac1/Rac2/Rac3 source naming; retain genuinely shared native vocabulary only when justified.
- Use `ObpSourceGame` for real source identity and a distinct typed authored-world identity where needed. Parse/format established wire strings only at explicit validated boundaries (`SourceIdentityWire`, #290). Preserve human-facing Going Commando / Up Your Arsenal names and historical evidence terminology.
- `OBP.Game` is the app assembly/namespace (`game/OBP.Game.csproj`, #293). Preserve Godot UIDs and accepted native IDs/build hashes, destination/save/replay wire strings, and documented CLI compatibility. Strict CLI parsing (#174) lives in `CommandLineArgs`; the canonical source flags are `--rac1-iso`, `--rac2-iso` and `--rac3-iso`, with the legacy `--gc-iso`/`--uya-iso` aliases retained at that same parser boundary (#294).
- The naming migration (#289-#295) is complete; its order and the retained exceptions are recorded in [ARCHITECTURE.md](docs/ARCHITECTURE.md#naming-and-migration-order). Do not reintroduce a second source vocabulary.

## Player, world and actor ownership
- Follow the single [ownership and version-1 contract](docs/PLAYER_WORLD_CONTRACT.md). A destination selects world content, never the player's avatar/provider, animation/movement clock, attack route, health, input, HUD, pause menu, Quick Select or audio profile.
- One persistent player session owns player-global configuration/state. World leases own scene/physics transients and source-qualified actors; teardown cannot reset the player session. Source campaigns and their native payloads remain separate.
- Qualify content by its own source/build/asset identity. Reuse `SourceWeaponKey`, `WeaponTargetKey` and `PlayerAvatarIdentity`; do not alias equal native numbers across games or invent competing player inventories.
- World/source adapters may inspect origin for authored starts, missions, water, taxis, native actor context and provenance. Those checks cannot grant player-global capabilities. Explicit retail-parity/diagnostic modes are chosen by the caller, never inferred from world origin.
- An unavailable source/provider or unrecovered capability needs an explicit failure/status. No silent capsule fallback in ordinary play; no placeholder avatar or gadget advertised as working.

## Working style
- Keep OBPGame a thin Godot composition root. New gameplay features belong in owned components, not in the root.
- `dev` is the protected integration branch. Never push directly to `dev`; work in a feature branch or isolated worktree and open a pull request targeting `dev`.
- Pull requests must compile and resolve substantive review comments. All merges are deliberate and manual; do not enable auto-merge.
- `dev` is the sole maintained integration/release branch; do not recreate a permanent `main` release pointer. Keep changes focused and avoid bookkeeping-only commits.
- Prioritize playable gameplay and native fidelity. Do not add automated tests or test infrastructure unless explicitly requested.
- When creating/moving scripts under `game/scripts` or `game/diagnostics`, move existing `.cs.uid` files alongside renamed scripts (`git mv`), or run `./tools/sync-godot-uids.ps1` (Linux: `bash tools/sync-godot-uids.sh`) to generate missing sidecars with the checksum-pinned Godot .NET editor. Commit the sidecars. The C#/.NET command skips importing and does not build the game when none are missing; `-Check` or `--check` validates without Godot. Do not manually invent UIDs.
- Use ./tools/build.ps1 on Windows or bash tools/build.sh on Linux to compile changes.
- Verify formatting with dotnet format OneBigPackage.sln --verify-no-changes --no-restore.
- Changes depending on retail bytes should be manually checked against authorised local game sources.
- Keep generated retail evidence payload-free and reproducible.
