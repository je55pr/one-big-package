import { pngDataUri } from "./lib-png.mjs";
import { parseGcLevelSettings } from "../.build/packages/gc-level-settings/src/index.js";
import { readUyaSky } from "../.build/packages/uya-sky/src/index.js";

/** Promote the retail-censused GC/UYA level-settings first part into OBP environment fields. */
export function uyaEnvironmentFromGameplay(gameplayData, source) {
  const settings = parseGcLevelSettings(gameplayData);
  return {
    environment: {
      ...(settings.backgroundColour ? { backgroundColor: settings.backgroundColour } : {}),
      ...(settings.fogColour ? { fogColor: settings.fogColour } : {}),
      fogNearDistance: settings.fogNearDistance,
      fogFarDistance: settings.fogFarDistance,
      fogNearIntensity: settings.fogNearIntensity,
      fogFarIntensity: settings.fogFarIntensity,
      deathHeight: settings.deathHeight,
      isSphericalWorld: settings.isSphericalWorld,
      sphereCenter: {
        x: settings.sphereCentre[0],
        y: settings.sphereCentre[2],
        z: settings.sphereCentre[1],
      },
      source: { ...source, assetKind: "level-settings" },
    },
    summary: {
      blockOffset: settings.blockOffset,
      backgroundColour: settings.backgroundColour,
      fogColour: settings.fogColour,
      fogNearDistance: settings.fogNearDistance,
      fogFarDistance: settings.fogFarDistance,
      fogNearIntensity: settings.fogNearIntensity,
      fogFarIntensity: settings.fogFarIntensity,
      deathHeight: settings.deathHeight,
      isSphericalWorld: settings.isSphericalWorld,
      sphereCentre: settings.sphereCentre,
      shipPosition: settings.shipPosition,
      shipRotationZ: settings.shipRotationZ,
    },
  };
}

/**
 * Decode the retail-censused UYA sky section and append a static initial-pose sky dome.
 * Native shell rotation/angular velocity and bloom are reported but deliberately not
 * simulated by the current neutral viewer.
 */
export function appendUyaSky({ decoded, fields, source, matPrefix, materials, meshes, bounds, environment, textures = true }) {
  const range = sectionRangeAllowZero(fields.sky, decoded.sectionBoundaries, decoded.assets.length);
  if (!range) throw new Error(`No UYA sky section range at asset offset ${fields.sky}.`);
  const sky = readUyaSky(decoded.assets.subarray(range.offset, range.offset + range.size), { framerate: 60 });

  if (textures) {
    for (const tex of sky.textures) {
      materials.push({
        id: `${matPrefix}-sky-tex${tex.index}`,
        name: `sky texture ${tex.index}`,
        image: pngDataUri(tex.width, tex.height, tex.rgba),
        source: { ...source, assetKind: "texture", originalId: `sky-${tex.index}` },
      });
    }
  }

  const ctr = {
    x: (bounds.min.x + bounds.max.x) / 2,
    y: (bounds.min.y + bounds.max.y) / 2,
    z: (bounds.min.z + bounds.max.z) / 2,
  };
  const levelRadius = Math.max(
    1,
    Math.hypot(
      bounds.max.x - bounds.min.x,
      bounds.max.y - bounds.min.y,
      bounds.max.z - bounds.min.z,
    ) / 2,
  );
  let shellMax = 0;
  for (const shell of sky.shells) {
    for (const value of shell.positions) shellMax = Math.max(shellMax, Math.abs(value));
  }
  const scale = shellMax > 0 ? (levelRadius * 1.7) / shellMax : 1;
  const skyColourPresent = sky.colour[3] > 0 || sky.colour.slice(0, 3).some((c) => c > 0);
  const gouraudTint = skyColourPresent
    ? sky.colour.slice(0, 3)
    : environment?.backgroundColor ?? environment?.fogColor ?? [0.05, 0.06, 0.09];
  const gouraudId = `${matPrefix}-sky-gouraud`;
  materials.push({
    id: gouraudId,
    name: "sky untextured",
    debugRgba: [gouraudTint[0], gouraudTint[1], gouraudTint[2], 1],
    source: { ...source, assetKind: "sky", originalId: "gouraud" },
  });

  let triangles = 0;
  let movingShells = 0;
  let bloomShells = 0;
  for (let shellIndex = 0; shellIndex < sky.shells.length; shellIndex++) {
    const shell = sky.shells[shellIndex];
    if (shell.angularVelocityRaw.some((v) => v !== 0) || shell.rotationRaw.some((v) => v !== 0)) movingShells++;
    if (shell.bloom) bloomShells++;
    const byTexture = new Map();
    for (let face = 0; face < shell.triangleTextureIds.length; face++) {
      const tex = shell.triangleTextureIds[face];
      let group = byTexture.get(tex);
      if (!group) byTexture.set(tex, (group = { positions: [], uvs: [], alpha: [], indices: [], remap: new Map() }));
      for (let k = 0; k < 3; k++) {
        const vi = shell.indices[face * 3 + k];
        let out = group.remap.get(vi);
        if (out === undefined) {
          out = group.positions.length / 3;
          group.remap.set(vi, out);
          const lx = shell.positions[vi * 3 + 0] * scale;
          const ly = shell.positions[vi * 3 + 1] * scale;
          const lz = shell.positions[vi * 3 + 2] * scale;
          group.positions.push(ctr.x + lx, ctr.y + lz, ctr.z + ly); // native Z-up -> OBP Y-up
          group.uvs.push(shell.uvs[vi * 2 + 0], shell.uvs[vi * 2 + 1]);
          group.alpha.push(shell.alpha[vi]);
        }
        group.indices.push(out);
      }
    }

    for (const [tex, group] of [...byTexture.entries()].sort((a, b) => a[0] - b[0])) {
      triangles += group.indices.length / 3;
      const materialId = tex >= 0 && textures ? `${matPrefix}-sky-tex${tex}` : gouraudId;
      meshes.push({
        id: `${matPrefix}-sky-shell${shellIndex}-tex${tex}`,
        name: `sky shell ${shellIndex}${tex >= 0 ? ` texture ${tex}` : " untextured"}`,
        geometry: {
          positions: group.positions,
          indices: group.indices,
          uvs: group.uvs,
          alpha: group.alpha,
        },
        materialId,
        source: {
          ...source,
          assetKind: "sky",
          originalId: shellIndex,
          notes: [
            ...(source.notes ?? []),
            `UYA sky initial pose; native rotation=${shell.rotationRaw.join(",")} angularVelocity=${shell.angularVelocityRaw.join(",")} bloom=${shell.bloom}.`,
          ],
        },
      });
    }
  }

  return {
    environment: skyColourPresent ? { ...environment, skyColor: sky.colour } : environment,
    summary: {
      sectionOffset: range.offset,
      sectionSize: range.size,
      textureCount: sky.textures.length,
      shellCount: sky.shells.length,
      clusterCount: sky.shells.reduce((sum, shell) => sum + shell.clusterCount, 0),
      vertices: sky.shells.reduce((sum, shell) => sum + shell.positions.length / 3, 0),
      triangles,
      movingShells,
      bloomShells,
      scale,
      shells: sky.shells.map((shell, index) => ({
        index,
        textured: shell.textured,
        bloom: shell.bloom,
        rotationRaw: shell.rotationRaw,
        angularVelocityRaw: shell.angularVelocityRaw,
        clusters: shell.clusterCount,
        vertices: shell.positions.length / 3,
        triangles: shell.indices.length / 3,
      })),
    },
  };
}

function sectionRangeAllowZero(offset, boundaries, assetsLength) {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset >= assetsLength) return undefined;
  let next;
  for (const bound of boundaries) {
    if (bound > offset && bound <= assetsLength && (next === undefined || bound < next)) next = bound;
  }
  return next === undefined ? undefined : { offset, size: next - offset };
}
