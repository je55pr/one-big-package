import { mkdir, writeFile } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { openGcLevelCore, gcLevelCoreCollision, gcLevelCoreSectionRange } from "../.build/packages/gc-level-core/src/index.js";
import { readRcCollision, toObpCollisionMesh } from "../.build/packages/rc-collision/src/index.js";
import { readGcTfrags, toObpTfragMeshes } from "../.build/packages/gc-tfrag/src/index.js";
import { readGcLevelTextures } from "../.build/packages/gc-level-textures/src/index.js";
import { readGcGameplayInstances, transformPoint, matrixFromPosRotScale } from "../.build/packages/gc-instances/src/index.js";
import { readGcTieClasses } from "../.build/packages/gc-tie/src/index.js";
import { readGcShrubClasses } from "../.build/packages/gc-shrub/src/index.js";
import { readGcMobyClasses } from "../.build/packages/gc-moby/src/index.js";
import { readGcLevelSky } from "../.build/packages/gc-sky/src/index.js";
import { readGcLevelSettings } from "../.build/packages/gc-level-settings/src/index.js";
import { pngDataUri } from "./lib-png.mjs";

/**
 * Build an OBPWorld (tfrag render geometry + octree collision) for one Going
 * Commando level, straight from the retail ISO.
 *
 * Usage:
 *   node tools/gc-world.mjs <gc-iso> --level 1 --out captures/level1.world.json [--no-collision] [--no-tfrags]
 */

const args = process.argv.slice(2);
const positional = [];
const opt = { level: 1, collision: true, tfrags: true, textures: true, ties: true, shrubs: true, mobies: true, mobyMarkers: true, sky: true, deathPlane: true };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--level") opt.level = Number(args[++i]);
  else if (a === "--out") opt.out = args[++i];
  else if (a === "--no-collision") opt.collision = false;
  else if (a === "--no-tfrags") opt.tfrags = false;
  else if (a === "--no-textures") opt.textures = false;
  else if (a === "--no-ties") opt.ties = false;
  else if (a === "--no-shrubs") opt.shrubs = false;
  else if (a === "--no-moby-markers") opt.mobyMarkers = false;
  else if (a === "--no-mobies") { opt.mobies = false; opt.mobyMarkers = false; }
  else if (a === "--no-sky") opt.sky = false;
  else if (a === "--no-death-plane") opt.deathPlane = false;
  else if (a === "--only-class") opt.onlyClass = Number(args[++i]);
  else if (a === "--all-chunk-tfrags") { /* now the default */ }
  else if (a === "--chunk0-tfrags-only") opt.chunk0TfragsOnly = true;
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1 || !opt.out) {
  console.error("Usage: node tools/gc-world.mjs <gc-iso> --level N --out <file.json> [--no-collision] [--no-tfrags]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
const source = { game: "rac2", buildId: "rac2-ntscu-v1.01", levelId: String(opt.level), assetKind: "world" };
const meshes = [];
const materials = [];
const collisionMeshes = [];
let environment;
const min = { x: Infinity, y: Infinity, z: Infinity };
const max = { x: -Infinity, y: -Infinity, z: -Infinity };
const grow = (b) => {
  min.x = Math.min(min.x, b.min.x); min.y = Math.min(min.y, b.min.y); min.z = Math.min(min.z, b.min.z);
  max.x = Math.max(max.x, b.max.x); max.y = Math.max(max.y, b.max.y); max.z = Math.max(max.z, b.max.z);
};

try {
  const fs = await Iso9660Filesystem.open(reader);
  const wad = await fs.openFile(`/G/LEVEL${opt.level}.WAD`);
  if (!wad) throw new Error(`/G/LEVEL${opt.level}.WAD not found`);
  const header = await readGcLevelWadHeader(wad);
  const chunkSlots = [4, 5, 6].filter((s) => header.lumps[s]?.present);
  const chunked = chunkSlots.length > 0;

  let coreCache;
  const getCore = async () => (coreCache ??= await openGcLevelCore(openGcLevelWadLump(wad, header, 0)));

  // --- level-core textures (shared: chunk tfrags reference the level-core table) ---
  const matPrefix = `rac2-${opt.level}`;
  if (opt.tfrags && opt.textures) {
    try {
      const core = await getCore();
      const textures = readGcLevelTextures(core, "tfrag");
      for (const tex of textures) {
        materials.push({
          id: `${matPrefix}-tex${tex.index}`,
          name: `tfrag texture ${tex.index}`,
          image: pngDataUri(tex.width, tex.height, tex.rgba),
          source: { ...source, assetKind: "texture", originalId: tex.index },
        });
      }
      console.log(`decoded ${textures.length} tfrag textures`);
    } catch (error) {
      console.log(`texture decode skipped: ${error.message}`);
    }
  }

  // --- tfrags ---
  if (opt.tfrags) {
    const blobs = [];
    if (chunked) {
      // Each present chunk slot is a spatial tile of the terrain (often disjoint
      // regions AND altitudes), so decode every one — chunk 0 alone is just the
      // area around the ship. `--chunk0-tfrags-only` keeps the old behaviour.
      const tfragSlots = opt.chunk0TfragsOnly ? chunkSlots.slice(0, 1) : chunkSlots;
      for (const slot of tfragSlots) {
        const chunk = openGcLevelWadLump(wad, header, slot);
        const ch = await chunk.read(0, 8);
        const tfragsOff = new DataView(ch.buffer, ch.byteOffset, ch.byteLength).getInt32(0, true);
        if (tfragsOff > 0) blobs.push((await readWadLz(chunk, tfragsOff)).data);
      }
    } else {
      const core = await getCore();
      const range = gcLevelCoreSectionRange(core, core.coreHeader.tfrags === 0 ? 0 : core.coreHeader.tfrags)
        ?? (() => { const next = core.sectionBoundaries.find((b) => b > 0); return next ? { offset: 0, size: next } : null; })();
      if (range) blobs.push(core.assets.subarray(range.offset, range.offset + range.size));
    }
    let ti = 0;
    for (const blob of blobs) {
      const tf = readGcTfrags(blob);
      console.log(`tfrags blob ${ti}: ${tf.tfragCount} tfrags -> ${tf.positions.length / 3} verts, ${tf.indices.length / 3} tris, ${tf.textureIds.length} textures`);
      for (const m of toObpTfragMeshes(tf, source, `rac2-${opt.level}-t${ti}`, matPrefix)) meshes.push(m);
      grow({ min: { x: tf.bounds.min[0], y: tf.bounds.min[1], z: tf.bounds.min[2] }, max: { x: tf.bounds.max[0], y: tf.bounds.max[1], z: tf.bounds.max[2] } });
      ti++;
    }
  }

  // --- tie + shrub instances (the instanced environment: buildings, rocks, foliage) ---
  const r2 = (v) => Math.round(v * 100) / 100;
  const r4 = (v) => Math.round(v * 10000) / 10000;

  const placeInstances = (kind, instances, classes, { boundsFromInstances = true } = {}) => {
    const byTex = new Map(); // texId -> { positions:[], uvs:[], indices:[], weld:Map }
    let placed = 0;
    for (const inst of instances) {
      if (opt.onlyClass !== undefined && inst.oClass !== opt.onlyClass) continue;
      const cls = classes.get(inst.oClass);
      if (!cls || cls.mesh.indices.length === 0) continue;
      const matrix = inst.matrix ?? matrixFromPosRotScale(inst.position, inst.rotation, inst.scale || 1);
      placed++;
      const m = cls.mesh;
      const worldPos = new Float64Array(m.positions.length);
      for (let i = 0; i < m.positions.length; i += 3) {
        const [x, y, z] = transformPoint(matrix, m.positions[i], m.positions[i + 1], m.positions[i + 2]);
        worldPos[i] = r2(x); worldPos[i + 1] = r2(z); worldPos[i + 2] = r2(y); // Z-up -> Y-up
        if (boundsFromInstances) {
          if (x < min.x) min.x = x; if (z < min.y) min.y = z; if (y < min.z) min.z = y;
          if (x > max.x) max.x = x; if (z > max.y) max.y = z; if (y > max.z) max.z = y;
        }
      }
      for (let f = 0; f < cls.triangleTextureIds.length; f++) {
        const tex = cls.triangleTextureIds[f];
        let g = byTex.get(tex);
        if (!g) byTex.set(tex, (g = { positions: [], uvs: [], indices: [], weld: new Map() }));
        for (let k = 0; k < 3; k++) {
          const vi = m.indices[f * 3 + k];
          const px = worldPos[vi * 3], py = worldPos[vi * 3 + 1], pz = worldPos[vi * 3 + 2];
          const s = r4(m.uvs[vi * 2]), t = r4(m.uvs[vi * 2 + 1]);
          const key = `${px},${py},${pz},${s},${t}`;
          let idx = g.weld.get(key);
          if (idx === undefined) {
            idx = g.positions.length / 3;
            g.weld.set(key, idx);
            g.positions.push(px, py, pz);
            g.uvs.push(s, t);
          }
          g.indices.push(idx);
        }
      }
    }
    let tris = 0;
    for (const [tex, g] of [...byTex.entries()].sort((a, b) => a[0] - b[0])) {
      tris += g.indices.length / 3;
      meshes.push({
        id: `${matPrefix}-${kind}-tex${tex}`,
        name: `${kind} texture ${tex}`,
        geometry: { positions: g.positions, indices: g.indices, uvs: g.uvs },
        materialId: `${matPrefix}-${kind}-tex${tex}`,
        source: { ...source, assetKind: kind, originalId: tex },
      });
    }
    console.log(`${kind}: ${placed}/${instances.length} instances placed, ${classes.size} classes -> ${tris} tris`);
  };

  if (opt.ties || opt.shrubs || opt.mobies || opt.mobyMarkers) {
    try {
      const gameplay = await readGcGameplayInstances(openGcLevelWadLump(wad, header, 2));
      const core = await getCore();
      const instanceLists = { tie: gameplay.tieInstances, shrub: gameplay.shrubInstances, moby: gameplay.mobyInstances };
      let mobyClasses;
      for (const [enabled, kind, table, readClasses] of [
        [opt.ties, "tie", "tie", readGcTieClasses],
        [opt.shrubs, "shrub", "shrub", readGcShrubClasses],
        [opt.mobies, "moby", "moby", readGcMobyClasses],
      ]) {
        if (!enabled) continue;
        if (opt.textures) {
          for (const tex of readGcLevelTextures(core, table)) {
            materials.push({
              id: `${matPrefix}-${kind}-tex${tex.index}`,
              name: `${kind} texture ${tex.index}`,
              image: pngDataUri(tex.width, tex.height, tex.rgba),
              source: { ...source, assetKind: "texture", originalId: tex.index },
            });
          }
        }
        const classes = readClasses(core);
        if (kind === "moby") mobyClasses = classes;
        // Mobies don't expand the world bounds (skybox / camera dummies sit far away).
        placeInstances(kind, instanceLists[kind], classes, { boundsFromInstances: kind !== "moby" || opt.onlyClass !== undefined });
      }

      // A small oriented cube for moby instances whose class has no decoded mesh
      // (triggers, spawners, and the ~8 classes that fail to parse). Culled to the
      // level volume; never expands bounds.
      if (opt.mobyMarkers && gameplay.mobyInstances.length && Number.isFinite(min.x)) {
        const pad = 1.5 * Math.max(max.x - min.x, max.y - min.y, max.z - min.z);
        const inRange = (x, y, z) =>
          x > min.x - pad && x < max.x + pad && z > min.y - pad && z < max.y + pad && y > min.z - pad && y < max.z + pad;

        const positions = [];
        const indices = [];
        const cube = [[-1, -1, -1], [1, -1, -1], [1, 1, -1], [-1, 1, -1], [-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1]];
        const faces = [[0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [0, 4, 5], [0, 5, 1], [1, 5, 6], [1, 6, 2], [2, 6, 7], [2, 7, 3], [3, 7, 4], [3, 4, 0]];
        let shown = 0;
        for (const moby of gameplay.mobyInstances) {
          if (!inRange(moby.position[0], moby.position[1], moby.position[2])) continue;
          if (mobyClasses?.get(moby.oClass)?.mesh.indices.length) continue; // has a real mesh
          shown++;
          const m = matrixFromPosRotScale(moby.position, moby.rotation, Math.max(0.4, Math.min(4, moby.scale)) * 0.9);
          const base = positions.length / 3;
          for (const [cx, cy, cz] of cube) {
            const [x, y, z] = transformPoint(m, cx, cy, cz);
            positions.push(r2(x), r2(z), r2(y)); // Z-up -> Y-up
          }
          for (const [a, b, c] of faces) indices.push(base + a, base + b, base + c);
        }
        meshes.push({
          id: `${matPrefix}-moby-markers`,
          name: "moby instance markers",
          geometry: { positions, indices, colors: positions.map((_, i) => (i % 3 === 0 ? 0.95 : i % 3 === 1 ? 0.35 : 0.55)) },
          materialId: `${matPrefix}-moby-markers`,
          source: { ...source, assetKind: "moby-marker" },
        });
        console.log(`moby markers: ${shown} instances (no decoded mesh)`);
      }
    } catch (error) {
      console.log(`instances skipped: ${error.message}`);
    }
  }

  // --- collision ---
  if (opt.collision) {
    const collBlobs = [];
    if (chunked) {
      for (const slot of chunkSlots) {
        const chunk = openGcLevelWadLump(wad, header, slot);
        const ch = await chunk.read(0, 8);
        const collOff = new DataView(ch.buffer, ch.byteOffset, ch.byteLength).getInt32(4, true);
        if (collOff > 0) collBlobs.push((await readWadLz(chunk, collOff)).data);
      }
    } else {
      const core = await getCore();
      const c = gcLevelCoreCollision(core);
      if (c) collBlobs.push(c);
    }
    let ci = 0;
    for (const blob of collBlobs) {
      const mesh = readRcCollision(blob);
      console.log(`collision blob ${ci}: ${mesh.octants.length} octants -> ${mesh.triangles.length} tris`);
      collisionMeshes.push(toObpCollisionMesh(mesh, source, `rac2-${opt.level}-collision-${ci}`, `LEVEL${opt.level} collision ${ci}`));
      grow({
        min: { x: mesh.bounds.min.x, y: mesh.bounds.min.z, z: mesh.bounds.min.y },
        max: { x: mesh.bounds.max.x, y: mesh.bounds.max.z, z: mesh.bounds.max.y },
      });
      ci++;
    }
  }

  // --- environment: level settings (death height, fog, background) ---
  try {
    const s = await readGcLevelSettings(openGcLevelWadLump(wad, header, 2));
    environment = {
      ...(s.backgroundColour ? { backgroundColor: s.backgroundColour } : {}),
      ...(s.fogColour ? { fogColor: s.fogColour } : {}),
      fogNearDistance: s.fogNearDistance,
      fogFarDistance: s.fogFarDistance,
      fogNearIntensity: s.fogNearIntensity,
      fogFarIntensity: s.fogFarIntensity,
      deathHeight: s.deathHeight, // native Z maps straight onto OBP Y
      isSphericalWorld: s.isSphericalWorld,
      sphereCenter: { x: s.sphereCentre[0], y: s.sphereCentre[2], z: s.sphereCentre[1] },
      source: { ...source, assetKind: "level-settings" },
    };
    console.log(`environment: deathHeight ${s.deathHeight}, fog ${s.fogColour?.map((v) => Math.round(v * 255)).join(",") ?? "-"}, bg ${s.backgroundColour?.map((v) => Math.round(v * 255)).join(",") ?? "-"}, spherical ${s.isSphericalWorld}`);
  } catch (error) {
    console.log(`level settings skipped: ${error.message}`);
  }

  // Level centre / radius in OBP (Y-up) space, for parking the sky dome.
  const haveBounds = Number.isFinite(min.x);
  const ctr = haveBounds
    ? { x: (min.x + max.x) / 2, y: (min.y + max.y) / 2, z: (min.z + max.z) / 2 }
    : { x: 0, y: 0, z: 0 };
  const levelRadius = haveBounds ? 0.5 * Math.hypot(max.x - min.x, max.y - min.y, max.z - min.z) : 100;

  // --- sky dome ---
  if (opt.sky) {
    try {
      const core = await getCore();
      const sky = readGcLevelSky(core);
      if (sky && sky.shells.length) {
        if (sky.colour[3] > 0 || sky.colour.slice(0, 3).some((c) => c > 0)) {
          environment = { ...(environment ?? {}), skyColor: sky.colour };
        }
        if (opt.textures) {
          for (const tex of sky.textures) {
            materials.push({
              id: `${matPrefix}-sky-tex${tex.index}`,
              name: `sky texture ${tex.index}`,
              image: pngDataUri(tex.width, tex.height, tex.rgba),
              source: { ...source, assetKind: "texture", originalId: `sky-${tex.index}` },
            });
          }
        }
        // Uniform scale so all shells keep their relative (concentric) sizes,
        // sized to sit comfortably outside the level. The game redraws the sky
        // centred on the camera; parking it as a static dome is a viewer choice.
        let shellMax = 0;
        for (const shell of sky.shells) {
          for (let i = 0; i < shell.positions.length; i++) shellMax = Math.max(shellMax, Math.abs(shell.positions[i]));
        }
        const scale = shellMax > 0 ? (levelRadius * 1.7) / shellMax : 1;
        const gouraudTint = (sky.colour.slice(0, 3).some((c) => c > 0.02) ? sky.colour.slice(0, 3)
          : environment?.backgroundColor ?? environment?.fogColor ?? [0.05, 0.06, 0.09]);
        materials.push({
          id: `${matPrefix}-sky-gouraud`,
          name: "sky backdrop",
          debugRgba: [gouraudTint[0], gouraudTint[1], gouraudTint[2], 1],
          source: { ...source, assetKind: "sky" },
        });

        let skyTris = 0;
        sky.shells.forEach((shell, si) => {
          // Group triangles by texture id (like tie/shrub/moby placement).
          const byTex = new Map();
          for (let f = 0; f < shell.triangleTextureIds.length; f++) {
            const tex = shell.triangleTextureIds[f];
            let g = byTex.get(tex);
            if (!g) byTex.set(tex, (g = { positions: [], uvs: [], alpha: [], indices: [], weld: new Map() }));
            for (let k = 0; k < 3; k++) {
              const vi = shell.indices[f * 3 + k];
              // local (Z-up) -> scaled + centred -> OBP (Y-up)
              const lx = shell.positions[vi * 3] * scale;
              const ly = shell.positions[vi * 3 + 1] * scale;
              const lz = shell.positions[vi * 3 + 2] * scale;
              const px = r2(ctr.x + lx), py = r2(ctr.y + lz), pz = r2(ctr.z + ly);
              const s = r4(shell.uvs[vi * 2]), t = r4(shell.uvs[vi * 2 + 1]);
              const a = r2(shell.alpha[vi]);
              const key = `${px},${py},${pz},${s},${t},${a}`;
              let idx = g.weld.get(key);
              if (idx === undefined) {
                idx = g.positions.length / 3;
                g.weld.set(key, idx);
                g.positions.push(px, py, pz);
                g.uvs.push(s, t);
                g.alpha.push(a);
              }
              g.indices.push(idx);
            }
          }
          for (const [tex, g] of byTex) {
            skyTris += g.indices.length / 3;
            const textured = tex >= 0;
            meshes.push({
              id: `${matPrefix}-sky-s${si}-tex${tex}`,
              name: `sky shell ${si}${textured ? ` texture ${tex}` : " backdrop"}`,
              geometry: { positions: g.positions, indices: g.indices, uvs: g.uvs, alpha: g.alpha },
              materialId: textured ? `${matPrefix}-sky-tex${tex}` : `${matPrefix}-sky-gouraud`,
              source: { ...source, assetKind: "sky", originalId: `shell-${si}` },
            });
          }
        });
        console.log(`sky: ${sky.shells.length} shells, ${sky.textures.length} textures -> ${skyTris} tris (scale ${scale.toFixed(2)})`);
      }
    } catch (error) {
      console.log(`sky skipped: ${error.message}`);
    }
  }

  // --- death-height plane (the kill / goo layer) ---
  if (opt.deathPlane && environment && Number.isFinite(environment.deathHeight) && haveBounds) {
    const y = environment.deathHeight;
    if (y > min.y - 6 * levelRadius && y < max.y + 2 * levelRadius) {
      const mx = 0.15 * (max.x - min.x), mz = 0.15 * (max.z - min.z);
      const x0 = r2(min.x - mx), x1 = r2(max.x + mx), z0 = r2(min.z - mz), z1 = r2(max.z + mz);
      const tint = environment.fogColor ?? environment.backgroundColor ?? [0.25, 0.6, 0.35];
      materials.push({
        id: `${matPrefix}-death-plane`,
        name: `death plane (Y=${y.toFixed(1)})`,
        debugRgba: [Math.min(1, tint[0] * 1.6 + 0.08), Math.min(1, tint[1] * 1.6 + 0.12), Math.min(1, tint[2] * 1.6 + 0.08), 0.45],
        source: { ...source, assetKind: "death-plane" },
      });
      meshes.push({
        id: `${matPrefix}-death-plane`,
        name: `death plane (native Z = ${y.toFixed(1)})`,
        geometry: {
          positions: [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1],
          indices: [0, 2, 1, 0, 3, 2],
        },
        materialId: `${matPrefix}-death-plane`,
        source: { ...source, assetKind: "death-plane" },
      });
      console.log(`death plane: OBP Y=${y.toFixed(1)} spanning x[${x0},${x1}] z[${z0},${z1}]`);
    } else {
      console.log(`death plane: deathHeight ${y} is far outside the level bounds - skipped`);
    }
  }
} finally {
  await reader.close();
}

if (!Number.isFinite(min.x)) { min.x = min.y = min.z = 0; max.x = max.y = max.z = 0; }

const world = {
  schemaVersion: 1,
  id: `rac2-level${opt.level}`,
  displayName: `Going Commando LEVEL${opt.level}`,
  source,
  bounds: { min, max },
  materials,
  meshes,
  collisionMeshes,
  instances: [],
  splines: [],
  volumes: [],
  spawnPoints: [],
  ...(environment ? { environment } : {}),
};
await mkdir(resolve(opt.out, ".."), { recursive: true });
await writeFile(resolve(opt.out), JSON.stringify(world));
console.log(`wrote ${resolve(opt.out)}  (${meshes.length} render meshes, ${collisionMeshes.length} collision meshes)`);
