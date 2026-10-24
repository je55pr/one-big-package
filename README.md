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

## Implementation catalogue

<div>🔴 Unassessed</div>
<div>🟠 Researched / basic implementation</div>
<div>🟡 Partially implemented</div>
<div>🟢 Fully implemented and verified</div>
<br>

<details>
<summary>Weapons and upgrade families</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Weapon / family</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/df/OmniWrench_8000_render.png/revision/latest/scale-to-width-down/144?cb=20190515022417" width="72" alt="OmniWrench 8000"></td><td nowrap="nowrap">OmniWrench 8000</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9a/Bomb_Glove_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155519" width="72" alt="Bomb Glove"></td><td nowrap="nowrap">Bomb Glove</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cb/Pyrocitor_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155651" width="72" alt="Pyrocitor"></td><td nowrap="nowrap">Pyrocitor</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/31/Blaster_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155925" width="72" alt="Blaster"></td><td nowrap="nowrap">Blaster</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cd/Glove_of_Doom_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155903" width="72" alt="Glove of Doom"></td><td nowrap="nowrap">Glove of Doom</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/aa/Suck_Cannon_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155830" width="72" alt="Suck Cannon"></td><td nowrap="nowrap">Suck Cannon</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9d/Taunter_icon.png/revision/latest?cb=20250708155840" width="72" alt="Taunter"></td><td nowrap="nowrap">Taunter</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/60/Mine_Glove_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155851" width="72" alt="Mine Glove"></td><td nowrap="nowrap">Mine Glove</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1a/R.Y.N.O._from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155709" width="72" alt="R.Y.N.O."></td><td nowrap="nowrap">R.Y.N.O.</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9d/Devastator_icon.png/revision/latest?cb=20250708155820" width="72" alt="Devastator"></td><td nowrap="nowrap">Devastator</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c0/Walloper_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155809" width="72" alt="Walloper"></td><td nowrap="nowrap">Walloper</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e8/Visibomb_Gun_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155802" width="72" alt="Visibomb Gun"></td><td nowrap="nowrap">Visibomb Gun</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fa/Decoy_Glove_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155752" width="72" alt="Decoy Glove"></td><td nowrap="nowrap">Decoy Glove</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/96/Drone_Device_icon.png/revision/latest?cb=20250708155740" width="72" alt="Drone Device"></td><td nowrap="nowrap">Drone Device</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cf/Tesla_Claw_from_R%26C_%282002%29_icon.png/revision/latest?cb=20250708155732" width="72" alt="Tesla Claw"></td><td nowrap="nowrap">Tesla Claw</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a7/Morph-o-Ray_icon.png/revision/latest?cb=20250708155720" width="72" alt="Morph-O-Ray"></td><td nowrap="nowrap">Morph-O-Ray</td><td nowrap="nowrap">🟠</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Weapon / family</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fd/OmniWrench_10000_render.png/revision/latest/scale-to-width-down/144?cb=20190515022532" width="72" alt="OmniWrench 10000 / 12000"></td><td nowrap="nowrap">OmniWrench 10000 / 12000</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f2/Lancer_from_GC_icon.png/revision/latest?cb=20260126005435" width="72" alt="Lancer"></td><td nowrap="nowrap">Lancer</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/34/Gravity_Bomb_from_GC_icon.png/revision/latest?cb=20260126005256" width="72" alt="Gravity Bomb"></td><td nowrap="nowrap">Gravity Bomb</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a9/Chopper_icon.png/revision/latest?cb=20260126005028" width="72" alt="Chopper"></td><td nowrap="nowrap">Chopper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a5/Seeker_Gun_icon.png/revision/latest?cb=20260126005832" width="72" alt="Seeker Gun"></td><td nowrap="nowrap">Seeker Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0a/Pulse_Rifle_icon.png/revision/latest?cb=20260126005752" width="72" alt="Pulse Rifle"></td><td nowrap="nowrap">Pulse Rifle</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/4f/Miniturret_Glove_from_GC_icon.png/revision/latest?cb=20260126005703" width="72" alt="Miniturret Glove"></td><td nowrap="nowrap">Miniturret Glove</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/45/Blitz_Gun_icon.png/revision/latest?cb=20260126004741" width="72" alt="Blitz Gun"></td><td nowrap="nowrap">Blitz Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/2f/Shield_Charger_from_GC_icon.png/revision/latest?cb=20260126005858" width="72" alt="Shield Charger"></td><td nowrap="nowrap">Shield Charger</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/55/Synthenoids_icon.png/revision/latest?cb=20260126005954" width="72" alt="Synthenoid"></td><td nowrap="nowrap">Synthenoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1e/Lava_Gun_from_GC_icon.png/revision/latest?cb=20260126005600" width="72" alt="Lava Gun"></td><td nowrap="nowrap">Lava Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f3/Bouncer_from_GC_icon.png/revision/latest?cb=20260126004825" width="72" alt="Bouncer"></td><td nowrap="nowrap">Bouncer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fe/Minirocket_Tube_from_GC_icon.png/revision/latest?cb=20260126005446" width="72" alt="Minirocket Tube"></td><td nowrap="nowrap">Minirocket Tube</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/12/Plasma_Coil_from_GC_icon.png/revision/latest?cb=20260126005817" width="72" alt="Plasma Coil"></td><td nowrap="nowrap">Plasma Coil</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/06/Hoverbomb_Gun_icon.png/revision/latest?cb=20260126005328" width="72" alt="Hoverbomb Gun"></td><td nowrap="nowrap">Hoverbomb Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/70/Spiderbot_Glove_icon.png/revision/latest?cb=20260126005914" width="72" alt="Spiderbot Glove"></td><td nowrap="nowrap">Spiderbot Glove</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0f/Sheepinator_from_GC_icon.png/revision/latest?cb=20260126005844" width="72" alt="Sheepinator"></td><td nowrap="nowrap">Sheepinator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/85/RYNO_II_icon.png/revision/latest?cb=20260126005734" width="72" alt="R.Y.N.O. II"></td><td nowrap="nowrap">R.Y.N.O. II</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1a/Zodiac_icon.png/revision/latest?cb=20260126010238" width="72" alt="Zodiac"></td><td nowrap="nowrap">Zodiac</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/89/Clank_Zapper_icon.png/revision/latest?cb=20260126005015" width="72" alt="Clank Zapper"></td><td nowrap="nowrap">Clank Zapper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/67/Tesla_Claw_from_GC_icon.png/revision/latest?cb=20260126005941" width="72" alt="Tesla Claw"></td><td nowrap="nowrap">Tesla Claw</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0b/Bomb_Glove_from_GC_icon.png/revision/latest?cb=20260126004753" width="72" alt="Bomb Glove"></td><td nowrap="nowrap">Bomb Glove</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/04/Walloper_from_GC_icon.png/revision/latest?cb=20260126010228" width="72" alt="Walloper"></td><td nowrap="nowrap">Walloper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3c/Visibomb_Gun_from_GC_icon.png/revision/latest?cb=20260126010150" width="72" alt="Visibomb Gun"></td><td nowrap="nowrap">Visibomb Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/25/Decoy_Glove_from_GC_icon.png/revision/latest?cb=20260126004951" width="72" alt="Decoy Glove"></td><td nowrap="nowrap">Decoy Glove</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Weapon / family</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/15/OmniWrench_12000_render.png/revision/latest/scale-to-width-down/144?cb=20190515022440" width="72" alt="OmniWrench 12000"></td><td nowrap="nowrap">OmniWrench 12000</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7f/Shock_Blaster_icon.png/revision/latest?cb=20260126012534" width="72" alt="Shock Blaster"></td><td nowrap="nowrap">Shock Blaster</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/55/Nitro_Launcher_icon.png/revision/latest?cb=20260126012211" width="72" alt="Nitro Launcher"></td><td nowrap="nowrap">Nitro Launcher</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/44/N60_Storm_icon.png/revision/latest?cb=20260126012254" width="72" alt="N60 Storm"></td><td nowrap="nowrap">N60 Storm</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d9/Plasma_Whip_from_UYA_icon.png/revision/latest?cb=20260126012358" width="72" alt="Plasma Whip"></td><td nowrap="nowrap">Plasma Whip</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/73/Infector_icon.png/revision/latest?cb=20260126011951" width="72" alt="Infector"></td><td nowrap="nowrap">Infector</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/48/Suck_Cannon_from_UYA_icon.png/revision/latest?cb=20260126012629" width="72" alt="Suck Cannon"></td><td nowrap="nowrap">Suck Cannon</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c7/Spitting_Hydra_icon.png/revision/latest?cb=20260126012517" width="72" alt="Spitting Hydra"></td><td nowrap="nowrap">Spitting Hydra</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/89/Agents_of_Doom_from_UYA_icon.png/revision/latest?cb=20260126010905" width="72" alt="Agents of Doom"></td><td nowrap="nowrap">Agents of Doom</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/ba/Flux_Rifle_icon.png/revision/latest?cb=20260126011545" width="72" alt="Flux Rifle"></td><td nowrap="nowrap">Flux Rifle</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9b/Annihilator_icon.png/revision/latest?cb=20260126011116" width="72" alt="Annihilator"></td><td nowrap="nowrap">Annihilator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/61/Holoshield_Glove_icon.png/revision/latest?cb=20260126011652" width="72" alt="Holoshield Glove"></td><td nowrap="nowrap">Holoshield Glove</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a1/Disc_Blade_Gun_icon.png/revision/latest?cb=20260126011400" width="72" alt="Disc Blade Gun"></td><td nowrap="nowrap">Disc Blade Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/ba/Rift_Inducer_icon.png/revision/latest?cb=20260126012456" width="72" alt="Rift Inducer"></td><td nowrap="nowrap">Rift Inducer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/81/Qwack-O-Ray_icon.png/revision/latest?cb=20260126012418" width="72" alt="Qwack-O-Ray"></td><td nowrap="nowrap">Qwack-O-Ray</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9a/Miniturret_Glove_from_UYA_icon.png/revision/latest?cb=20260126012133" width="72" alt="Miniturret Glove"></td><td nowrap="nowrap">Miniturret Glove</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f4/Lava_Gun_from_UYA_icon.png/revision/latest?cb=20260126012018" width="72" alt="Lava Gun"></td><td nowrap="nowrap">Lava Gun</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/96/Bouncer_from_UYA_icon.png/revision/latest?cb=20260126011310" width="72" alt="Bouncer"></td><td nowrap="nowrap">Bouncer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cc/Plasma_Coil_from_UYA_icon.png/revision/latest?cb=20260126012200" width="72" alt="Plasma Coil"></td><td nowrap="nowrap">Plasma Coil</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/12/Shield_Charger_from_UYA_icon.png/revision/latest?cb=20260126012548" width="72" alt="Shield Charger"></td><td nowrap="nowrap">Shield Charger</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/4a/RY3NO_icon.png/revision/latest?cb=20260126012614" width="72" alt="R.Y.3.N.O."></td><td nowrap="nowrap">R.Y.3.N.O.</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

Reference: [R&C1 weapons](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank_%282002_game%29), [GC weapon upgrades](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Going_Commando/Weapons), [UYA weapon upgrades](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal/Weapons), [R&C1 arsenal evidence](research/RAC1_ARSENAL.md), [cross-game weapon evidence](docs/FUSION_WEAPONS_TESTING.md).

</details>

<details>
<summary>Gadgets and traversal equipment</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Gadget / equipment</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/8f/Heli-Pack_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170311" width="72" alt="Heli-Pack"></td><td nowrap="nowrap">Heli-Pack</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ee/Thruster-Pack_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170714" width="72" alt="Thruster-Pack"></td><td nowrap="nowrap">Thruster-Pack</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7b/Hydro-Pack_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170346" width="72" alt="Hydro-Pack"></td><td nowrap="nowrap">Hydro-Pack</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c3/O2_Mask_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170535" width="72" alt="O2 Mask"></td><td nowrap="nowrap">O2 Mask</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cd/Pilot%27s_Helmet_icon.png/revision/latest?cb=20260124170603" width="72" alt="Pilot&#39;s Helmet"></td><td nowrap="nowrap">Pilot&#39;s Helmet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cc/Swingshot_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170651" width="72" alt="Swingshot"></td><td nowrap="nowrap">Swingshot</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/85/Trespasser_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170701" width="72" alt="Trespasser"></td><td nowrap="nowrap">Trespasser</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/75/Grind_Boots_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170250" width="72" alt="Grind Boots"></td><td nowrap="nowrap">Grind Boots</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fe/Magneboots_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170404" width="72" alt="Magneboots"></td><td nowrap="nowrap">Magneboots</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/73/Metal_Detector_icon.png/revision/latest?cb=20260124170508" width="72" alt="Metal Detector"></td><td nowrap="nowrap">Metal Detector</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/60/Hydrodisplacer_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170354" width="72" alt="Hydrodisplacer"></td><td nowrap="nowrap">Hydrodisplacer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/07/Sonic_Summoner_icon.png/revision/latest?cb=20260124170644" width="72" alt="Sonic Summoner"></td><td nowrap="nowrap">Sonic Summoner</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9d/Hologuise_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170318" width="72" alt="Hologuise"></td><td nowrap="nowrap">Hologuise</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f1/Gadgetron_PDA_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170239" width="72" alt="Gadgetron PDA"></td><td nowrap="nowrap">Gadgetron PDA</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Gadget / equipment</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d5/Swingshot_from_GC_icon.png/revision/latest?cb=20260126010003" width="72" alt="Swingshot"></td><td nowrap="nowrap">Swingshot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b0/Dynamo_icon.png/revision/latest?cb=20260126005147" width="72" alt="Dynamo"></td><td nowrap="nowrap">Dynamo</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/60/Electrolyzer_icon.png/revision/latest?cb=20260126005159" width="72" alt="Electrolyzer"></td><td nowrap="nowrap">Electrolyzer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e5/Hypnomatic_icon.png/revision/latest?cb=20260126005355" width="72" alt="Hypnomatic"></td><td nowrap="nowrap">Hypnomatic</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0d/Infiltrator_icon.png/revision/latest?cb=20260126005413" width="72" alt="Infiltrator"></td><td nowrap="nowrap">Infiltrator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7a/Thermanator_icon.png/revision/latest?cb=20260126010015" width="72" alt="Thermanator"></td><td nowrap="nowrap">Thermanator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/74/Tractor_Beam_icon.png/revision/latest?cb=20260126010305" width="72" alt="Tractor Beam"></td><td nowrap="nowrap">Tractor Beam</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/2b/Heli-Pack_from_GC_icon.png/revision/latest?cb=20260126005216" width="72" alt="Heli-Pack"></td><td nowrap="nowrap">Heli-Pack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/4d/Thruster-Pack_from_GC_icon.png/revision/latest?cb=20260126010029" width="72" alt="Thruster-Pack"></td><td nowrap="nowrap">Thruster-Pack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7c/Hydro-Pack_from_GC_icon.png/revision/latest?cb=20260126005339" width="72" alt="Hydro-Pack"></td><td nowrap="nowrap">Hydro-Pack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7c/Momentum_Glider_icon.png/revision/latest?cb=20260126005721" width="72" alt="Glider"></td><td nowrap="nowrap">Glider</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/35/Levitator_icon.png/revision/latest?cb=20260126005547" width="72" alt="Levitator"></td><td nowrap="nowrap">Levitator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ee/Charge_Boots_from_GC_icon.png/revision/latest?cb=20260126004939" width="72" alt="Charge Boots"></td><td nowrap="nowrap">Charge Boots</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0c/Gravity_Boots_from_GC_icon.png/revision/latest?cb=20260126005236" width="72" alt="Gravity Boots"></td><td nowrap="nowrap">Gravity Boots</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/65/Grind_Boots_from_GC_icon.png/revision/latest?cb=20260126005224" width="72" alt="Grind Boots"></td><td nowrap="nowrap">Grind Boots</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Gadget / equipment</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d4/Hacker_icon.png/revision/latest?cb=20260126011819" width="72" alt="Hacker"></td><td nowrap="nowrap">Hacker</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/bd/Hypershot_from_UYA_icon.png/revision/latest?cb=20260126011832" width="72" alt="Hypershot"></td><td nowrap="nowrap">Hypershot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/ce/Refractor_icon.png/revision/latest?cb=20260126012445" width="72" alt="Refractor"></td><td nowrap="nowrap">Refractor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/87/Tyhrra-Guise_icon.png/revision/latest?cb=20260126012648" width="72" alt="Tyhrraguise"></td><td nowrap="nowrap">Tyhrraguise</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/46/Warp_Pad_icon.png/revision/latest?cb=20260126012811" width="72" alt="Warp Pad"></td><td nowrap="nowrap">Warp Pad</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b5/Gadgetron_PDA_from_UYA_icon.png/revision/latest?cb=20260126011509" width="72" alt="Gadgetron PDA"></td><td nowrap="nowrap">Gadgetron PDA</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c1/Gravity_Boots_from_UYA_icon.png/revision/latest?cb=20260126011418" width="72" alt="Gravity Boots"></td><td nowrap="nowrap">Gravity Boots</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/34/Charge_Boots_from_UYA_icon.png/revision/latest?cb=20260126011332" width="72" alt="Charge Boots"></td><td nowrap="nowrap">Charge Boots</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/98/Heli-Pack_from_UYA_icon.png/revision/latest?cb=20260126011633" width="72" alt="Heli-Pack"></td><td nowrap="nowrap">Heli-Pack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1c/Thruster-Pack_from_UYA_icon.png/revision/latest?cb=20260126012637" width="72" alt="Thruster-Pack"></td><td nowrap="nowrap">Thruster-Pack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hydro-Pack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Banana-Guided Autonomous Monkey Device</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [R&C1 equipment](research/RAC1_TRAVERSAL_EQUIPMENT.md), [GC equipment](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA equipment](https://strategywiki.org/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal/Gadgets).

</details>

<details>
<summary>Enemies and hostile actor types</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Enemy / actor</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e4/Alien_snapper_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214132" width="72" alt="Alien snapper"></td><td nowrap="nowrap">Alien snapper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b6/Alien_swarmer_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214141" width="72" alt="Alien swarmer"></td><td nowrap="nowrap">Alien swarmer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0e/Amoeboid_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214151" width="72" alt="Amoeboid"></td><td nowrap="nowrap">Amoeboid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f7/Anklebiter_render.png/revision/latest/scale-to-width-down/144?cb=20260830214200" width="72" alt="Anklebiter"></td><td nowrap="nowrap">Anklebiter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/70/Axebot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214221" width="72" alt="Axebot"></td><td nowrap="nowrap">Axebot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/4d/Big_axebot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214243" width="72" alt="Big axebot"></td><td nowrap="nowrap">Big axebot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1e/Birdbot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214323" width="72" alt="Birdbot"></td><td nowrap="nowrap">Birdbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c5/Armed_transport_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181018034709" width="72" alt="Blarg armored transport"></td><td nowrap="nowrap">Blarg armored transport</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/57/Blarg_bombthrower_from_R%26C_%282002%29_screen.png/revision/latest/scale-to-width-down/144?cb=20181018032939" width="72" alt="Blarg bombthrower"></td><td nowrap="nowrap">Blarg bombthrower</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/13/Blarg_commander_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214340" width="72" alt="Blarg commander"></td><td nowrap="nowrap">Blarg commander</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/04/Blarg_destroyer_render.png/revision/latest/scale-to-width-down/144?cb=20230720003225" width="72" alt="Blarg destroyer"></td><td nowrap="nowrap">Blarg destroyer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e5/Blarg_elite_commando_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214349" width="72" alt="Blarg elite commando"></td><td nowrap="nowrap">Blarg elite commando</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/98/Blarg_generator_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214359" width="72" alt="Blarg generator"></td><td nowrap="nowrap">Blarg generator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d3/Blarg_gunner_screen.png/revision/latest/scale-to-width-down/144?cb=20181021144108" width="72" alt="Blarg gunner"></td><td nowrap="nowrap">Blarg gunner</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b9/Blarg_heavy_interceptor_render.png/revision/latest/scale-to-width-down/144?cb=20181022142913" width="72" alt="Blarg heavy interceptor"></td><td nowrap="nowrap">Blarg heavy interceptor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5b/Blarg_heli-commander_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214409" width="72" alt="Blarg heli-commander"></td><td nowrap="nowrap">Blarg heli-commander</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/20/Blarg_paratrooper_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214419" width="72" alt="Blarg paratrooper"></td><td nowrap="nowrap">Blarg paratrooper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/14/Blarg_saucer_render.png/revision/latest/scale-to-width-down/144?cb=20180807152221" width="72" alt="Blarg saucer"></td><td nowrap="nowrap">Blarg saucer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/51/Blarg_space_commando_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20180807152222" width="72" alt="Blarg space commando"></td><td nowrap="nowrap">Blarg space commando</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/30/Blarg_space_fighter_render.png/revision/latest/scale-to-width-down/144?cb=20181027211210" width="72" alt="Blarg space fighter"></td><td nowrap="nowrap">Blarg space fighter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e1/Blarg_trooper_from_R%26C_%282002%29_screen.png/revision/latest/scale-to-width-down/144?cb=20181018032940" width="72" alt="Blarg trooper"></td><td nowrap="nowrap">Blarg trooper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c7/Bolt_thief_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214439" width="72" alt="Bolt thief"></td><td nowrap="nowrap">Bolt thief</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0a/Bomb-o-matic_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181018034709" width="72" alt="Bomb-o-matic"></td><td nowrap="nowrap">Bomb-o-matic</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/06/Cannonball_tank_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181010004141" width="72" alt="Cannonball tank"></td><td nowrap="nowrap">Cannonball tank</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5e/Chomper_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214524" width="72" alt="Chomper"></td><td nowrap="nowrap">Chomper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/71/Constructorbot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214537" width="72" alt="Constructo bot"></td><td nowrap="nowrap">Constructo bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1b/Dark_Gadge-Bot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181018034710" width="72" alt="Dark Gadge-Bot"></td><td nowrap="nowrap">Dark Gadge-Bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/24/Darter_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214546" width="72" alt="Darter"></td><td nowrap="nowrap">Darter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/52/Electroid_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20230627194918" width="72" alt="Electroid"></td><td nowrap="nowrap">Electroid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5f/Extermibot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214615" width="72" alt="Extermibot (Rilgar)"></td><td nowrap="nowrap">Extermibot (Rilgar)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/bc/Extermitank_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181018225606" width="72" alt="Extermitank"></td><td nowrap="nowrap">Extermitank</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d8/Homing_mine_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181018034710" width="72" alt="Homing mine"></td><td nowrap="nowrap">Homing mine</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/53/Horny_toad_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214756" width="72" alt="Horned toad"></td><td nowrap="nowrap">Horned toad</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Machinegun turret</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7d/Mine_layer_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215057" width="72" alt="Mine layer"></td><td nowrap="nowrap">Mine layer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5d/Minibomber_render.png/revision/latest/scale-to-width-down/144?cb=20181018224418" width="72" alt="Minibomber"></td><td nowrap="nowrap">Minibomber</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/59/Peckbot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215250" width="72" alt="Peckbot"></td><td nowrap="nowrap">Peckbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/66/Plasmabot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215311" width="72" alt="Plasmabot"></td><td nowrap="nowrap">Plasmabot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/60/Psy-tcopus_render.png/revision/latest/scale-to-width-down/144?cb=20260830215337" width="72" alt="Psy-tcopus"></td><td nowrap="nowrap">Psy-tcopus</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a1/Puffoid_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215346" width="72" alt="Puffoid"></td><td nowrap="nowrap">Puffoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/37/Robomutt_from_R%26C_%282002%29_render_1.png/revision/latest/scale-to-width-down/144?cb=20181010000118" width="72" alt="Robomutt"></td><td nowrap="nowrap">Robomutt</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/82/Robosquawker_render.png/revision/latest/scale-to-width-down/144?cb=20260830215439" width="72" alt="Robosquawker"></td><td nowrap="nowrap">Robosquawker</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3e/Rocket_tank_render.png/revision/latest/scale-to-width-down/144?cb=20181018034710" width="72" alt="Rocket tank"></td><td nowrap="nowrap">Rocket tank</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/02/Sandshark_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20180816025756" width="72" alt="Sandshark"></td><td nowrap="nowrap">Sandshark</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/80/Sandshark_nest_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215500" width="72" alt="Sandshark nest"></td><td nowrap="nowrap">Sandshark nest</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6d/Sawbot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215505" width="72" alt="Sawbot"></td><td nowrap="nowrap">Sawbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Scoutbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e0/Screamer_render.png/revision/latest/scale-to-width-down/144?cb=20260830215516" width="72" alt="Screamer"></td><td nowrap="nowrap">Screamer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/af/Seeker_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215527" width="72" alt="Seeker"></td><td nowrap="nowrap">Seeker</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6a/Sentry-bot_from_R%26C_%282002%29_screen.png/revision/latest/scale-to-width-down/144?cb=20231217232422" width="72" alt="Sentry-bot"></td><td nowrap="nowrap">Sentry-bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/48/Sharkigator_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215537" width="72" alt="Sharkigator"></td><td nowrap="nowrap">Sharkigator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3e/Test_dummies_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20230629190147" width="72" alt="Test dummy"></td><td nowrap="nowrap">Test dummy</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/16/Toxic_crab_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215732" width="72" alt="Toxic crab"></td><td nowrap="nowrap">Toxic crab</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/89/Ultra-Mech_Unlimited_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215816" width="72" alt="Ultra-Mech Unlimited"></td><td nowrap="nowrap">Ultra-Mech Unlimited</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Veldin flame robot (class 1440)</td><td nowrap="nowrap">🟠</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Enemy / actor</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/20/Arctic_leviathan_render.png/revision/latest/scale-to-width-down/144?cb=20260830214211" width="72" alt="Arctic leviathan"></td><td nowrap="nowrap">Arctic leviathan</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/de/Attack-Copter_render.png/revision/latest/scale-to-width-down/144?cb=20190715013812" width="72" alt="Attack-Copter"></td><td nowrap="nowrap">Attack-Copter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Bandit ship</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fd/Desert_dune_monster_screen.png/revision/latest/scale-to-width-down/144?cb=20190714204935" width="72" alt="Desert dune monster"></td><td nowrap="nowrap">Desert dune monster</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/45/Extermibot_from_GC_screen.png/revision/latest/scale-to-width-down/144?cb=20190714205005" width="72" alt="Extermibot"></td><td nowrap="nowrap">Extermibot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/75/Gadgetron_Hound_of_Cuddly_Death_render.png/revision/latest/scale-to-width-down/144?cb=20260830214640" width="72" alt="Gadgetron Hound of Cuddly Death"></td><td nowrap="nowrap">Gadgetron Hound of Cuddly Death</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/59/Grulch_render.png/revision/latest/scale-to-width-down/144?cb=20190715013645" width="72" alt="Grulch"></td><td nowrap="nowrap">Grulch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Machinegun turret</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c9/Megacorp_Chickenbot_render.png/revision/latest/scale-to-width-down/144?cb=20190712203520" width="72" alt="Megacorp Chickenbot"></td><td nowrap="nowrap">Megacorp Chickenbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/be/Megacorp_Chickenbot_Generator_render.png/revision/latest/scale-to-width-down/144?cb=20190712211539" width="72" alt="Megacorp Chickenbot Generator"></td><td nowrap="nowrap">Megacorp Chickenbot Generator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1d/Megacorp_gladiator_screen.png/revision/latest/scale-to-width-down/144?cb=20190714205210" width="72" alt="Megacorp gladiator"></td><td nowrap="nowrap">Megacorp gladiator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ab/Elite_Megacorp_gladiator_screen.png/revision/latest/scale-to-width-down/144?cb=20190714204944" width="72" alt="Megacorp gladiator (Elite)"></td><td nowrap="nowrap">Megacorp gladiator (Elite)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b5/Megacorp_Hover_Tank_render.png/revision/latest/scale-to-width-down/144?cb=20260830214943" width="72" alt="Megacorp Hover Tank"></td><td nowrap="nowrap">Megacorp Hover Tank</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/52/Megacorp_Hover_Tank_v2.0_render.png/revision/latest/scale-to-width-down/144?cb=20260830214954" width="72" alt="Megacorp Hover Tank V2.0"></td><td nowrap="nowrap">Megacorp Hover Tank V2.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9e/Megacorp_Laser_Bot_render.png/revision/latest/scale-to-width-down/144?cb=20260830215003" width="72" alt="Megacorp Laser Bot"></td><td nowrap="nowrap">Megacorp Laser Bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d3/Megacorp_Rivet_Bot_render.png/revision/latest/scale-to-width-down/144?cb=20260830215014" width="72" alt="Megacorp Rivet Bot"></td><td nowrap="nowrap">Megacorp Rivet Bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/94/Megacorp_Robot_Guard_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190715215217" width="72" alt="Megacorp Robot Guard"></td><td nowrap="nowrap">Megacorp Robot Guard</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e5/MSR_I_-_Chainsaw_Model_screen.png/revision/latest/scale-to-width-down/144?cb=20190717004334" width="72" alt="Megacorp Security Robot I (Chainsaw)"></td><td nowrap="nowrap">Megacorp Security Robot I (Chainsaw)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b6/MSR_I_-_Chainsaw_Model_v2.0.png/revision/latest/scale-to-width-down/144?cb=20190717004342" width="72" alt="Megacorp Security Robot I v2.0"></td><td nowrap="nowrap">Megacorp Security Robot I v2.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/8d/MSR_I_-_Chainsaw_Model_v3.0.png/revision/latest/scale-to-width-down/144?cb=20210219162142" width="72" alt="Megacorp Security Robot I v3.0"></td><td nowrap="nowrap">Megacorp Security Robot I v3.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d2/MSR_II_-_Flamethrower_Model_screen.png/revision/latest/scale-to-width-down/144?cb=20190717004817" width="72" alt="Megacorp Security Robot II (Flamethrower)"></td><td nowrap="nowrap">Megacorp Security Robot II (Flamethrower)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/13/MSR_II_-_Flamethrower_Model_v2.0_screen.png/revision/latest/scale-to-width-down/144?cb=20190717004915" width="72" alt="Megacorp Security Robot II v2.0"></td><td nowrap="nowrap">Megacorp Security Robot II v2.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3a/MSR_II_-_Flamethrower_Model_v3.0_screen.png/revision/latest/scale-to-width-down/144?cb=20190717004924" width="72" alt="Megacorp Security Robot II v3.0"></td><td nowrap="nowrap">Megacorp Security Robot II v3.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f7/Megacorp_Security_Turret_render.png/revision/latest/scale-to-width-down/144?cb=20260830215025" width="72" alt="Megacorp Security Turret"></td><td nowrap="nowrap">Megacorp Security Turret</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/87/Megacorp_Sentinel_render.png/revision/latest/scale-to-width-down/144?cb=20260830215035" width="72" alt="Megacorp Sentinel"></td><td nowrap="nowrap">Megacorp Sentinel</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f4/Megacorp_Sweeper_Bot_render.png/revision/latest/scale-to-width-down/144?cb=20260830215046" width="72" alt="Megacorp Sweeper Bot"></td><td nowrap="nowrap">Megacorp Sweeper Bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/02/Megacorp_Trooper_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190715215613" width="72" alt="Megacorp Trooper"></td><td nowrap="nowrap">Megacorp Trooper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ed/Megacorp_Trooper_v2.0_screen.png/revision/latest/scale-to-width-down/144?cb=20190716031830" width="72" alt="Megacorp Trooper v2.0"></td><td nowrap="nowrap">Megacorp Trooper v2.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f7/Mutant_firefly_render.png/revision/latest/scale-to-width-down/144?cb=20260830215129" width="72" alt="Mutant firefly"></td><td nowrap="nowrap">Mutant firefly</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c9/Mutant_muckdweller_render.png/revision/latest/scale-to-width-down/144?cb=20260830215142" width="72" alt="Mutant muckdweller"></td><td nowrap="nowrap">Mutant muckdweller</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Mutant sandfly</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1b/Mutant_swamp_beast_render.png/revision/latest/scale-to-width-down/144?cb=20260830215200" width="72" alt="Mutant swamp beast"></td><td nowrap="nowrap">Mutant swamp beast</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ae/Mutant_test-squirrel_promo_render.png/revision/latest/scale-to-width-down/144?cb=20260830215210" width="72" alt="Mutant test-squirrel"></td><td nowrap="nowrap">Mutant test-squirrel</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d3/Nidbot_render.png/revision/latest/scale-to-width-down/144?cb=20230710014836" width="72" alt="Nidbot"></td><td nowrap="nowrap">Nidbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/89/Protopet_render.png/revision/latest/scale-to-width-down/144?cb=20260830215323" width="72" alt="Protopet"></td><td nowrap="nowrap">Protopet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/be/PX6_BladeBall_render.png/revision/latest/scale-to-width-down/144?cb=20260830215354" width="72" alt="PX6 BladeBall"></td><td nowrap="nowrap">PX6 BladeBall</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/be/Blade_Ball_v2.0_render.png/revision/latest/scale-to-width-down/144?cb=20260830214332" width="72" alt="PX6 BladeBall v2.0"></td><td nowrap="nowrap">PX6 BladeBall v2.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Receiver Bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9d/Receiver_Bot_v1.0_render.png/revision/latest/scale-to-width-down/144?cb=20260830215412" width="72" alt="Receiver Bot v1.0"></td><td nowrap="nowrap">Receiver Bot v1.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/dc/Receiver_Bot_v3.0_render.png/revision/latest/scale-to-width-down/144?cb=20260830215422" width="72" alt="Receiver Bot v3.0"></td><td nowrap="nowrap">Receiver Bot v3.0</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/77/Repair_drone_render.png/revision/latest/scale-to-width-down/144?cb=20260830215431" width="72" alt="Repair drone"></td><td nowrap="nowrap">Repair drone</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/20/Berserk_repair_drone_render.png/revision/latest/scale-to-width-down/144?cb=20190422002244" width="72" alt="Repair drone (Berserk)"></td><td nowrap="nowrap">Repair drone (Berserk)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b1/Saboteur_render.png/revision/latest/scale-to-width-down/144?cb=20260830215450" width="72" alt="Saboteur"></td><td nowrap="nowrap">Saboteur</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e2/Barlowian_saur-beast_render.png/revision/latest/scale-to-width-down/144?cb=20260830214233" width="72" alt="Saur-beast (Barlow)"></td><td nowrap="nowrap">Saur-beast (Barlow)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/8a/Jobian_saur-beast_render.png/revision/latest/scale-to-width-down/144?cb=20260830214847" width="72" alt="Saur-beast (Joba)"></td><td nowrap="nowrap">Saur-beast (Joba)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Sharkigator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e7/Space_wasp_render.png/revision/latest/scale-to-width-down/144?cb=20260830215545" width="72" alt="Space wasp"></td><td nowrap="nowrap">Space wasp</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7d/Spawner_render.png/revision/latest/scale-to-width-down/144?cb=20260830215555" width="72" alt="Spawner"></td><td nowrap="nowrap">Spawner</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d5/Spitting_dune_crawler_screen.png/revision/latest/scale-to-width-down/144?cb=20190714220402" width="72" alt="Spitting dune crawler"></td><td nowrap="nowrap">Spitting dune crawler</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6e/Thugs-4-Less_attack_ship_render.png/revision/latest/scale-to-width-down/144?cb=20260830215657" width="72" alt="Thugs-4-Less attack ship"></td><td nowrap="nowrap">Thugs-4-Less attack ship</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a0/Thugs-4-Less_Brute_screen.png/revision/latest/scale-to-width-down/144?cb=20190714214556" width="72" alt="Thugs-4-Less Brute"></td><td nowrap="nowrap">Thugs-4-Less Brute</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ae/Thugs-4-Less_Brute_Class_II_screen.png/revision/latest/scale-to-width-down/144?cb=20190714214733" width="72" alt="Thugs-4-Less Brute Class II"></td><td nowrap="nowrap">Thugs-4-Less Brute Class II</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Thugs-4-Less Carrier Ship</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3c/Thugs-4-Less_Elite_Henchman_screen.png/revision/latest/scale-to-width-down/144?cb=20190714221809" width="72" alt="Thugs-4-Less Elite Henchman"></td><td nowrap="nowrap">Thugs-4-Less Elite Henchman</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f1/Thugs-4-Less_fighter_green.png/revision/latest/scale-to-width-down/144?cb=20260830215707" width="72" alt="Thugs-4-Less fighter"></td><td nowrap="nowrap">Thugs-4-Less fighter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/00/Thugs-4-Less_Henchman_screen.png/revision/latest/scale-to-width-down/144?cb=20190714221813" width="72" alt="Thugs-4-Less Henchman"></td><td nowrap="nowrap">Thugs-4-Less Henchman</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/26/Joba_tribesman_screen.png/revision/latest/scale-to-width-down/144?cb=20190714215402" width="72" alt="Tribesman (Joba)"></td><td nowrap="nowrap">Tribesman (Joba)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cb/Two-headed_Smolgian_snapper_screen.png/revision/latest/scale-to-width-down/144?cb=20190714215910" width="72" alt="Two-headed Smolgian snapper"></td><td nowrap="nowrap">Two-headed Smolgian snapper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/74/Vukovar_tribesman_screen.png/revision/latest/scale-to-width-down/144?cb=20190714215356" width="72" alt="Vukovar tribesman"></td><td nowrap="nowrap">Vukovar tribesman</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/93/Y.E.T.I._render.png/revision/latest/scale-to-width-down/144?cb=20260830215835" width="72" alt="Y.E.T.I."></td><td nowrap="nowrap">Y.E.T.I.</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Enemy / actor</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/45/%27Noid_commander_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718185054" width="72" alt="&#39;Noid commander"></td><td nowrap="nowrap">&#39;Noid commander</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9c/%27Noid_missile_station_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126202833" width="72" alt="&#39;Noid missile station"></td><td nowrap="nowrap">&#39;Noid missile station</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Amoeboid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Attack-Copter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6b/Backup_dancer_render.png/revision/latest/scale-to-width-down/144?cb=20191225034108" width="72" alt="Backup dancer"></td><td nowrap="nowrap">Backup dancer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5c/Bash_bot_1_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191110041636" width="72" alt="Bash bot"></td><td nowrap="nowrap">Bash bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ae/Blade_bot_render_1.png/revision/latest/scale-to-width-down/144?cb=20191128161231" width="72" alt="Blade bot"></td><td nowrap="nowrap">Blade bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/32/Drone_bot_render.png/revision/latest/scale-to-width-down/144?cb=20260830214553" width="72" alt="Drone bot"></td><td nowrap="nowrap">Drone bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ae/Elite_sharpshooter_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191115141005" width="72" alt="Elite sharpshooter"></td><td nowrap="nowrap">Elite sharpshooter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Extermibot (Rilgar)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e6/Floranian_blood_fly_render.png/revision/latest/scale-to-width-down/144?cb=20191110045557" width="72" alt="Floranian blood fly"></td><td nowrap="nowrap">Floranian blood fly</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7e/Four-eyed_robonoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718185300" width="72" alt="Four-eyed robonoid"></td><td nowrap="nowrap">Four-eyed robonoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cc/Four-eyed_robonoid_V2_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718184830" width="72" alt="Four-eyed robonoid V2"></td><td nowrap="nowrap">Four-eyed robonoid V2</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Four-eyed tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/2c/Gatling_turret_render.png/revision/latest/scale-to-width-down/144?cb=20260830214657" width="72" alt="Gatling turret"></td><td nowrap="nowrap">Gatling turret</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/ca/Giant_ninja_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191115140550" width="72" alt="Giant ninja"></td><td nowrap="nowrap">Giant ninja</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ab/Gladiator_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191110042042" width="72" alt="Gladiator"></td><td nowrap="nowrap">Gladiator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/55/Guard_bot.png/revision/latest/scale-to-width-down/144?cb=20210213221549" width="72" alt="Guard bot"></td><td nowrap="nowrap">Guard bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/54/Heli-bot_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209013412" width="72" alt="Heli-bot"></td><td nowrap="nowrap">Heli-bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/01/Henchman_promo_render_1.png/revision/latest/scale-to-width-down/144?cb=20191209013429" width="72" alt="Henchman"></td><td nowrap="nowrap">Henchman</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/93/Hover_bot_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191110042200" width="72" alt="Hover bot"></td><td nowrap="nowrap">Hover bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/55/Insectoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209013654" width="72" alt="Insectoid"></td><td nowrap="nowrap">Insectoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6f/Insectoid_swarm_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209013643" width="72" alt="Insectoid swarm"></td><td nowrap="nowrap">Insectoid swarm</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6b/King_amoeboid_render.png/revision/latest/scale-to-width-down/144?cb=20260830214854" width="72" alt="King amoeboid"></td><td nowrap="nowrap">King amoeboid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f9/Laser_bot_1_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191110042306" width="72" alt="Laser bot"></td><td nowrap="nowrap">Laser bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d9/Lawn_ninja_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126201514" width="72" alt="Lawn ninja"></td><td nowrap="nowrap">Lawn ninja</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9d/Lawn_ninja_generator_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209014923" width="72" alt="Lawn ninja generator"></td><td nowrap="nowrap">Lawn ninja generator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/73/Mecha-tyhrranoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126201021" width="72" alt="Mecha-tyhrranoid"></td><td nowrap="nowrap">Mecha-tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b7/Mega_bot_render.png/revision/latest/scale-to-width-down/144?cb=20200125163818" width="72" alt="Mega bot"></td><td nowrap="nowrap">Mega bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp Sentinel</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c8/Mini-ninja_henchman_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191115140710" width="72" alt="Mini-ninja henchman"></td><td nowrap="nowrap">Mini-ninja henchman</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/98/Nabla_native_promo_render_1.png/revision/latest/scale-to-width-down/144?cb=20191110042609" width="72" alt="Nabla native"></td><td nowrap="nowrap">Nabla native</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f8/Ninja_bot_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126201644" width="72" alt="Ninja bot"></td><td nowrap="nowrap">Ninja bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/4d/One-eyed_robonoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718191350" width="72" alt="One-eyed robonoid"></td><td nowrap="nowrap">One-eyed robonoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/47/One-eyed_tyhrranoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718191317" width="72" alt="One-eyed tyhrranoid"></td><td nowrap="nowrap">One-eyed tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/07/One-eyed_tyhrranoid_v2_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718191557" width="72" alt="One-eyed tyhrranoid V2"></td><td nowrap="nowrap">One-eyed tyhrranoid V2</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/26/Qwark_bot_from_UYA_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126201956" width="72" alt="Qwark bot"></td><td nowrap="nowrap">Qwark bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Robonoid commander</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Robonoid saucer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/31/Robotic_pirate_ghost_promo_render_1.png/revision/latest/scale-to-width-down/144?cb=20191209013025" width="72" alt="Robotic pirate ghost"></td><td nowrap="nowrap">Robotic pirate ghost</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/70/Scoutbot_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126205225" width="72" alt="Scoutbot"></td><td nowrap="nowrap">Scoutbot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Sentinel bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Sharkigator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/63/Sharpshooter_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191115140854" width="72" alt="Sharpshooter"></td><td nowrap="nowrap">Sharpshooter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Snipe bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9b/Soldier_bot_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191115140426" width="72" alt="Soldier bot"></td><td nowrap="nowrap">Soldier bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ab/Space_rat_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209013041" width="72" alt="Space rat"></td><td nowrap="nowrap">Space rat</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e9/Swamp_monster_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191110042830" width="72" alt="Swamp monster"></td><td nowrap="nowrap">Swamp monster</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/17/Tank_bot_render.png/revision/latest/scale-to-width-down/144?cb=20200424180707" width="72" alt="Tank bot"></td><td nowrap="nowrap">Tank bot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Test dummy</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a7/Three-eyed_robonoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718190002" width="72" alt="Three-eyed robonoid"></td><td nowrap="nowrap">Three-eyed robonoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/75/Three-eyed_robonoid_V2_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718185907" width="72" alt="Three-eyed robonoid V2"></td><td nowrap="nowrap">Three-eyed robonoid V2</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/65/Three-eyed_tyhrranoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718185745" width="72" alt="Three-eyed tyhrranoid"></td><td nowrap="nowrap">Three-eyed tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/54/Three-eyed_tyhrranoid_V2.png/revision/latest/scale-to-width-down/144?cb=20210213185221" width="72" alt="Three-eyed tyhrranoid V2"></td><td nowrap="nowrap">Three-eyed tyhrranoid V2</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b7/Troop_transport_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126203907" width="72" alt="Troop transport"></td><td nowrap="nowrap">Troop transport</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Two-eyed robonoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/94/Two-eyed_tyhrranoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718190356" width="72" alt="Two-eyed tyhrranoid"></td><td nowrap="nowrap">Two-eyed tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b8/Tyhrranoid_attack_ship_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718190738" width="72" alt="Tyhrranoid attack ship"></td><td nowrap="nowrap">Tyhrranoid attack ship</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e5/Tyhrranoid_dropship_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126204010" width="72" alt="Tyhrranoid dropship"></td><td nowrap="nowrap">Tyhrranoid dropship</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/84/Tyhrranoid_saucer_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191126201259" width="72" alt="Tyhrranoid saucer"></td><td nowrap="nowrap">Tyhrranoid saucer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ultra mecha-robonoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/2c/Ultra_mecha-tyhrranoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718190851" width="72" alt="Ultra mecha-tyhrranoid"></td><td nowrap="nowrap">Ultra mecha-tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [R&C1 enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Ratchet_%26_Clank_%282002_game%29), [GC enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Going_Commando), [UYA enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Up_Your_Arsenal), [OBP Veldin evidence](research/RAC1_VELDIN_ENEMIES.md). Enemy names and variants reflect community indexes; matching to retail actor classes is pending.

</details>

<details>
<summary>Bosses and set-piece opponents</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Boss / encounter</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/57/Alien_Queen_render.png/revision/latest/scale-to-width-down/144?cb=20260830214117" width="72" alt="Alien Queen"></td><td nowrap="nowrap">Alien Queen</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/ba/Blargian_snagglebeast_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830214431" width="72" alt="Blargian Snagglebeast"></td><td nowrap="nowrap">Blargian Snagglebeast</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/0d/Qwark%27s_starfighter.png/revision/latest/scale-to-width-down/144?cb=20181026015427" width="72" alt="Captain Qwark&#39;s starfighter"></td><td nowrap="nowrap">Captain Qwark&#39;s starfighter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/38/Ultra_Supreme_Executive_Chairman_Drek-Mech_render.png/revision/latest/scale-to-width-down/144?cb=20181018224711" width="72" alt="Chairman Drek (Drek-Mech)"></td><td nowrap="nowrap">Chairman Drek (Drek-Mech)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">PlanetBuster Maximus</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/89/Ultra-Mech_Unlimited_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20260830215816" width="72" alt="Ultra-Mech Unlimited"></td><td nowrap="nowrap">Ultra-Mech Unlimited</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Boss / encounter</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Unknown Thief</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d6/Arachnoid_screen.png/revision/latest/scale-to-width-down/144?cb=20190716031622" width="72" alt="Arachnoid"></td><td nowrap="nowrap">Arachnoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/2f/B2_Brawler_screen.png/revision/latest/scale-to-width-down/144?cb=20190716031536" width="72" alt="B2 Brawler"></td><td nowrap="nowrap">B2 Brawler</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/99/Chainblade_render.png/revision/latest/scale-to-width-down/144?cb=20260830214503" width="72" alt="Chainblade"></td><td nowrap="nowrap">Chainblade</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b2/Giganto-Mech_render.png/revision/latest/scale-to-width-down/144?cb=20260830214707" width="72" alt="Giganto-Mech"></td><td nowrap="nowrap">Giganto-Mech</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/75/Megapede_render.png/revision/latest/scale-to-width-down/144?cb=20190716030806" width="72" alt="Megapede"></td><td nowrap="nowrap">Megapede</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/85/Mothership_render.png/revision/latest/scale-to-width-down/144?cb=20260830215114" width="72" alt="Mothership"></td><td nowrap="nowrap">Mothership</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c5/Mutant_Protopet_render.png/revision/latest/scale-to-width-down/144?cb=20260830215150" width="72" alt="Mutant Protopet"></td><td nowrap="nowrap">Mutant Protopet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1e/Swamp_monster_I_screen.png/revision/latest/scale-to-width-down/144?cb=20190716031313" width="72" alt="Swamp monster I"></td><td nowrap="nowrap">Swamp monster I</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/84/Swamp_monster_II_render.png/revision/latest/scale-to-width-down/144?cb=20260830215613" width="72" alt="Swamp monster II"></td><td nowrap="nowrap">Swamp monster II</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Thug Leader</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/03/Thug_Leader%27s_helicopter_screen.png/revision/latest/scale-to-width-down/144?cb=20190716032019" width="72" alt="Thug Leader&#39;s helicopter"></td><td nowrap="nowrap">Thug Leader&#39;s helicopter</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/43/Thug_Leader%27s_mech_render.png/revision/latest/scale-to-width-down/144?cb=20260830215645" width="72" alt="Thug Leader&#39;s mech"></td><td nowrap="nowrap">Thug Leader&#39;s mech</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Boss / encounter</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/55/Biobliterator_from_UYA_render.png/revision/latest/scale-to-width-down/144?cb=20260830214311" width="72" alt="Biobliterator"></td><td nowrap="nowrap">Biobliterator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/74/Captain_Blackstar_render.png/revision/latest/scale-to-width-down/144?cb=20260830214452" width="72" alt="Blackstar"></td><td nowrap="nowrap">Blackstar</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/39/Courtney_Gears_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190711180526" width="72" alt="Courtney Gears"></td><td nowrap="nowrap">Courtney Gears</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/20/Dr._Nefarious_from_UYA_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190717035616" width="72" alt="Dr. Nefarious"></td><td nowrap="nowrap">Dr. Nefarious</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Gary and Helen</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/39/Giant_Klunk_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209020202" width="72" alt="Giant Klunk"></td><td nowrap="nowrap">Giant Klunk</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Klunk</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/2f/Momma_tyhrranoid_promo_render.png/revision/latest/scale-to-width-down/144?cb=20190718192034" width="72" alt="Momma Tyhrranoid"></td><td nowrap="nowrap">Momma Tyhrranoid</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Captain Qwark</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3f/Robotic_space_eel_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191209013022" width="72" alt="Robotic space eel"></td><td nowrap="nowrap">Robotic space eel</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f6/Scorpio_render.png/revision/latest/scale-to-width-down/144?cb=20191128151118" width="72" alt="Scorpio"></td><td nowrap="nowrap">Scorpio</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/53/Terror_of_Talos_promo_render.png/revision/latest/scale-to-width-down/144?cb=20191115143022" width="72" alt="Terror of Talos"></td><td nowrap="nowrap">Terror of Talos</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/27/Warship_render.png/revision/latest/scale-to-width-down/144?cb=20191115135059" width="72" alt="Warship"></td><td nowrap="nowrap">Warship</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [R&C1 enemy index](https://ratchetandclank.fandom.com/wiki/Category:Enemies_in_Ratchet_%26_Clank_%282002_game%29), [GC boss index](https://ratchetandclank.fandom.com/wiki/Monsterpedia), [UYA boss index](https://ratchetandclank.fandom.com/wiki/Category%3ABosses_in_Up_Your_Arsenal).

</details>

<details>
<summary>Planets, levels and destinations</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Planet / level</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/77/Veldin_from_R%26C_%282002%29.png/revision/latest?cb=20180803035614" width="72" alt="Veldin: Kyzil Plateau (opening)"></td><td nowrap="nowrap">Veldin: Kyzil Plateau (opening)</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/73/Novalis_from_R%26C_%282002%29.png/revision/latest?cb=20180803035609" width="72" alt="Novalis: Tobruk Crater"></td><td nowrap="nowrap">Novalis: Tobruk Crater</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5b/Aridia_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035603" width="72" alt="Aridia: Outpost X11"></td><td nowrap="nowrap">Aridia: Outpost X11</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/3f/Kerwan_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035608" width="72" alt="Kerwan: Metropolis"></td><td nowrap="nowrap">Kerwan: Metropolis</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/93/Eudora.png/revision/latest/scale-to-width-down/144?cb=20180803035605" width="72" alt="Eudora: Logging Site"></td><td nowrap="nowrap">Eudora: Logging Site</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Nebula G34: Blarg Station</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/80/Rilgar_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035612" width="72" alt="Rilgar: Blackwater City"></td><td nowrap="nowrap">Rilgar: Blackwater City</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/65/Umbris.png/revision/latest/scale-to-width-down/144?cb=20180803035613" width="72" alt="Umbris: Qwark&#39;s HQ"></td><td nowrap="nowrap">Umbris: Qwark&#39;s HQ</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/da/Batalia_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035604" width="72" alt="Batalia: Fort Krontos"></td><td nowrap="nowrap">Batalia: Fort Krontos</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/69/Gaspar_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035606" width="72" alt="Gaspar: Blarg Depot"></td><td nowrap="nowrap">Gaspar: Blarg Depot</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/87/Orxon_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035610" width="72" alt="Orxon: Kogor Refinery"></td><td nowrap="nowrap">Orxon: Kogor Refinery</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7f/Pokitaru_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035611" width="72" alt="Pokitaru: Jowai Resort"></td><td nowrap="nowrap">Pokitaru: Jowai Resort</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d8/Hoven.png/revision/latest/scale-to-width-down/144?cb=20180803035606" width="72" alt="Hoven: Bomb Factory"></td><td nowrap="nowrap">Hoven: Bomb Factory</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Oltanis Orbit: Gemlik Base</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/97/Oltanis.png/revision/latest/scale-to-width-down/144?cb=20180803035609" width="72" alt="Oltanis: Gorda City"></td><td nowrap="nowrap">Oltanis: Gorda City</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b3/Quartu_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035611" width="72" alt="Quartu: Robot Factory"></td><td nowrap="nowrap">Quartu: Robot Factory</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/41/Kalebo_III_from_R%26C_%282002%29.png/revision/latest/scale-to-width-down/144?cb=20180803035607" width="72" alt="Kalebo III: Gadgetron Site"></td><td nowrap="nowrap">Kalebo III: Gadgetron Site</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Veldin Orbit: Drek&#39;s Fleet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/77/Veldin_from_R%26C_%282002%29.png/revision/latest?cb=20180803035614" width="72" alt="Veldin: Kyzil Plateau (revisit)"></td><td nowrap="nowrap">Veldin: Kyzil Plateau (revisit)</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Planet / level</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/eb/Aranos.png/revision/latest/scale-to-width-down/144?cb=20180807095911" width="72" alt="Aranos: Flying Lab"></td><td nowrap="nowrap">Aranos: Flying Lab</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/dd/Oozla.png/revision/latest?cb=20180807095920" width="72" alt="Oozla: Megacorp Outlet"></td><td nowrap="nowrap">Oozla: Megacorp Outlet</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c3/Wupash_Nebula_1.png/revision/latest/scale-to-width-down/144?cb=20190706161205" width="72" alt="Wupash Nebula"></td><td nowrap="nowrap">Wupash Nebula</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f3/Maktar_Resort_1.png/revision/latest/scale-to-width-down/144?cb=20190416154417" width="72" alt="Maktar Nebula: Maktar Resort"></td><td nowrap="nowrap">Maktar Nebula: Maktar Resort</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ec/Jamming_array_1.png/revision/latest/scale-to-width-down/144?cb=20190416155102" width="72" alt="Maktar Nebula: Jamming Array"></td><td nowrap="nowrap">Maktar Nebula: Jamming Array</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f4/Barlow.png/revision/latest/scale-to-width-down/144?cb=20180807095912" width="72" alt="Barlow: Vukovar Canyon"></td><td nowrap="nowrap">Barlow: Vukovar Canyon</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fc/Endako.png/revision/latest/scale-to-width-down/144?cb=20180807095915" width="72" alt="Endako: Megapolis"></td><td nowrap="nowrap">Endako: Megapolis</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/16/Feltzin_System_render.png/revision/latest/scale-to-width-down/144?cb=20190706162736" width="72" alt="Feltzin System: Thug Rendezvous"></td><td nowrap="nowrap">Feltzin System: Thug Rendezvous</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/79/Notak.png/revision/latest/scale-to-width-down/144?cb=20180807095920" width="72" alt="Notak: Canal City"></td><td nowrap="nowrap">Notak: Canal City</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/4c/Ship_Shack_1.png/revision/latest/scale-to-width-down/144?cb=20190417135347" width="72" alt="Slim Cognito&#39;s Ship Shack"></td><td nowrap="nowrap">Slim Cognito&#39;s Ship Shack</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fe/Siberius.png/revision/latest/scale-to-width-down/144?cb=20180807095921" width="72" alt="Siberius: Frozen Base"></td><td nowrap="nowrap">Siberius: Frozen Base</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/98/Tabora.png/revision/latest/scale-to-width-down/144?cb=20180807095923" width="72" alt="Tabora: Mining Area"></td><td nowrap="nowrap">Tabora: Mining Area</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f9/Dobbo.png/revision/latest/scale-to-width-down/144?cb=20180807095915" width="72" alt="Dobbo: Testing Facility"></td><td nowrap="nowrap">Dobbo: Testing Facility</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Dobbo Orbit: Testing Facility Moon</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/66/Deep_Space_Disposal_Facility_1.png/revision/latest/scale-to-width-down/144?cb=20190716181810" width="72" alt="Hrugis Cloud: Deep Space Disposal"></td><td nowrap="nowrap">Hrugis Cloud: Deep Space Disposal</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1a/Joba.png/revision/latest/scale-to-width-down/144?cb=20180807095918" width="72" alt="Joba: Megacorp Games"></td><td nowrap="nowrap">Joba: Megacorp Games</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/cb/Todano.png/revision/latest/scale-to-width-down/144?cb=20180807095923" width="72" alt="Todano: Megacorp Armory"></td><td nowrap="nowrap">Todano: Megacorp Armory</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/99/Boldan.png/revision/latest/scale-to-width-down/144?cb=20180807095913" width="72" alt="Boldan: Silver City"></td><td nowrap="nowrap">Boldan: Silver City</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/eb/Aranos.png/revision/latest/scale-to-width-down/144?cb=20180807095911" width="72" alt="Aranos (revisit)"></td><td nowrap="nowrap">Aranos (revisit)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/15/Gorn_screen.png/revision/latest/scale-to-width-down/144?cb=20190716194237" width="72" alt="Gorn: Thugs-4-Less Fleet"></td><td nowrap="nowrap">Gorn: Thugs-4-Less Fleet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ed/Snivelak.png/revision/latest/scale-to-width-down/144?cb=20180807095922" width="72" alt="Snivelak: Thug HQ"></td><td nowrap="nowrap">Snivelak: Thug HQ</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/9f/Smolg.png/revision/latest/scale-to-width-down/144?cb=20180807095921" width="72" alt="Smolg: Distribution Facility"></td><td nowrap="nowrap">Smolg: Distribution Facility</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a9/Damosel.png/revision/latest/scale-to-width-down/144?cb=20180807095913" width="72" alt="Damosel: Allgon City"></td><td nowrap="nowrap">Damosel: Allgon City</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/ff/Damosel_moon_1.png/revision/latest/scale-to-width-down/144?cb=20190712002706" width="72" alt="Damosel Orbit: Allgon Moon"></td><td nowrap="nowrap">Damosel Orbit: Allgon Moon</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/24/Grelbin.png/revision/latest/scale-to-width-down/144?cb=20180807095916" width="72" alt="Grelbin: Tundor Wastes"></td><td nowrap="nowrap">Grelbin: Tundor Wastes</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/96/Yeedil.png/revision/latest/scale-to-width-down/144?cb=20180807095919" width="72" alt="Yeedil: Protopet Factory"></td><td nowrap="nowrap">Yeedil: Protopet Factory</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Planet / level</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/16/Veldin_from_UYA.png/revision/latest?cb=20180808152708" width="72" alt="Veldin: Kyzil Plateau"></td><td nowrap="nowrap">Veldin: Kyzil Plateau</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/77/Florana.png/revision/latest/scale-to-width-down/144?cb=20180808152701" width="72" alt="Florana"></td><td nowrap="nowrap">Florana</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/17/Starship_Phoenix_1.png/revision/latest/scale-to-width-down/144?cb=20191202232256" width="72" alt="Starship Phoenix"></td><td nowrap="nowrap">Starship Phoenix</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/fe/Marcadia.png/revision/latest/scale-to-width-down/144?cb=20180808152703" width="72" alt="Marcadia"></td><td nowrap="nowrap">Marcadia</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/58/Annihilation_Nation_1.png/revision/latest/scale-to-width-down/144?cb=20200105223435" width="72" alt="Annihilation Nation"></td><td nowrap="nowrap">Annihilation Nation</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6a/Aquatos.png/revision/latest/scale-to-width-down/144?cb=20180808152659" width="72" alt="Aquatos"></td><td nowrap="nowrap">Aquatos</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6c/Tyhrranosis.png/revision/latest/scale-to-width-down/144?cb=20180808152707" width="72" alt="Tyhrranosis"></td><td nowrap="nowrap">Tyhrranosis</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c9/Daxx.png/revision/latest/scale-to-width-down/144?cb=20180808152700" width="72" alt="Daxx"></td><td nowrap="nowrap">Daxx</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/eb/Obani_Gemini_1.png/revision/latest/scale-to-width-down/144?cb=20191230152827" width="72" alt="Obani Gemini"></td><td nowrap="nowrap">Obani Gemini</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c2/Obani_Draco_1.png/revision/latest/scale-to-width-down/144?cb=20191230011203" width="72" alt="Obani Draco"></td><td nowrap="nowrap">Obani Draco</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/bc/Holostar_Studios_1.png/revision/latest/scale-to-width-down/144?cb=20200122180200" width="72" alt="Holostar Studios"></td><td nowrap="nowrap">Holostar Studios</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/11/Rilgar_from_UYA.png/revision/latest/scale-to-width-down/144?cb=20180808152705" width="72" alt="Rilgar"></td><td nowrap="nowrap">Rilgar</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/ab/Zeldrin_Starport.png/revision/latest/scale-to-width-down/144?cb=20180808152709" width="72" alt="Zeldrin Starport"></td><td nowrap="nowrap">Zeldrin Starport</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f3/Kerwan_from_UYA.png/revision/latest/scale-to-width-down/144?cb=20180808152702" width="72" alt="Kerwan: Metropolis"></td><td nowrap="nowrap">Kerwan: Metropolis</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/1/1f/Crash_site_1.png/revision/latest/scale-to-width-down/144?cb=20200107171350" width="72" alt="Zeldrin crash site"></td><td nowrap="nowrap">Zeldrin crash site</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/70/Aridia_from_UYA.png/revision/latest/scale-to-width-down/144?cb=20180808152659" width="72" alt="Aridia"></td><td nowrap="nowrap">Aridia</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/df/Thran_Asteroid_Belt.png/revision/latest/scale-to-width-down/144?cb=20180808152707" width="72" alt="Thran Asteroid Belt"></td><td nowrap="nowrap">Thran Asteroid Belt</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ef/Koros.png/revision/latest/scale-to-width-down/144?cb=20180808152813" width="72" alt="Koros"></td><td nowrap="nowrap">Koros</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/81/Mylon.png/revision/latest/scale-to-width-down/144?cb=20180808152831" width="72" alt="Mylon"></td><td nowrap="nowrap">Mylon</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [R&C1 level order](https://strategywiki.org/wiki/Ratchet_%26_Clank/Table_of_Contents), [GC level catalogue](research/GC_LEVEL_CATALOGUE.md), [UYA locations](https://ratchetclank.neoseeker.com/wiki/Planets). Separate orbit, arena and return visits are listed where they are distinct destinations.

</details>

<details>
<summary>Items, upgrades and collectibles</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Item / collectible type</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Bolts (currency)</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c7/Gold_bolt_render.png/revision/latest/scale-to-width-down/144?cb=20150621193218" width="72" alt="Gold bolts"></td><td nowrap="nowrap">Gold bolts</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Skill points</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/93/Infobot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20150621224303" width="72" alt="Infobots"></td><td nowrap="nowrap">Infobots</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/33/CodeBot_icon.png/revision/latest?cb=20260124170215" width="72" alt="CodeBots"></td><td nowrap="nowrap">CodeBots</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/af/Map-o-Matic_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170459" width="72" alt="Map-o-Matic"></td><td nowrap="nowrap">Map-o-Matic</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/ed/Bolt_Grabber_from_R%26C_%282002%29_icon.png/revision/latest?cb=20260124170147" width="72" alt="Bolt Grabber"></td><td nowrap="nowrap">Bolt Grabber</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d6/Persuader_icon.png/revision/latest?cb=20260124170545" width="72" alt="Persuader"></td><td nowrap="nowrap">Persuader</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Premium Nanotech</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ultra Nanotech</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/b8/Platinum_Zoomerator_icon.png/revision/latest?cb=20260124170611" width="72" alt="Platinum Zoomerator"></td><td nowrap="nowrap">Platinum Zoomerator</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f7/Hoverboard_icon.png/revision/latest?cb=20260124170327" width="72" alt="Hoverboard"></td><td nowrap="nowrap">Hoverboard</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Item / collectible type</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Bolts (currency)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/8b/Platinum_bolt_render.png/revision/latest/scale-to-width-down/144?cb=20150621214216" width="72" alt="Platinum bolts"></td><td nowrap="nowrap">Platinum bolts</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Skill points</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/a/a0/Nanotech_boost_screen.png/revision/latest/scale-to-width-down/144?cb=20190423161604" width="72" alt="Nanotech boosts"></td><td nowrap="nowrap">Nanotech boosts</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/97/Raritanium_from_GC_render.png/revision/latest/scale-to-width-down/144?cb=20260830215406" width="72" alt="Raritanium"></td><td nowrap="nowrap">Raritanium</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/99/Qwark_statuette_icon.png/revision/latest?cb=20260126005742" width="72" alt="Qwark Statuette"></td><td nowrap="nowrap">Qwark Statuette</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/8f/Box_Breaker_from_GC_icon.png/revision/latest?cb=20260126004851" width="72" alt="Box Breaker"></td><td nowrap="nowrap">Box Breaker</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7f/Mapper_icon.png/revision/latest?cb=20260126005528" width="72" alt="Mapper"></td><td nowrap="nowrap">Mapper</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/89/Armor_Magnetizer_from_GC_icon.png/revision/latest?cb=20260126022522" width="72" alt="Armor Magnetizer"></td><td nowrap="nowrap">Armor Magnetizer</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/4/40/Megacorp_Helmet_icon.png/revision/latest?cb=20260126005505" width="72" alt="Megacorp Helmet"></td><td nowrap="nowrap">Megacorp Helmet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/0/03/Commando_suit_icon.png/revision/latest?cb=20260126005001" width="72" alt="Commando Suit"></td><td nowrap="nowrap">Commando Suit</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/54/Biker_helmet_icon.png/revision/latest?cb=20260126000209" width="72" alt="Biker Helmet"></td><td nowrap="nowrap">Biker Helmet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/87/Tetrafiber_armor_icon.png/revision/latest?cb=20260126005932" width="72" alt="Tetrafiber Armor"></td><td nowrap="nowrap">Tetrafiber Armor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp weapon modifications (acid)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp weapon modifications (shock)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp weapon modifications (lock-on)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Challenge-mode Mega / Ultra weapon upgrades</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ship upgrades and components</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Item / collectible type</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Bolts (currency)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/67/Titanium_bolt_from_UYA_render.png/revision/latest/scale-to-width-down/144?cb=20150621215038" width="72" alt="Titanium bolts"></td><td nowrap="nowrap">Titanium bolts</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Skill points</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/82/Starship_Phoenix_trophy_room.png/revision/latest/scale-to-width-down/144?cb=20191205155500" width="72" alt="Trophies (Starship Phoenix)"></td><td nowrap="nowrap">Trophies (Starship Phoenix)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/6c/Bolt_Grabber_V2_icon.png/revision/latest?cb=20260126011129" width="72" alt="Bolt Grabber V2"></td><td nowrap="nowrap">Bolt Grabber V2</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/3/30/Map-o-Matic_from_UYA_icon.png/revision/latest?cb=20260126012043" width="72" alt="Map-O-Matic"></td><td nowrap="nowrap">Map-O-Matic</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/95/Nano-pak_icon.png/revision/latest?cb=20260126012235" width="72" alt="Nano-pak"></td><td nowrap="nowrap">Nano-pak</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Master plan</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Star map</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp Helmet</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/e/e6/Alpha_combat_suit_icon.png/revision/latest?cb=20260126010923" width="72" alt="Alpha Combat Suit"></td><td nowrap="nowrap">Alpha Combat Suit</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Adamantine Armor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/c/c2/Aegis_Mark_V_armor_icon.png/revision/latest?cb=20191212043908" width="72" alt="Aegis Mark V Armor"></td><td nowrap="nowrap">Aegis Mark V Armor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/60/Infernox_armor_icon.png/revision/latest?cb=20260126013128" width="72" alt="Infernox Armor"></td><td nowrap="nowrap">Infernox Armor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Gadgetron discount</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Challenge-mode Mega / Giga / Omega upgrades</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [R&C1 items](https://ratchetandclank.fandom.com/wiki/Category%3AItems_in_Ratchet_%26_Clank_%282002_game%29), [GC equipment](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA items](https://ratchetandclank.fandom.com/wiki/Category%3AItems_in_Up_Your_Arsenal). Collectible categories are tracked here, not all individual bolt or skill-point locations.

</details>

<details>
<summary>Crates, currency and pickups</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Crate / pickup</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Wooden bolt crate</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/80/Metal_crate_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181006023001" width="72" alt="Metal bolt crate"></td><td nowrap="nowrap">Metal bolt crate</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/25/Explosive_crate_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181006022857" width="72" alt="Explosive crate"></td><td nowrap="nowrap">Explosive crate</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/f/f2/Ammo_crate_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181006022818" width="72" alt="Ammo crate"></td><td nowrap="nowrap">Ammo crate</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/7/7b/Nanotech_crate_from_R%26C_%282002%29_screen.png/revision/latest/scale-to-width-down/144?cb=20181006023038" width="72" alt="Nanotech crate"></td><td nowrap="nowrap">Nanotech crate</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Jet Fighter health crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">World-placed bolts</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Crate-emitted bolts</td><td nowrap="nowrap">🟠</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Crate / pickup</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Wooden bolt crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Explosive crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/ba/Ammo_crate_from_GC_render.png/revision/latest/scale-to-width-down/144?cb=20181006225303" width="72" alt="Megacorp ammo crate"></td><td nowrap="nowrap">Megacorp ammo crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Nanotech crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ultra Nanotech crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Crate / enemy bolts</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Crate / pickup</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Wooden bolt crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Metal bolt crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Explosive crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/53/Ammo_crate_from_UYA_render.png/revision/latest/scale-to-width-down/144?cb=20181006225454" width="72" alt="Gadgetron ammo crate"></td><td nowrap="nowrap">Gadgetron ammo crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Nanotech crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/b/bb/Jackpot_crate_from_UYA_render.png/revision/latest/scale-to-width-down/144?cb=20201022195105" width="72" alt="Jackpot crate"></td><td nowrap="nowrap">Jackpot crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/2/29/Inferno_crate_render.png/revision/latest/scale-to-width-down/144?cb=20181006225452" width="72" alt="Inferno crate"></td><td nowrap="nowrap">Inferno crate</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Crate / enemy bolts</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [Crate appearances](https://ratchetandclank.fandom.com/wiki/Crates), [OBP R&C1 crate implementation](research/RAC1_CRATES_PICKUPS.md), [GC crate descriptions](https://www.neoseeker.com/ratchet-clank-commando/faqs/74417-ratchet-and-clank-gc-d.html).

</details>

<details>
<summary>Common world objects and interactive mechanisms</summary>

Mechanism labels describe interactive object families. They are not necessarily the original games' internal class names.

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Object / mechanism</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Bolt crank and linked doors (Novalis)</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/6/61/Teleporter_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20181108142221" width="72" alt="Teleporter pad"></td><td nowrap="nowrap">Teleporter pad</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">World button / hit switch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Floor pressure switch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Locked / timed door</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Trespasser lock</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/5c/Gadgetron_vendor_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20150203195216" width="72" alt="Gadgetron weapon / ammo vendor"></td><td nowrap="nowrap">Gadgetron weapon / ammo vendor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Nanotech dispenser</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Swingshot target (pull)</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Swingshot target (swing)</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Grind rail / branch</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Magne-Strip / magnetic surface</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Wall-jump slot</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Zipline</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Lift / moving platform</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/93/Infobot_from_R%26C_%282002%29_render.png/revision/latest/scale-to-width-down/144?cb=20150621224303" width="72" alt="Infobot / mission trigger"></td><td nowrap="nowrap">Infobot / mission trigger</td><td nowrap="nowrap">🟠</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ship landing pad</td><td nowrap="nowrap">🟠</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Object / mechanism</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Bolt crank (jamming array)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Teleporter pad</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Translocator pad (Endako)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">World button / hit switch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Door / bridge activation switch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Dynamo target</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Electrolyzer circuit terminal</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Infiltrator puzzle lock</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Tractor Beam target / movable object</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Thermanator freeze / thaw water target</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hypnomatic target</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Swingshot target</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Grind rail / junction</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Magnetic Gravity Boots surface</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp weapon / ammo vendor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp armor vendor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Slim Cognito&#39;s ship-upgrade shop</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Arena / race challenge activation</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ship landing pad</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Object / mechanism</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Bolt crank (Kavu Island)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Capturable Galactic Ranger node</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/d2/Teleporter_from_UYA_render.png/revision/latest/scale-to-width-down/144?cb=20191128151636" width="72" alt="Teleporter pad / return to ship"></td><td nowrap="nowrap">Teleporter pad / return to ship</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Museum teleporter (Kerwan / Phoenix)</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">World button / hit switch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Bridge / objective activation switch</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hacker puzzle terminal</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Refractor laser reflector</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hypershot swing target</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hypershot holographic platform activation</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Warp Pad anchor point</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Gravity Boots magnetic surface</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/9/94/Gadgetron_vendor_from_UYA.png/revision/latest/scale-to-width-down/144?cb=20150202190921" width="72" alt="Gadgetron weapon / ammo vendor"></td><td nowrap="nowrap">Gadgetron weapon / ammo vendor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Armor vendor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Annihilation Nation arena entry</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Galactic Ranger objective / capture point</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hovership boarding pad</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ship landing pad</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

References: [OBP Novalis bolt crank](game/scripts/OBPGame.Rac1NovalisCranks.cs), [R&C1 traversal](research/RAC1_TRAVERSAL_EQUIPMENT.md), [Teleporter appearances](https://ratchetandclank.fandom.com/wiki/Teleporter), [GC gameplay instructions](https://ratchetandclank.fandom.com/wiki/Going_Commando_script), [UYA gameplay instructions](https://ratchetandclank.fandom.com/wiki/Up_Your_Arsenal_script), [UYA capture nodes](https://ratchetandclank.fandom.com/wiki/Node).

</details>

<details>
<summary>Level activities and special encounter types</summary>

**Ratchet & Clank (2002)**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Activity / level feature</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/d/da/Hoverboard_from_R%26C_%282002%29_gameplay_1.png/revision/latest/scale-to-width-down/144?cb=20180925224129" width="72" alt="Hoverboard race"></td><td nowrap="nowrap">Hoverboard race</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hoverboard trick/boost challenges</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Clank-only Gadgebot puzzles</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Giant Clank sequence</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Fighter ship battle</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Jet Fighter mission</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Trespasser laser puzzle</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Grindrail traversal</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Nanotech upgrade purchase</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Gold weapon vendor</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Skill point challenge</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Going Commando**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Activity / level feature</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td></td><td nowrap="nowrap">Hoverbike race</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Starship space combat</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Giant Clank combat</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Glider course</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Levitator traversal</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Megacorp Arena challenges</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Platinum Bolt hunting</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Insomniac Museum access</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Weapon XP and mods</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Nanotech XP and boosts</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ship customization</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

**Up Your Arsenal**

<table width="100%">
<thead><tr><th width="84"></th><th width="1">Activity / level feature</th><th width="1">Status</th><th>Notes</th></tr></thead>
<tbody>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/5/58/Annihilation_Nation_1.png/revision/latest/scale-to-width-down/144?cb=20200105223435" width="72" alt="Annihilation Nation arena"></td><td nowrap="nowrap">Annihilation Nation arena</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Galactic Ranger battle missions</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Qwark Vid-Comic levels</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Starship Phoenix VR training</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td><img src="https://static.wikia.nocookie.net/ratchet/images/8/82/Starship_Phoenix_trophy_room.png/revision/latest/scale-to-width-down/144?cb=20191205155500" width="72" alt="Starship Phoenix trophy room"></td><td nowrap="nowrap">Starship Phoenix trophy room</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Hacker puzzle</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Tyhrraguise infiltration</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Refractor beam puzzle</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Ship/space encounters</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Multi-level weapon XP</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Challenge Mode</td><td nowrap="nowrap">🔴</td><td></td></tr>
<tr><td></td><td nowrap="nowrap">Titanium Bolt hunting</td><td nowrap="nowrap">🔴</td><td></td></tr>
</tbody></table>

These are named activities and broad encounter types rather than an exhaustive list of all mission scripts. Reference: [R&C1 overview](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank_%282002_game%29), [GC overview](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Going_Commando), [UYA overview](https://ratchetandclank.fandom.com/wiki/Ratchet_%26_Clank%3A_Up_Your_Arsenal).

</details>

Ordinary planets and the Fusion Lab now share one persistent player session and
world-entry path. R&C1 Ratchet, selected equipment, health and camera preferences
survive destination changes. The selected avatar's source is required even on
another game's planet; an asset-free capsule requires `--diagnostic-player`.
This is the lifecycle foundation; mixed combat, full equipment and unified menus
remain follow-up work. See the [ownership contract](docs/PLAYER_WORLD_CONTRACT.md).

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
- [Player, world and actor ownership](docs/PLAYER_WORLD_CONTRACT.md)
- [Project vision](docs/PROJECT_VISION.md)
- [Research index](research/README.md)
- [Retail testing](docs/RETAIL_TESTING.md)
- [Development guidelines](AGENTS.md)
- [External source policy](research/EXTERNAL_SOURCE_POLICY.md)

Development uses feature branches and pull requests into the protected `dev` branch. Merges are manual.

This is an unofficial fan project and is not affiliated with Sony Interactive Entertainment or Insomniac Games.
