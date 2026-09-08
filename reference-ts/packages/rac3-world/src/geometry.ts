import type { OBPBounds, OBPInstance, OBPMesh, OBPModel, OBPSourceRef } from "../../core/src/index.js";
import type { GcInstance, GcMobyInstance } from "../../gc-instances/src/index.js";
import type { GcMobyClass } from "../../gc-moby/src/index.js";
import type { UyaSky } from "../../uya-sky/src/index.js";

interface StaticClassLike {
  readonly mesh: {
    readonly positions: Float64Array;
    readonly uvs: Float32Array;
    readonly indices: Uint32Array;
  };
  readonly triangleTextureIds: Int32Array;
}

export interface PlacedStaticResult {
  readonly meshes: readonly OBPMesh[];
  readonly bounds: OBPBounds | null;
  readonly placed: number;
  readonly triangles: number;
}

export interface Rac3MobyModelResult {
  readonly models: readonly OBPModel[];
  readonly modelIdsByClass: ReadonlyMap<number, string>;
  readonly emptyGeometryClasses: readonly number[];
  readonly unresolvedSkinningClasses: readonly number[];
}

/** Native RAC2/RAC3 Z-up point -> neutral OBP Y-up point. */
export function rac3PointToObp(point: readonly [number, number, number]): readonly [number, number, number] {
  return [point[0], point[2], point[1]];
}

/**
 * Convert the native `Rz * Ry * Rx` Euler orientation into the same physical
 * orientation after swapping native Y/Z axes into OBP Y-up space.
 */
export function rac3MobyRotationToObpEuler(rotation: readonly [number, number, number]): readonly [number, number, number] {
  const [rx, ry, rz] = rotation;
  const sx = Math.sin(rx), cx = Math.cos(rx);
  const sy = Math.sin(ry), cy = Math.cos(ry);
  const sz = Math.sin(rz), cz = Math.cos(rz);

  const native = [
    cy * cz, cy * sz, -sy,
    sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy,
    cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy,
  ];
  // P * R * P where P swaps Y/Z. `swap` indexes row/column 0,2,1.
  const swap = [0, 2, 1] as const;
  const r = new Array<number>(9);
  for (let row = 0; row < 3; row++) {
    const mappedRow = swap[row]!;
    for (let col = 0; col < 3; col++) {
      const mappedCol = swap[col]!;
      r[row * 3 + col] = native[mappedRow * 3 + mappedCol]!;
    }
  }
  return decomposeRzRyRx(r);
}

function decomposeRzRyRx(r: readonly number[]): readonly [number, number, number] {
  const r20 = clamp(r[6]!, -1, 1);
  const ry = Math.asin(-r20);
  const cy = Math.cos(ry);
  if (Math.abs(cy) > 1e-8) {
    return [Math.atan2(r[7]!, r[8]!), ry, Math.atan2(r[3]!, r[0]!)];
  }
  // Gimbal lock: choose rx = 0 and preserve the remaining observable rotation.
  const rz = Math.atan2(-r[1]!, r[4]!);
  return [0, ry, rz];
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

export function tfragBoundsToObp(bounds: { readonly min: readonly number[]; readonly max: readonly number[] }): OBPBounds {
  return {
    min: { x: bounds.min[0]!, y: bounds.min[2]!, z: bounds.min[1]! },
    max: { x: bounds.max[0]!, y: bounds.max[2]!, z: bounds.max[1]! },
  };
}

export function unionBounds(a: OBPBounds, b: OBPBounds): OBPBounds {
  return {
    min: { x: Math.min(a.min.x, b.min.x), y: Math.min(a.min.y, b.min.y), z: Math.min(a.min.z, b.min.z) },
    max: { x: Math.max(a.max.x, b.max.x), y: Math.max(a.max.y, b.max.y), z: Math.max(a.max.z, b.max.z) },
  };
}

export function placeRac3StaticInstances(
  kind: "tie" | "shrub",
  instances: readonly GcInstance[],
  classes: ReadonlyMap<number, StaticClassLike>,
  buildId: string,
  levelId: string,
  materialPrefix: string,
  materialIds: ReadonlySet<string>,
): PlacedStaticResult {
  interface Group {
    positions: number[];
    uvs: number[];
    indices: number[];
    classIds: Set<number>;
    instanceCount: number;
  }
  const groups = new Map<number, Group>();
  let min = { x: Infinity, y: Infinity, z: Infinity };
  let max = { x: -Infinity, y: -Infinity, z: -Infinity };
  let triangles = 0;

  for (const instance of instances) {
    const cls = classes.get(instance.oClass);
    if (!cls) throw new Error(`RAC3 ${kind} instance ${instance.index} references missing decoded class ${instance.oClass}.`);
    const mesh = cls.mesh;
    if (cls.triangleTextureIds.length !== mesh.indices.length / 3) {
      throw new Error(`RAC3 ${kind} class ${instance.oClass} has inconsistent triangle texture mapping.`);
    }

    const worldPositions = new Float64Array(mesh.positions.length);
    for (let v = 0; v < mesh.positions.length; v += 3) {
      const native = transformMatrixPoint(instance.matrix, mesh.positions[v]!, mesh.positions[v + 1]!, mesh.positions[v + 2]!);
      const converted = rac3PointToObp(native);
      const ox = converted[0], oy = converted[1], oz = converted[2];
      worldPositions[v] = ox;
      worldPositions[v + 1] = oy;
      worldPositions[v + 2] = oz;
      min.x = Math.min(min.x, ox); min.y = Math.min(min.y, oy); min.z = Math.min(min.z, oz);
      max.x = Math.max(max.x, ox); max.y = Math.max(max.y, oy); max.z = Math.max(max.z, oz);
    }

    const remaps = new Map<number, Map<number, number>>();
    const touched = new Set<number>();
    for (let face = 0; face < cls.triangleTextureIds.length; face++) {
      const textureId = cls.triangleTextureIds[face]!;
      let group = groups.get(textureId);
      if (!group) {
        group = { positions: [], uvs: [], indices: [], classIds: new Set(), instanceCount: 0 };
        groups.set(textureId, group);
      }
      group.classIds.add(instance.oClass);
      touched.add(textureId);
      let remap = remaps.get(textureId);
      if (!remap) {
        remap = new Map();
        remaps.set(textureId, remap);
      }
      for (let k = 0; k < 3; k++) {
        const localVertex = mesh.indices[face * 3 + k]!;
        let placedVertex = remap.get(localVertex);
        if (placedVertex === undefined) {
          placedVertex = group.positions.length / 3;
          remap.set(localVertex, placedVertex);
          group.positions.push(
            worldPositions[localVertex * 3]!,
            worldPositions[localVertex * 3 + 1]!,
            worldPositions[localVertex * 3 + 2]!,
          );
          group.uvs.push(mesh.uvs[localVertex * 2]!, mesh.uvs[localVertex * 2 + 1]!);
        }
        group.indices.push(placedVertex);
      }
      triangles++;
    }
    for (const textureId of touched) groups.get(textureId)!.instanceCount++;
  }

  const meshes: OBPMesh[] = [];
  for (const [textureId, group] of [...groups.entries()].sort((a, b) => a[0] - b[0])) {
    const materialId = textureId >= 0 ? `${materialPrefix}-tex${textureId}` : undefined;
    if (materialId && !materialIds.has(materialId)) {
      throw new Error(`RAC3 ${kind} placement references missing native texture material '${materialId}'.`);
    }
    meshes.push({
      id: `rac3-${levelId}-${kind}-tex${textureId}`,
      name: `RAC3 placed ${kind} geometry texture ${textureId}`,
      geometry: { positions: group.positions, indices: group.indices, uvs: group.uvs },
      ...(materialId ? { materialId } : {}),
      source: {
        game: "rac3",
        buildId,
        levelId,
        assetKind: kind,
        originalId: textureId,
        notes: [
          `${group.instanceCount} native ${kind} placements contribute to this mesh`,
          `${group.classIds.size} distinct native class ids`,
          "native matrix applied before deterministic Z-up -> OBP Y-up conversion",
        ],
      },
    });
  }
  return { meshes, bounds: Number.isFinite(min.x) ? { min, max } : null, placed: instances.length, triangles };
}

function transformMatrixPoint(matrix: readonly number[], x: number, y: number, z: number): [number, number, number] {
  return [
    matrix[0]! * x + matrix[4]! * y + matrix[8]! * z + matrix[12]!,
    matrix[1]! * x + matrix[5]! * y + matrix[9]! * z + matrix[13]!,
    matrix[2]! * x + matrix[6]! * y + matrix[10]! * z + matrix[14]!,
  ];
}

export function buildRac3MobyModels(
  classes: ReadonlyMap<number, GcMobyClass>,
  buildId: string,
  levelId: string,
  materialPrefix: string,
  materialIds: ReadonlySet<string>,
): Rac3MobyModelResult {
  const models: OBPModel[] = [];
  const modelIdsByClass = new Map<number, string>();
  const emptyGeometryClasses: number[] = [];
  const unresolvedSkinningClasses: number[] = [];

  for (const [oClass, cls] of [...classes.entries()].sort((a, b) => a[0] - b[0])) {
    if (cls.mesh.indices.length === 0) {
      emptyGeometryClasses.push(oClass);
      continue;
    }
    if (cls.mesh.skinned && !cls.mesh.skinningApplied) {
      unresolvedSkinningClasses.push(oClass);
      continue;
    }

    const groups = groupMobyTriangles(cls);
    const meshes: OBPMesh[] = [];
    for (const [textureId, group] of [...groups.entries()].sort((a, b) => a[0] - b[0])) {
      const materialId = textureId >= 0 && textureId !== 0xff ? `${materialPrefix}-tex${textureId}` : undefined;
      if (materialId && !materialIds.has(materialId)) {
        throw new Error(`RAC3 Moby class ${oClass} references missing native texture material '${materialId}'.`);
      }
      meshes.push({
        id: `rac3-${levelId}-moby-class${oClass}-tex${textureId}`,
        name: textureId === 0xff ? `RAC3 Moby class ${oClass} no-texture geometry` : `RAC3 Moby class ${oClass} texture ${textureId}`,
        geometry: { positions: group.positions, indices: group.indices, uvs: group.uvs },
        ...(materialId ? { materialId } : {}),
        source: {
          game: "rac3",
          buildId,
          levelId,
          assetKind: "moby-class-geometry",
          originalId: oClass,
          notes: [
            `class-local high-LOD bind-pose geometry`,
            `native skinning=${cls.mesh.skinned} applied=${cls.mesh.skinningApplied}`,
            textureId === 0xff ? "native 0xff no-texture sentinel retained as materialless geometry" : `native level Moby texture ${textureId}`,
          ],
        },
      });
    }
    if (meshes.length === 0) {
      emptyGeometryClasses.push(oClass);
      continue;
    }
    const modelId = `rac3-${levelId}-moby-class${oClass}`;
    modelIdsByClass.set(oClass, modelId);
    models.push({
      id: modelId,
      name: `RAC3 Moby class ${oClass}`,
      meshes,
      source: { game: "rac3", buildId, levelId, assetKind: "moby-model", originalId: oClass },
    });
  }

  return {
    models,
    modelIdsByClass,
    emptyGeometryClasses,
    unresolvedSkinningClasses,
  };
}

function groupMobyTriangles(cls: GcMobyClass): Map<number, { positions: number[]; uvs: number[]; indices: number[] }> {
  const groups = new Map<number, { positions: number[]; uvs: number[]; indices: number[]; remap?: Map<number, number> }>();
  for (let face = 0; face < cls.triangleTextureIds.length; face++) {
    const textureId = cls.triangleTextureIds[face]!;
    let group = groups.get(textureId);
    if (!group) {
      group = { positions: [], uvs: [], indices: [], remap: new Map() };
      groups.set(textureId, group);
    }
    const remap = group.remap!;
    for (let k = 0; k < 3; k++) {
      const localVertex = cls.mesh.indices[face * 3 + k]!;
      let outVertex = remap.get(localVertex);
      if (outVertex === undefined) {
        outVertex = group.positions.length / 3;
        remap.set(localVertex, outVertex);
        const native: [number, number, number] = [
          cls.mesh.positions[localVertex * 3]!, cls.mesh.positions[localVertex * 3 + 1]!, cls.mesh.positions[localVertex * 3 + 2]!,
        ];
        group.positions.push(...rac3PointToObp(native));
        group.uvs.push(cls.mesh.uvs[localVertex * 2]!, cls.mesh.uvs[localVertex * 2 + 1]!);
      }
      group.indices.push(outVertex);
    }
  }
  return groups;
}

export interface Rac3AuthoredMobyMetadata {
  readonly uid: number;
  readonly pvarIndex: number;
  readonly modeBits: number;
  readonly raw0x14: number;
  readonly pvar: { readonly index: number; readonly offset: number; readonly size: number; readonly dataOffset: number } | null;
}

export function buildRac3MobyInstances(
  placements: readonly GcMobyInstance[],
  authoredByIndex: ReadonlyMap<number, Rac3AuthoredMobyMetadata>,
  modelIdsByClass: ReadonlyMap<number, string>,
  buildId: string,
  levelId: string,
): readonly OBPInstance[] {
  return placements.map((placement) => {
    const authored = authoredByIndex.get(placement.index);
    if (!authored) throw new Error(`RAC3 Moby placement ${placement.index} has no authored gameplay metadata.`);
    const converted = rac3PointToObp(placement.position);
    const rotation = rac3MobyRotationToObpEuler(placement.rotation);
    const modelId = modelIdsByClass.get(placement.oClass);
    return {
      id: `rac3-${levelId}-moby-${placement.index}`,
      name: `RAC3 Moby ${placement.oClass} instance ${placement.index}`,
      sourceClass: placement.oClass,
      ...(modelId ? { modelId } : {}),
      transform: {
        position: { x: converted[0], y: converted[1], z: converted[2] },
        rotationEuler: { x: rotation[0], y: rotation[1], z: rotation[2] },
        scale: { x: placement.scale, y: placement.scale, z: placement.scale },
      },
      properties: {
        nativeRotation: [...placement.rotation],
        nativeScale: placement.scale,
        nativeLightColour: [...placement.lightColour],
        nativeLightIndex: placement.lightIndex,
        authoredUid: authored.uid,
        authoredRaw0x14: authored.raw0x14,
        authoredModeBits: authored.modeBits,
        pvarIndex: authored.pvarIndex,
        ...(authored.pvar ? {
          pvarSize: authored.pvar.size,
          pvarDataOffset: authored.pvar.dataOffset,
        } : {}),
        classGeometryBinding: modelId ? "neutral-model" : "unavailable",
      },
      source: {
        game: "rac3",
        buildId,
        levelId,
        assetKind: "moby-instance",
        originalId: placement.index,
        notes: [
          `native oClass ${placement.oClass}`,
          "UID/mode/PVar fields are preserved through retail-confirmed GC-layout compatibility; UYA semantic field names remain compatibility labels until native executable proof",
          modelId ? "linked to reusable class-local visual model" : "no evidence-safe local model is available for this authored object",
        ],
      },
    };
  });
}

export interface Rac3SkyGeometryResult {
  readonly meshes: readonly OBPMesh[];
  readonly triangles: number;
  readonly movingShells: number;
  readonly bloomShells: number;
  readonly scale: number;
}

export function buildRac3SkyGeometry(
  sky: UyaSky,
  bounds: OBPBounds,
  source: OBPSourceRef,
  materialPrefix: string,
  gouraudMaterialId: string,
): Rac3SkyGeometryResult {
  const center = {
    x: (bounds.min.x + bounds.max.x) / 2,
    y: (bounds.min.y + bounds.max.y) / 2,
    z: (bounds.min.z + bounds.max.z) / 2,
  };
  const levelRadius = Math.max(1, Math.hypot(
    bounds.max.x - bounds.min.x,
    bounds.max.y - bounds.min.y,
    bounds.max.z - bounds.min.z,
  ) / 2);
  let shellMax = 0;
  for (const shell of sky.shells) for (const value of shell.positions) shellMax = Math.max(shellMax, Math.abs(value));
  const scale = shellMax > 0 ? (levelRadius * 1.7) / shellMax : 1;
  const meshes: OBPMesh[] = [];
  let triangles = 0, movingShells = 0, bloomShells = 0;

  for (let shellIndex = 0; shellIndex < sky.shells.length; shellIndex++) {
    const shell = sky.shells[shellIndex]!;
    if (shell.angularVelocityRaw.some((value) => value !== 0) || shell.rotationRaw.some((value) => value !== 0)) movingShells++;
    if (shell.bloom) bloomShells++;
    const groups = new Map<number, { positions: number[]; uvs: number[]; alpha: number[]; indices: number[]; remap: Map<number, number> }>();

    for (let face = 0; face < shell.triangleTextureIds.length; face++) {
      const textureId = shell.triangleTextureIds[face]!;
      let group = groups.get(textureId);
      if (!group) {
        group = { positions: [], uvs: [], alpha: [], indices: [], remap: new Map() };
        groups.set(textureId, group);
      }
      for (let k = 0; k < 3; k++) {
        const localVertex = shell.indices[face * 3 + k]!;
        let outVertex = group.remap.get(localVertex);
        if (outVertex === undefined) {
          outVertex = group.positions.length / 3;
          group.remap.set(localVertex, outVertex);
          const native: [number, number, number] = [
            shell.positions[localVertex * 3]! * scale,
            shell.positions[localVertex * 3 + 1]! * scale,
            shell.positions[localVertex * 3 + 2]! * scale,
          ];
          const local = rac3PointToObp(native);
          group.positions.push(center.x + local[0], center.y + local[1], center.z + local[2]);
          group.uvs.push(shell.uvs[localVertex * 2]!, shell.uvs[localVertex * 2 + 1]!);
          group.alpha.push(shell.alpha[localVertex]!);
        }
        group.indices.push(outVertex);
      }
    }

    for (const [textureId, group] of [...groups.entries()].sort((a, b) => a[0] - b[0])) {
      triangles += group.indices.length / 3;
      meshes.push({
        id: `rac3-${source.levelId}-sky-shell${shellIndex}-tex${textureId}`,
        name: textureId >= 0 ? `RAC3 sky shell ${shellIndex} texture ${textureId}` : `RAC3 sky shell ${shellIndex} untextured`,
        geometry: {
          positions: group.positions,
          indices: group.indices,
          uvs: group.uvs,
          alpha: group.alpha,
        },
        materialId: textureId >= 0 ? `${materialPrefix}-tex${textureId}` : gouraudMaterialId,
        source: {
          ...source,
          assetKind: "sky",
          originalId: shellIndex,
          notes: [
            `UYA sky initial pose only`,
            `native rotation=${shell.rotationRaw.join(",")} angularVelocity=${shell.angularVelocityRaw.join(",")} bloom=${shell.bloom}`,
          ],
        },
      });
    }
  }

  return { meshes, triangles, movingShells, bloomShells, scale };
}
