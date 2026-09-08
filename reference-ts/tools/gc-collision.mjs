import { mkdir, writeFile } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { openGcLevelCore, gcLevelCoreCollision } from "../.build/packages/gc-level-core/src/index.js";
import { readRcCollision, toObpCollisionMesh } from "../.build/packages/rc-collision/src/index.js";

/**
 * Extract and verify the collision of a Going Commando level WAD.
 *
 * Streamed levels (chunks in header slots 4/5/6): each chunk starts with a
 * ChunkHeader { s32 tfrags; s32 collision } pointing at WAD-LZ blocks.
 * All other levels: collision is a raw section of the decompressed level-core
 * asset blob (data lump -> GcUyaLevelDataHeader -> LevelCoreHeader.collision).
 * Either way the collision bytes are the shared RC octree collision format.
 *
 * Usage:
 *   node tools/gc-collision.mjs <gc-iso> [--level 1] [--all] [--obj out.obj] [--world out.json]
 */

const args = process.argv.slice(2);
const positional = [];
const opt = { level: 1 };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--level") opt.level = Number(args[++i]);
  else if (a === "--all") opt.all = true;
  else if (a === "--obj") opt.obj = args[++i];
  else if (a === "--world") opt.world = args[++i];
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-collision.mjs <gc-iso> [--level N | --all] [--obj <file>] [--world <file>]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const fs = await Iso9660Filesystem.open(reader);
  const levels = opt.all ? range(0, 27) : [opt.level];
  const combined = { positions: [], triangles: [] };

  for (const n of levels) {
    const wad = await fs.openFile(`/G/LEVEL${n}.WAD`);
    if (!wad) continue;
    const header = await readGcLevelWadHeader(wad);
    const chunkSlots = [4, 5, 6].filter((s) => header.lumps[s]?.present);

    if (chunkSlots.length === 0) {
      const dataLump = openGcLevelWadLump(wad, header, 0);
      const core = await openGcLevelCore(dataLump);
      const collBytes = gcLevelCoreCollision(core);
      if (!collBytes) { console.log(`LEVEL${n} (id ${header.levelId}): level-core has no collision section`); continue; }
      const mesh = readRcCollision(collBytes);
      console.log(
        `LEVEL${n} (id ${header.levelId}) core: ` +
        `assets ${core.coreHeader.assetsCompressedSize} -> ${core.assets.length} B; ` +
        `collision @0x${core.coreHeader.collision.toString(16)} (${collBytes.length} B); ` +
        `${mesh.octants.length} octants, ${mesh.positions.length / 3} verts, ${mesh.triangles.length} tris, ` +
        `${mesh.heroGroupCount} hero groups; materials [${mesh.materialIds.join(",")}]; ` +
        `bounds (${fmt(mesh.bounds.min)})..(${fmt(mesh.bounds.max)})`,
      );
      accumulate(mesh);
      continue;
    }

    for (const slot of chunkSlots) {
      const chunk = openGcLevelWadLump(wad, header, slot);
      const ch = await chunk.read(0, 8);
      const collOff = new DataView(ch.buffer, ch.byteOffset, ch.byteLength).getInt32(4, true);
      if (collOff <= 0) { console.log(`LEVEL${n} chunk${slot - 4}: no collision`); continue; }
      const { data, compressedSize } = await readWadLz(chunk, collOff);
      const mesh = readRcCollision(data);
      console.log(
        `LEVEL${n} (id ${header.levelId}) chunk${slot - 4}: ` +
        `compressed ${compressedSize} -> ${data.length} B; ` +
        `${mesh.octants.length} octants, ${mesh.positions.length / 3} verts, ${mesh.triangles.length} tris, ` +
        `${mesh.heroGroupCount} hero groups; materials [${mesh.materialIds.join(",")}]; ` +
        `bounds (${fmt(mesh.bounds.min)})..(${fmt(mesh.bounds.max)})`,
      );
      accumulate(mesh);
    }
  }

  function accumulate(mesh) {
    if (!opt.obj && !opt.world) return;
    const base = combined.positions.length / 3;
    for (const p of mesh.positions) combined.positions.push(p);
    for (const t of mesh.triangles) combined.triangles.push({ a: t.a + base, b: t.b + base, c: t.c + base, materialId: t.materialId });
  }

  if (opt.obj) {
    const lines = [];
    for (let i = 0; i < combined.positions.length; i += 3) lines.push(`v ${combined.positions[i]} ${combined.positions[i + 1]} ${combined.positions[i + 2]}`);
    for (const t of combined.triangles) lines.push(`f ${t.a + 1} ${t.b + 1} ${t.c + 1}`);
    await writeFile(resolve(opt.obj), lines.join("\n") + "\n");
    console.log(`wrote ${resolve(opt.obj)} (${combined.positions.length / 3} verts, ${combined.triangles.length} tris)`);
  }

  if (opt.world) {
    const source = { game: "rac2", buildId: "rac2-ntscu-v1.01", levelId: String(opt.level), assetKind: "collision" };
    const collisionMesh = toObpCollisionMesh(
      { meshOffset: 0x40, heroGroupsOffset: 0, heroGroupCount: 0, octants: [], positions: Float64Array.from(combined.positions), triangles: combined.triangles, bounds: { min: { x: 0, y: 0, z: 0 }, max: { x: 0, y: 0, z: 0 } }, materialIds: [] },
      source, `rac2-${opt.level}-collision`, `LEVEL${opt.level} collision`,
    );
    const p = collisionMesh.geometry.positions;
    const min = { x: Infinity, y: Infinity, z: Infinity };
    const max = { x: -Infinity, y: -Infinity, z: -Infinity };
    for (let i = 0; i < p.length; i += 3) {
      min.x = Math.min(min.x, p[i]); max.x = Math.max(max.x, p[i]);
      min.y = Math.min(min.y, p[i + 1]); max.y = Math.max(max.y, p[i + 1]);
      min.z = Math.min(min.z, p[i + 2]); max.z = Math.max(max.z, p[i + 2]);
    }
    const world = {
      schemaVersion: 1, id: `rac2-level${opt.level}-collision`, displayName: `Going Commando LEVEL${opt.level} (collision only)`,
      source, bounds: { min, max }, materials: [], meshes: [], collisionMeshes: [collisionMesh],
      instances: [], splines: [], volumes: [], spawnPoints: [],
    };
    await mkdir(resolve(opt.world, ".."), { recursive: true });
    await writeFile(resolve(opt.world), JSON.stringify(world));
    console.log(`wrote ${resolve(opt.world)}`);
  }
} finally {
  await reader.close();
}

function range(a, b) { return Array.from({ length: b - a }, (_, i) => a + i); }
function fmt(v) { return `${v.x.toFixed(0)}, ${v.y.toFixed(0)}, ${v.z.toFixed(0)}`; }
