# Up Your Arsenal build probing

Authority build: `rac3-ntscu-original` — NTSC-U original retail.

This file records direct observations from the supported retail bytes. Hashes were computed from bounded reads of the first split chunk; no complete ISO was reconstructed.

## Authority identity

| Field | Retail result |
|---|---|
| Disc payload | `Ratchet & Clank - Up Your Arsenal (USA) (En,Fr,Es).iso` |
| Disc size | 4,379,377,664 bytes |
| Disc SHA-256 | `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444` |
| ISO volume id | `RATCHETANDCLANK3` |
| `SYSTEM.CNF` LBA | 1000 |
| `BOOT2` | `cdrom0:\\SCUS_973.53;1` |
| Serial | `SCUS-97353` |
| Revision | original retail / `VER = 1.00` |
| Video mode | NTSC |

The ISO/disc identity above was already established by the Stage-0 report and authority manifest.

## Executable hashes — direct retail evidence

The authoritative boot target named by `SYSTEM.CNF` is `/SCUS_973.53` at LBA 1463, 771,008 bytes.

- **`SCUS_973.53` SHA-256:** `200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d`
- ELF entry: `0x00800008`
- program-header offset: `0x34`
- program-header count: 1
- section-header offset: `0x000bc258`
- section-header count: 9

The adjacent ISO file `/I5BOOTN.ELF` was hashed separately rather than being silently treated as the supported boot executable:

- LBA 1840
- 809,864 bytes
- **SHA-256:** `65acfb274288a3242e05227fc7924de82f1acbfd29481bd45de31ff527c1ffa3`
- ELF entry: `0x00800008`
- program-header offset: `0x34`
- program-header count: 1
- section-header offset: `0x000c5a20`
- section-header count: 9

`I5BOOTN.ELF` is therefore a distinct executable image. Its role should be established from executable behaviour/boot flow before assigning semantics; `SYSTEM.CNF` remains the authority for the supported boot target.

## Packed boot-executable archaeology path

`packages/uya-packed-elf` and `tools/uya-packed-elf.mjs` implement a deliberately staged experiment for the public lead that the retail boot ELF contains a WAD-LZ-packed Ratchet executable stream.

For HTTP authority runs, the tool first requires the independently recorded retail `/SCUS_973.53` identity above: exact size **771,008 bytes** and SHA-256 `200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d`. A mismatch aborts before embedded WAD scanning or section-stream semantics are applied.

The unique-candidate decode path then reuses the unchanged OBP WAD-LZ decoder and the pinned-public 16-byte Ratchet section-stream layout. The decoded stream now also produces a **loader-anchor candidate census** for later disassembly. It records only obvious aligned sites for:

- raw u32 value `1001`;
- MIPS `addiu`/`ori` loading immediate `1001` directly from `$zero`;
- adjacent MIPS `lui reg, 0x001f` plus `addiu`/`ori reg, reg, 0x4800`, which materialises byte address `0x001f4800`.

Each hit records its decoded section index/type, section-relative offset, packed-stream offset and proposed virtual address. These are **candidate constant sites only**. A constant can be unrelated data or code; no hit promotes LBA 1001 to native loader truth without disassembly/control-flow evidence tying that site to disc I/O or an independent native source.

## 2026-09-07 live shared-folder part-1 execution

The user's `Anyone with the link -> Viewer` Drive folder was enumerated successfully, retail split part 1 was discovered from that folder, and its individual share identifier was passed only as a temporary GitLab CI input. No folder URL, file URL, or Drive ID was committed.

Pipeline **2826746793** (iid **260**) ran the sparse HTTP source with **part 1 only**. The packed-ELF job **16348345764** succeeded and independently matched the exact boot executable authority hash above.

Observed packed executable results:

- one plausible embedded WAD candidate at executable offset **28,800** (`0x7080`);
- compressed size **741,754 bytes**;
- decoded size **3,521,764 bytes**;
- decoded SHA-256 `39ddc0784e58f7f94fd4c17341060a7d02a4afcd480bdf693c5ae5173c7d21f9`;
- public section-stream entry point **`0x0013ada8`**;
- **18** parsed sections;
- **0** trailing bytes;
- no stream warnings.

The constrained loader-anchor census found exactly one candidate site:

- `mips-direct-load-lba-1001`;
- instruction word `0x240403e9` = `addiu $a0, $zero, 1001`;
- decoded section index **2**;
- section-relative offset `0x2636c`;
- packed-stream offset `0x3d264`;
- proposed virtual address **`0x0013d2ec`**.

This is a strong next disassembly target, not loader proof. No raw aligned-u32 or `lui 0x001f` + low-half materialisation hit survived the deliberately narrow census in this retail stream.

The same part-1-only pipeline then failed the row-1 world job exactly when it requested logical byte **2,380,404,736** (`0x8de21800`), which lies in split **part 5** at offset **367,138,816**. The sparse reader raised an unmapped-read error rather than filling or shifting the missing range. This is the expected fail-closed behavior.

The durable folder/discovery/CI handoff is `research/UYA_SHARED_DRIVE_AUTHORITY_WORKFLOW.md`.

## Public candidate hidden-ToC anchor — direct bytes, loader provenance pending

A bounded read of exactly 2 MiB beginning at the pinned public candidate LBA 1001 (`0x001f4800`) succeeded against authority split chunk `.001`.

- read offset: 2,050,048 bytes
- read length: 2,097,152 bytes
- **window SHA-256:** `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`

The bytes at that candidate address form the coherent resident-header/table structure documented in `UYA_RETAIL_TOC.md`. This is strong retail corroboration of the public LBA-1001 lead, but the bounded read alone does **not** prove that the retail loader obtains its native table of contents from that address. Keep the address provenance explicitly provisional until packed boot-executable archaeology or another independent native source corroborates the loader behaviour.

Likewise, observing header sizes and sector-range topology is direct retail evidence; semantic names assigned to those structures remain subject to the normal evidence hierarchy.

See `UYA_RETAIL_TOC.md` and `generated/rac3-ntscu-original.uya-toc.csv` for the structural census.
