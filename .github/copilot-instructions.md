Follow the repository-wide rules in `AGENTS.md`.

For GitHub Copilot specifically:
- Treat native Godot 4 + C# as the production architecture.
- Preserve the boundary `IO -> PS2 -> RAC1/2/3 -> Runtime -> Godot/game`.
- Do not invent native Ratchet behaviour to fill archaeology gaps; mark uncertainty explicitly.
- Do not commit retail game bytes, ISOs, executables, secrets, credentials, personal paths, or machine-specific state.
- Use `reference-ts/` only as temporary archaeology/equivalence evidence where native parity is incomplete; do not revive it as a browser product runtime.
- Add or update deterministic tests for parser/runtime changes.
- Before finalizing changes, run the relevant tests and `dotnet format OneBigPackage.sln --verify-no-changes --no-restore`.
- Prefer focused pull requests with clear provenance and avoid unrelated cleanup in feature PRs.
