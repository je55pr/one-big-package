# External reverse-engineering source policy

OBP uses public reverse-engineering projects as evidence and research leads, but keeps licensing boundaries explicit.

## Wrench

The inspected Wrench snapshot (`chaoticgd/wrench` commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`) is distributed under the GNU GPL v3. Its source is extremely useful for identifying asset families, format lineage, field meanings and places where the PS2 games diverge.

Until OBP deliberately chooses a compatible licensing strategy:

- Wrench source may be read and cited as reverse-engineering evidence.
- OBP research notes may record facts learned from that evidence: structures, offsets, field meanings, format relationships, test observations and source locations.
- Do **not** copy/paste Wrench implementation code into OBP runtime/importer source.
- Do **not** mechanically translate Wrench functions line-for-line into another language.
- Implement OBP codecs from documented format facts + direct tests against user-supplied game data, keeping our own tests and provenance.
- If we later decide that directly integrating or adapting Wrench code is desirable, stop and make that an explicit project/licensing decision first.

This is project hygiene rather than a legal opinion; the goal is simply to avoid accidentally blurring provenance while archaeology is moving quickly.

## Original game data

Original Ratchet & Clank disc images, executable code, audio, textures, models and other copyrighted game assets remain private user-supplied research inputs. They are not committed to the Git repository or included in OBP releases. Derived caches are rebuildable from the user's own local sources.
