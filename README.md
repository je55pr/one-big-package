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

## Current status

- **Ratchet & Clank (2002):** Most active gameplay work. Player movement, weapons, enemies, pickups, campaign state and other systems are implemented to varying degrees.
- **Going Commando (2003):** World loading, presentation and some game systems are implemented. Gameplay is incomplete.
- **Up Your Arsenal (2004):** World loading, presentation and some game systems are implemented. Gameplay is incomplete.

See [issues](https://github.com/je55pr/one-big-package/issues) and [pull requests](https://github.com/je55pr/one-big-package/pulls) for current work.

R&C1 Quick Select includes the GC Lancer and UYA Shock Cannon by default alongside
owned R&C1 weapons. Their shared assets prepare during Game Sources setup and
stay loaded across level changes. The original game sources are required; see
[cross-game weapon controls and limits](docs/FUSION_WEAPONS_TESTING.md).

## Trilogy content catalogue

Named content from the original PS2 trilogy, grouped into collapsible tables. This is an **initial name-level inventory** covering all three games; it is **not** a verified census of every placed instance, enemy subclass, scripted event or upgrade stage. The retail-source audit is still needed to establish exhaustive coverage and accurate implementation status.

**Status:** **Playable** = working within the stated game/scope; **Partial** = implemented with known gaps; **Researched** = source behaviour documented; **Unassessed** = no implementation claim. Unassessed does **not** mean absent. Upgrade names are grouped with their base weapons; a playable base weapon does not imply its upgrades are implemented. Each repeated object placement is counted as a type, not as an individual row.

<details>
<summary>Weapons and upgrade families</summary>

**Ratchet & Clank (2002)**

| Weapon / family | Original game | Upgrade / variant | OBP status |
| --- | --- | --- | --- |
| OmniWrench 8000 | Ratchet & Clank (2002) | Basic wrench attacks | Partial (ordinary wrench playable) |
| Bomb Glove | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Pyrocitor | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Blaster | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Glove of Doom | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Suck Cannon | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Taunter | Ratchet & Clank (2002) |  | Partial (ordinary weapon playable) |
| Mine Glove | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| R.Y.N.O. | Ratchet & Clank (2002) |  | Partial (ordinary weapon playable) |
| Devastator | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Walloper | Ratchet & Clank (2002) | | Partial (ordinary weapon playable) |
| Visibomb Gun | Ratchet & Clank (2002) | | Partial (ordinary weapon playable) |
| Decoy Glove | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Drone Device | Ratchet & Clank (2002) | | Partial (ordinary weapon playable) |
| Tesla Claw | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |
| Morph-O-Ray | Ratchet & Clank (2002) | Gold variant | Partial (ordinary weapon playable) |

**Going Commando**

| Weapon / family | Original game | Upgrade / variant | OBP status |
| --- | --- | --- | --- |
| OmniWrench 10000 / 12000 | Going Commando | Wrench progression | Unassessed |
| Lancer | Going Commando | Heavy Lancer | Partial (firing in R&C1) |
| Gravity Bomb | Going Commando | Mini-Nuke | Unassessed |
| Chopper | Going Commando | Multi-Star | Unassessed |
| Seeker Gun | Going Commando | HK22 Gun | Unassessed |
| Pulse Rifle | Going Commando | Vaporizer | Unassessed |
| Miniturret Glove | Going Commando | Megaturret Glove | Unassessed |
| Blitz Gun | Going Commando | Blitz Cannon | Unassessed |
| Shield Charger | Going Commando | Tesla Barrier | Unassessed |
| Synthenoid | Going Commando | Kilonoid | Unassessed |
| Lava Gun | Going Commando | Meteor Gun | Unassessed |
| Bouncer | Going Commando | Heavy Bouncer | Unassessed |
| Minirocket Tube | Going Commando | Megarocket Cannon | Unassessed |
| Plasma Coil | Going Commando | Plasma Storm | Unassessed |
| Hoverbomb Gun | Going Commando | Tetrabomb Gun | Unassessed |
| Spiderbot Glove | Going Commando | Tankbot Glove | Unassessed |
| Sheepinator | Going Commando | Black Sheepinator | Unassessed |
| R.Y.N.O. II | Going Commando |  | Unassessed |
| Zodiac | Going Commando |  | Unassessed |
| Clank Zapper | Going Commando | Clank Shocker | Unassessed |
| Tesla Claw | Going Commando | Returning R&C1 weapon | Unassessed |
| Bomb Glove | Going Commando | Returning R&C1 weapon | Unassessed |
| Walloper | Going Commando | Returning R&C1 weapon | Unassessed |
| Visibomb Gun | Going Commando | Returning R&C1 weapon | Unassessed |
| Decoy Glove | Going Commando | Returning R&C1 weapon | Unassessed |

**Up Your Arsenal**

| Weapon / family | Original game | Upgrade / variant | OBP status |
| --- | --- | --- | --- |
| OmniWrench 12000 | Up Your Arsenal | Wrench progression | Unassessed |
| Shock Blaster | Up Your Arsenal | Shock Cannon | Partial (Shock Cannon firing in R&C1) |
| Nitro Launcher | Up Your Arsenal | Nitro Eruptor | Unassessed |
| N60 Storm | Up Your Arsenal | N90 Hurricane | Unassessed |
| Plasma Whip | Up Your Arsenal | Quantum Whip | Unassessed |
| Infector | Up Your Arsenal | Infecto-Bomb | Unassessed |
| Suck Cannon | Up Your Arsenal | Vortex Cannon | Unassessed |
| Spitting Hydra | Up Your Arsenal | Tempest | Unassessed |
| Agents of Doom | Up Your Arsenal | Agents of Dread | Unassessed |
| Flux Rifle | Up Your Arsenal | Splitter Rifle | Unassessed |
| Annihilator | Up Your Arsenal | Decimator | Unassessed |
| Holoshield Glove | Up Your Arsenal | Ultrashield Launcher | Unassessed |
| Disc Blade Gun | Up Your Arsenal | Multi-Disc Gun | Unassessed |
| Rift Inducer | Up Your Arsenal | Rift Ripper | Unassessed |
| Qwack-O-Ray | Up Your Arsenal | Qwack-O-Blitzer | Unassessed |
| Miniturret Glove | Up Your Arsenal | Megaturret Glove | Unassessed |
| Lava Gun | Up Your Arsenal | Liquid Nitrogen Gun | Unassessed |
| Bouncer | Up Your Arsenal | Heavy Bouncer | Unassessed |
| Plasma Coil | Up Your Arsenal | Plasma Storm | Unassessed |
| Shield Charger | Up Your Arsenal | Tesla Barrier | Unassessed |
| R.Y.3.N.O. | Up Your Arsenal | RYNOCIRATOR | Unassessed |

Reference: [R&C1 weapons](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank_%282002_game%29), [GC weapon upgrades](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Going_Commando/Weapons), [UYA weapon upgrades](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal/Weapons), [R&C1 arsenal evidence](research/RAC1_ARSENAL.md), [cross-game weapon evidence](docs/FUSION_WEAPONS_TESTING.md).

</details>

<details>
<summary>Gadgets and traversal equipment</summary>

**Ratchet & Clank (2002)**

| Gadget / equipment | Original game | OBP status |
| --- | --- | --- |
| Heli-Pack | Ratchet & Clank (2002) | Researched |
| Thruster-Pack | Ratchet & Clank (2002) | Researched |
| Hydro-Pack | Ratchet & Clank (2002) | Researched |
| O2 Mask | Ratchet & Clank (2002) | Unassessed |
| Pilot's Helmet | Ratchet & Clank (2002) | Unassessed |
| Swingshot | Ratchet & Clank (2002) | Researched |
| Trespasser | Ratchet & Clank (2002) | Unassessed |
| Grind Boots | Ratchet & Clank (2002) | Researched |
| Magneboots | Ratchet & Clank (2002) | Researched |
| Metal Detector | Ratchet & Clank (2002) | Partial (digging) |
| Hydrodisplacer | Ratchet & Clank (2002) | Unassessed |
| Sonic Summoner | Ratchet & Clank (2002) | Unassessed |
| Hologuise | Ratchet & Clank (2002) | Unassessed |
| Gadgetron PDA | Ratchet & Clank (2002) | Unassessed |

**Going Commando**

| Gadget / equipment | Original game | OBP status |
| --- | --- | --- |
| Swingshot | Going Commando | Unassessed |
| Dynamo | Going Commando | Unassessed |
| Electrolyzer | Going Commando | Unassessed |
| Hypnomatic | Going Commando | Unassessed |
| Infiltrator | Going Commando | Unassessed |
| Thermanator | Going Commando | Unassessed |
| Tractor Beam | Going Commando | Unassessed |
| Heli-Pack | Going Commando | Unassessed |
| Thruster-Pack | Going Commando | Unassessed |
| Hydro-Pack | Going Commando | Unassessed |
| Glider | Going Commando | Unassessed |
| Levitator | Going Commando | Unassessed |
| Charge Boots | Going Commando | Unassessed |
| Gravity Boots | Going Commando | Unassessed |
| Grind Boots | Going Commando | Unassessed |

**Up Your Arsenal**

| Gadget / equipment | Original game | OBP status |
| --- | --- | --- |
| Hacker | Up Your Arsenal | Unassessed |
| Hypershot | Up Your Arsenal | Unassessed |
| Refractor | Up Your Arsenal | Unassessed |
| Tyhrraguise | Up Your Arsenal | Unassessed |
| Warp Pad | Up Your Arsenal | Unassessed |
| Gadgetron PDA | Up Your Arsenal | Unassessed |
| Gravity Boots | Up Your Arsenal | Unassessed |
| Charge Boots | Up Your Arsenal | Unassessed |
| Heli-Pack | Up Your Arsenal | Unassessed |
| Thruster-Pack | Up Your Arsenal | Unassessed |
| Hydro-Pack | Up Your Arsenal | Unassessed |
| Banana-Guided Autonomous Monkey Device | Up Your Arsenal | Unassessed |

References: [R&C1 equipment](research/RAC1_TRAVERSAL_EQUIPMENT.md), [GC equipment](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA equipment](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal/Gadgets).

</details>

<details>
<summary>Enemies and hostile actor types</summary>

**Ratchet & Clank (2002)**

| Enemy / actor | Original game | OBP status |
| --- | --- | --- |
| Alien snapper | Ratchet & Clank (2002) | Unassessed |
| Alien swarmer | Ratchet & Clank (2002) | Unassessed |
| Amoeboid | Ratchet & Clank (2002) | Unassessed |
| Anklebiter | Ratchet & Clank (2002) | Unassessed |
| Axebot | Ratchet & Clank (2002) | Unassessed |
| Big axebot | Ratchet & Clank (2002) | Unassessed |
| Birdbot | Ratchet & Clank (2002) | Unassessed |
| Blarg armored transport | Ratchet & Clank (2002) | Unassessed |
| Blarg bombthrower | Ratchet & Clank (2002) | Unassessed |
| Blarg commander | Ratchet & Clank (2002) | Unassessed |
| Blarg destroyer | Ratchet & Clank (2002) | Unassessed |
| Blarg elite commando | Ratchet & Clank (2002) | Unassessed |
| Blarg generator | Ratchet & Clank (2002) | Unassessed |
| Blarg gunner | Ratchet & Clank (2002) | Unassessed |
| Blarg heavy interceptor | Ratchet & Clank (2002) | Unassessed |
| Blarg heli-commander | Ratchet & Clank (2002) | Unassessed |
| Blarg paratrooper | Ratchet & Clank (2002) | Unassessed |
| Blarg saucer | Ratchet & Clank (2002) | Unassessed |
| Blarg space commando | Ratchet & Clank (2002) | Unassessed |
| Blarg space fighter | Ratchet & Clank (2002) | Unassessed |
| Blarg trooper | Ratchet & Clank (2002) | Unassessed |
| Bolt thief | Ratchet & Clank (2002) | Unassessed |
| Bomb-o-matic | Ratchet & Clank (2002) | Unassessed |
| Cannonball tank | Ratchet & Clank (2002) | Unassessed |
| Chomper | Ratchet & Clank (2002) | Unassessed |
| Constructo bot | Ratchet & Clank (2002) | Unassessed |
| Dark Gadge-Bot | Ratchet & Clank (2002) | Unassessed |
| Darter | Ratchet & Clank (2002) | Unassessed |
| Electroid | Ratchet & Clank (2002) | Unassessed |
| Extermibot (Rilgar) | Ratchet & Clank (2002) | Unassessed |
| Extermitank | Ratchet & Clank (2002) | Unassessed |
| Homing mine | Ratchet & Clank (2002) | Unassessed |
| Horned toad | Ratchet & Clank (2002) | Partial (Veldin class 749) |
| Machinegun turret | Ratchet & Clank (2002) | Unassessed |
| Mine layer | Ratchet & Clank (2002) | Unassessed |
| Minibomber | Ratchet & Clank (2002) | Unassessed |
| Peckbot | Ratchet & Clank (2002) | Unassessed |
| Plasmabot | Ratchet & Clank (2002) | Unassessed |
| Psy-tcopus | Ratchet & Clank (2002) | Unassessed |
| Puffoid | Ratchet & Clank (2002) | Unassessed |
| Robomutt | Ratchet & Clank (2002) | Unassessed |
| Robosquawker | Ratchet & Clank (2002) | Unassessed |
| Rocket tank | Ratchet & Clank (2002) | Unassessed |
| Sandshark | Ratchet & Clank (2002) | Unassessed |
| Sandshark nest | Ratchet & Clank (2002) | Unassessed |
| Sawbot | Ratchet & Clank (2002) | Unassessed |
| Scoutbot | Ratchet & Clank (2002) | Unassessed |
| Screamer | Ratchet & Clank (2002) | Unassessed |
| Seeker | Ratchet & Clank (2002) | Unassessed |
| Sentry-bot | Ratchet & Clank (2002) | Unassessed |
| Sharkigator | Ratchet & Clank (2002) | Unassessed |
| Test dummy | Ratchet & Clank (2002) | Unassessed |
| Toxic crab | Ratchet & Clank (2002) | Unassessed |
| Ultra-Mech Unlimited | Ratchet & Clank (2002) | Unassessed |
| Veldin flame robot (class 1440) | Ratchet & Clank (2002) | Partial (Veldin) |

**Going Commando**

| Enemy / actor | Original game | OBP status |
| --- | --- | --- |
| Arctic leviathan | Going Commando | Unassessed |
| Attack-Copter | Going Commando | Unassessed |
| Bandit ship | Going Commando | Unassessed |
| Desert dune monster | Going Commando | Unassessed |
| Extermibot | Going Commando | Unassessed |
| Gadgetron Hound of Cuddly Death | Going Commando | Unassessed |
| Grulch | Going Commando | Unassessed |
| Machinegun turret | Going Commando | Unassessed |
| Megacorp Chickenbot | Going Commando | Unassessed |
| Megacorp Chickenbot Generator | Going Commando | Unassessed |
| Megacorp gladiator | Going Commando | Unassessed |
| Megacorp gladiator (Elite) | Going Commando | Unassessed |
| Megacorp Hover Tank | Going Commando | Unassessed |
| Megacorp Hover Tank V2.0 | Going Commando | Unassessed |
| Megacorp Laser Bot | Going Commando | Unassessed |
| Megacorp Rivet Bot | Going Commando | Unassessed |
| Megacorp Robot Guard | Going Commando | Unassessed |
| Megacorp Security Robot I (Chainsaw) | Going Commando | Unassessed |
| Megacorp Security Robot I v2.0 | Going Commando | Unassessed |
| Megacorp Security Robot I v3.0 | Going Commando | Unassessed |
| Megacorp Security Robot II (Flamethrower) | Going Commando | Unassessed |
| Megacorp Security Robot II v2.0 | Going Commando | Unassessed |
| Megacorp Security Robot II v3.0 | Going Commando | Unassessed |
| Megacorp Security Turret | Going Commando | Unassessed |
| Megacorp Sentinel | Going Commando | Unassessed |
| Megacorp Sweeper Bot | Going Commando | Unassessed |
| Megacorp Trooper | Going Commando | Unassessed |
| Megacorp Trooper v2.0 | Going Commando | Unassessed |
| Mutant firefly | Going Commando | Unassessed |
| Mutant muckdweller | Going Commando | Unassessed |
| Mutant sandfly | Going Commando | Unassessed |
| Mutant swamp beast | Going Commando | Unassessed |
| Mutant test-squirrel | Going Commando | Unassessed |
| Nidbot | Going Commando | Unassessed |
| Protopet | Going Commando | Unassessed |
| PX6 BladeBall | Going Commando | Unassessed |
| PX6 BladeBall v2.0 | Going Commando | Unassessed |
| Receiver Bot | Going Commando | Unassessed |
| Receiver Bot v1.0 | Going Commando | Unassessed |
| Receiver Bot v3.0 | Going Commando | Unassessed |
| Repair drone | Going Commando | Unassessed |
| Repair drone (Berserk) | Going Commando | Unassessed |
| Saboteur | Going Commando | Unassessed |
| Saur-beast (Barlow) | Going Commando | Unassessed |
| Saur-beast (Joba) | Going Commando | Unassessed |
| Sharkigator | Going Commando | Unassessed |
| Space wasp | Going Commando | Unassessed |
| Spawner | Going Commando | Unassessed |
| Spitting dune crawler | Going Commando | Unassessed |
| Thugs-4-Less attack ship | Going Commando | Unassessed |
| Thugs-4-Less Brute | Going Commando | Unassessed |
| Thugs-4-Less Brute Class II | Going Commando | Unassessed |
| Thugs-4-Less Carrier Ship | Going Commando | Unassessed |
| Thugs-4-Less Elite Henchman | Going Commando | Unassessed |
| Thugs-4-Less fighter | Going Commando | Unassessed |
| Thugs-4-Less Henchman | Going Commando | Unassessed |
| Tribesman (Joba) | Going Commando | Unassessed |
| Two-headed Smolgian snapper | Going Commando | Unassessed |
| Vukovar tribesman | Going Commando | Unassessed |
| Y.E.T.I. | Going Commando | Unassessed |

**Up Your Arsenal**

| Enemy / actor | Original game | OBP status |
| --- | --- | --- |
| 'Noid commander | Up Your Arsenal | Unassessed |
| 'Noid missile station | Up Your Arsenal | Unassessed |
| Amoeboid | Up Your Arsenal | Unassessed |
| Attack-Copter | Up Your Arsenal | Unassessed |
| Backup dancer | Up Your Arsenal | Unassessed |
| Bash bot | Up Your Arsenal | Unassessed |
| Blade bot | Up Your Arsenal | Unassessed |
| Drone bot | Up Your Arsenal | Unassessed |
| Elite sharpshooter | Up Your Arsenal | Unassessed |
| Extermibot (Rilgar) | Up Your Arsenal | Unassessed |
| Floranian blood fly | Up Your Arsenal | Unassessed |
| Four-eyed robonoid | Up Your Arsenal | Unassessed |
| Four-eyed robonoid V2 | Up Your Arsenal | Unassessed |
| Four-eyed tyhrranoid | Up Your Arsenal | Unassessed |
| Gatling turret | Up Your Arsenal | Unassessed |
| Giant ninja | Up Your Arsenal | Unassessed |
| Gladiator | Up Your Arsenal | Unassessed |
| Guard bot | Up Your Arsenal | Unassessed |
| Heli-bot | Up Your Arsenal | Unassessed |
| Henchman | Up Your Arsenal | Unassessed |
| Hover bot | Up Your Arsenal | Unassessed |
| Insectoid | Up Your Arsenal | Unassessed |
| Insectoid swarm | Up Your Arsenal | Unassessed |
| King amoeboid | Up Your Arsenal | Unassessed |
| Laser bot | Up Your Arsenal | Unassessed |
| Lawn ninja | Up Your Arsenal | Unassessed |
| Lawn ninja generator | Up Your Arsenal | Unassessed |
| Mecha-tyhrranoid | Up Your Arsenal | Unassessed |
| Mega bot | Up Your Arsenal | Unassessed |
| Megacorp Sentinel | Up Your Arsenal | Unassessed |
| Mini-ninja henchman | Up Your Arsenal | Unassessed |
| Nabla native | Up Your Arsenal | Unassessed |
| Ninja bot | Up Your Arsenal | Unassessed |
| One-eyed robonoid | Up Your Arsenal | Unassessed |
| One-eyed tyhrranoid | Up Your Arsenal | Unassessed |
| One-eyed tyhrranoid V2 | Up Your Arsenal | Unassessed |
| Qwark bot | Up Your Arsenal | Unassessed |
| Robonoid commander | Up Your Arsenal | Unassessed |
| Robonoid saucer | Up Your Arsenal | Unassessed |
| Robotic pirate ghost | Up Your Arsenal | Unassessed |
| Scoutbot | Up Your Arsenal | Unassessed |
| Sentinel bot | Up Your Arsenal | Unassessed |
| Sharkigator | Up Your Arsenal | Unassessed |
| Sharpshooter | Up Your Arsenal | Unassessed |
| Snipe bot | Up Your Arsenal | Unassessed |
| Soldier bot | Up Your Arsenal | Unassessed |
| Space rat | Up Your Arsenal | Unassessed |
| Swamp monster | Up Your Arsenal | Unassessed |
| Tank bot | Up Your Arsenal | Unassessed |
| Test dummy | Up Your Arsenal | Unassessed |
| Three-eyed robonoid | Up Your Arsenal | Unassessed |
| Three-eyed robonoid V2 | Up Your Arsenal | Unassessed |
| Three-eyed tyhrranoid | Up Your Arsenal | Unassessed |
| Three-eyed tyhrranoid V2 | Up Your Arsenal | Unassessed |
| Troop transport | Up Your Arsenal | Unassessed |
| Two-eyed robonoid | Up Your Arsenal | Unassessed |
| Two-eyed tyhrranoid | Up Your Arsenal | Unassessed |
| Tyhrranoid attack ship | Up Your Arsenal | Unassessed |
| Tyhrranoid dropship | Up Your Arsenal | Unassessed |
| Tyhrranoid saucer | Up Your Arsenal | Unassessed |
| Ultra mecha-robonoid | Up Your Arsenal | Unassessed |
| Ultra mecha-tyhrranoid | Up Your Arsenal | Unassessed |

References: [R&C1 enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Ratchet_%26_Clank_%282002_game%29), [GC enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Going_Commando), [UYA enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Up_Your_Arsenal), [OBP Veldin evidence](research/RAC1_VELDIN_ENEMIES.md). Enemy names and variants reflect community indexes; matching to retail actor classes is pending.

</details>

<details>
<summary>Bosses and set-piece opponents</summary>

**Ratchet & Clank (2002)**

| Boss / encounter | Original game | OBP status |
| --- | --- | --- |
| Alien Queen | Ratchet & Clank (2002) | Unassessed |
| Blargian Snagglebeast | Ratchet & Clank (2002) | Unassessed |
| Captain Qwark's starfighter | Ratchet & Clank (2002) | Unassessed |
| Chairman Drek (Drek-Mech) | Ratchet & Clank (2002) | Unassessed |
| PlanetBuster Maximus | Ratchet & Clank (2002) | Unassessed |
| Ultra-Mech Unlimited | Ratchet & Clank (2002) | Unassessed |

**Going Commando**

| Boss / encounter | Original game | OBP status |
| --- | --- | --- |
| Unknown Thief | Going Commando | Unassessed |
| Arachnoid | Going Commando | Unassessed |
| B2 Brawler | Going Commando | Unassessed |
| Chainblade | Going Commando | Unassessed |
| Giganto-Mech | Going Commando | Unassessed |
| Megapede | Going Commando | Unassessed |
| Mothership | Going Commando | Unassessed |
| Mutant Protopet | Going Commando | Unassessed |
| Swamp monster I | Going Commando | Unassessed |
| Swamp monster II | Going Commando | Unassessed |
| Thug Leader | Going Commando | Unassessed |
| Thug Leader's helicopter | Going Commando | Unassessed |
| Thug Leader's mech | Going Commando | Unassessed |

**Up Your Arsenal**

| Boss / encounter | Original game | OBP status |
| --- | --- | --- |
| Biobliterator | Up Your Arsenal | Unassessed |
| Blackstar | Up Your Arsenal | Unassessed |
| Courtney Gears | Up Your Arsenal | Unassessed |
| Dr. Nefarious | Up Your Arsenal | Unassessed |
| Gary and Helen | Up Your Arsenal | Unassessed |
| Giant Klunk | Up Your Arsenal | Unassessed |
| Klunk | Up Your Arsenal | Unassessed |
| Momma Tyhrranoid | Up Your Arsenal | Unassessed |
| Captain Qwark | Up Your Arsenal | Unassessed |
| Robotic space eel | Up Your Arsenal | Unassessed |
| Scorpio | Up Your Arsenal | Unassessed |
| Terror of Talos | Up Your Arsenal | Unassessed |
| Warship | Up Your Arsenal | Unassessed |

References: [R&C1 enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Ratchet_%26_Clank_%282002_game%29), [GC boss index](https://ratchetandclank.fandom.com/wiki/Monsterpedia), [UYA boss index](https://ratchetandclank.fandom.com/wiki/Category%3ABosses_in_Up_Your_Arsenal).

</details>

<details>
<summary>Planets, levels and destinations</summary>

**Ratchet & Clank (2002)**

| Planet / level | Original game | OBP status |
| --- | --- | --- |
| Veldin: Kyzil Plateau (opening) | Ratchet & Clank (2002) | Partial (playable slice) |
| Novalis: Tobruk Crater | Ratchet & Clank (2002) | Unassessed |
| Aridia: Outpost X11 | Ratchet & Clank (2002) | Unassessed |
| Kerwan: Metropolis | Ratchet & Clank (2002) | Unassessed |
| Eudora: Logging Site | Ratchet & Clank (2002) | Unassessed |
| Nebula G34: Blarg Station | Ratchet & Clank (2002) | Unassessed |
| Rilgar: Blackwater City | Ratchet & Clank (2002) | Unassessed |
| Umbris: Qwark's HQ | Ratchet & Clank (2002) | Unassessed |
| Batalia: Fort Krontos | Ratchet & Clank (2002) | Unassessed |
| Gaspar: Blarg Depot | Ratchet & Clank (2002) | Unassessed |
| Orxon: Kogor Refinery | Ratchet & Clank (2002) | Unassessed |
| Pokitaru: Jowai Resort | Ratchet & Clank (2002) | Unassessed |
| Hoven: Bomb Factory | Ratchet & Clank (2002) | Unassessed |
| Oltanis Orbit: Gemlik Base | Ratchet & Clank (2002) | Unassessed |
| Oltanis: Gorda City | Ratchet & Clank (2002) | Unassessed |
| Quartu: Robot Factory | Ratchet & Clank (2002) | Unassessed |
| Kalebo III: Gadgetron Site | Ratchet & Clank (2002) | Unassessed |
| Veldin Orbit: Drek's Fleet | Ratchet & Clank (2002) | Unassessed |
| Veldin: Kyzil Plateau (revisit) | Ratchet & Clank (2002) | Unassessed |

**Going Commando**

| Planet / level | Original game | OBP status |
| --- | --- | --- |
| Aranos: Flying Lab | Going Commando | Unassessed |
| Oozla: Megacorp Outlet | Going Commando | Partial (world loading) |
| Wupash Nebula | Going Commando | Unassessed |
| Maktar Nebula: Maktar Resort | Going Commando | Unassessed |
| Maktar Nebula: Jamming Array | Going Commando | Unassessed |
| Barlow: Vukovar Canyon | Going Commando | Unassessed |
| Endako: Megapolis | Going Commando | Unassessed |
| Feltzin System: Thug Rendezvous | Going Commando | Unassessed |
| Notak: Canal City | Going Commando | Unassessed |
| Slim Cognito's Ship Shack | Going Commando | Unassessed |
| Siberius: Frozen Base | Going Commando | Unassessed |
| Tabora: Mining Area | Going Commando | Unassessed |
| Dobbo: Testing Facility | Going Commando | Unassessed |
| Dobbo Orbit: Testing Facility Moon | Going Commando | Unassessed |
| Hrugis Cloud: Deep Space Disposal | Going Commando | Unassessed |
| Joba: Megacorp Games | Going Commando | Unassessed |
| Todano: Megacorp Armory | Going Commando | Unassessed |
| Boldan: Silver City | Going Commando | Unassessed |
| Aranos (revisit) | Going Commando | Unassessed |
| Gorn: Thugs-4-Less Fleet | Going Commando | Unassessed |
| Snivelak: Thug HQ | Going Commando | Unassessed |
| Smolg: Distribution Facility | Going Commando | Unassessed |
| Damosel: Allgon City | Going Commando | Unassessed |
| Damosel Orbit: Allgon Moon | Going Commando | Unassessed |
| Grelbin: Tundor Wastes | Going Commando | Unassessed |
| Yeedil: Protopet Factory | Going Commando | Unassessed |

**Up Your Arsenal**

| Planet / level | Original game | OBP status |
| --- | --- | --- |
| Veldin: Kyzil Plateau | Up Your Arsenal | Partial (world loading) |
| Florana | Up Your Arsenal | Unassessed |
| Starship Phoenix | Up Your Arsenal | Unassessed |
| Marcadia | Up Your Arsenal | Unassessed |
| Annihilation Nation | Up Your Arsenal | Unassessed |
| Aquatos | Up Your Arsenal | Unassessed |
| Tyhrranosis | Up Your Arsenal | Unassessed |
| Daxx | Up Your Arsenal | Unassessed |
| Obani Gemini | Up Your Arsenal | Unassessed |
| Obani Draco | Up Your Arsenal | Unassessed |
| Holostar Studios | Up Your Arsenal | Unassessed |
| Rilgar | Up Your Arsenal | Unassessed |
| Zeldrin Starport | Up Your Arsenal | Unassessed |
| Kerwan: Metropolis | Up Your Arsenal | Unassessed |
| Zeldrin crash site | Up Your Arsenal | Unassessed |
| Aridia | Up Your Arsenal | Unassessed |
| Thran Asteroid Belt | Up Your Arsenal | Unassessed |
| Koros | Up Your Arsenal | Unassessed |
| Mylon | Up Your Arsenal | Unassessed |

References: [R&C1 level order](https://strategywiki.org/wiki/Ratchet_%26_Clank/Table_of_Contents), [GC level catalogue](research/GC_LEVEL_CATALOGUE.md), [UYA locations](https://ratchetclank.neoseeker.com/wiki/Planets). Separate orbit, arena and return visits are listed where they are distinct destinations.

</details>

<details>
<summary>Items, upgrades and collectibles</summary>

**Ratchet & Clank (2002)**

| Item / collectible type | Original game | OBP status |
| --- | --- | --- |
| Bolts (currency) | Ratchet & Clank (2002) | Partial (R&C1) |
| Gold bolts | Ratchet & Clank (2002) | Unassessed |
| Skill points | Ratchet & Clank (2002) | Unassessed |
| Infobots | Ratchet & Clank (2002) | Unassessed |
| CodeBots | Ratchet & Clank (2002) | Unassessed |
| Map-o-Matic | Ratchet & Clank (2002) | Unassessed |
| Bolt Grabber | Ratchet & Clank (2002) | Unassessed |
| Persuader | Ratchet & Clank (2002) | Unassessed |
| Raritanium | Ratchet & Clank (2002) | Unassessed |
| Premium Nanotech | Ratchet & Clank (2002) | Unassessed |
| Ultra Nanotech | Ratchet & Clank (2002) | Unassessed |
| Platinum Zoomerator | Ratchet & Clank (2002) | Unassessed |
| Hoverboard | Ratchet & Clank (2002) | Unassessed |

**Going Commando**

| Item / collectible type | Original game | OBP status |
| --- | --- | --- |
| Bolts (currency) | Going Commando | Unassessed |
| Platinum bolts | Going Commando | Unassessed |
| Skill points | Going Commando | Unassessed |
| Nanotech boosts | Going Commando | Unassessed |
| Raritanium | Going Commando | Unassessed |
| Qwark Statuette | Going Commando | Unassessed |
| Box Breaker | Going Commando | Unassessed |
| Mapper | Going Commando | Unassessed |
| Armor Magnetizer | Going Commando | Unassessed |
| Megacorp Helmet | Going Commando | Unassessed |
| Commando Suit | Going Commando | Unassessed |
| Biker Helmet | Going Commando | Unassessed |
| Tetrafiber Armor | Going Commando | Unassessed |
| Megacorp weapon modifications (acid) | Going Commando | Unassessed |
| Megacorp weapon modifications (shock) | Going Commando | Unassessed |
| Megacorp weapon modifications (lock-on) | Going Commando | Unassessed |
| Challenge-mode Mega / Ultra weapon upgrades | Going Commando | Unassessed |
| Ship upgrades and components | Going Commando | Unassessed |

**Up Your Arsenal**

| Item / collectible type | Original game | OBP status |
| --- | --- | --- |
| Bolts (currency) | Up Your Arsenal | Unassessed |
| Titanium bolts | Up Your Arsenal | Unassessed |
| Skill points | Up Your Arsenal | Unassessed |
| Trophies (Starship Phoenix) | Up Your Arsenal | Unassessed |
| Bolt Grabber V2 | Up Your Arsenal | Unassessed |
| Map-O-Matic | Up Your Arsenal | Unassessed |
| Nano-pak | Up Your Arsenal | Unassessed |
| Master plan | Up Your Arsenal | Unassessed |
| Star map | Up Your Arsenal | Unassessed |
| Megacorp Helmet | Up Your Arsenal | Unassessed |
| Alpha Combat Suit | Up Your Arsenal | Unassessed |
| Adamantine Armor | Up Your Arsenal | Unassessed |
| Aegis Mark V Armor | Up Your Arsenal | Unassessed |
| Infernox Armor | Up Your Arsenal | Unassessed |
| Gadgetron discount | Up Your Arsenal | Unassessed |
| Challenge-mode Mega / Giga / Omega upgrades | Up Your Arsenal | Unassessed |

References: [R&C1 items](https://ratchetandclank.fandom.com/wiki/Category%3AItems_in_Ratchet_%26_Clank_%282002_game%29), [GC equipment](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA items](https://ratchetandclank.fandom.com/wiki/Category%3AItems_in_Up_Your_Arsenal). Collectible categories are tracked here, not all individual bolt or skill-point locations.

</details>

<details>
<summary>Crates, currency and pickups</summary>

| Object / pickup type | Original game | OBP status |
| --- | --- | --- |
| Wooden bolt crate | Trilogy / appearances vary | Partial (R&C1) |
| Metal bolt crate | Trilogy / appearances vary | Partial (R&C1) |
| Explosive crate | Trilogy / appearances vary | Partial (R&C1) |
| Ammunition crate | Trilogy / appearances vary | Partial (R&C1) |
| Nanotech crate | Trilogy / appearances vary | Partial (R&C1) |
| Jackpot crate | Trilogy / appearances vary | Unassessed |
| Ultra Nanotech crate | Trilogy / appearances vary | Unassessed |
| Inferno crate | Trilogy / appearances vary | Unassessed |
| Loose bolts (world-placed) | Trilogy / appearances vary | Playable (R&C1) |
| Enemy-dropped bolts | Trilogy / appearances vary | Unassessed |
| Ammo pickup | Trilogy / appearances vary | Unassessed |
| Nanotech pickup | Trilogy / appearances vary | Unassessed |

Reference: [Crate types](https://ratchetandclank.fandom.com/wiki/Crates), [OBP crate and bolt research](research/RAC1_CRATES_PICKUPS.md).

</details>

<details>
<summary>Common world objects and interactive mechanisms</summary>

| Object / mechanism | Original game | OBP status |
| --- | --- | --- |
| Environmental breakables | Trilogy / appearances vary | Unassessed |
| Destructible props | Trilogy / appearances vary | Unassessed |
| Breakable walls | Trilogy / appearances vary | Unassessed |
| Destructible doors | Trilogy / appearances vary | Unassessed |
| Locked doors | Trilogy / appearances vary | Unassessed |
| Timed doors | Trilogy / appearances vary | Unassessed |
| Moving doors | Trilogy / appearances vary | Unassessed |
| Wrench-operated bolt crank | Trilogy / appearances vary | Unassessed |
| Floor switch / pressure plate | Trilogy / appearances vary | Unassessed |
| World button / hit switch | Trilogy / appearances vary | Unassessed |
| Timed button / switch | Trilogy / appearances vary | Unassessed |
| Remote-activated switch | Trilogy / appearances vary | Unassessed |
| Lever / control panel | Trilogy / appearances vary | Unassessed |
| Laser barrier / forcefield | Trilogy / appearances vary | Unassessed |
| Bridge activation | Trilogy / appearances vary | Unassessed |
| Elevator / lift | Trilogy / appearances vary | Unassessed |
| Moving platform | Trilogy / appearances vary | Unassessed |
| Rotating platform | Trilogy / appearances vary | Unassessed |
| Collapsing platform | Trilogy / appearances vary | Unassessed |
| Conveyor belt | Trilogy / appearances vary | Unassessed |
| Grind rail | Trilogy / appearances vary | Unassessed |
| Grind rail branch / junction | Trilogy / appearances vary | Unassessed |
| Zipline | Trilogy / appearances vary | Unassessed |
| Swingshot Versa-Target (pull) | Trilogy / appearances vary | Unassessed |
| Swingshot Versa-Target (swing) | Trilogy / appearances vary | Unassessed |
| Swingshot Versa-Target (pull object) | Trilogy / appearances vary | Unassessed |
| Magne-Strip / magnetic walk surface | Trilogy / appearances vary | Unassessed |
| Wall jump slot | Trilogy / appearances vary | Unassessed |
| Dynamo target | Trilogy / appearances vary | Unassessed |
| Electrolyzer puzzle terminal | Trilogy / appearances vary | Unassessed |
| Infiltrator puzzle terminal | Trilogy / appearances vary | Unassessed |
| Hacker terminal | Trilogy / appearances vary | Unassessed |
| Trespasser lock | Trilogy / appearances vary | Unassessed |
| Invinco-Vault / CodeBot lock | Trilogy / appearances vary | Unassessed |
| Tractor Beam target | Trilogy / appearances vary | Unassessed |
| Pushable / pullable puzzle object | Trilogy / appearances vary | Unassessed |
| Thermanator water pool | Trilogy / appearances vary | Unassessed |
| Refractor beam / mirror target | Trilogy / appearances vary | Unassessed |
| Bolt-operated bridge / platform | Trilogy / appearances vary | Unassessed |
| Teleporter pad | Trilogy / appearances vary | Unassessed |
| Return-to-ship teleporter | Trilogy / appearances vary | Unassessed |
| Short-cut teleporter | Trilogy / appearances vary | Unassessed |
| Planet ship landing pad | Trilogy / appearances vary | Unassessed |
| Weapon / ammo vendor | Trilogy / appearances vary | Unassessed |
| Armor vendor | Trilogy / appearances vary | Unassessed |
| Ship upgrade vendor | Trilogy / appearances vary | Unassessed |
| Infobot / mission unlock pickup | Trilogy / appearances vary | Unassessed |
| Checkpoint trigger | Trilogy / appearances vary | Unassessed |
| Mission trigger / completion volume | Trilogy / appearances vary | Unassessed |
| Enemy spawner / encounter trigger | Trilogy / appearances vary | Unassessed |
| Scripted NPC interaction | Trilogy / appearances vary | Unassessed |
| Transport vehicle boarding point | Trilogy / appearances vary | Unassessed |
| Racing start / finish trigger | Trilogy / appearances vary | Unassessed |
| Arena wave trigger | Trilogy / appearances vary | Unassessed |
| Gadgebot command terminal | Trilogy / appearances vary | Unassessed |
| Clank bot / Gadgebot spawn point | Trilogy / appearances vary | Unassessed |
| Giant Clank transformation pad | Trilogy / appearances vary | Unassessed |

Names here describe reusable object families, not claims that each appears in all three games. Some mechanisms are game-specific, and their exact retail class IDs, placements and per-level behaviour require an authored-entity census. References: [R&C1 NPC and interactable census](research/RAC1_NPC_INTERACTABLE_CENSUS.md), [R&C1 traversal](research/RAC1_TRAVERSAL_EQUIPMENT.md), [GC interactions](https://ratchetandclank.fandom.com/wiki/Going_Commando_script), [UYA interactions](https://ratchetandclank.fandom.com/wiki/Up_Your_Arsenal_script).

</details>

<details>
<summary>Level activities and special encounter types</summary>

**Ratchet & Clank (2002)**

| Activity / level feature | Original game | OBP status |
| --- | --- | --- |
| Hoverboard race | Ratchet & Clank (2002) | Unassessed |
| Hoverboard trick/boost challenges | Ratchet & Clank (2002) | Unassessed |
| Clank-only Gadgebot puzzles | Ratchet & Clank (2002) | Unassessed |
| Giant Clank sequence | Ratchet & Clank (2002) | Unassessed |
| Fighter ship battle | Ratchet & Clank (2002) | Unassessed |
| Jet Fighter mission | Ratchet & Clank (2002) | Unassessed |
| Trespasser laser puzzle | Ratchet & Clank (2002) | Unassessed |
| Grindrail traversal | Ratchet & Clank (2002) | Unassessed |
| Nanotech upgrade purchase | Ratchet & Clank (2002) | Unassessed |
| Gold weapon vendor | Ratchet & Clank (2002) | Unassessed |
| Skill point challenge | Ratchet & Clank (2002) | Unassessed |

**Going Commando**

| Activity / level feature | Original game | OBP status |
| --- | --- | --- |
| Hoverbike race | Going Commando | Unassessed |
| Starship space combat | Going Commando | Unassessed |
| Giant Clank combat | Going Commando | Unassessed |
| Glider course | Going Commando | Unassessed |
| Levitator traversal | Going Commando | Unassessed |
| Megacorp Arena challenges | Going Commando | Unassessed |
| Platinum Bolt hunting | Going Commando | Unassessed |
| Insomniac Museum access | Going Commando | Unassessed |
| Weapon XP and mods | Going Commando | Unassessed |
| Nanotech XP and boosts | Going Commando | Unassessed |
| Ship customization | Going Commando | Unassessed |

**Up Your Arsenal**

| Activity / level feature | Original game | OBP status |
| --- | --- | --- |
| Annihilation Nation arena | Up Your Arsenal | Unassessed |
| Galactic Ranger battle missions | Up Your Arsenal | Unassessed |
| Qwark Vid-Comic levels | Up Your Arsenal | Unassessed |
| Starship Phoenix VR training | Up Your Arsenal | Unassessed |
| Starship Phoenix trophy room | Up Your Arsenal | Unassessed |
| Hacker puzzle | Up Your Arsenal | Unassessed |
| Tyhrraguise infiltration | Up Your Arsenal | Unassessed |
| Refractor beam puzzle | Up Your Arsenal | Unassessed |
| Ship/space encounters | Up Your Arsenal | Unassessed |
| Multi-level weapon XP | Up Your Arsenal | Unassessed |
| Challenge Mode | Up Your Arsenal | Unassessed |
| Titanium Bolt hunting | Up Your Arsenal | Unassessed |

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

## Project layout

| Directory | Contents |
| --- | --- |
| `src/OBP.IO`, `src/OBP.PS2` | Disc access and PS2 file formats |
| `src/OBP.RAC1`, `src/OBP.RAC2`, `src/OBP.RAC3` | Game-specific formats and behaviour |
| `src/OBP.Runtime` | Engine-independent runtime |
| `src/OBP.Godot`, `game/` | Godot integration and application |
| `research/` | Reverse-engineering notes and evidence |
| `tests/`, `tools/` | Tests and development scripts |

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Project vision](docs/PROJECT_VISION.md)
- [Research index](research/README.md)
- [Retail testing](docs/RETAIL_TESTING.md)
- [Development guidelines](AGENTS.md)
- [External source policy](research/EXTERNAL_SOURCE_POLICY.md)

Development uses feature branches and pull requests into the protected `dev` branch. Merges are manual.

This is an unofficial fan project and is not affiliated with Sony Interactive Entertainment or Insomniac Games.
