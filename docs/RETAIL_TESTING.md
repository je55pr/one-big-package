# Retail-authority testing

OBP's portable CI never contains or downloads retail game media. Retail ISOs stay on an authorized local development machine and are supplied to the test suite only as local filesystem paths.

The native tests recognize three process environment variables:

- `OBP_RAC1_ISO`
- `OBP_GC_ISO`
- `OBP_UYA_ISO`

`tools/test-retail.ps1` is the canonical local gate. It accepts the three paths explicitly (or uses already-set environment variables), validates that each file exists, temporarily exports the variables for the test process, and restores the caller's environment afterwards.

```powershell
./tools/test-retail.ps1 `
  -Rac1Iso 'X:\path\to\rac1.iso' `
  -GcIso 'X:\path\to\gc.iso' `
  -UyaIso 'X:\path\to\uya.iso'
```

The default configuration is `Release`.

## Security and provenance

Do not commit, upload, cache, attach, artifact, or otherwise transmit retail ISOs through GitHub. GitHub-hosted CI should exercise synthetic fixtures, deterministic baselines, and payload-free evidence only.

For parser, archaeology, or runtime changes whose correctness depends on retail bytes, a successful portable CI run is necessary but not sufficient. Run the retail-authority gate locally before treating the change as authority-validated.
