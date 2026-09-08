# Trilogy opportunity matrix

> **NON-BINDING BRAINSTORM.** This is a design-opportunity map, not a specification or roadmap.

The central observation is that the three games are unusually complementary:

- **Ratchet & Clank (2002)** contributes exploration, planet identity, gadgets, environmental variety, and local ecology.
- **Going Commando** contributes repeatable activities, economy, weapon/health progression, armour, ship gameplay, arenas, racing, and resource loops.
- **Up Your Arsenal** contributes organised warfare, Ranger mission grammar, the Phoenix/VR infrastructure, arena deathcourses, and multiplayer-derived battlefield systems.

OBP does not need to pick one game's structure as the winner.

## High-level matrix

| Area | R&C1 | Going Commando | UYA | OBP opportunity |
|---|---|---|---|---|
| Exploration | Excellent | Strong | Reduced emphasis | Use R&C1-style branching and gadget revisits across the whole galaxy |
| Planet-specific ecology | Excellent | Strong | Often overshadowed by war factions | Preserve native populations and layer invasions on top |
| Gadgets / puzzles | Excellent | Excellent | Good but reduced | Treat all trilogy gadgets as one shared exploration vocabulary |
| Weapon progression | Gold Weapons / collectible-linked upgrades | XP evolution + mods | Deep V1+ evolution | Preserve multiple progression philosophies instead of flattening them |
| Armour | None | Megacorp armour | Gadgetron armour | Competing armour manufacturers across one market |
| Health progression | Fixed upgrades | XP + boosts | XP + later upgrades | Potentially combine combat growth with rare permanent upgrades |
| Arenas | Umbris/Snagglebeast pit as unused venue | Maktar + Joba | Annihilation Nation | Galactic arena circuit with distinct venue identities |
| Racing | Hoverboards | Hoverbikes | Little campaign racing | Keep both sports alive on one map |
| Ship gameplay | Limited set pieces | Full Star Explorer system | Mostly transport despite war setting | Persistent ship progression and space activity across the combined region |
| Military missions | Battlefield flavour, little reusable framework | Limited | Rangers + Siege-style systems | Spread UYA mission grammar to suitable R&C1/GC worlds |
| Repeatable activities | Limited | Extensive | Rangers / arena / VR | Give completed planets reasons to remain relevant |
| World-state changes | Some revisits | Outbreaks/occupations | Strong invasion/revisit precedent | Planets can evolve instead of remaining frozen levels |
| Hub infrastructure | Worlds themselves | Mostly distributed | Phoenix | Phoenix can be a major hub without becoming the whole game |
| Rare collectible economies | Gold Bolts | Platinum Bolts | Titanium Bolts | Keep distinct identities/purposes rather than one generic rare currency |
| Skill Points | Yes | Yes | Yes | Natural trilogy-wide challenge/interaction layer |
| Enemy diversity | Excellent local variety | Excellent local/faction variety | Large roster but repetitive campaign usage | Cross-pollinate intelligently while preserving homeworld ecology |
| Clank gameplay | Gadgebots + Giant Clank | Specialist Microbots + stronger Giant Clank | Smaller role | Combine puzzle vocabularies and keep Clank meaningful |
| Multiplayer-derived systems | — | — | Siege/control points/vehicles/bases | Re-purpose as single-player Ranger battlefield machinery |
| Boss reuse | Mostly one-shot | Arena-friendly bosses | Arena/VR-friendly bosses | Rematches, simulations, and challenge circuits without rewriting canon |

## High-leverage opportunities

Working impact/cost ranking:

| Rank | Opportunity | Likely impact | Rough new-content cost |
|---:|---|---|---|
| 1 | Cross-game enemy ecology | Massive | Very low |
| 2 | Ranger missions on R&C1 / GC worlds | Massive | Low-medium |
| 3 | Galactic arena circuit | Huge | Low |
| 4 | Persistent Star Explorer / ship gameplay | Huge | Medium |
| 5 | Armour vendors/lines across the combined galaxy | High | Low |
| 6 | Trilogy-wide Monsterpedia | High | Low |
| 7 | Metal Detector / gadget secrets in old levels | High | Low |
| 8 | Hoverboard + hoverbike sports circuits | High | Low-medium |
| 9 | Slim Cognito as persistent specialist vendor | High | Low |
| 10 | VR deck as universal training/simulation space | High | Low-medium |
| 11 | UYA multiplayer Siege concepts as PvE battlefield system | Huge | Medium |
| 12 | Boss reuse via arena/VR | High | Low-medium |
| 13 | Combined Clank bot systems | Medium-high | Medium |
| 14 | Raritanium as shared specialist/ship resource | High | Low |
| 15 | Branching weapon upgrades | Huge | High design complexity |
| 16 | Dynamic planet states | Enormous | High |
| 17 | New gadget routes throughout old worlds | Huge | Level-edit effort |
| 18 | Expanded Giant Clank content | High | Medium-high |

These rankings are brainstorming heuristics only.

## Arena circuit concept

Keep venues physically and mechanically distinct:

| Venue | Source | Potential identity |
|---|---|---|
| Umbris / Ring of Heroes space | R&C1 | Old-school monster/boss pit |
| Galactic Gladiators, Maktar | GC | Commercial televised combat |
| Megacorp Games, Joba | GC | Combat + extreme sports |
| Annihilation Nation | UYA | Combat + deathcourses / spectacle |
| Phoenix VR deck | UYA | Simulation, training, impossible rematches |

The goal is not one generic "arena menu". Each venue should remain a place.

## Ranger / warfare transplant

UYA supplies reusable mission verbs that can fit worlds from all three games:

- secure
- defend
- assault
- escort / cover
- destroy
- capture
- activate
- assassinate
- reclaim
- dogfight
- turret defence
- vehicle assault

Potential recipients include Batalia, Hoven, Gaspar, Quartu, Oltanis, Pokitaru, Endako, Boldan, Snivelak, Smolg, Damosel, Yeedil, and other worlds whose native geography already supports military/industrial conflict.

A particularly promising extension is adapting UYA Siege concepts to PvE:

```text
friendly base
enemy base
neutral/capturable nodes
vehicle nodes
turret / defence nodes
reinforcement points
destructible objective
```

This remains exploratory until the native UYA systems are understood.

## Economy / progression opportunities

Do not automatically collapse distinct currencies or progression systems.

| Resource/system | Native identity | Possible OBP role |
|---|---|---|
| Bolts | Universal normal money | Normal economy |
| Gold Bolts | R&C1 secrets / Gold Weapons | Classic or Gadgetron-linked rare progression |
| Platinum Bolts | GC weapon mods | Megacorp/Slim specialist modifications |
| Titanium Bolts | UYA rare collectible, mostly cosmetics | Wider specialist rewards if useful |
| Raritanium | Introduced in R&C1, major GC ship currency | Persistent ship/specialist technology resource |
| Desert crystals | Tabora | Local commodity |
| Moonstones | Grelbin | Local commodity |
| Sewer crystals | Aquatos | Local commodity |
| Skill Points | Trilogy-wide challenges | Cross-system experimentation / optional mastery |

Local commodities are useful precisely because they make particular worlds matter.

## Weapon progression: preserve differences

Do not assume every weapon should become one homogeneous V1-V8 ladder.

- R&C1 links some upgrades to exploration/Gold Bolts.
- GC introduces use-based evolution and Platinum Bolt mods.
- UYA deepens use-based evolution and behaviour changes.

Potential long-term model:

```text
base weapon
├─ experience -> native evolution where appropriate
├─ rare collectible -> special/gold enhancement where appropriate
└─ modification -> shock / acid / lock-on / other compatible mods
```

A key canonical example is the Lava Gun's different sequel evolutions:

```text
Lava Gun
├─ Meteor Gun (GC)
└─ Liquid Nitrogen Gun (UYA)
```

OBP might eventually preserve both as branches rather than deleting one. That is a concept, not a committed design.

## Things currently worth keeping distinct

Fusion should not erase identity. Current examples:

- hoverboards vs hoverbikes
- Gadgetron vs Megacorp armour
- Gold vs Platinum vs Titanium Bolts
- Trespasser vs Infiltrator vs Hacker
- Hologuise vs Tyhrra-Guise
- individual arena venues
- native planet fauna
- local commodities
- manufacturers/vendors
- alternative weapon evolutions

## One-session test

A useful conceptual test for whether OBP feels fused:

> Start on Kerwan, visit a Megacorp armour vendor opened after the convergence, answer a Ranger call on Batalia, fight a mixed Blarg/Thug/Tyhrranoid encounter, find raritanium with an older exploration gadget, spend it on the Star Explorer, compete at Maktar, train a new weapon on the Phoenix, then finish with a Rilgar hoverboard race.

If that sequence feels like one coherent game rather than changing between "R&C1 mode", "GC mode", and "UYA mode", the fusion is working.
