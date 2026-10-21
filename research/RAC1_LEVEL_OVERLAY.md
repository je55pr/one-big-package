# R&C1 offline gameplay-code access

Authority: original NTSC-U `SCUS-97199`, `rac1-ntscu-original`. This is static
disc/executable evidence. No emulator, savestate or live gameplay capture is
required by this probe, and no new gameplay observation is claimed.

## Verified Veldin source

The existing `Rac1DiscIndex` and `Rac1LevelData` readers locate Veldin's code in
outer level range 0, inner range 0. On the authority ISO that inner range is
1,645,644 bytes, SHA-256
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.

It contains seven serialized sections. Each has a 16-byte little-endian header:
destination address, payload size, section type, and common entry point. The
payload follows the header immediately. All seven headers identify entry point
`0x00245c28`. The addresses, sizes and offsets are retained in
[`generated/rac1-level-overlay-probe.json`](generated/rac1-level-overlay-probe.json).
Type-8 sections also carry serialized bytes in this container; a conventional
ELF assumption that they occupy no file space would misalign later sections.

This layout was checked against the retail bytes through the existing C# readers
and independently through the new bounded Python probe. Wrench's
[level-data reader](https://github.com/chaoticgd/wrench/blob/master/src/wrenchbuild/level/level_data_wad.cpp)
and [executable section reader](https://github.com/chaoticgd/wrench/blob/master/src/core/elf.cpp)
provide public corroboration of the container layout, not gameplay authority.

Mapping each section by its own destination reproduces **all 12** instruction
signatures previously retained by `rac1-savestate-movement-probe.py` from loaded
retail memory. These cover the stick conditioner, activation, heading, and
high-magnitude facing branch. This establishes a useful offline route to the
same code that earlier archaeology inspected through a savestate.

There is no single file-to-memory offset for the whole container. In particular,
neither boot-ELF virtual addresses nor an offset inferred from the final code
section can be applied to all the earlier data sections. Unmapped player state,
inputs, runtime pointers and other dynamically produced values remain unavailable.

## Animation finding for issue 151

The initial audit found that OBP retained sequence 3 for `Walk`, with
`Loop=false`, eventually holding frame 32 of 33. The smoke instead expected
sequence 4 after enough time at low stick. Further caller tracing resolved
this conflict: ordinary sequence 3 loops, and speed selects sequence 4.
See [ordinary locomotion animation recovery](RAC1_LOCOMOTION_ANIMATION.md) for
the complete branch, guards, thresholds, playback multipliers and frame mapping.

The offline Veldin code identifies a player sequence store at `0x00232494`, in
a helper that obtains the player Moby through `P+0x2080`. The player animation
advance routine at `0x00232670` reads the same pointer. Its ordinary frame path
at `0x00232930..0x00232998` compares the next-frame byte `Moby+0x51` with the
current sequence's frame count and resets that byte to zero on reaching the
count. That branch retains the current sequence and marks an end-of-cycle bit
in `P+0xa98`; it does not itself select sequence 4.

This distinguishes **frame wrapping within a clip** from **selecting a different
clip**. The ordinary caller clears the subrange loop, and its helper at
`0x00228c18` selects 3/4 from scalar-speed thresholds with hysteresis. Presentation
now loops sequence 3, and the smoke requires continued frame advancement and
wrapping. Native speed scaling and frame/blend alignment remain separate host
integration work; the new C# arithmetic contract does not claim that integration.

## Reproduction and limits

Use Python with the same Capstone dependency as the existing movement probes:

```powershell
$sourceIso = [Environment]::GetEnvironmentVariable('OBP_RAC1_ISO', 'User')
.tools/movement-python/Scripts/python.exe tools/rac1-level-overlay-probe.py `
  --iso $sourceIso --out research/generated/rac1-level-overlay-probe.json
.tools/movement-python/Scripts/python.exe -m unittest discover -s tests/tooling
```

The probe hashes the complete ISO, bounds both disc-directory layers, verifies
the exact overlay hash, rejects malformed/overlapping section destinations, and
checks the existing loaded-memory signatures. Its report contains addresses,
sizes, hashes, derived constants/rules and evidence limits. It does not export executable payloads or
record personal source paths.

The probe also admits Hoven's independently mapped overlay for the bounded
[ordinary-versus-ice crouch braking recovery](RAC1_SURFACE_MOVEMENT.md).
Its surface-flag producer differs, while its relocated coefficient-selection
block matches Veldin exactly. This is not general validation of all Hoven code.
Other levels, dynamic state initialization, full action semantics and
retail-versus-OBP motion equivalence remain separate work. Offline code analysis
can continue while visual retail capture is unavailable; live experiments are
still required for unresolved runtime behavior and final comparison.
