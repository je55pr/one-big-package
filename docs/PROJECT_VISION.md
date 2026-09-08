# One Big Package — Project Vision

## The pitch

**One Big Package (OBP)** aims to turn the original PS2 Ratchet & Clank trilogy into one cohesive game rather than three isolated games that happen to share a launcher.

The defining idea is that the boundaries between **Ratchet & Clank**, **Going Commando**, and **Up Your Arsenal** become permeable. Their planets, systems, equipment, characters, encounters, progression ideas and story material should be available to a shared runtime that can combine them in ways the original separate discs could not.

Exactly what shape that combined game ultimately takes is intentionally **not prescribed yet**.

OBP might preserve large parts of the original campaign structure. It might weave those campaigns together more tightly. It might substantially remix story beats, level order, progression, revisits and cross-game content. Those are design questions to explore after we understand the source games well enough to make informed choices.

A useful long-term fantasy is:

> What if the PS2 trilogy had somehow existed as one enormous Ratchet & Clank game?

That is a creative direction, not a specification for how its story or progression must work.

---

## Core scope

The primary scope is the original PS2 trilogy:

- **Ratchet & Clank**
- **Ratchet & Clank: Going Commando**
- **Ratchet & Clank: Up Your Arsenal**

The goal is not merely to make all three playable through one program. OBP should eventually make meaningful combinations across their original boundaries possible.

Examples of the design space include:

- equipment from one game interacting with worlds or systems from another;
- revisiting or reusing planets in new contexts;
- enemies, encounters or challenge content drawing from more than one game;
- shared or reconciled versions of recurring systems such as weapons, armour, vendors, bolts, gadgets, Clank abilities and ship travel;
- story, mission and world-state relationships that were impossible when each sequel had to begin from a separate disc and save structure;
- entirely new combinations made from faithfully recovered original content.

These are **possibilities and areas of scope**, not promises that every example will appear or rules for when they must appear.

---

## Project pillars

### 1. One game, not three silos

Cross-game integration is the defining ambition.

A technically perfect recreation of all three games that still presents them as three sealed campaigns would be valuable preservation work, but it would not by itself fulfil the idea of One Big Package.

OBP should provide enough common runtime structure that content and systems can meaningfully cross their original game boundaries when the design calls for it.

How aggressively those boundaries should dissolve is intentionally open.

### 2. Preserve before remixing

Retail game data and executable behaviour are the authority for understanding what the original games actually did.

OBP should reconstruct native assets, systems, story state, mission logic and gameplay behaviour as accurately as practical before relying on assumptions about them. Unknown data remains unknown until evidence supports an interpretation. Reverse-engineered guesses must not quietly become “original behaviour.”

This remains valuable even if the final OBP design later changes or remixes that behaviour. To remix something intelligently, we first need to know what it was.

That means archaeology of apparently ordinary campaign machinery is still worthwhile:

- story flags and mission state;
- level transitions and unlock conditions;
- cutscene triggers;
- planet availability;
- vendors and progression gates;
- rewards and collectibles;
- encounter activation;
- save-state relationships;
- any other systems that explain how the original game moves from one state to another.

Recovering those systems does **not** commit OBP to reproducing the original campaign structure unchanged. It gives us reliable building blocks and a trustworthy baseline.

### 3. Native truth and OBP design are different things

A fusion project will eventually require decisions that no retail game can answer.

For example, the original trilogy cannot tell us the canonical answer to questions such as:

- when or whether equipment from one game should become available in another game's content;
- whether two originally separate story threads should remain sequential, overlap, or be restructured;
- how duplicate or conflicting progression systems should coexist;
- whether a planet should be revisited for an original purpose, a new purpose, or both;
- what enemies or rewards belong in newly combined content;
- how much of the original level order should survive in the final experience.

Those are legitimate **OBP design decisions**.

The rule is not “never invent anything.” The rule is:

> Never present an OBP design choice as recovered native behaviour.

Code, documentation and tests should preserve that distinction wherever it matters.

### 4. Keep the source games recognisable

Combining the trilogy should not automatically flatten everything into a single lowest-common-denominator ruleset.

R&C1, Going Commando and UYA have different pacing, economies, movement assumptions, encounter structures, aesthetics and mechanical identities. Those differences are useful material, not merely incompatibilities to eliminate.

OBP may eventually harmonise some systems, preserve others locally, or deliberately create hybrids. Which approach works best should be decided by the resulting game rather than by architectural convenience alone.

### 5. Keep the design space open while the archaeology is young

The project is currently much better at answering **what is on the discs?** than **what should the final combined game do with all of it?**

That is healthy.

Infrastructure and reverse engineering should avoid baking in unnecessary assumptions about:

- campaign chronology;
- level order;
- which planets are available at a given time;
- whether progression is linear, branching or partly open;
- how much original story structure is preserved;
- how inventories and currencies are reconciled;
- what constitutes first-playthrough versus post-game content.

Where practical, recovered native systems should be represented flexibly enough that future OBP design can reuse, reorder, combine or replace their orchestration without having to rediscover the underlying game data.

### 6. Source content stays local

OBP does not ship Sony/Insomniac game assets, executables or disc images.

Users provide their own supported game data locally. OBP reads that source, verifies it where possible, reconstructs the required native content and stores rebuildable/normalised data locally.

The repository contains code, schemas, tests, research and derived non-infringing metadata — not the retail games themselves.

---

## Story, campaign and level structure

**No final structure is currently prescribed.**

The original story beats, mission chains and level progression are important source material and should be reconstructed accurately enough that we understand how they work. They may also provide useful playable milestones during development.

But the final OBP experience is free to explore a spectrum ranging from:

- mostly original campaign structures connected by richer shared systems;
- to interwoven campaigns with altered transitions and revisits;
- to a much more substantial remix of story order, mission progression and level use.

The project should not assume today where on that spectrum it will eventually land.

Original chronology is evidence about the source games, **not automatically a constraint on OBP**.

Similarly, recovering a native story gate does not mean OBP must retain that gate. It means we know what the original gate did and can choose deliberately whether to preserve, adapt or replace it.

---

## Progression and shared systems

Cross-game progression is a major part of the design space, but its exact form remains open.

Potentially relevant systems include:

- weapons and ammunition;
- weapon experience and upgrades;
- gadgets and traversal tools;
- armour and defensive progression;
- Clank abilities and forms;
- ship travel and ship-related systems;
- bolts and other currencies/rewards;
- vendors and purchase state;
- arenas, races and repeatable challenges;
- collectibles and optional objectives;
- enemy and encounter rosters;
- world and mission state;
- difficulty and replay systems.

For each system, OBP should eventually answer separate questions:

1. What did each retail game actually do?
2. Which pieces can coexist directly?
3. Which pieces conflict or become redundant when combined?
4. What behaviour produces the most coherent and enjoyable OBP experience?

The first question is archaeology. The last three are game design.

The project should avoid deciding the latter accidentally while implementing the former.

---

## Retail revisions, fixes and unused content

OBP uses selected original retail builds as primary mechanical authorities, while other revisions and regions can provide comparison evidence and intentional content.

The final content policy does not have to mean copying one chosen disc byte-for-byte forever. Where official versions differ, OBP may eventually choose a union of intentional content or well-supported fixes while avoiding accidental regional/timing artefacts.

Provenance should make it possible to distinguish:

- behaviour/content present in the primary authority build;
- behaviour/content found in another official revision or region;
- unused or cut retail content;
- an explicit OBP-created rule, edit or addition.

Unused content is interesting source material, not an automatic inclusion mandate. As with story and progression, understanding it comes before deciding what role it should have.

---

## Deadlocked

**Ratchet: Deadlocked** is a natural possible expansion because of its technical and mechanical relationship to the trilogy, but it is not part of the initial core scope.

The architecture should avoid making future support unnecessarily difficult, but current trilogy work does not need to solve Deadlocked-specific design questions.

---

## Non-goals

OBP is not intended to be:

- merely a three-game launcher with no meaningful cross-game integration;
- an emulator that simply boots the retail executables;
- a repository or redistribution mechanism for copyrighted game data;
- a project that invents unknown native behaviour merely to make implementation easier;
- a project where reverse-engineering hypotheses silently become design canon;
- a forced lowest-common-denominator ruleset that erases useful differences between the source games.

It is also **not yet committed** to preserving the original trilogy's campaign ordering, progression structure or story presentation unchanged.

---

## Open design questions

The following are intentionally unresolved and should remain explicit design work rather than accidental implementation decisions:

- the overall story structure of the combined game;
- whether original campaign chronology is preserved, altered, interwoven or substantially remixed;
- level and planet progression order;
- when and why previously visited worlds are revisited;
- how much freedom the player has to choose destinations or objectives;
- what player progression persists across originally separate game content;
- trilogy-wide weapon balance and upgrade behaviour;
- vendor inventories and equipment introduction;
- currency/economy reconciliation;
- armour reconciliation;
- gadget and traversal gating;
- Clank's differing abilities and forms;
- ship progression and space-combat integration;
- persistent versus repeatable world state;
- cross-game enemy and encounter placement;
- difficulty across a much larger combined experience;
- replay / Challenge Mode / NG+ structure;
- use of regional differences, unused content and later official fixes;
- how much entirely new connective content OBP should create;
- the eventual role of Deadlocked.

These questions should be solved experimentally and deliberately as the native games become better understood.

---

## What success looks like

The technical work is successful when OBP can reliably reconstruct the necessary content and behaviour from supported user-supplied retail data through one shared local-first runtime.

The project is successful when that foundation enables a combined experience that feels intentionally designed rather than mechanically concatenated.

A successful OBP should make it possible for:

- worlds and systems from the trilogy to coexist;
- content from different games to interact where useful;
- original behaviour to be reproduced when we want it;
- original structure to be altered when we have a better combined-game idea;
- design experiments to happen without corrupting our understanding of native behaviour;
- the three source games to remain recognisable even as their boundaries become less important.

The ultimate measure is not whether OBP preserves a particular campaign order. It is whether the recovered trilogy can be transformed into something that genuinely feels like **One Big Package** rather than three separate discs placed next to each other.
