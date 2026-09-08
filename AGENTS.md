# One Big Package agent guide

## Scope and authority
- Production code is native Godot 4 + C#.
- Retail NTSC-U game data and executable behaviour are authority for recovered native behaviour.
- Keep OBP-created design choices clearly separate from recovered retail behaviour.
- Never commit retail ISOs, executables, credentials, tokens, personal paths, or unrelated machine data.

## Architecture
- `OBP.IO`: bounded/random-access IO and authority verification.
- `OBP.PS2`: shared PS2 formats/codecs.
- `OBP.RAC1/2/3`: game-specific formats and provenance.
- `OBP.Runtime`: engine-independent runtime concepts.
- `OBP.Godot` + `game/`: presentation/application host only.
- Godot must not become the source of truth for native game formats or gameplay rules.

## Development
- Work on a dedicated branch/worktree; do not share writable checkouts between agents.
- Prefer small evidence-backed changes with deterministic tests.
- Run `./tools/test.ps1 -Configuration Release` on Windows or `./tools/test.sh Release` where supported.
- Run `dotnet format OneBigPackage.sln --verify-no-changes --no-restore` before proposing a merge.
- Keep generated retail evidence payload-free and reproducible.
- For changes whose correctness depends on retail bytes, run ./tools/test-retail.ps1 on an authorized local machine before calling the change authority-validated.
- Never upload retail media to GitHub Actions, artifacts, caches, releases, LFS, or other cloud storage as part of this workflow.

## TypeScript reference
- `reference-ts/` is archaeology/equivalence code, not the product runtime.
- Do not expand browser architecture or add new product features there.
- It remains temporarily important where native parity is incomplete, especially UYA/RAC3.
- Prefer promoting stable discoveries into native C#; remove corresponding TS only after native parity/evidence is preserved.
