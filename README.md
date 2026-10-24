<p align="center">
  <img src="docs/assets/branding/obp-logo.png" alt="One Big Package logo" width="520">
</p>

# One Big Package

One Big Package (OBP) is a Godot 4 and C# project to combine the original PlayStation 2 games *Ratchet & Clank*, *Going Commando* and *Up Your Arsenal* into one game.

The first step is reconstructing the original games' levels and systems from user-supplied discs. These are the building blocks for the combined game, rather than three separate campaigns being the final goal.

## Goals

The long-term aim is to let content and progression cross the boundaries between the three games. For example:

- **One connected galaxy:** Travel between planets from all three games on a shared galaxy map, with one save for equipment, bolts, collectibles and mission progress.
- **Equipment across games:** Keep weapons from the whole trilogy, use later gadgets or Clank abilities on earlier planets, and work out how upgrades and vendors fit together.
- **Reasons to revisit planets:** Return to familiar locations with new equipment, routes, characters and missions.
- **Combined encounters:** Mix enemies from different games, including arena challenges that draw on the full weapon collection and enemy roster.

The original mechanics and encounters are being recovered first, so they can be preserved or deliberately adapted. The final campaign structure, balance and progression have not been decided yet.

**OBP is still in development and is not a complete playable trilogy.**

## Trilogy content catalogue

Named content from the original PS2 trilogy, grouped into collapsible tables. This is an **initial name-level inventory** covering all three games; it is **not** a verified census of every placed instance, enemy subclass, scripted event or upgrade stage. The retail-source audit is still needed to establish exhaustive coverage and accurate implementation status.

**Status:** **Playable** = working within the stated game/scope; **Partial** = implemented with known gaps; **Researched** = source behaviour documented; **Unassessed** = no implementation claim. Unassessed does **not** mean absent. Upgrade names are grouped with their base weapons; a playable base weapon does not imply its upgrades are implemented. Each repeated object placement is counted as a type, not as an individual row.

<details>
<summary>Weapons and upgrade families</summary>

**Ratchet & Clank (2002)**

| Weapon / family | Upgrade / variant | OBP status |
| --- | --- | --- |
| OmniWrench 8000 | Basic wrench attacks | Partial (ordinary wrench playable) |
| Bomb Glove | Gold variant | Partial (ordinary weapon playable) |
| Pyrocitor | Gold variant | Partial (ordinary weapon playable) |
| Blaster | Gold variant | Partial (ordinary weapon playable) |
| Glove of Doom | Gold variant | Partial (ordinary weapon playable) |
| Suck Cannon | Gold variant | Partial (ordinary weapon playable) |
| Taunter |  | Partial (ordinary weapon playable) |
| Mine Glove | Gold variant | Partial (ordinary weapon playable) |
| R.Y.N.O. |  | Partial (ordinary weapon playable) |
| Devastator | Gold variant | Partial (ordinary weapon playable) |
| Walloper | | Partial (ordinary weapon playable) |
| Visibomb Gun | | Partial (ordinary weapon playable) |
| Decoy Glove | Gold variant | Partial (ordinary weapon playable) |
| Drone Device | | Partial (ordinary weapon playable) |
| Tesla Claw | Gold variant | Partial (ordinary weapon playable) |
| Morph-O-Ray | Gold variant | Partial (ordinary weapon playable) |

**Going Commando**

| Weapon / family | Upgrade / variant | OBP status |
| --- | --- | --- |
| OmniWrench 10000 / 12000 | Wrench progression | Unassessed |
| Lancer | Heavy Lancer | Partial (firing in R&C1) |
| Gravity Bomb | Mini-Nuke | Unassessed |
| Chopper | Multi-Star | Unassessed |
| Seeker Gun | HK22 Gun | Unassessed |
| Pulse Rifle | Vaporizer | Unassessed |
| Miniturret Glove | Megaturret Glove | Unassessed |
| Blitz Gun | Blitz Cannon | Unassessed |
| Shield Charger | Tesla Barrier | Unassessed |
| Synthenoid | Kilonoid | Unassessed |
| Lava Gun | Meteor Gun | Unassessed |
| Bouncer | Heavy Bouncer | Unassessed |
| Minirocket Tube | Megarocket Cannon | Unassessed |
| Plasma Coil | Plasma Storm | Unassessed |
| Hoverbomb Gun | Tetrabomb Gun | Unassessed |
| Spiderbot Glove | Tankbot Glove | Unassessed |
| Sheepinator | Black Sheepinator | Unassessed |
| R.Y.N.O. II |  | Unassessed |
| Zodiac |  | Unassessed |
| Clank Zapper | Clank Shocker | Unassessed |
| Tesla Claw | Returning R&C1 weapon | Unassessed |
| Bomb Glove | Returning R&C1 weapon | Unassessed |
| Walloper | Returning R&C1 weapon | Unassessed |
| Visibomb Gun | Returning R&C1 weapon | Unassessed |
| Decoy Glove | Returning R&C1 weapon | Unassessed |

**Up Your Arsenal**

| Weapon / family | Upgrade / variant | OBP status |
| --- | --- | --- |
| OmniWrench 12000 | Wrench progression | Unassessed |
| Shock Blaster | Shock Cannon | Partial (Shock Cannon firing in R&C1) |
| Nitro Launcher | Nitro Eruptor | Unassessed |
| N60 Storm | N90 Hurricane | Unassessed |
| Plasma Whip | Quantum Whip | Unassessed |
| Infector | Infecto-Bomb | Unassessed |
| Suck Cannon | Vortex Cannon | Unassessed |
| Spitting Hydra | Tempest | Unassessed |
| Agents of Doom | Agents of Dread | Unassessed |
| Flux Rifle | Splitter Rifle | Unassessed |
| Annihilator | Decimator | Unassessed |
| Holoshield Glove | Ultrashield Launcher | Unassessed |
| Disc Blade Gun | Multi-Disc Gun | Unassessed |
| Rift Inducer | Rift Ripper | Unassessed |
| Qwack-O-Ray | Qwack-O-Blitzer | Unassessed |
| Miniturret Glove | Megaturret Glove | Unassessed |
| Lava Gun | Liquid Nitrogen Gun | Unassessed |
| Bouncer | Heavy Bouncer | Unassessed |
| Plasma Coil | Plasma Storm | Unassessed |
| Shield Charger | Tesla Barrier | Unassessed |
| R.Y.3.N.O. | RYNOCIRATOR | Unassessed |

Reference: [R&C1 weapons](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank_%282002_game%29), [GC weapon upgrades](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Going_Commando/Weapons), [UYA weapon upgrades](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal/Weapons), [R&C1 arsenal evidence](research/RAC1_ARSENAL.md), [cross-game weapon evidence](docs/FUSION_WEAPONS_TESTING.md).

</details>

<details>
<summary>Gadgets and traversal equipment</summary>

**Ratchet & Clank (2002)**

| Gadget / equipment | OBP status |
| --- | --- |
| Heli-Pack | Researched |
| Thruster-Pack | Researched |
| Hydro-Pack | Researched |
| O2 Mask | Unassessed |
| Pilot's Helmet | Unassessed |
| Swingshot | Researched |
| Trespasser | Unassessed |
| Grind Boots | Researched |
| Magneboots | Researched |
| Metal Detector | Partial (digging) |
| Hydrodisplacer | Unassessed |
| Sonic Summoner | Unassessed |
| Hologuise | Unassessed |
| Gadgetron PDA | Unassessed |

**Going Commando**

| Gadget / equipment | OBP status |
| --- | --- |
| Swingshot | Unassessed |
| Dynamo | Unassessed |
| Electrolyzer | Unassessed |
| Hypnomatic | Unassessed |
| Infiltrator | Unassessed |
| Thermanator | Unassessed |
| Tractor Beam | Unassessed |
| Heli-Pack | Unassessed |
| Thruster-Pack | Unassessed |
| Hydro-Pack | Unassessed |
| Glider | Unassessed |
| Levitator | Unassessed |
| Charge Boots | Unassessed |
| Gravity Boots | Unassessed |
| Grind Boots | Unassessed |

**Up Your Arsenal**

| Gadget / equipment | OBP status |
| --- | --- |
| Hacker | Unassessed |
| Hypershot | Unassessed |
| Refractor | Unassessed |
| Tyhrraguise | Unassessed |
| Warp Pad | Unassessed |
| Gadgetron PDA | Unassessed |
| Gravity Boots | Unassessed |
| Charge Boots | Unassessed |
| Heli-Pack | Unassessed |
| Thruster-Pack | Unassessed |
| Hydro-Pack | Unassessed |
| Banana-Guided Autonomous Monkey Device | Unassessed |

References: [R&C1 equipment](research/RAC1_TRAVERSAL_EQUIPMENT.md), [GC equipment](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA equipment](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal/Gadgets).

</details>

<details>
<summary>Enemies and hostile actor types</summary>

**Ratchet & Clank (2002)**

| Enemy / actor | OBP status |
| --- | --- |
| Alien snapper | Unassessed |
| Alien swarmer | Unassessed |
| Amoeboid | Unassessed |
| Anklebiter | Unassessed |
| Axebot | Unassessed |
| Big axebot | Unassessed |
| Birdbot | Unassessed |
| Blarg armored transport | Unassessed |
| Blarg bombthrower | Unassessed |
| Blarg commander | Unassessed |
| Blarg destroyer | Unassessed |
| Blarg elite commando | Unassessed |
| Blarg generator | Unassessed |
| Blarg gunner | Unassessed |
| Blarg heavy interceptor | Unassessed |
| Blarg heli-commander | Unassessed |
| Blarg paratrooper | Unassessed |
| Blarg saucer | Unassessed |
| Blarg space commando | Unassessed |
| Blarg space fighter | Unassessed |
| Blarg trooper | Unassessed |
| Bolt thief | Unassessed |
| Bomb-o-matic | Unassessed |
| Cannonball tank | Unassessed |
| Chomper | Unassessed |
| Constructo bot | Unassessed |
| Dark Gadge-Bot | Unassessed |
| Darter | Unassessed |
| Electroid | Unassessed |
| Extermibot (Rilgar) | Unassessed |
| Extermitank | Unassessed |
| Homing mine | Unassessed |
| Horned toad | Partial (Veldin class 749) |
| Machinegun turret | Unassessed |
| Mine layer | Unassessed |
| Minibomber | Unassessed |
| Peckbot | Unassessed |
| Plasmabot | Unassessed |
| Psy-tcopus | Unassessed |
| Puffoid | Unassessed |
| Robomutt | Unassessed |
| Robosquawker | Unassessed |
| Rocket tank | Unassessed |
| Sandshark | Unassessed |
| Sandshark nest | Unassessed |
| Sawbot | Unassessed |
| Scoutbot | Unassessed |
| Screamer | Unassessed |
| Seeker | Unassessed |
| Sentry-bot | Unassessed |
| Sharkigator | Unassessed |
| Test dummy | Unassessed |
| Toxic crab | Unassessed |
| Ultra-Mech Unlimited | Unassessed |
| Veldin flame robot (class 1440) | Partial (Veldin) |

**Going Commando**

| Enemy / actor | OBP status |
| --- | --- |
| Arctic leviathan | Unassessed |
| Attack-Copter | Unassessed |
| Bandit ship | Unassessed |
| Desert dune monster | Unassessed |
| Extermibot | Unassessed |
| Gadgetron Hound of Cuddly Death | Unassessed |
| Grulch | Unassessed |
| Machinegun turret | Unassessed |
| Megacorp Chickenbot | Unassessed |
| Megacorp Chickenbot Generator | Unassessed |
| Megacorp gladiator | Unassessed |
| Megacorp gladiator (Elite) | Unassessed |
| Megacorp Hover Tank | Unassessed |
| Megacorp Hover Tank V2.0 | Unassessed |
| Megacorp Laser Bot | Unassessed |
| Megacorp Rivet Bot | Unassessed |
| Megacorp Robot Guard | Unassessed |
| Megacorp Security Robot I (Chainsaw) | Unassessed |
| Megacorp Security Robot I v2.0 | Unassessed |
| Megacorp Security Robot I v3.0 | Unassessed |
| Megacorp Security Robot II (Flamethrower) | Unassessed |
| Megacorp Security Robot II v2.0 | Unassessed |
| Megacorp Security Robot II v3.0 | Unassessed |
| Megacorp Security Turret | Unassessed |
| Megacorp Sentinel | Unassessed |
| Megacorp Sweeper Bot | Unassessed |
| Megacorp Trooper | Unassessed |
| Megacorp Trooper v2.0 | Unassessed |
| Mutant firefly | Unassessed |
| Mutant muckdweller | Unassessed |
| Mutant sandfly | Unassessed |
| Mutant swamp beast | Unassessed |
| Mutant test-squirrel | Unassessed |
| Nidbot | Unassessed |
| Protopet | Unassessed |
| PX6 BladeBall | Unassessed |
| PX6 BladeBall v2.0 | Unassessed |
| Receiver Bot | Unassessed |
| Receiver Bot v1.0 | Unassessed |
| Receiver Bot v3.0 | Unassessed |
| Repair drone | Unassessed |
| Repair drone (Berserk) | Unassessed |
| Saboteur | Unassessed |
| Saur-beast (Barlow) | Unassessed |
| Saur-beast (Joba) | Unassessed |
| Sharkigator | Unassessed |
| Space wasp | Unassessed |
| Spawner | Unassessed |
| Spitting dune crawler | Unassessed |
| Thugs-4-Less attack ship | Unassessed |
| Thugs-4-Less Brute | Unassessed |
| Thugs-4-Less Brute Class II | Unassessed |
| Thugs-4-Less Carrier Ship | Unassessed |
| Thugs-4-Less Elite Henchman | Unassessed |
| Thugs-4-Less fighter | Unassessed |
| Thugs-4-Less Henchman | Unassessed |
| Tribesman (Joba) | Unassessed |
| Two-headed Smolgian snapper | Unassessed |
| Vukovar tribesman | Unassessed |
| Y.E.T.I. | Unassessed |

**Up Your Arsenal**

| Enemy / actor | OBP status |
| --- | --- |
| 'Noid commander | Unassessed |
| 'Noid missile station | Unassessed |
| Amoeboid | Unassessed |
| Attack-Copter | Unassessed |
| Backup dancer | Unassessed |
| Bash bot | Unassessed |
| Blade bot | Unassessed |
| Drone bot | Unassessed |
| Elite sharpshooter | Unassessed |
| Extermibot (Rilgar) | Unassessed |
| Floranian blood fly | Unassessed |
| Four-eyed robonoid | Unassessed |
| Four-eyed robonoid V2 | Unassessed |
| Four-eyed tyhrranoid | Unassessed |
| Gatling turret | Unassessed |
| Giant ninja | Unassessed |
| Gladiator | Unassessed |
| Guard bot | Unassessed |
| Heli-bot | Unassessed |
| Henchman | Unassessed |
| Hover bot | Unassessed |
| Insectoid | Unassessed |
| Insectoid swarm | Unassessed |
| King amoeboid | Unassessed |
| Laser bot | Unassessed |
| Lawn ninja | Unassessed |
| Lawn ninja generator | Unassessed |
| Mecha-tyhrranoid | Unassessed |
| Mega bot | Unassessed |
| Megacorp Sentinel | Unassessed |
| Mini-ninja henchman | Unassessed |
| Nabla native | Unassessed |
| Ninja bot | Unassessed |
| One-eyed robonoid | Unassessed |
| One-eyed tyhrranoid | Unassessed |
| One-eyed tyhrranoid V2 | Unassessed |
| Qwark bot | Unassessed |
| Robonoid commander | Unassessed |
| Robonoid saucer | Unassessed |
| Robotic pirate ghost | Unassessed |
| Scoutbot | Unassessed |
| Sentinel bot | Unassessed |
| Sharkigator | Unassessed |
| Sharpshooter | Unassessed |
| Snipe bot | Unassessed |
| Soldier bot | Unassessed |
| Space rat | Unassessed |
| Swamp monster | Unassessed |
| Tank bot | Unassessed |
| Test dummy | Unassessed |
| Three-eyed robonoid | Unassessed |
| Three-eyed robonoid V2 | Unassessed |
| Three-eyed tyhrranoid | Unassessed |
| Three-eyed tyhrranoid V2 | Unassessed |
| Troop transport | Unassessed |
| Two-eyed robonoid | Unassessed |
| Two-eyed tyhrranoid | Unassessed |
| Tyhrranoid attack ship | Unassessed |
| Tyhrranoid dropship | Unassessed |
| Tyhrranoid saucer | Unassessed |
| Ultra mecha-robonoid | Unassessed |
| Ultra mecha-tyhrranoid | Unassessed |

References: [R&C1 enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Ratchet_%26_Clank_%282002_game%29), [GC enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Going_Commando), [UYA enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Up_Your_Arsenal), [OBP Veldin evidence](research/RAC1_VELDIN_ENEMIES.md). Enemy names and variants reflect community indexes; matching to retail actor classes is pending.

</details>

<details>
<summary>Bosses and set-piece opponents</summary>

**Ratchet & Clank (2002)**

| Boss / encounter | OBP status |
| --- | --- |
| Alien Queen | Unassessed |
| Blargian Snagglebeast | Unassessed |
| Captain Qwark's starfighter | Unassessed |
| Chairman Drek (Drek-Mech) | Unassessed |
| PlanetBuster Maximus | Unassessed |
| Ultra-Mech Unlimited | Unassessed |

**Going Commando**

| Boss / encounter | OBP status |
| --- | --- |
| Unknown Thief | Unassessed |
| Arachnoid | Unassessed |
| B2 Brawler | Unassessed |
| Chainblade | Unassessed |
| Giganto-Mech | Unassessed |
| Megapede | Unassessed |
| Mothership | Unassessed |
| Mutant Protopet | Unassessed |
| Swamp monster I | Unassessed |
| Swamp monster II | Unassessed |
| Thug Leader | Unassessed |
| Thug Leader's helicopter | Unassessed |
| Thug Leader's mech | Unassessed |

**Up Your Arsenal**

| Boss / encounter | OBP status |
| --- | --- |
| Biobliterator | Unassessed |
| Blackstar | Unassessed |
| Courtney Gears | Unassessed |
| Dr. Nefarious | Unassessed |
| Gary and Helen | Unassessed |
| Giant Klunk | Unassessed |
| Klunk | Unassessed |
| Momma Tyhrranoid | Unassessed |
| Captain Qwark | Unassessed |
| Robotic space eel | Unassessed |
| Scorpio | Unassessed |
| Terror of Talos | Unassessed |
| Warship | Unassessed |

References: [R&C1 enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Ratchet_%26_Clank_%282002_game%29), [GC boss index](https://ratchetandclank.fandom.com/wiki/Monsterpedia), [UYA boss index](https://ratchetandclank.fandom.com/wiki/Category%3ABosses_in_Up_Your_Arsenal).

</details>

<details>
<summary>Planets, levels and destinations</summary>

**Ratchet & Clank (2002)**

| Planet / level | OBP status |
| --- | --- |
| Veldin: Kyzil Plateau (opening) | Partial (playable slice) |
| Novalis: Tobruk Crater | Unassessed |
| Aridia: Outpost X11 | Unassessed |
| Kerwan: Metropolis | Unassessed |
| Eudora: Logging Site | Unassessed |
| Nebula G34: Blarg Station | Unassessed |
| Rilgar: Blackwater City | Unassessed |
| Umbris: Qwark's HQ | Unassessed |
| Batalia: Fort Krontos | Unassessed |
| Gaspar: Blarg Depot | Unassessed |
| Orxon: Kogor Refinery | Unassessed |
| Pokitaru: Jowai Resort | Unassessed |
| Hoven: Bomb Factory | Unassessed |
| Oltanis Orbit: Gemlik Base | Unassessed |
| Oltanis: Gorda City | Unassessed |
| Quartu: Robot Factory | Unassessed |
| Kalebo III: Gadgetron Site | Unassessed |
| Veldin Orbit: Drek's Fleet | Unassessed |
| Veldin: Kyzil Plateau (revisit) | Unassessed |

**Going Commando**

| Planet / level | OBP status |
| --- | --- |
| Aranos: Flying Lab | Unassessed |
| Oozla: Megacorp Outlet | Partial (world loading) |
| Wupash Nebula | Unassessed |
| Maktar Nebula: Maktar Resort | Unassessed |
| Maktar Nebula: Jamming Array | Unassessed |
| Barlow: Vukovar Canyon | Unassessed |
| Endako: Megapolis | Unassessed |
| Feltzin System: Thug Rendezvous | Unassessed |
| Notak: Canal City | Unassessed |
| Slim Cognito's Ship Shack | Unassessed |
| Siberius: Frozen Base | Unassessed |
| Tabora: Mining Area | Unassessed |
| Dobbo: Testing Facility | Unassessed |
| Dobbo Orbit: Testing Facility Moon | Unassessed |
| Hrugis Cloud: Deep Space Disposal | Unassessed |
| Joba: Megacorp Games | Unassessed |
| Todano: Megacorp Armory | Unassessed |
| Boldan: Silver City | Unassessed |
| Aranos (revisit) | Unassessed |
| Gorn: Thugs-4-Less Fleet | Unassessed |
| Snivelak: Thug HQ | Unassessed |
| Smolg: Distribution Facility | Unassessed |
| Damosel: Allgon City | Unassessed |
| Damosel Orbit: Allgon Moon | Unassessed |
| Grelbin: Tundor Wastes | Unassessed |
| Yeedil: Protopet Factory | Unassessed |

**Up Your Arsenal**

| Planet / level | OBP status |
| --- | --- |
| Veldin: Kyzil Plateau | Partial (world loading) |
| Florana | Unassessed |
| Starship Phoenix | Unassessed |
| Marcadia | Unassessed |
| Annihilation Nation | Unassessed |
| Aquatos | Unassessed |
| Tyhrranosis | Unassessed |
| Daxx | Unassessed |
| Obani Gemini | Unassessed |
| Obani Draco | Unassessed |
| Holostar Studios | Unassessed |
| Rilgar | Unassessed |
| Zeldrin Starport | Unassessed |
| Kerwan: Metropolis | Unassessed |
| Zeldrin crash site | Unassessed |
| Aridia | Unassessed |
| Thran Asteroid Belt | Unassessed |
| Koros | Unassessed |
| Mylon | Unassessed |

References: [R&C1 level order](https://strategywiki.org/wiki/Ratchet_%26_Clank/Table_of_Contents), [GC level catalogue](research/GC_LEVEL_CATALOGUE.md), [UYA locations](https://ratchetclank.neoseeker.com/wiki/Planets). Separate orbit, arena and return visits are listed where they are distinct destinations.

</details>

<details>
<summary>Items, upgrades and collectibles</summary>

**Ratchet & Clank (2002)**

| Item / collectible type | OBP status |
| --- | --- |
| Bolts (currency) | Partial (R&C1) |
| Gold bolts | Unassessed |
| Skill points | Unassessed |
| Infobots | Unassessed |
| CodeBots | Unassessed |
| Map-o-Matic | Unassessed |
| Bolt Grabber | Unassessed |
| Persuader | Unassessed |
| Premium Nanotech | Unassessed |
| Ultra Nanotech | Unassessed |
| Platinum Zoomerator | Unassessed |
| Hoverboard | Unassessed |

**Going Commando**

| Item / collectible type | OBP status |
| --- | --- |
| Bolts (currency) | Unassessed |
| Platinum bolts | Unassessed |
| Skill points | Unassessed |
| Nanotech boosts | Unassessed |
| Raritanium | Unassessed |
| Qwark Statuette | Unassessed |
| Box Breaker | Unassessed |
| Mapper | Unassessed |
| Armor Magnetizer | Unassessed |
| Megacorp Helmet | Unassessed |
| Commando Suit | Unassessed |
| Biker Helmet | Unassessed |
| Tetrafiber Armor | Unassessed |
| Megacorp weapon modifications (acid) | Unassessed |
| Megacorp weapon modifications (shock) | Unassessed |
| Megacorp weapon modifications (lock-on) | Unassessed |
| Challenge-mode Mega / Ultra weapon upgrades | Unassessed |
| Ship upgrades and components | Unassessed |

**Up Your Arsenal**

| Item / collectible type | OBP status |
| --- | --- |
| Bolts (currency) | Unassessed |
| Titanium bolts | Unassessed |
| Skill points | Unassessed |
| Trophies (Starship Phoenix) | Unassessed |
| Bolt Grabber V2 | Unassessed |
| Map-O-Matic | Unassessed |
| Nano-pak | Unassessed |
| Master plan | Unassessed |
| Star map | Unassessed |
| Megacorp Helmet | Unassessed |
| Alpha Combat Suit | Unassessed |
| Adamantine Armor | Unassessed |
| Aegis Mark V Armor | Unassessed |
| Infernox Armor | Unassessed |
| Gadgetron discount | Unassessed |
| Challenge-mode Mega / Giga / Omega upgrades | Unassessed |

References: [R&C1 items](https://ratchetandclank.fandom.com/wiki/Category%3AItems_in_Ratchet_%26_Clank_%282002_game%29), [GC equipment](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA items](https://ratchetandclank.fandom.com/wiki/Category%3AItems_in_Up_Your_Arsenal). Collectible categories are tracked here, not all individual bolt or skill-point locations.

</details>

<details>
<summary>Crates, currency and pickups</summary>

**Ratchet & Clank (2002)**

| Crate / pickup | OBP status |
| --- | --- |
| Wooden bolt crate | Partial (R&C1) |
| Metal bolt crate | Partial (R&C1) |
| Explosive crate | Partial (R&C1) |
| Ammo crate | Partial (R&C1) |
| Nanotech crate | Partial (R&C1) |
| Jet Fighter health crate | Unassessed |
| World-placed bolts | Playable (R&C1) |
| Crate-emitted bolts | Partial (R&C1) |

**Going Commando**

| Crate / pickup | OBP status |
| --- | --- |
| Wooden bolt crate | Unassessed |
| Explosive crate | Unassessed |
| Megacorp ammo crate | Unassessed |
| Nanotech crate | Unassessed |
| Ultra Nanotech crate | Unassessed |
| Crate / enemy bolts | Unassessed |

**Up Your Arsenal**

| Crate / pickup | OBP status |
| --- | --- |
| Wooden bolt crate | Unassessed |
| Metal bolt crate | Unassessed |
| Explosive crate | Unassessed |
| Gadgetron ammo crate | Unassessed |
| Nanotech crate | Unassessed |
| Jackpot crate | Unassessed |
| Inferno crate | Unassessed |
| Crate / enemy bolts | Unassessed |

References: [Crate appearances](https://ratchetandclank.fandom.com/wiki/Crates), [OBP R&C1 crate implementation](research/RAC1_CRATES_PICKUPS.md), [GC crate descriptions](https://www.neoseeker.com/ratchet-clank-commando/faqs/74417-ratchet-and-clank-gc-d.html).

</details>

<details>
<summary>Common world objects and interactive mechanisms</summary>

Mechanism labels describe interactive object families. They are not necessarily the original games' internal class names.

**Ratchet & Clank (2002)**

| Object / mechanism | OBP status |
| --- | --- |
| Bolt crank and linked doors (Novalis) | Partial (Novalis) |
| Teleporter pad | Unassessed |
| World button / hit switch | Unassessed |
| Floor pressure switch | Unassessed |
| Locked / timed door | Unassessed |
| Trespasser lock | Unassessed |
| Gadgetron weapon / ammo vendor | Unassessed |
| Nanotech dispenser | Unassessed |
| Swingshot target (pull) | Researched |
| Swingshot target (swing) | Researched |
| Grind rail / branch | Researched |
| Magne-Strip / magnetic surface | Researched |
| Wall-jump slot | Researched |
| Zipline | Researched |
| Lift / moving platform | Unassessed |
| Infobot / mission trigger | Partial (selected R&C1) |
| Ship landing pad | Partial (R&C1 travel) |

**Going Commando**

| Object / mechanism | OBP status |
| --- | --- |
| Bolt crank (jamming array) | Unassessed |
| Teleporter pad | Unassessed |
| Translocator pad (Endako) | Unassessed |
| World button / hit switch | Unassessed |
| Door / bridge activation switch | Unassessed |
| Dynamo target | Unassessed |
| Electrolyzer circuit terminal | Unassessed |
| Infiltrator puzzle lock | Unassessed |
| Tractor Beam target / movable object | Unassessed |
| Thermanator freeze / thaw water target | Unassessed |
| Hypnomatic target | Unassessed |
| Swingshot target | Unassessed |
| Grind rail / junction | Unassessed |
| Magnetic Gravity Boots surface | Unassessed |
| Megacorp weapon / ammo vendor | Unassessed |
| Megacorp armor vendor | Unassessed |
| Slim Cognito's ship-upgrade shop | Unassessed |
| Arena / race challenge activation | Unassessed |
| Ship landing pad | Unassessed |

**Up Your Arsenal**

| Object / mechanism | OBP status |
| --- | --- |
| Bolt crank (Kavu Island) | Unassessed |
| Capturable Galactic Ranger node | Unassessed |
| Teleporter pad / return to ship | Unassessed |
| Museum teleporter (Kerwan / Phoenix) | Unassessed |
| World button / hit switch | Unassessed |
| Bridge / objective activation switch | Unassessed |
| Hacker puzzle terminal | Unassessed |
| Refractor laser reflector | Unassessed |
| Hypershot swing target | Unassessed |
| Hypershot holographic platform activation | Unassessed |
| Warp Pad anchor point | Unassessed |
| Gravity Boots magnetic surface | Unassessed |
| Gadgetron weapon / ammo vendor | Unassessed |
| Armor vendor | Unassessed |
| Annihilation Nation arena entry | Unassessed |
| Galactic Ranger objective / capture point | Unassessed |
| Hovership boarding pad | Unassessed |
| Ship landing pad | Unassessed |

References: [OBP Novalis bolt crank](game/scripts/OBPGame.Rac1NovalisCranks.cs), [R&C1 traversal](research/RAC1_TRAVERSAL_EQUIPMENT.md), [Teleporter appearances](https://ratchetandclank.fandom.com/wiki/Teleporter), [GC gameplay instructions](https://ratchetandclank.fandom.com/wiki/Going_Commando_script), [UYA gameplay instructions](https://ratchetandclank.fandom.com/wiki/Up_Your_Arsenal_script), [UYA capture nodes](https://ratchetandclank.fandom.com/wiki/Node).

</details>

<details>
<summary>Level activities and special encounter types</summary>

**Ratchet & Clank (2002)**

| Activity / level feature | OBP status |
| --- | --- |
| Hoverboard race | Unassessed |
| Hoverboard trick/boost challenges | Unassessed |
| Clank-only Gadgebot puzzles | Unassessed |
| Giant Clank sequence | Unassessed |
| Fighter ship battle | Unassessed |
| Jet Fighter mission | Unassessed |
| Trespasser laser puzzle | Unassessed |
| Grindrail traversal | Unassessed |
| Nanotech upgrade purchase | Unassessed |
| Gold weapon vendor | Unassessed |
| Skill point challenge | Unassessed |

**Going Commando**

| Activity / level feature | OBP status |
| --- | --- |
| Hoverbike race | Unassessed |
| Starship space combat | Unassessed |
| Giant Clank combat | Unassessed |
| Glider course | Unassessed |
| Levitator traversal | Unassessed |
| Megacorp Arena challenges | Unassessed |
| Platinum Bolt hunting | Unassessed |
| Insomniac Museum access | Unassessed |
| Weapon XP and mods | Unassessed |
| Nanotech XP and boosts | Unassessed |
| Ship customization | Unassessed |

**Up Your Arsenal**

| Activity / level feature | OBP status |
| --- | --- |
| Annihilation Nation arena | Unassessed |
| Galactic Ranger battle missions | Unassessed |
| Qwark Vid-Comic levels | Unassessed |
| Starship Phoenix VR training | Unassessed |
| Starship Phoenix trophy room | Unassessed |
| Hacker puzzle | Unassessed |
| Tyhrraguise infiltration | Unassessed |
| Refractor beam puzzle | Unassessed |
| Ship/space encounters | Unassessed |
| Multi-level weapon XP | Unassessed |
| Challenge Mode | Unassessed |
| Titanium Bolt hunting | Unassessed |

These are named activities and broad encounter types rather than an exhaustive list of all mission scripts. Reference: [R&C1 overview](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank_%282002_game%29), [GC overview](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA overview](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal).

</details>

## Getting started

On Windows, with the .NET 8 SDK installed, run from the repository root:

```powershell
./tools/bootstrap-dev.ps1
./tools/play.ps1
```

The bootstrap script downloads the project's pinned Godot .NET version, builds the project and syncs Godot C# script identity sidecars. If no game source is configured, the app opens the Game Sources screen.

When adding a `.cs` file under `game/scripts` or `game/diagnostics`, run `./tools/sync-godot-uids.ps1` (Linux: `bash tools/sync-godot-uids.sh`). This uses the pinned Godot .NET editor to generate any missing `.cs.uid` files. Commit the generated sidecars with your scripts. Run `./tools/sync-godot-uids.ps1 -Check` (Linux: `bash tools/sync-godot-uids.sh --check`) for read-only validation. The small .NET UID tool runs without a Godot import or game build when sidecars already exist. If a script moves, move its `.cs.uid` with it to preserve its identity. Duplicate, malformed or orphaned UIDs require manual correction rather than automatic replacement.

OBP requires your own supported retail game files to load original levels. **No disc images, game assets or executables are included in this repository.**

## Testing

```powershell
# Build and unit tests
./tools/test.ps1 -Configuration Release

# Tests that require locally supplied game data
./tools/test-retail.ps1
```

Pull requests run .NET tests (including strict C# UID sidecar coverage), CodeQL analysis and an asset-free Godot startup test. The Godot smoke also checks that the preceding pinned headless import left the game working tree unchanged. The six .NET xUnit UID-tooling regressions run with the ordinary test suite. Some R&C1 gameplay can be recorded and replayed; see [movement and replay documentation](docs/MOVEMENT_REPRO_STATES.md).

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Project vision](docs/PROJECT_VISION.md)
- [Research index](research/README.md)
- [Retail testing](docs/RETAIL_TESTING.md)
- [Development guidelines](AGENTS.md)
- [External source policy](research/EXTERNAL_SOURCE_POLICY.md)

Development uses feature branches and pull requests into the protected `dev` branch. Merges are manual.

This is an unofficial fan project and is not affiliated with Sony Interactive Entertainment or Insomniac Games.
