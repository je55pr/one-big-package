# Third-party acknowledgements

R&C1 player-audio and weapon reconstruction uses the public `rac1-native-game` source and its
function/address annotations as a reference, including Blaster contact and
steering behavior, Taunter queries, player sound/footstep dispatch, weapon/deployment timing,
particle constructors/updates, weapon explosion recipes and Tesla draw routines.

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
