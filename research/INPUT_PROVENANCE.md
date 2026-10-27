# Input provenance

OBP treats original game data as private research input, never repository content.

Every source should distinguish the **authoritative payload** from the way it was stored or transported. Historical research inputs included split archives, numbered raw-ISO parts and complete ISO files; those formats are not interchangeable, and the native importer now accepts only a complete ISO.

Record:

- game and display title
- region / TV standard
- disc serial
- revision
- authority status (`primary`, `secondary-comparison`, etc.)
- payload/original disc-image filename, exact byte size and SHA-256
- transport/storage format when applicable
- transport archive filename, exact byte size and SHA-256 when practical
- chunking method and volume size
- per-volume SHA-256 values when practical

## Historical split transport provenance

Older authority manifests record two distinct `binary-concat` transport formats. These are historical storage records, not supported native import inputs:

1. `raw-iso-split`: the numbered parts are contiguous slices of the authoritative disc payload. Concatenating them reconstructs the ISO itself, so final size/SHA-256 verification must use `payload.sizeBytes` and `payload.sha256`.
2. split archive transport such as `7z`: concatenating the numbered parts reconstructs the archive transport. A second extraction step yields the authoritative disc payload, which then has its own size/SHA-256.

A manifest must never verify a reconstructed raw ISO only against transport metadata that omits the payload identity.

## Historical raw ISO split manifest example

```json
{
  "game": "rac2",
  "region": "NTSC-U",
  "serial": "SCUS-97268",
  "revision": "1.01 original retail",
  "authority": "primary",
  "payload": {
    "filename": "Ratchet & Clank - Going Commando (USA) (v1.01).iso",
    "sizeBytes": 3828350976,
    "sha256": "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5"
  },
  "transport": {
    "format": "raw-iso-split",
    "chunking": {
      "scheme": "binary-concat",
      "chunkSizeBytes": 503316480,
      "chunks": [
        { "file": "...iso.001", "sizeBytes": 503316480, "sha256": null },
        { "file": "...iso.002", "sizeBytes": 503316480, "sha256": null }
      ]
    }
  }
}
```

The native importer accepts a complete, seekable ISO file only. Historical numbered parts cannot be passed directly to the importer: if reconstructed outside OBP, verify the resulting ISO against the authoritative payload size and SHA-256 before importing. The old split-file random-access reader API has been removed.

## Initial authority plan

| Game | Baseline |
|---|---|
| Ratchet & Clank | NTSC-U original retail, SCUS-97199 |
| Going Commando | NTSC-U v1.01 original retail, SCUS-97268 |
| Up Your Arsenal | NTSC-U original retail, SCUS-97353 |
| Deadlocked | Later expansion; version to be selected before ingestion |

Regional/revision builds can be added as comparison evidence. OBP's intended content policy is a union of intentional content while avoiding accidental PAL timing artifacts and retaining well-supported fixes.
