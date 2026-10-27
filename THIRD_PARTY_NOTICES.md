# Third-party acknowledgements

## ReRAC type-6 emitter reference — ISC License

The source-owned R&C1 class-27/type-6 emitter uses the parameter layout and
algorithm reference in `crates/rc-game/src/particles/type06.rs` from
https://github.com/re-rac/rerac at revision
`7d0d031a4f293c1214d01de90918fe3ec234b673`.
OBP independently compares the implementation against original retail EE/VU
instructions. No retail code or assets are included. The optional type-6
research oracle builds the external MIT interpreter acknowledged below.

Copyright (c) 2026 ReRAC contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## rac1-native-game reference

R&C1 camera, player-audio, weapon and mission reconstruction uses the public `rac1-native-game` source and its
function/address annotations as a reference, including Blaster contact and
steering behavior, Taunter queries, player sound/footstep dispatch, weapon/deployment timing,
particle constructors/updates, weapon explosion recipes and Tesla draw routines.
The camera reference covers the type-0 follow pipeline, native axis/speed options,
projection and collision probe sequence. Mission references cover compiled talk
records, Veldin's encounter, the Novalis Plumber and actor-owned level exits.
The optional camera and mission research oracles build its MIT-licensed R5900/VU
interpreters locally to execute original disc functions. UYA's Shock Cannon
oracle also uses those interpreters with UYA's own boot VU0 math upload, beam
draw routines and sound attenuation code from the authorized original disc.
The trilogy skeleton oracle uses the same interpreters to execute GC/UYA
packed animation decoding, arm-layer baking and semantic joint queries on
temporary, locally extracted retail inputs.
The GC Museum particle research oracle also uses these interpreters to execute
original menu writes, particle allocation/simulation, texture relocation and
sprite descriptor construction. GC reconstructed C sources are not included or
translated; this adapter supplies declared external UI/view boundaries only.
The UYA reusable-particle oracle uses the same MIT interpreter to execute the
original type-97 constructor, callback, allocator, two Cannon recipe blocks,
resource relocation and sprite descriptors with declared pose/world/view inputs.
It includes no UYA reconstructed C or assembly payload.

Upstream: https://github.com/daino-selvatico/rac1-native-game

Reference revision: `3fd14c12e4dff2fd4b6661a6e54187faea3f76c8`.

The original project's code has the following license. No upstream generated
translation of retail machine code or original game assets is distributed here.
OBP's retail inputs and locally generated research remain separate.

## rac1-native-game — MIT License

Copyright (c) 2026 Daino Selvatico

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## 989snd reference — ISC License

The bounded SBlk grain renderer and SPU envelope arithmetic use the 989snd
reference bundled by rac1-native-game. The original per-file envelope notice is
Copyright 2021–2024 Ziemas; the bundle license is reproduced below.
The compact SBlk v3 parameter layout and local-register tone-volume semantics
were cross-checked against the same ISC-licensed loader/grain implementation.

Copyright (c) 2020-2026 OpenGOAL Team

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
