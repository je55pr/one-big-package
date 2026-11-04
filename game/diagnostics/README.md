# Diagnostic script layout

Diagnostic harnesses, scenarios, fixtures and support code are grouped by domain:
Framework, Fixtures, Movement, Player, CaptureReplay, Startup, UI,
and source-specific Rac1, Rac2, Rac3. Native test policy and behavior do
not change with these directory moves.

Existing .tscn smoke-scene entrypoints remain directly under game/diagnostics.
Keep the .cs.uid file beside each script and update any scene or tooling paths
when relocating scripts.