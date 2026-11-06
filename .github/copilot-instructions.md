Follow the repository-wide rules in `AGENTS.md`.

For GitHub Copilot specifically:
- Treat native Godot 4 + C# as the production architecture.
- Preserve the boundary `IO -> PS2 -> RAC1/2/3 -> Runtime -> Godot/game`.
- Do not invent native Ratchet behaviour to fill archaeology gaps; mark uncertainty explicitly.
- Do not commit retail game bytes, ISOs, executables, secrets, credentials, personal paths, or machine-specific state.
- Do not add automated tests or test infrastructure unless the maintainer explicitly requests them.
- Before integrating code changes, run a build and `dotnet format OneBigPackage.sln --verify-no-changes --no-restore`.
- Avoid coordination-only commits or unrelated process scaffolding.
