# Authored ammunition families

Normal play requires all three supported game sources. Only first-run source setup may be incomplete. A required source disappearing after setup, or required ammo assets failing to prepare, is a fatal error (exit 2), not a source-local visual fallback or permission to retire ammo crates and continue. Explicit retail diagnostics retain their own source-scoped paths.

Ordinary OBP ammo crates use the persistent player's owned, implemented, attached-source equipment. They choose uniformly among families with missing ammunition when each piece is emitted. If every admitted weapon is full, a piece may still be emitted for an owned family and wait for ammunition to be spent. No eligible owned weapon means no ammunition piece; crates never manufacture ownership or enable an unrecovered weapon.

The visible family stays fixed. At collection, a piece chooses one currently eligible, non-full recipient **within that family**, using that weapon's own source-provided refill amount and capacity. A family does not refill every weapon at once. A full, removed, or unavailable recipient does not consume the piece. Selection is not based on the current world, equipped weapon or shared native numbers.

## Design mapping

This table is the user's OBP-authored design, not a recovered retail classification. Native upgrades must be explicitly bound to their base family when their source-qualified implementation is registered.

| Pickup family / visual donor | Weapon members |
|---|---|
| Bomb Glove | Bomb Glove, Gravity Bomb, Nitro Launcher, Bouncer, Hoverbomb |
| Pyrocitor | Pyrocitor, Decoy Glove, Lava Gun, Plasma Whip |
| Blaster | Blaster, Lancer, N60 Storm, Blitz Gun |
| Glove of Doom | Glove of Doom, Miniturret, Spider Bot, Holoshield |
| Mine Glove | Shield Charger, Clank Zapper, Infector, Rift Inducer |
| Devastator | Devastator, Minirocket Tube, Annihilator |
| Visibomb | Visibomb, Seeker Gun, Pulse Rifle, Flux Rifle |
| Tesla Claw | Tesla Claw, Plasma Coil, Shock Blaster, Spitting Hydra |
| R.Y.N.O. | R.Y.N.O., R.Y.N.O. 2, R.Y.N.O. 3, Zodiac |
| Drone Device | Drone Device, Chopper, Disc Blade, **Mine Glove**, Synthenoid |

Mine Glove's placement in Drone Device is intentional. Pyrocitor and Decoy use the same cleaned Pyrocitor donor geometry. Tesla's lightning symbol remains intact; R.Y.N.O. retains its original artwork and a separate family.

## Implemented boundary

`PlayerAmmoFamilies` binds only currently implemented recipient identities: the eleven ammo-bearing RAC1 items, RAC2 Lancer (item 30), and RAC3 V5 Shock Cannon (definition 43, in the Shock Blaster line). Unsupported names in the design table do not become equipped or refillable merely by being listed here. The Mine Glove **pickup family** currently has no implemented recipients; its visual is prepared for the future members in the table.

RAC1 and RAC2's implemented ammo-crate paths use these family drops in ordinary play. Their existing source-specific crate break admission and piece counts remain source-owned. Explicit RAC1 retail diagnostic modes retain their native preferred-item pickup path; explicit RAC2 parity uses its original canister. RAC3 weapon ammunition can receive a family drop, but RAC3 class-511 ammo-crate break interaction remains unrecovered/unimplemented and is not advertised as working by this change.

The trajectory, attraction envelope and 12-tick visible-drop grace are authored shared presentation policy. `PlayerAmmoPickupMotion` never owns inventory. `PlayerAmmoPickupHost` owns transient scene nodes, physics queries and collection admission for one world lease. A successful clamped grant claims a piece once; subsequent homing cannot grant again. World teardown retires pieces without resetting player ammunition. Falling out of the world does not remotely award ammunition.

## Asset provenance

The ten meshes and UVs are decoded from the attached supported RAC1 ISO. The host applies the eight user-authored, 64x64 difference overlays under `game/assets/pickups/ammo/cleanup/rac1/`, matching each donor's decoded atlas SHA-256 before applying it. Native texture-table indices are local remaps, never global identifiers. Tesla and both R.Y.N.O. textures remain unmodified.

Only replacement pixels are stored in the overlays; unchanged pixels are transparent black. The original source atlas and the user's full edited atlas are not committed. There are no badge plates, generated replacement meshes or capsule fallbacks. If the source, donor, matching atlas or cleanup resource is unavailable, preparation reports an explicit failure.

The full intended mapping and selection rules live in Fusion. RAC1 owns its native pickup definitions and refill amounts; RAC2 owns Lancer's descriptor-backed 25-round refill; RAC3 owns Shock Cannon's four-round refill. Godot components only bind those facts to source visuals, collision queries, HUD/audio and persistent-player grants.
