# One Big Package agent guide

## Ground rules
- Production code is Godot 4 + C#.
- Retail NTSC-U game data and executable behaviour are authority for recovered native behaviour.
- Keep OBP-created design choices clearly separate from recovered retail behaviour.
- Never commit retail ISOs, executables, credentials, tokens, personal paths, captures, or unrelated machine data.

## Architecture
- `OBP.IO`: bounded/random-access IO and authority verification.
- `OBP.PS2`: shared PS2 formats/codecs.
- `OBP.RAC1/2/3`: game-specific formats, evidence and native behaviour.
- `OBP.Runtime`: engine-independent runtime concepts.
- `OBP.Godot` + `game/`: presentation/application host only.
- Godot must not become the source of truth for native game formats or gameplay rules.

## Working style
- `dev` is the normal working branch. Work directly there when no concurrent worker can collide with you.
- Use a separate branch/worktree only when concurrent work needs isolation; integrate the actual result back into `dev` and do not create bookkeeping-only commits.
- `main` is a release pointer: advance it only to a known-good `dev` commit.
- Prefer small evidence-backed changes with deterministic tests, but do not create process for process's sake.
- Run `./tools/test.ps1 -Configuration Release` on Windows or `./tools/test.sh Release` where supported.
- Run `dotnet format OneBigPackage.sln --verify-no-changes --no-restore` before integrating code changes.
- For changes that depend on retail bytes, run `./tools/test-retail.ps1` on an authorized local machine before calling them authority-validated.
- Keep generated retail evidence payload-free and reproducible.
