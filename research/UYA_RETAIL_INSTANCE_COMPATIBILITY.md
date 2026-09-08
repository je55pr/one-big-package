# UYA retail gameplay-instance compatibility

This note records a retail compatibility experiment against the canonical NTSC-U original `SCUS-97353` authority image. It does **not** promote the public-derived level table or slot labels to native-loader-proven truth; it records what happens when those hypotheses are applied to exact retail bytes.

## Result

For sampled physical table rows 1, 8, 20 and 50, the public-derived outer level slot 2 is a structurally valid WAD-LZ block and the unchanged Going Commando gameplay instance parser accepts its TIE and shrub placement blocks without loss.

The compatibility wrapper independently checks the proposed block pointers (`0x34` TIE, `0x40` shrub), declared counts, complete `count × structSize` extents and every matrix component before comparing with the unchanged GC parser. Across all four samples:

- the gameplay WAD-LZ stream decompresses cleanly;
- the complete declared TIE and shrub spans lie inside the decompressed gameplay buffer;
- every declared matrix component is finite;
- the unchanged GC parser returns exactly every declared TIE and shrub entry;
- every observed TIE/shrub `oClass` resolves to the independently eligible retail UYA class table from the core index;
- no sampled placement references a missing candidate class.

| table row | gameplay decoded bytes | TIE instances | TIE classes referenced / eligible | shrub instances | shrub classes referenced / eligible |
|---:|---:|---:|---:|---:|---:|
| 1 | 3,147,136 | 1,960 | 78 / 78 | 1,894 | 22 / 22 |
| 8 | 2,538,560 | 646 | 32 / 32 | 499 | 5 / 5 |
| 20 | 2,387,200 | 595 | 51 / 51 | 1,105 | 19 / 19 |
| 50 | 2,182,336 | 365 | 55 / 55 | 85 | 4 / 4 |

Deterministic placement-matrix SHA-256 values:

| row | TIE matrices SHA-256 | shrub matrices SHA-256 |
|---:|---|---|
| 1 | `73285d7304ffed03e005e94c099f634ad5947d1d1bcaa92a1dad165c6a3ab943` | `6d06ba36324fc349b68434f3f4570dad23f80fcaea7d0bc891ee864e167939ec` |
| 8 | `475c723d974cc2b8b62437de39eb3dd6b4b5995d20e62f58be56fe5fe190879e` | `b6e9cbb657587222ccda3a23e4225867f1c127d87b9f13e0320baeffecd8b5c0` |
| 20 | `828c40b7fef355cd107fa0387a2a51a416dc3443626937d4eeb7a1123f23303a` | `cd220cc5a5d2c1388a94d2ad567da099a777bb5eb2efb31f69c0e1d3bda81514` |
| 50 | `7b94cee7dedce9ad1b0afb4afe29d1af4ce7669a15e2de45bf30c235ed8ba6e4` | `1e7bb1aa94f2c6c2d9b41ef066f45a0ea6af6cb77381b4818ddc30acce6a2724` |

The row-1 gameplay compressed stream is 1,357,437 bytes with SHA-256 `52c76b7a4e9930129e38955578c20660601174d41bec48eafdad441889d25937`; its decompressed bytes hash to `0b499c872d36fb2d40baa01abea0eff0ac3067b8dce15a6a5eaddeb5756997b2`.

## Promotion boundary

This is strong retail evidence that the sampled UYA gameplay placement representation is GC-compatible for TIEs and shrubs. It does **not** by itself prove that the public name `gameplay`, outer slot number, or block-pointer provenance is the native engine's own semantic naming. Those provenance questions remain separate.

Reproduction uses `tools/uya-instance-compat-probe.mjs` and the self-hosted `local-uya-instance-census` CI mode. Pipeline `2827266520` / iid 362, job `16352074461`, passed on `Jess-Laptop`; the repository suite was 201/201 green in the same job.
