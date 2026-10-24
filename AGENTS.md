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
- `OBP.Runtime`: engine-independent runtime concepts.
- `OBP.Godot` + `game/`: presentation/application host only.
- Godot must not become the source of truth for native game formats or gameplay rules.

## Working style
- `dev` is the protected integration branch. Never push directly to `dev`; work in a feature branch or isolated worktree and open a pull request targeting `dev`.
- Pull requests must pass the required GitHub checks and resolve review conversations. All merges are deliberate and manual; do not enable auto-merge.
- `dev` is the sole maintained integration/release branch; do not recreate a permanent `main` release pointer. Keep changes focused and avoid bookkeeping-only commits.
- Prefer small evidence-backed changes with deterministic tests, but do not create process for process's sake.
- When creating/moving scripts under `game/scripts` or `game/diagnostics`, move existing `.cs.uid` files alongside renamed scripts (`git mv`), or run `./tools/sync-godot-uids.ps1` (Linux: `bash tools/sync-godot-uids.sh`) to generate missing sidecars with the checksum-pinned Godot .NET editor. Commit the sidecars. The command skips importing when none are missing; `-Check` or `--check` validates without Godot. Do not manually invent UIDs.
- Run `./tools/test.ps1 -Configuration Release` on Windows or `./tools/test.sh Release` where supported; both check UID coverage and run the sync diagnostics tests before .NET tests.
- Run `dotnet format OneBigPackage.sln --verify-no-changes --no-restore` before integrating code changes.
- For changes that depend on retail bytes, run `./tools/test-retail.ps1` on an authorized local machine before calling them authority-validated.
- Keep generated retail evidence payload-free and reproducible.
