# Going Commando destination browsing

The source manager attaches an authorized local GC image, and the world browser
loads destinations through `Rac2PlanetCatalogue` and `Rac2WorldProvider`.
File number, native level ID and display label are separate; LEVEL21 has native
ID30 and is the Insomniac Museum. Source names come from the retail string-bank evidence in
[GC_PLANET_NAMES.md](../research/GC_PLANET_NAMES.md).

```powershell
./tools/play.ps1
./tools/play.ps1 -Planet endako
./tools/play.ps1 -Planet 19
./tools/capture-planets.ps1
./tools/capture-planets.ps1 -Framed
```

The launcher uses `OBP_GC_ISO` or an explicitly supplied source. Godot arguments
include `--gc-iso PATH`, `--planet NAME_OR_ID`, `--direct`, `--capture-frame N`,
`--capture-out PATH`, `--collision-debug`, `--verify-hash` and `--overlay LAYERS`.
Check `tools/play.ps1` and `CommandLineArgs` for the actual supported options.

Escape navigates back through the application. Source reidentification, provider
ownership and unload/reload boundaries are described in
[ARCHITECTURE.md](ARCHITECTURE.md); capture and inspector controls are in
[GODOT_PRESENTATION.md](GODOT_PRESENTATION.md).
Use [RETAIL_TESTING.md](RETAIL_TESTING.md) for validation. Historical showcase
counts, milestones and fixed-bug reports are retained in Git rather than here.
