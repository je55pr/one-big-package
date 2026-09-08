# Input provenance

OBP treats original game data as private research input, never repository content.

Every source should distinguish the **authoritative payload** from the way that payload is stored or transported. Jess may supply a split archive, a raw ISO split into numbered parts, or a complete ISO. Those forms are not interchangeable and need explicit identities.

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

## Split source semantics

Two `binary-concat` cases are intentionally distinguished:

1. `raw-iso-split`: the numbered parts are contiguous slices of the authoritative disc payload. Concatenating them reconstructs the ISO itself, so final size/SHA-256 verification must use `payload.sizeBytes` and `payload.sha256`.
2. split archive transport such as `7z`: concatenating the numbered parts reconstructs the archive transport. A second extraction step yields the authoritative disc payload, which then has its own size/SHA-256.

A manifest must never verify a reconstructed raw ISO only against transport metadata that omits the payload identity.

## Raw ISO split example

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

The streaming assembly helper writes `<output>.partial` and only atomically promotes it after supplied size/hash checks pass. For `raw-iso-split`, those final checks are taken from the payload identity. The complete source is never loaded into RAM.

For importer probing, reassembly is unnecessary: ordered split parts can be wrapped as `RandomAccessReader`s and exposed through `ConcatenatedRandomAccessReader`, so a native offset read touches only the necessary source ranges.

## Initial authority plan

| Game | Baseline |
|---|---|
| Ratchet & Clank | NTSC-U original retail, SCUS-97199 |
| Going Commando | NTSC-U v1.01 original retail, SCUS-97268 |
| Up Your Arsenal | NTSC-U original retail, SCUS-97353 |
| Deadlocked | Later expansion; version to be selected before ingestion |

Regional/revision builds can be added as comparison evidence. OBP's intended content policy is a union of intentional content while avoiding accidental PAL timing artifacts and retaining well-supported fixes.
