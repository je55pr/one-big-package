# PS2 geometry pipeline — boundaries and remaining caveats

The earlier version of this note was a pre-implementation plan. The merged GC work has now validated the core architecture on retail data, so this version records only the reusable design boundary and the places that are **not** yet generic.

## What current retail GC proves

Current `main` contains a deliberately small `packages/ps2-vif` layer plus native GC tfrag, TIE, shrub and Moby readers. On the authority Going Commando disc, those readers successfully reconstruct ordinary renderable geometry without running PS2 VU microprograms.

The working architecture is:

```text
bounded native byte range
        |
        v
VIF command framing / UNPACK access
        |
        v
asset-specific packet interpretation
(tfrag / TIE / shrub / Moby)
        |
        v
normalised OBP geometry/material data
```

This is preferable to a VU-emulator-shaped abstraction for current importer needs. The native asset reader knows which VIF transfers are positions, UVs, indices, material switches, matrix slots, and so on; the generic layer only needs to frame and expose commands safely.

## Keep `packages/ps2-vif` small

A useful generic boundary is:

- decode a VIF code word;
- calculate/check command payload ranges for the supported packet forms;
- expose bounded UNPACK data and relevant state-setting commands;
- reject truncation/implausible lengths;
- leave Ratchet-specific meaning to the asset decoder.

Do **not** advertise the package as a complete PS2 VIF/VU implementation simply because it handles every command shape encountered in the GC assets decoded so far.

## `STCYCL` caveat

The important known public-reader limitation is VIF `STCYCL` **filling** mode (`WL > CL`). Public Ratchet readers largely assume the simpler write/skipping patterns, and current OBP geometry support has been validated against the retail packets it actually consumes rather than against the complete VIF state machine.

Before broadening the generic decoder to another game or asset family:

1. catalogue encountered VIF command ids;
2. record active `STCYCL` values around each UNPACK;
3. verify how many source words are consumed versus how many VU-memory elements are logically written;
4. add only the state semantics demonstrated by authority packets;
5. preserve deterministic bounds/caps so a malformed packet cannot create unbounded work.

If an authority packet eventually uses `WL > CL`, implement that statefully and add a focused fixture before relying on it.

## GS/GIF boundary

Tfrag/TIE/shrub/Moby readers also reconstruct small pieces of GS/GIF-oriented packet intent (texture/material switches, primitive organisation). Keep those rules local until enough common authority evidence justifies a shared GS/GIF package.

A general GS emulator is not required merely to render recovered assets. A shared decoder becomes worthwhile when multiple formats need the same register interpretation for material fidelity (blend modes, alpha test, wrap/filter state, etc.).

## VU boundary

Current GC recovery demonstrates that static/import-time geometry can often be reconstructed from the data transferred *to* the VU and from the surrounding packet grammar. Even Moby bind-pose recovery now simulates only the relevant matrix-slot/blend bookkeeping rather than executing arbitrary microcode.

That is the preferred importer strategy:

- model the smallest evidenced state machine;
- never execute retail code/microprograms as part of parsing;
- keep packet lengths and output sizes bounded;
- preserve unknown control words where they may matter later.

If a later format truly requires VU execution semantics, treat that as a separate, evidence-backed subsystem rather than silently growing the file parser into an emulator.

## Cross-game rule

Do not infer `GC packet shape == trilogy packet shape`. RAC1, UYA and Deadlocked can share the same broad asset family while changing packet details or packed layouts. Catalogue each authority build first, then factor common code only where the bytes justify it.
