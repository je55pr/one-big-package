# Build probing and confidence

OBP must separate **disc identification** from **exact revision authority**. A cheap range probe can establish strong evidence without pretending it proves every byte of a build.

## Probe layers

1. **ISO-9660 structure** — read the primary volume descriptor and root directory through bounded range reads. This establishes that the source looks like the expected disc filesystem, but does not identify a game.
2. **PS2 boot identity** — read root `SYSTEM.CNF`, parse `BOOT2`/`BOOT`, normalize the executable name to a serial such as `SCUS-97268`, resolve the `cdrom0:` path through ISO-9660, and read only the target's fixed 52-byte ELF32 header. Strong title evidence requires the expected serial plus a little-endian `ET_EXEC` MIPS boot executable. Program headers remain an optional bounded archaeology read and segment payloads are not touched by the probe.
3. **Exact revision authority** — match the authoritative payload SHA-256, or another explicitly revision-specific signature backed by archaeology. This is required before calling a revision exact. The native `OBP.IO` stack hashes seekable sources incrementally; the preserved TypeScript reference has the equivalent `packages/hashing` / `source-verification` path. Exact verification never requires buffering the whole multi-gigabyte image in memory.

Serials alone are not enough to distinguish every retail revision. In particular, `SCUS-97268` identifies Going Commando NTSC-U but does not by itself prove the v1.01 payload rather than another revision carrying the same serial. Valid PS2 ELF structure strengthens title/disc-family confidence but still does not make the revision exact.

## Production/native source contract

The production runtime uses the C# stack:

```text
OBP.IO random-access source
  -> OBP.PS2 ISO / boot identity
  -> game-specific OBP.RAC* decoder
  -> neutral/runtime data
```

A source may be one seekable image or a validated numbered split set exposed through `ConcatenatedRandomAccessReader`. `Ps2BuildIdentification` and the authority-manifest helpers keep cheap identification distinct from full exact verification.

Going Commando additionally exposes `OBP.RAC2.GcIsoLoad`: `Identify` performs the fast supported-build gate, `LoadLevel` imports through the native C# world path, and `Verify` performs the slower streamed SHA-256 authority check.

## TypeScript reference probe contract

The older importer/probe abstractions remain under [`../reference-ts/`](../reference-ts/) as executable research/reference code. In that tree, probing runs before a build identity is resolved: `ProbeSourceSet` contains byte sources plus optional caller-verified identity hints, while `ImportSourceSet` requires a resolved `OBPBuildIdentity`.

The reference trilogy importer shells use source key `disc`, and may receive a browser `File`/`Blob` or a `ConcatenatedRandomAccessReader` over ordered raw split parts. `packages/ps2-disc`, `packages/input-sources`, `packages/hashing` and `packages/source-verification` describe that historical/reference implementation. They are useful equivalence tools, but browser input semantics are no longer a production-runtime requirement.

## Current primary authorities

| Game | Build ID | Serial | Payload SHA-256 |
|---|---|---|---|
| Ratchet & Clank | `rac1-ntscu-original` | `SCUS-97199` | `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d` |
| Going Commando v1.01 | `rac2-ntscu-v1.01` | `SCUS-97268` | `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5` |
| Up Your Arsenal | `rac3-ntscu-original` | `SCUS-97353` | `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444` |

The locally stored copies of all three have also been full-hash verified through the self-hosted `obp-local` runner; see [`../docs/LOCAL_RUNNER.md`](../docs/LOCAL_RUNNER.md).

## Importer confidence guidance

- `possible`: structurally plausible source, but no expected title identity yet.
- `strong`: expected PS2 boot serial agrees and the referenced target is a valid little-endian ELF32 MIPS executable.
- `exact`: authoritative payload hash or equivalently exact revision evidence agrees.
- `none`: structural or identity evidence contradicts the importer.

These labels originated in the TypeScript importer registry, but the evidence distinction is still useful across the native implementation: cheap structural/title identification is not a substitute for exact payload authority.

## Current world/import status

The old TypeScript `importer-rac*` status should not be confused with the production native runtime:

- The **merged C# GC path** already identifies, imports and renders supported retail Going Commando levels through `OBP.RAC2` / Godot; production integration is no longer waiting on a browser `OBPImporter.importWorld()` hook.
- The **TypeScript reference** still preserves its older public importer contracts and `reference-ts/tools/gc-world.mjs` world-assembly path for archaeology/equivalence.
- R&C1 and UYA specialist branches are actively proving native world compatibility and formats; their branch-local progress is summarized in [`../docs/CURRENT_STATE.md`](../docs/CURRENT_STATE.md) until promoted/merged into the C# production stack.

For agent-driven retail questions, prefer a narrow `obp-local` job against the verified local authority when the runner is online. ChatGPT sandbox materialisation and remote split/range transport remain fallbacks, not production architecture.
