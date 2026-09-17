# Native audio foundation

## Decision

Use **Going Commando NTSC-U v1.01** as the first native audio authority:
`rac2-ntscu-v1.01`, serial `SCUS-97268`, authority payload SHA-256
`9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`.

This is an engineering choice about evidence quality and recovery cost, not a preference for one game.
GC is the only trilogy authority that already gives OBP all of these at once:

- file-addressable global and per-level audio containers through ISO-9660;
- bounded level-local sound-bank ranges already identified inside `LEVEL*.WAD`;
- preserved native/core remap offsets that can later tie sound IDs to bank entries;
- a recovered environment-sample field currently identified as `music_track`;
- an existing bounded/random-access importer path that can expose all of the above without copying retail payloads.

R&C1 and UYA remain valid later authorities. They are worse *first* authorities because audio discovery
still begins behind raw-disc tables or less-settled container semantics.

The first implementation milestone should therefore recover the minimum GC catalogue/container/codec
path needed to prove one native music or ambience stream plus one ordinary one-shot effect. It should
not attempt trilogy-wide playback, complete event routing, dialogue, cutscenes or FMV audio.

## Starting point from the repository audit

The prerequisite audit found no landed decoder or playback implementation to preserve or replace.

The current boundary is:

| Layer | Existing audio evidence | Missing implementation |
|---|---|---|
| R&C1 | raw-disc level catalogue; Ratchet/Moby animation data preserves sound counts and raw trigger words | no audio container/catalogue, bank parser, sample mapping, codec or playback |
| GC | `AUDIO.WAD`; `AUDIO0.WAD` … `AUDIO26.WAD`; level/chunk sound-bank ranges; core sound-remap offsets; reverb/music metadata leads | no audio-WAD parser, SBlk parser, remap decoder, sample/stream codec, music/SFX classification or playback |
| UYA | coherent hidden-ToC associated parts; retail header-size census; core sound-remap offset preserved | semantic container labels still partly public-derived; no payload parser, catalogue, codec or playback |
| shared/runtime | PS2 ISO/ELF/WAD-LZ/geometry/texture infrastructure | no shared audio codec, neutral runtime audio contract, Godot playback or audio tests |

This matches [`../docs/CURRENT_STATE.md`](../docs/CURRENT_STATE.md): runtime audio is still an
open capability rather than a partially landed subsystem.

## Why GC is the cleanest first authority

### 1. Discovery is already bounded and named

The verified GC disc exposes the entire per-level audio family as ordinary ISO-9660 files:
`/G/AUDIO0.WAD` through `/G/AUDIO26.WAD`, plus global `/G/AUDIO.WAD`.
The generated authority report records exact extents and sizes for each file, so format work can
start with one bounded file instead of reconstructing a raw-disc catalogue first.

By contrast, R&C1 exposes almost none of its main game payload through ISO-9660.
UYA has a reproducible hidden table at the candidate LBA-1001 region, but the per-row slot meaning
is still deliberately separated from the public label that calls slot 0 audio.

### 2. The outer audio header family is directly reproducible

A bounded authority probe against the verified GC ISO confirmed the `RC2.HDR` three-header packing
for every file-number row `0..26`:

- row start `+0x0000`: first word `0x00000060` on 27/27 rows;
- row `+0x0800`: first word `0x00001018` on 27/27 rows;
- row `+0x2000`: first word `0x0000137c` on 27/27 rows;
- next row begins after the established `0x3800` stride.

For row 0 specifically, `RC2.HDR + 0x5804` is `0x0013a098`, the ISO LBA of
`/G/LEVEL0.WAD`. These retail words settle the structural packing question relevant to this
foundation: the middle three sectors are one padded `0x1018` associated header, not three
independent semantic records.

Direct file-header reads also gave:

| Bounded retail window | Observation | SHA-256 of inspected window |
|---|---|---|
| first `0x80` bytes of `/G/AUDIO.WAD` | first word `0x1800` | `d1488e98f85a66f05f41abe06587ab3225825bb2fa020297d04da3f7b22db1b9` |
| first `0x80` bytes of `/G/AUDIO0.WAD` | first word `0x1018` | `caa479914aa83bd23b88b36b729e8ae1144887fb9b57e737bfc17357b67e9184` |

These hashes identify tiny payload-free research observations. They do **not** establish what every
inner field means, and the first implementation should retain unknown words until a consumer or
executable trace proves them.

### 3. Ordinary one-shot recovery has a bounded bank witness

`GcLevelWad` already exposes the level-WAD lump table as bounded subranges. Existing research maps
slot 1 to the level sound bank and slots 7–9 to chunk sound banks on streamed levels.

A direct read of `LEVEL0.WAD` slot 1 confirms a structured bank prefix: the bank begins at level-WAD
sector 1, has 1,503,232 bytes, and contains the ASCII `SBlk` marker at bank offset `0x18`.
The first 4096 bytes of that exact bank range hash to
`289d418a4a2daacfa3c3b1942dfe707cc780692739d456cc9132eeecd5ffb26c`.

This is the cleanest first one-shot candidate because the parser does not need to scan the disc or
guess bank boundaries. The remaining work is deliberately narrower: recover the SBlk header/index,
identify its encoded sample data and metadata, and then prove how at least one ordinary native sound
ID/remap selects one bank entry.

`GcLevelCore` also preserves `SoundRemapOffset` (`0x70`) and
`MobySoundRemapOffset` (`0xb4`). Their *names/semantics* are not yet independently decoded by OBP,
but the offsets are preserved and already participate in safe section-boundary construction.
They are therefore high-value mapping leads, not permission to invent a remap format.

### 4. Music/ambience has a native mapping lead

GC environment samples preserve a 16-bit field at packed offset `0x0c` that current research,
corroborated by the pinned public format source, identifies as `music_track`.
That field is not consumed by the runtime today.

This gives the format-recovery task a concrete experiment: correlate observed environment-sample
track values with entries/ranges in the matching `AUDIO<n>.WAD`, and require a reproducible retail
match before promoting the mapping to native-confirmed semantics.

## Why not R&C1 first

R&C1 has useful trigger archaeology, but no recovered native audio catalogue or bank boundary.
Its ISO-9660 tree exposes only three ordinary files; essentially all campaign content is addressed
outside that filesystem. Starting there would combine two unknowns at once: locate the audio
containers in raw-disc structures *and* recover their sample/stream formats.

That work will be valuable later, especially because animation data already preserves sound-count
and raw trigger evidence, but it is not the smallest route to a working native music + SFX spine.

## Why not UYA first

UYA is much closer than R&C1, but still has one extra layer of uncertainty compared with GC.
The bounded retail ToC census proves a coherent physical slot-0 family with `0x1818` headers on
35/35 observed parts and a resident/global `0x2340` header in the candidate ToC prefix.
Public evidence calls those audio, but OBP correctly keeps that semantic label provisional.

UYA also changes the associated-header shape relative to GC (`0x1818` versus `0x1018`) and reaches
campaign containers through the hidden raw-disc table rather than ordinary ISO-9660 file entries.
That makes it an excellent second authority for testing which lower-level codecs are genuinely
shared, but a less bounded first implementation target.

## Native format boundary for the first recovery pass

The selected GC pass should recover only enough native structure to answer four questions:

1. How do `AUDIO.WAD` and one representative `AUDIO<n>.WAD` describe bounded payload ranges?
2. How does one `SBlk` bank describe entries and encoded sample ranges?
3. What metadata proves sample/stream rate, channels, loop state and encoded length?
4. How do one music/ambience selector and one ordinary SFX/remap resolve to playable native data?

Everything else is out of scope until those witnesses exist:

- complete dialogue/voice localization;
- cutscene or `SCENE*.WAD` audio;
- FMV/MPEG audio;
- every music transition rule;
- complete Moby/weapon/event sound mapping;
- reverb DSP semantics;
- streaming-performance policy beyond bounded reads;
- R&C1/UYA container support;
- Godot playback.

### Library ownership

Keep GC-native container/index semantics in `OBP.RAC2`. An `Audio<n>.WAD` header, SBlk catalogue,
GC remap table or native track/sound ID is game provenance and must not leak into shared runtime types.

If direct retail evidence shows that the encoded elementary stream/sample format is a genuinely
generic PS2 codec, put only that codec and codec-level metadata in `OBP.PS2`.
For example, do not create a shared `Vag`/PS-ADPCM decoder merely because the bytes look familiar;
first establish the framing, predictor/flags, rate/channel source and loop interpretation from
retail samples. Shared code follows evidence, not naming convention.

`OBP.Runtime` should eventually receive only game-neutral playback intent/data:
a decoded or streamable clip/stream description, loop intent, gain/category, and optional spatial
placement. It should not know `SBlk`, `AUDIO.WAD`, `SCUS-97268`, native remap indices or file LBAs.

`OBP.Godot`/`game` should remain presentation hosts: choose Godot stream/player nodes, buses and
spatial playback from neutral runtime values. They must never become the source of truth for
native catalogue parsing or codec behaviour.

## Provenance rules

Audio archaeology must preserve the same evidence ladder as the rest of OBP:

1. **Retail-confirmed:** bytes/ranges/behaviour reproduced from the verified authority build.
2. **Executable-confirmed:** loader or playback behaviour traced in the authority executable.
3. **Public-corroborated lead:** a format name/field/relationship from a pinned external implementation
   that has not yet been independently promoted by retail behaviour.
4. **OBP design:** runtime/API/playback policy chosen by this project rather than recovered from retail.

Apply that ladder explicitly:

- Do not call an unknown associated range "music" or "SFX" solely because it lives in `AUDIO*.WAD`.
- Do not promote `music_track`, sound-remap or SBlk field meanings beyond the evidence actually checked.
- Preserve unknown header words and native IDs in the game-specific model until their semantics are proven.
- Never infer GC/UYA binary identity from shared lineage; verify both games before moving a codec to `OBP.PS2`.
- Pinned Wrench/source research may supply offsets, labels and experiment ideas, but no GPL implementation
  code is copied or mechanically translated. See [`EXTERNAL_SOURCE_POLICY.md`](EXTERNAL_SOURCE_POLICY.md).
- Retail ISO/audio/sample bytes remain private user-supplied inputs and never enter Git, fixtures or releases.
- Committed evidence may contain sizes, offsets, counts, hashes and small scalar/header observations only;
  tests that need retail payloads stay retail-gated.
- Derived caches must be rebuildable from a verified source and never become a second authority.

## Reproducible evidence used for this selection

The authority identity and ISO inventory come from
[`RETAIL_TRILOGY_DISC_LAYOUT.md`](RETAIL_TRILOGY_DISC_LAYOUT.md) and the generated
`rac2-ntscu-v1.01.stage0` report. The selected ISO was locally present and matched the previously
verified authority input by filename and prior generated identity; this task did not rehash the full
3.8 GiB payload. It performed bounded reads only and did not copy source payloads.

The bounded reads performed for this decision were:

- `RC2.HDR` row starts across file indices 0–26: `0x60 / 0x1018 / 0x137c` at
  row-relative offsets `0x0000 / 0x0800 / 0x2000`, 27/27 each;
- `/G/AUDIO.WAD` first `0x80` bytes, hash recorded above;
- `/G/AUDIO0.WAD` first `0x80` bytes, hash recorded above;
- `/G/LEVEL0.WAD` header plus its slot-1 bank and the bank's first 4096 bytes.

The all-row result settles the specific `RC2.HDR` packing ambiguity called out in
[`GC_LEVEL_LOADING.md`](GC_LEVEL_LOADING.md) for this authority build: the repeated region is three
sector-padded headers. It does **not** by itself settle deeper loader semantics, global directory
fields, or any inner `AUDIO`/`SCENE` format.

## Acceptance bar for the next task

The follow-up native-format recovery should not be considered complete merely because bytes can be
decoded to something audible. It should produce deterministic, payload-free evidence for:

- bounded parsing of one representative numbered GC audio container and the global container fields
  required by that representative path;
- bounded parsing of one representative SBlk bank and its entry/sample ranges;
- explicit encoded-format metadata: sample rate, channels, loop state and encoded length where applicable;
- a retail-backed mapping from one environment/native music selector to one playable stream;
- a retail-backed mapping from one ordinary native sound/remap ID to one playable one-shot;
- codec tests built from synthetic/generated vectors or tiny non-copyrighted constructed inputs;
- retail-gated assertions using hashes/metadata rather than committed audio bytes;
- clear rejection or preservation of unknown variants rather than permissive guessing.

Only after that evidence exists should the project design the neutral runtime audio contract and
wire Godot playback. This keeps the first audio milestone small enough to verify and broad enough
to become reusable infrastructure rather than a one-off soundtrack hack.