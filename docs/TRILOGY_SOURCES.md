# Trilogy retail source and world-provider architecture

Status: **trilogy source/destination/provider infrastructure is merged into `main`; R&C1 and Going Commando are native production providers, while UYA still awaits native C# provider promotion.**

OBP ultimately needs all three original PS2 games to coexist in one process. Source ownership, destination discovery and world loading therefore belong to separate application/runtime capabilities rather than to the Going Commando importer.

## Current production architecture

The merged application supports this architecture:

```text
launch OBP
  -> Game Sources
      -> attach Ratchet & Clank ISO
      -> attach Going Commando ISO
      -> attach Up Your Arsenal ISO
  -> bounded PS2 source identification
  -> remember local paths in user://sources.json
  -> re-open and re-identify remembered paths on startup
  -> Worlds
      -> enumerate registered destination providers
      -> select a neutral ObpDestination
  -> ObpWorldProviderRegistry
      -> correct IObpWorldProvider
      -> provider.Load(sourcePath, destination)
  -> RuntimeWorld
  -> RuntimeWorldScene
  -> Godot world / collision / player
```

R&C1 and Going Commando are both merged native production providers. R&C1 exposes all 19 authority destinations as `rac1:LEVEL0` through `rac1:LEVEL18` through the same `IObpWorldProvider` contract used by GC. UYA can already be attached and retained as a valid retail source, and its full retail TypeScript/reference importer is merged; its native C# `IObpWorldProvider` remains pending.

**Source availability, destination discovery and world-provider availability are intentionally separate capabilities.**

## Source-library boundary

The source library is engine-neutral:

```text
Rac1Authority --\
Rac2Authority ----> TrilogySourceDefinitions (application composition root)
Rac3Authority --/                 |
                                  v
                         ObpSourceLibrary
                    (OBP.PS2 + OBP.Core models)
                                  |
                                  v
                      attached-source session
                                  |
                   user://sources.json
```

`ObpSourceLibrary` does not depend on Godot, RAC1, RAC2 or RAC3. The application supplies `ObpSourceDefinition` values using authority identities owned by the game-specific projects. Godot owns only the host UI and `user://` configuration location.

## Fast identification versus exact authority verification

Attaching/restoring a source performs a **bounded fast probe**:

1. open ISO-9660;
2. read `SYSTEM.CNF`;
3. resolve the boot executable;
4. validate the fixed ELF32 MIPS header;
5. normalize the boot serial;
6. match that serial to one configured OBP authority;
7. require the exact payload byte length for that authority.

A known serial with the wrong payload length is **recognized but unsupported**. A serial alone is not sufficient evidence for every retail revision.

The source manager deliberately does **not** stream a multi-gigabyte SHA-256 every time OBP starts. Exact full-payload verification remains an explicit operation using the existing authority-manifest/hash infrastructure.

## Persistence and privacy

`user://sources.json` stores only:

- source game;
- expected OBP build id;
- local filesystem path.

Retail payload bytes are never copied into OBP storage.

Persisted metadata is not trusted as proof. On every restore, OBP reopens the file at that path and repeats the bounded disc/serial/size probe. A missing path, replaced file, wrong game or unsupported revision is not silently admitted.

`Forget` removes the session/config entry only. It never changes the retail file.

## Neutral destination identity

`OBP.Core.ObpDestination` deliberately separates native destination identity from human planet/location labels. Important fields include:

- globally unique `DestinationId`, e.g. `rac2:LEVEL21`;
- source `Game` and `BuildId`;
- `NativeDestinationId`, e.g. `LEVEL21`;
- optional `NativeEngineId`;
- `PlanetLabel` and `LocationLabel`;
- native container provenance;
- coarse debug `Kind`.

This prevents repeated planet names from being accidentally flattened. Going Commando alone already demonstrates why this matters: three distinct native destinations are labelled Aranos, and `LEVEL21.WAD` uses file/index identity 21 while self-reporting native engine level id 30.

The host therefore preserves:

```text
rac2:LEVEL21
  destination id       rac2:LEVEL21
  native file/index    LEVEL21
  native engine id     30
  planet               Aranos
  location             Floating Prison
```

Future cross-game relationships such as multiple Kerwan, Rilgar or Aridia regions can be designed deliberately instead of being implied by string equality.

## Destination catalogue and provider contracts

Discovery and loading are separate contracts:

```csharp
IObpDestinationCatalogue
  Game
  BuildId
  Destinations

IObpWorldProvider
  Game
  BuildId
  Catalogue
  CanLoad(destination)
  Load(sourcePath, destination) -> RuntimeWorld
```

`GcDestinationCatalogue` is a neutral projection of the existing retail-backed `GcPlanetCatalogue`; it is not a duplicate hand-maintained GC truth table.

`GcWorldProvider` validates the neutral destination and invokes the existing `GcWorldImport` path. In particular, it resolves the native file/index identity rather than assuming the engine id is the file id.

`ObpWorldProviderRegistry` validates composition at startup:

- at most one provider per source game;
- provider game/build must match its catalogue;
- every destination must carry matching provenance;
- global destination ids must be unique;
- destination lookup is case-insensitive for CLI/debug use.

Merged `main` registers `GcWorldProvider`. The current `chatgpt/obp` integration branch registers both `Rac1WorldProvider` and `GcWorldProvider`; this is the first proof that the neutral registry/UI does not need source-game-specific rewrites when a second importer becomes production-usable.

## Generic Godot world entry

The neutral interactive path no longer asks the application to convert a destination back into GC-specific world data before rendering.

```text
ObpDestination
  -> ObpWorldProviderRegistry.Load
  -> IObpWorldProvider.Load
  -> RuntimeWorld
  -> OBPGame.AdoptRuntimeWorld
  -> RuntimeWorldScene
```

`AdoptRuntimeWorld` owns the host-side environment, shared Godot world builder, collision, spawn/camera and debug presentation. That is the seam future R&C1/UYA providers will use.

The old GC-specific selector/direct-load path remains temporarily for existing GC regression, stress and compatibility tooling; it is not the architecture future providers should copy.

## Current UI behaviour

A normal interactive launch lands on **One Big Package — Game Sources**.

Each trilogy source has attach/change/forget controls and reports whether a production world provider is available. If at least one attached game has a provider, **Browse available worlds ->** opens the neutral Worlds browser.

On `chatgpt/obp` this now means:

```text
Ratchet & Clank
  source attached -> 19 native destinations available

Going Commando
  source attached -> destinations available

Up Your Arsenal
  source attached -> runtime importer pending
```

R&C1 destinations intentionally retain evidence-safe labels (`R&C1 native level N`) until the retail/executable level-to-planet mapping is promoted as authority. The provider does not invent names merely to make the browser prettier.

Navigation is:

```text
Game Sources
   -> Worlds
      -> Runtime world

Esc from runtime world -> Worlds
Esc from Worlds        -> Game Sources
```

No process restart is required.

## CLI / deterministic capture

Source preparation remains available through:

```text
--rac1-iso <path>
--gc-iso <path>
--uya-iso <path>
```

A canonical neutral destination may be addressed directly:

```text
--destination rac2:LEVEL1
```

The deterministic browser screen is:

```text
--test-scene worlds
```

`tools/capture.ps1` understands the trilogy source arguments, `-Worlds`, and `-Destination`. Existing GC-specific arguments remain for compatibility with the mature GC regression harness.

## Validation

Synthetic/unit coverage verifies:

- recognition of all three configured trilogy serials;
- rejection of unknown serials;
- known-serial/wrong-size rejection;
- one attached source per game;
- persisted config round-trip;
- re-probing on restore rather than trusting stale saved identity;
- preservation of all GC native destinations without planet-name collapse;
- separation of GC file/index identity from engine identity;
- provider rejection of foreign/invented destinations;
- registry destination resolution and provider provenance.

Retail-backed source validation is performed by the self-hosted `obp-local` runner. The bounded trilogy source probe has passed against all three real authority ISOs under `C:\ChatGPT\ISOs`.

The provider-driven architecture through commit `0286f51a` passed a full local .NET solution build/test in pipeline **494** on 2026-09-07 after a missing `OBP.Godot` namespace import was caught and fixed.

The opt-in `local-ui-capture` acceptance gate then passed in pipeline **497** against the three real authority sources. It produced six archived artifacts (three PNG + three JSON metadata files):

- `sources.png` — source manager with R&C1, GC and UYA all attached;
- `worlds.png` — neutral Worlds browser with one registered runtime provider;
- `oozla-neutral-entry.png` — `rac2:LEVEL1` entered through `ObpWorldProviderRegistry -> GcWorldProvider -> RuntimeWorld` and rendered through the shared Godot builder.

That neutral Oozla run imported 304 runtime meshes / 1,882,531 render triangles / 328,923 collision triangles before Godot scene assembly and successfully captured the resulting world. The capture artifacts are retained by GitLab for seven days.

On 2026-09-08 the R&C1 provider was validated directly on Jess-Laptop against the exact `rac1-ntscu-original` authority image. The retail all-level census passes: every one of the 19 native R&C1 destinations loads through `IObpWorldProvider` with non-empty terrain, textures and collision. After the merged sky plus TIE/shrub static-instance promotion, `rac1:LEVEL0` imports 286 runtime meshes / 751,435 render triangles / 86,184 collision triangles, and the debug player can ground on reconstructed R&C1 collision.

## Deliberately not solved here

This infrastructure does **not** define:

- a combined Solana/Bogon star map;
- cross-game planet identity or chronology;
- the native C# UYA world provider and the still-unpromoted R&C1 Moby/gameplay layers;
- shared inventory/progression/save semantics;
- how recurring planets should eventually be presented;
- final destination unlock rules;
- automatic full hashing on startup.

Those are higher-level reconstruction/design questions. This layer exists specifically so they do not have to be decided in order to make all three retail games first-class runtime sources.
