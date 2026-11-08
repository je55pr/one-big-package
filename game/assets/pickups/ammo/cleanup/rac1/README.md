# Authored ammo cleanup overlays

These eight 64x64 RGBA PNGs contain only the differences between the user's edited ammo atlases and the verified RAC1 originals. Unchanged pixels are transparent black. Apply each overlay to its matching decoded source atlas using normal alpha compositing and nearest texture sampling.

The source atlases and full edited textures are not distributed. `AmmoPickupPresentation` checks the expected decoded RGBA SHA-256 before composing the overlay. Filenames identify the donor appearance, not a weapon recipient; family membership is specified in `docs/AMMO_PICKUPS.md`.

Pyrocitor supplies the shared Pyrocitor/Decoy shape. Tesla Claw needs no cleanup. R.Y.N.O. retains its original textures. This directory contains no extracted models, native atlas files, UI glyphs, previews or machine paths.
