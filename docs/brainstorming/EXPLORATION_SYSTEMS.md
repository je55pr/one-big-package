# Exploration systems brainstorm

> **NON-BINDING BRAINSTORM.** Native behaviour must be recovered on the game-specific branches before OBP treats any cross-game interaction as real.

## Core idea

Exploration may be the strongest structural spine for OBP.

A trilogy gadget should not only solve the handful of puzzles in the game that introduced it. Once acquired, it can change how the player interprets previously visited worlds across the combined galaxy.

Desired player reaction:

> "I just got the Thermanator. Where have I seen water?"

That is the Metroidvania loop OBP can amplify.

## Exploration vocabulary

### Movement

- Swingshot / Hypershot
- Grind Boots
- Magneboots / Gravity Boots
- Charge Boots
- Levitator
- Momentum Glider
- Heli-Pack / Thruster-Pack / Hydro-Pack
- Clank traversal

### Environment manipulation

- Hydrodisplacer
- Thermanator
- Dynamo
- Tractor Beam
- Refractor

### Access / security

- Trespasser
- Infiltrator
- Hacker

### Remote / small-space access

- Visibomb
- Spiderbot
- Clank / bots

### Social / infiltration

- Hologuise
- Tyhrra-Guise
- Hypnomatic-style control where appropriate

### Discovery

- Metal Detector
- Map-o-Matic

## Compound interaction ideas

These are prompts for later design, not promises.

### Hydrodisplacer × Thermanator

The clearest high-value combination.

```text
water source
├─ Hydrodisplacer -> drain
├─ Hydrodisplacer -> fill elsewhere
├─ Thermanator -> freeze
├─ Thermanator -> thaw
└─ both -> move water, then change its state
```

Potential results:

- move water into a chamber, then freeze it into a traversal surface
- freeze a surface to cross before later draining for a submerged/underfloor route
- drain a room to expose machinery or Tractor Beam objects
- redirect water into a mechanism and then freeze/thaw to alter its state

### Dynamo × Refractor

Power a laser/security system, then redirect the beam.

### Tractor Beam × Swingshot

Move an authored object/platform/anchor into a traversal position, then use it as part of a route.

### Gravity Boots × Refractor

Maintain beam angle while walking on walls/ceilings.

### Levitator × Gravity Boots

Reach an unusual surface by free flight, attach, then continue through a route inaccessible from ordinary ground.

### Momentum Glider × Swingshot/Hypershot

Use grapple points to redirect or extend a glider route.

### Spiderbot × machinery

Send a remote unit through service ducts to operate inaccessible machinery.

### Disguise × security interaction

Enter an area socially disguised, then use a terminal/lock mechanism from the trusted side to open a permanent route.

### Hypnomatic × environment

Control an enemy/NPC to operate something from the opposite side of a barrier where native behaviour supports it.

### Metal Detector × other gadgets

The detector should often be the clue rather than the whole solution:

- cache detected behind a water-state puzzle
- cache under a magnetic route
- cache accessible only by Spiderbot
- cache requiring environmental machinery to expose

## Physical rules over coloured keys

Avoid turning every gadget into an arbitrary icon lock.

Prefer understandable world logic:

- water -> water-state tools
- large movable object -> Tractor Beam
- laser -> Refractor
- machinery -> Dynamo
- old security technology -> its appropriate hacking gadget
- narrow service space -> Spiderbot / Clank

Native marked interaction points can remain where appropriate, but OBP-authored exploration should prefer readable environmental reasoning where practical.

## Preserve technological identity

Newer should not automatically obsolete older.

Potential examples:

- **Trespasser** — older Solana / classic security systems
- **Infiltrator** — Megacorp/Bogon systems
- **Hacker** — later Gadgetron/Nefarious-era systems
- **Hologuise** — robot/security infiltration
- **Tyhrra-Guise** — Tyhrranoid social infiltration

Exact manufacturer mapping needs evidence/design review. The underlying principle is simply that multiple gadgets can remain useful because they target different technologies or factions.

## Planet-specific exploration languages

Do not use every gadget everywhere. Give worlds 2-4 strong mechanical identities.

Examples:

- **Pokitaru:** water, Hydro-Pack, Hydrodisplacer, Thermanator, O2 exploration
- **Batalia:** rails, Swingshot, military machinery, Dynamo/Refractor
- **Endako:** vertical city, Spiderbot, locks, Gravity Boots, Charge Boots
- **Quartu:** Clank/bots, machinery, Hologuise, Dynamo, Tractor Beam
- **Oozla:** wetland/water state, ecology, Metal Detector
- **Boldan:** Gravity Boots, Charge Boots, rails, remote/service routes
- **Aquatos:** water, Hydrodisplacer/Thermanator, Gravity Boots, security

## Backtracking structure

A planet can support layered discovery:

```text
first visit
├─ main route
├─ obvious optional route
└─ visible but inaccessible mysteries

later gadgets
├─ new traversal route
├─ secret room
├─ resource cache
├─ optional encounter
└─ new sub-area

late game
└─ compound-gadget route
```

The player should often remember the obstacle before receiving the solution.

Bad:

> Gain Thermanator -> UI says "return to Novalis".

Better:

> First visit shows a memorable inaccessible water/ice feature; hours later the new gadget triggers recognition.

## Shortcuts

Backtracking works better when exploration permanently improves traversal.

```text
first visit: A -> B -> C -> D -> E
later:       A -> gadget shortcut -> D
```

Shortcuts can be rewards in their own right when planets host repeatable activities or later world states.

## Soft gates / sequence breaking

Not every gate needs to be binary. If movement and level geometry allow a skilled player to reach something early, avoid invisible walls solely to enforce intended gadget order unless progression genuinely requires it.

Potential examples:

- difficult Charge Boot jump reaching a nominally later route
- advanced Swingshot use shortening a path
- creative movement accessing an optional reward early

This should be tested carefully rather than designed as a universal rule.

## Secret complexity tiers

Possible design vocabulary:

1. **Single gadget** — e.g. Metal Detector cache
2. **Gadget + observation** — freeze/thaw a route
3. **Two-system interaction** — move water, then freeze it
4. **Traversal chain** — Gravity Boots -> Refractor -> Swingshot
5. **Late-game mastery** — multi-gadget puzzle with a genuinely rare reward

## Map-o-Matic / completion support

A much larger galaxy may need better completion feedback without turning secrets into checklist chores.

Possible approach:

- reveal that unexplored space exists without revealing the solution
- show planet completion categories only after the player encounters the relevant system
- allow unknown/hidden categories to remain `???`

Example only:

```text
POKITARU
Main objectives      4/4
Exploration zones    6/8
Gold Bolts           2/3
Skill Points         3/5
Monsterpedia        11/13
Races                1/1
???
```

## Fluids / material identity

Hydrodisplacer and Thermanator interactions should not imply that every coloured liquid behaves identically.

Potential neutral categories to investigate:

- water
- ice
- coolant
- toxic sludge
- acid
- lava
- swamp water
- industrial fluids

The native games should determine what actually exists and how it is represented. OBP can later decide which cross-system interactions make physical and gameplay sense.

Example philosophy:

```text
Hydrodisplacer
water -> likely
acid -> probably not by default
lava -> definitely not by default

Thermanator
water/ice -> native core use
other fluids -> only if deliberately supported
```

## Movement calibration dependency

New exploration routes should eventually be authored against evidence-backed Ratchet movement metrics rather than arbitrary Godot defaults.

Useful baselines include:

- normal jump airtime/range
- double-jump envelope
- Heli/Thruster boost and stretch-jump ranges
- wall-jump slot widths
- ledge reach
- maximum walkable slope
- Charge Boot speed

The Going Commando Insomniac Museum movement-test geometry is a particularly valuable authority source for this work. Movement archaeology belongs on the GC/native research side; OBP should consume the resulting metrics.
